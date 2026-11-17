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
                // DoMe: surfaced = red, blue only once we dive (submerged or diving)
                bool under = ship.SubmergedOrGoingToSubmerge;
                if (Settings.B("AutoLightHuntedBlue") && under) wanted = LightController.Preset.SilentRun;
                else if (Settings.B("AutoLightNightRed") && !under) wanted = LightController.Preset.Alarm;

                if (lastWanted != wanted)
                {
                    var sws = Util.Find<LightSwitch>(); var sw = sws.Length > 0 ? sws[0] : null;   // 0.4.6: interior scene too
                    // 0.4.9: since 0.4.6 the switch in the interior is found; right after a load it is not ready yet
                    // (error in the game's OnLightPresetChanged), so wait for the boat and try again next second.
                    if (sw != null && Util.ShipReady)
                    {
                        try { sw.SetColor(wanted, quiet: true); lastWanted = wanted; }
                        catch (System.Exception) { }
                    }
                }
            }
            else lastWanted = null;

            if (Settings.B("AutoSearchlight"))
            {
                bool on = night && !ship.SubmergedOrGoingToSubmerge && !ship.IsHunted;
                if (lastSearchlight != on)
                {
                    foreach (var s in Util.Find<Searchlight>())
                        if (Util.IsPlayers(s)) s.SetDisableFlag(DeviceDisableFlag.TurnedOff, !on);
                    lastSearchlight = on;
                }
            }
            else lastSearchlight = null;
        }
    }
}
