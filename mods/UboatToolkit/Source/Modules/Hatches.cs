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
        private CharacterSwitchCloseHatchesUI ui;

        public override void Tick()
        {
            var ship = Util.PlayerShip;
            bool danger = ship.SubmergedOrGoingToSubmerge || ship.IsHunted;
            if (Settings.B("AutoHatches") && danger && !lastDanger)
            {
                if (ui == null) ui = Object.FindObjectOfType<CharacterSwitchCloseHatchesUI>(true);   // scene-wide search once, not at every dive
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
