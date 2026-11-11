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

        public override void Tick()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 5f;
            var ship = Util.PlayerShip;
            bool noWeight = Settings.B("CheatNoWeight");
            float stack = Mathf.Max(1f, Settings.F("CheatStackSize"));

            foreach (var s in ship.GetComponentsInChildren<Storage>(true))
            {
                if (s.IsShop) continue;
                var t = Traverse.Create(s);
                bool origIgnore = Util.Original<bool>(s, "ignoreStackLimits");
                t.Field("ignoreStackLimits").SetValue(stack > 1f || origIgnore);
                Util.ScaleParameter(t.Field("itemsMassLimit").GetValue<Parameter>(), noWeight ? 1000000f : 1f);
            }
            foreach (var e in ship.GetComponentsInChildren<Equipment>(true))
            {
                Util.ScaleParameter(e.Mass, noWeight ? 0.0000001f : 1f);
                if (e is Cargo c) Util.ScaleParameter(c.StackLimit, stack);
            }
        }
    }
}
