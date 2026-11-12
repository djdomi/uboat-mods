using HarmonyLib;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene.Characters;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // QoL #37: full crew from the start. Before the first patrol, sailors are added
    // until the crew has the wanted size (a real U-boat left with 45+ men).
    public class CrewFill : ModuleBase
    {
        private PlayerCrew crew;
        private PlayerCareer career;

        public override void Tick()
        {
            if (!Settings.B("FullCrewAtStart")) return;
            if (crew == null) crew = Object.FindObjectOfType<PlayerCrew>();
            if (career == null) career = Util.Career;
            if (crew == null || career == null) return;
            if (!Traverse.Create(career).Field("beforeFirstPatrol").GetValue<bool>()) return;

            int target = Mathf.RoundToInt(Settings.F("FullCrewSize"));
            int added = 0;
            while (crew.Count < target && added < 10)   // a few per second, so the game is not stalled
            {
                crew.AddRandomRecruit();
                added++;
            }
            if (added > 0) Util.Log("Volle Crew: " + added + " Matrosen dazu, jetzt " + crew.Count);
        }
    }
}
