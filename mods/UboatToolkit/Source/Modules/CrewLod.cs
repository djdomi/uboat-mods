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
        internal static int Frames = 1, IdleFrames = 1;
        internal static bool Busy;   // alarm / emergency
        private float next;

        public override void Tick()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.5f;
            On = Settings.B("CrewLod");
            Frames = Mathf.Max(1, Mathf.RoundToInt(Settings.F("CrewLodFrames")));
            IdleFrames = Mathf.Max(1, Mathf.RoundToInt(Settings.F("CrewLodIdleFrames")));
            var ship = Util.PlayerShip;
            Busy = ship != null && (ship.Alarmed || ship.Emergency);
            if (Time.unscaledTime >= nextLine)
            {
                if (nextLine > 0f && On) Util.Log("Crew-Detailstufen: " + Skipped + " mal Überlegen ausgesetzt, " + OrderKept + " mal nicht ausgesetzt wegen wartendem Befehl, " + Direct + " direkte Aufrufe des Spiels (Befehle) nie ausgesetzt; Werte " + Frames + "/" + IdleFrames);
                nextLine = Time.unscaledTime + 60f; if (Urgent > 0) Util.Log("Crew-Detailstufen: " + Urgent + " mal nicht ausgesetzt, weil ein Pflicht-Posten leer war"); Skipped = 0; OrderKept = 0; Direct = 0; Urgent = 0;
            }
        }

        private static readonly Dictionary<CharacterAI, InteractiveCharacter> owner = new Dictionary<CharacterAI, InteractiveCharacter>();

        // true = skip the thinking this frame
        internal static bool Skip(CharacterAI ai)
        {
            if (!On || Busy || ai == null || Time.timeScale <= 0f) return false;
            // 0.4.13 (DoMe: many idle men, but the helm stays empty): while a station that must be manned is empty,
            // nobody's thinking is paused, so the game's own shift jobs can fill it at once.
            if (Time.unscaledTime < UrgentUntil) { Urgent++; return false; }
            // 0.4.5 (DoMe 2026-11-17: orders must be given twice and still are not carried out): the game calls
            // UpdateAI directly (68 places) right after an order is given or cancelled. Those calls were skipped
            // here too, so the man only noticed his order x frames later. Now only the routine call once per frame
            // may be skipped, and never for a man who has an order (profit 2 and more) that is not running yet.
            if (!FrameCall) { Direct++; return false; }
            if (!owner.TryGetValue(ai, out var ch))
            {
                if (owner.Count > 3000) owner.Clear();
                ch = ai.GetComponent<InteractiveCharacter>();
                owner[ai] = ch;
            }
            if (ch == null) return false;
            int n = Frames;
            if (ch.IsVisible)
            {
                // Visible men only when they are idle (standing around, resting, smoking, chatting, sleeping):
                // they notice a new job a fraction of a second later, nobody else is affected.
                if (IdleFrames <= 1 || !Idle(ai)) return false;
                n = IdleFrames;
            }
            else if (Idle(ai)) n = Mathf.Max(n, IdleFrames);
            // 0.4.13: in time compression x frames are x times more game time -> pause proportionally less
            if (Time.timeScale > 1.5f) n = Mathf.RoundToInt(n / Time.timeScale);
            if (n <= 1) return false;
            int id = ai.GetInstanceID();
            if (id < 0) id = -id;
            if ((Time.frameCount + id) % n == 0) return false;
            if (HasWaitingOrder(ai)) { OrderKept++; return false; }
            Skipped++;
            return true;
        }

        internal static bool FrameCall;
        internal static int Direct, OrderKept, Skipped, Urgent;
        internal static float UrgentUntil;
        private float nextLine;

        private static bool HasWaitingOrder(CharacterAI ai)
        {
            int c = ai.BehavioursCount;
            for (int i = 0; i < c; i++)
            {
                var b = ai.GetBehaviourAt(i);
                if (b != null && b.Profit >= 2f && b.State != AIBehaviourState.Active) return true;
            }
            return false;
        }

        // Idle = something is active, and everything active is free time (idle, rest, smoke, chat, sleep).
        private static bool Idle(CharacterAI ai)
        {
            var act = ai.GetActiveBehavioursDirect();
            if (act == null || act.Count == 0) return false;
            for (int i = 0; i < act.Count; i++)
            {
                var b = act[i];
                if (b == null) return false;
                if (!(b is UBOAT.Game.Core.AI.Behaviours.IdleBehaviour || b is UBOAT.Game.Core.AI.Behaviours.IRelaxBehaviour)) return false;
                if (b.State != AIBehaviourState.Active) return false;
            }
            return true;
        }
    }

    // Marks the routine call once per frame (the only caller that passes true); every other call is a direct one.
    [HarmonyPatch(typeof(CharacterAI), nameof(CharacterAI.UpdateAI))]
    internal static class CrewLod_FrameCall
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(bool ignoreIsUpdatingFlag, out bool __state) { __state = CrewLod.FrameCall; CrewLod.FrameCall = ignoreIsUpdatingFlag; }
        private static void Finalizer(bool __state) { CrewLod.FrameCall = __state; }
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
