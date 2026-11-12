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
        private float next;

        // Sets the stack size of one item right away (used for new items too, so they do not
        // wait for the next 5-second pass). At least "CheatMinStackSize" per slot.
        public static void ApplyStack(Cargo c)
        {
            if (c == null || c.StackLimit == null) return;
            float f = Mathf.Max(1f, Settings.F("CheatStackSize"));
            float minStack = Mathf.Max(0f, Settings.F("CheatMinStackSize"));
            float baseLimit = Mathf.Max(1f, c.Data != null ? c.Data.StackLimit : 1f);
            if (minStack > 0f) f = Mathf.Max(f, minStack / baseLimit);
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
            foreach (var e in Util.Find<Equipment>())
            {
                Util.ScaleParameter(e.Mass, noWeight ? 0.0000001f : 1f);
                if (e is Cargo c) ApplyStack(c);
            }
        }
    }
}
