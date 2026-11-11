using UnityEngine;
using UBOAT.Game.Scene.Effects;
using UBOAT.Game.Scene.Environment;
using UBOAT.Game.Scene.Items;

namespace UboatToolkit.Modules
{
    // QoL #15: red light at night, blue light while hunted. #16: automatic searchlight (off by default).
    // The light only switches when the situation changes, so you can still switch by hand in between.
    public class Lighting : ModuleBase
    {
        private LightController.Preset? lastWanted;
        private bool? lastSearchlight;
        private Weather weather;

        public override void Tick()
        {
            var ship = Util.PlayerShip;
            if (weather == null) weather = Object.FindObjectOfType<Weather>();
            bool night = weather != null && weather.IsNight;

            if (Settings.B("AutoLight"))
            {
                var wanted = LightController.Preset.Surface;
                if (Settings.B("AutoLightHuntedBlue") && ship.IsHunted) wanted = LightController.Preset.SilentRun;
                else if (Settings.B("AutoLightNightRed") && night) wanted = LightController.Preset.Alarm;

                if (lastWanted != wanted)
                {
                    var sw = ship.GetComponentInChildren<LightSwitch>(true);
                    if (sw != null) { sw.SetColor(wanted, quiet: true); lastWanted = wanted; }
                }
            }
            else lastWanted = null;

            if (Settings.B("AutoSearchlight"))
            {
                bool on = night && !ship.SubmergedOrGoingToSubmerge && !ship.IsHunted;
                if (lastSearchlight != on)
                {
                    foreach (var s in ship.GetComponentsInChildren<Searchlight>(true))
                        s.SetDisableFlag(DeviceDisableFlag.TurnedOff, !on);
                    lastSearchlight = on;
                }
            }
            else lastSearchlight = null;
        }
    }
}
