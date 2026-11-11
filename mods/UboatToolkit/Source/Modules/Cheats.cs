using HarmonyLib;
using UBOAT.Game.Core.Data;

namespace UboatToolkit.Modules
{
    // Cheats that only change values of the player's boat (single player).
    public class Cheats : ModuleBase
    {
        public override void Tick()
        {
            var ship = Util.PlayerShip;
            // Undetectable: seen, heard (sonar + hydrophones) and radar-detected at almost zero range.
            float f = Settings.B("CheatUndetectable") ? 0.001f : 1f;
            var t = Traverse.Create(ship);
            foreach (var field in new[] { "visualDetectability", "sonarDetectability", "radarDetectorDetectability", "hydroacousticDetectorsRangeScale" })
                Util.ScaleParameter(t.Field(field).GetValue<Parameter>(), f);
        }
    }
}
