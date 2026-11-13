using HarmonyLib;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Scene.Items;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // Cheats #18 + #23: items and ammunition (almost) weigh nothing (0.0000001, never 0),
    // and one storage slot holds (almost) unlimited items of one kind.
    // Adding real extra slots broke items in the old "Slot Machine" mod, so this uses
    // the game's own "ignore stack limits" switch instead (safe for saves).
    public class Inventory : ModuleBase
    {
        private float next, nextMerge;

        // Same items in several slots of one storage are put together into one stack
        // (e.g. medkits bought as 5-packs). No reordering, slots with open loading orders are left alone.
        internal static int MergeStacks()
        {
            int moves = 0;
            foreach (var s in Util.Find<Storage>())
            {
                if (s == null || s.IsShop || s.GetComponentInParent<UBOAT.Game.Scene.Characters.PlayableCharacter>() != null) continue;
                int slots = Util.Slots(s);
                for (int a = 0; a < slots; a++)
                {
                    if (!(s.GetItemAt(a) is Cargo ca) || ca.Data == null || ca.Data.StackLimit <= 1f || s.HasTransportOrders(a)) continue;
                    for (int b = a + 1; b < slots; b++)
                    {
                        if (!(s.GetItemAt(b) is Cargo cb) || cb.Data == null || cb.Data.Name != ca.Data.Name || s.HasTransportOrders(b)) continue;
                        float room = (float)ca.StackLimit - ca.Count;
                        if (room <= 0f) break;
                        float move = Mathf.Min(room, cb.Count);
                        if (move <= 0f) continue;
                        UBOAT.Game.Scene.Utilities.StorageUtility.TransferInstantly(s, b, s, a, move);
                        moves++;
                    }
                }
            }
            return moves;
        }

        // Sets the stack size of one item right away (used for new items too, so they do not
        // wait for the next 5-second pass). At least "CheatMinStackSize" per slot.
        public static void ApplyStack(Cargo c)
        {
            if (c == null || c.StackLimit == null) return;
            float dataLimit = c.Data != null ? c.Data.StackLimit : 1f;
            // Stackable items: the bigger stack is written into the item database itself (see StackData),
            // because the game's storage window (drag, buy, split) only reads the database value.
            // Keep the item's own "Base" value in step with it.
            if (dataLimit > 1f)
            {
                var m = c.StackLimit.GetDeltaModifier("Base", true);
                if (m != null && Mathf.Abs(m.Value - dataLimit) > 0.0001f) m.Value = dataLimit;
                Util.ScaleParameter(c.StackLimit, 1f);
                return;
            }
            float f = Mathf.Max(1f, Settings.F("CheatStackSize"));
            float minStack = Mathf.Max(0f, Settings.F("CheatMinStackSize"));
            if (minStack > 0f) f = Mathf.Max(f, minStack);
            Util.ScaleParameter(c.StackLimit, f);
        }

        // Cheat: the dealer in port has at least "CheatShopStock" of every stackable item (DoMe 2026-11-13: buy slider
        // stopped at the dealer's 15 decoys although one storage place holds 100000). Only while docked, every 5 s.
        private static int shopLogged;
        private static void ShopStock()
        {
            float want = Settings.F("CheatShopStock");
            var ship = Util.PlayerShip;
            if (want <= 0f || ship == null || !ship.Docked) return;
            int n = 0;
            foreach (var st in UnityEngine.Object.FindObjectsOfType<Storage>())
            {
                if (st == null || !st.IsShop) continue;
                for (int i = Util.Slots(st) - 1; i >= 0; i--)
                {
                    var c = st.GetItemAt(i) as Cargo;
                    if (c == null || c.Data == null || c.Data.StackLimit <= 1f || c is Fuel) continue;
                    if (c.Count < want) { c.Count = want; n++; }
                }
            }
            if (n > 0 && shopLogged++ < 5) Util.Log("Händler: " + n + " Waren auf " + want + " Stück aufgefüllt");
        }

        public override void Tick()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 5.3f;
            var ship = Util.PlayerShip;
            bool noWeight = Settings.B("CheatNoWeight");
            float stack = Mathf.Max(1f, Settings.F("CheatStackSize"));
            float minStack = Mathf.Max(0f, Settings.F("CheatMinStackSize"));

            foreach (var s in Util.Find<Storage>())
            {
                if (s.IsShop) continue;
                var t = Traverse.Create(s);
                bool origIgnore = Util.Original<bool>(s, "ignoreStackLimits");
                t.Field("ignoreStackLimits").SetValue(stack > 1f || minStack > 0f || origIgnore);
                // Never for diesel tanks: the game shows fuel % as (fuel mass / tank mass limit), so a bigger
                // limit made the tank read 0 % and the boat "out of fuel" although it was full.
                bool fuelTank = s.StorageType != null && s.StorageType.StartsWith("Fuel");
                Util.ScaleParameter(t.Field("itemsMassLimit").GetValue<Parameter>(), noWeight && !fuelTank ? 1000000f : 1f);
            }
            StackData.Apply();
            Util.Safe("ShopStock", ShopStock);
            if (Settings.B("CheatMergeStacks") && Time.unscaledTime >= nextMerge)
            {
                nextMerge = Time.unscaledTime + 15f;
                int m = MergeStacks();
                if (m > 0) Util.Log("Stapel zusammengelegt: " + m);
            }
            foreach (var e in Util.Find<Equipment>())
            {
                // Never for diesel: the game reads the fuel amount from its weight, so "no weight"
                // made every tank show 0 kg and look empty, even right after refuelling.
                Util.ScaleParameter(e.Mass, noWeight && !(e is Fuel) ? 0.0000001f : 1f);
                if (e is Cargo c) ApplyStack(c);
            }
        }
    }

    // (ShopStock is a member of Inventory, see below)
    // One place that writes the stack size into the item database (all stackable items, e.g. medkits 5 -> 100000).
    // Stack cheat: base x "CheatStackSize", at least "CheatMinStackSize". Tight packing skill adds its bonus on top.
    // Switched off = the game's own value again.
    internal static class StackData
    {
        private static readonly System.Collections.Generic.Dictionary<UBOAT.Game.Sandbox.SandboxEquipmentData, float> baseStack =
            new System.Collections.Generic.Dictionary<UBOAT.Game.Sandbox.SandboxEquipmentData, float>();
        internal static int PackBonus;
        private static string last = "";

        internal static void Apply()
        {
            float f = Mathf.Max(1f, Settings.F("CheatStackSize"));
            float minStack = Mathf.Max(0f, Settings.F("CheatMinStackSize"));
            bool all = Settings.B("CheatStackAll") && (f > 1f || minStack > 0f);
            string key = f + "/" + minStack + "/" + PackBonus + "/" + all;
            if (key == last) return;
            var db = DWS.Common.InjectionFramework.ScriptableObjectSingleton.LoadSingleton<UBOAT.Game.Sandbox.Devices.EquipmentDatabase>();
            var bps = db != null ? db.GetEquipmentDirect() : null;
            if (bps == null) return;
            int n = 0;
            foreach (var d in bps)
            {
                if (d == null) continue;
                if (!baseStack.TryGetValue(d, out float b)) { b = d.StackLimit; baseStack[d] = b; }
                // single items (game stack 1): only plain goods (Cargo types, not diesel) and only with "CheatStackAll";
                // switched off = back to the game's 1
                if (b <= 1f && !(all && PlainGoods(d)))
                {
                    if (Mathf.Abs(d.StackLimit - b) > 0.0001f) { d.StackLimit = b; n++; }
                    continue;
                }
                float v = Mathf.Max(Mathf.Max(b, 1f) * f, minStack) + PackBonus;
                if (Mathf.Abs(d.StackLimit - v) > 0.0001f) { d.StackLimit = v; n++; }
            }
            last = key;
            foreach (var c in UnityEngine.Object.FindObjectsOfType<Cargo>()) Inventory.ApplyStack(c);
            Util.Log("Stapel: " + n + " Gegenstaende neu (x" + f + ", mind. " + minStack + ", Packen +" + PackBonus + ", Einzel-Sachen " + (all ? "auch" : "nein") + ")");
        }

        private static bool PlainGoods(UBOAT.Game.Sandbox.SandboxEquipmentData d)
        {
            var t = d.Type;
            return t != null && typeof(UBOAT.Game.Scene.Items.Cargo).IsAssignableFrom(t) && !typeof(UBOAT.Game.Scene.Items.Fuel).IsAssignableFrom(t);
        }
    }
}
