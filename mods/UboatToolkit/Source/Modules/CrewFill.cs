using System.Linq;
using HarmonyLib;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene.Characters;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // QoL #37: full crew from the start. Before the first patrol, new sailors are recruited
    // the same way the game does it at a new start (generated "German Sailor" added to the career)
    // until the crew roster has the wanted size. Only runs in the real game scene with a boat.
    public class CrewFill : ModuleBase
    {
        private PlayerCareer career;
        private CharacterGenerator generator;
        private int failures;

        public override void Tick()
        {
            if (!Settings.B("FullCrewAtStart") || failures >= 3) return;
            if (Util.PlayerShip == null) return;                 // not in main menu / commander creation
            if (career == null) career = Util.Career;
            if (career == null) return;
            if (!Traverse.Create(career).Field("beforeFirstPatrol").GetValue<bool>()) return;
            if (generator == null) generator = Resources.FindObjectsOfTypeAll<CharacterGenerator>().FirstOrDefault();
            if (generator == null) return;

            int target = Mathf.RoundToInt(Settings.F("FullCrewSize"));
            int have = career.Characters.Count(c => c != null && (c.CharacterType == PlayableCharacterData.Type.Sailor || c.CharacterType == PlayableCharacterData.Type.Officer || c.CharacterType == PlayableCharacterData.Type.Skipper));
            int added = 0;
            try
            {
                while (have + added < target && added < 5)   // a few per second, so the game is not stalled
                {
                    var data = generator.GenerateCharacterData("German Sailor", PlayableCharacterData.Type.Sailor);
                    if (data == null) { failures++; break; }
                    career.AddCharacter(data);
                    added++;
                }
            }
            catch (System.Exception e) { failures++; Util.Log("Volle Crew: Fehler beim Anwerben (" + failures + "/3): " + e.Message); }
            if (added > 0) Util.Log("Volle Crew: " + added + " Matrosen angeworben, jetzt " + (have + added) + " von " + target);
        }
    }
}
