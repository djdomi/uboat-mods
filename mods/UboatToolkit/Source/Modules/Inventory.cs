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

        public override void Tick()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 5f;
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
            if (Settings.B("CheatMergeStacks") && Time.unscaledTime >= nextMerge)
            {
                nextMerge = Time.unscaledTime + 15f;
                int m = MergeStacks();
                if (m > 0) Util.Log("Stapel zusammengelegt: " + m);
            }
            foreach (var e in Util.Find<Equipment>())
            {
                Util.ScaleParameter(e.Mass, noWeight ? 0.0000001f : 1f);
                if (e is Cargo c) ApplyStack(c);
            }
        }
    }

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
            string key = f + "/" + minStack + "/" + PackBonus;
            if (key == last) return;
            var db = DWS.Common.InjectionFramework.ScriptableObjectSingleton.LoadSingleton<UBOAT.Game.Sandbox.Devices.EquipmentDatabase>();
            var bps = db != null ? db.GetEquipmentDirect() : null;
            if (bps == null) return;
            int n = 0;
            foreach (var d in bps)
            {
                if (d == null) continue;
                if (!baseStack.TryGetValue(d, out float b)) { b = d.StackLimit; baseStack[d] = b; }
                if (b <= 1f) continue;
                float v = Mathf.Max(b * f, minStack) + PackBonus;
                if (Mathf.Abs(d.StackLimit - v) > 0.0001f) { d.StackLimit = v; n++; }
            }
            last = key;
            foreach (var c in UnityEngine.Object.FindObjectsOfType<Cargo>()) Inventory.ApplyStack(c);
            Util.Log("Stapel: " + n + " Gegenstaende neu (x" + f + ", mind. " + minStack + ", Packen +" + PackBonus + ")");
        }
    }
}
