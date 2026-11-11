using HarmonyLib;
using UnityEngine;
using UBOAT.Game.UI.CharacterSwitchUI;

namespace UboatToolkit.Modules
{
    // QoL #22: when the boat starts to dive or gets hunted, the crew closes all hatches.
    // Uses the game's own "close all hatches" button logic.
    public class Hatches : ModuleBase
    {
        private bool lastDanger;

        public override void Tick()
        {
            var ship = Util.PlayerShip;
            bool danger = ship.SubmergedOrGoingToSubmerge || ship.IsHunted;
            if (Settings.B("AutoHatches") && danger && !lastDanger)
            {
                var ui = Object.FindObjectOfType<CharacterSwitchCloseHatchesUI>(true);
                if (ui != null)
                {
                    var t = Traverse.Create(ui);
                    t.Method("ValidateState").GetValue();
                    if (!t.Field("areHatchesClosed").GetValue<bool>()) t.Method("OnClick").GetValue();
                }
            }
            lastDanger = danger;
        }
    }
}
