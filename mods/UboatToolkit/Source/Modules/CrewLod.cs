using System.Collections.Generic;
using HarmonyLib;
using UBOAT.Game.Core.AI;
using UBOAT.Game.Scene.Characters;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // Crew detail levels (against low FPS with a big crew, e.g. 100+ men in port).
    // Every man normally re-thinks every frame: "what is my best job now?" (all his behaviours are rated).
    // For men you cannot see, this thinking now runs only every x frames, staggered so not all think
    // in the same frame. What they are DOING right now (walking, working, sleeping, eating) still runs
    // every frame, so jobs, rest and food take exactly as long as before.
    // Always full speed: men you can see, and everyone during alarm or emergency.
    // Faces/body shape (deformers) of men you cannot see are not recalculated.
    public class CrewLod : ModuleBase
    {
        internal static bool On;
        internal static int Frames = 1;
        internal static bool Busy;   // alarm / emergency
        private float next;

        public override void Tick()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.5f;
            On = Settings.B("CrewLod");
            Frames = Mathf.Max(1, Mathf.RoundToInt(Settings.F("CrewLodFrames")));
            var ship = Util.PlayerShip;
            Busy = ship != null && (ship.Alarmed || ship.Emergency);
        }

        private static readonly Dictionary<CharacterAI, InteractiveCharacter> owner = new Dictionary<CharacterAI, InteractiveCharacter>();

        // true = skip the thinking this frame
        internal static bool Skip(CharacterAI ai)
        {
            if (!On || Frames <= 1 || Busy || ai == null || Time.timeScale <= 0f) return false;
            if (!owner.TryGetValue(ai, out var ch))
            {
                if (owner.Count > 3000) owner.Clear();
                ch = ai.GetComponent<InteractiveCharacter>();
                owner[ai] = ch;
            }
            if (ch == null || ch.IsVisible) return false;
            int id = ai.GetInstanceID();
            if (id < 0) id = -id;
            return (Time.frameCount + id) % Frames != 0;
        }
    }

    [HarmonyPatch(typeof(CharacterAI), "UpdateInactiveBehaviours")]
    internal static class CrewLod_Background
    {
        private static bool Prefix(CharacterAI __instance) => !CrewLod.Skip(__instance);
    }

    [HarmonyPatch(typeof(CharacterAI), "AnalyzeBehaviours")]
    internal static class CrewLod_Analyze
    {
        private static bool Prefix(CharacterAI __instance) => !CrewLod.Skip(__instance);
    }

    [HarmonyPatch(typeof(CharacterAI), "AnalyzeBehavioursFast")]
    internal static class CrewLod_AnalyzeFast
    {
        private static bool Prefix(CharacterAI __instance) => !CrewLod.Skip(__instance);
    }

    [HarmonyPatch(typeof(DWS.Common.Characters.Deformers.DeformerBase), "LateUpdate")]
    internal static class CrewLod_Deformer
    {
        private static readonly AccessTools.FieldRef<DWS.Common.Characters.Deformers.DeformerBase, SkinnedMeshRenderer> smr =
            AccessTools.FieldRefAccess<DWS.Common.Characters.Deformers.DeformerBase, SkinnedMeshRenderer>("skinnedMeshRenderer");

        private static bool Prefix(DWS.Common.Characters.Deformers.DeformerBase __instance)
        {
            if (!CrewLod.On) return true;
            try
            {
                var r = smr(__instance);
                return r == null || r.isVisible;
            }
            catch { return true; }
        }
    }
}
