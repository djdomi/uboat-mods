using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using DWS.Common.Pooling;
using HarmonyLib;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // Fix "Cannot set the parent of the GameObject 'Default Tooltip(Clone)' while activating or deactivating
    // the parent GameObject 'GUI'": the game re-parents a reused tooltip to the canvas every time it opens,
    // even when it already sits there. Unity refuses that while the GUI is being switched on/off.
    // Now the move is skipped when the tooltip is already under the canvas, and otherwise done one frame later.
    [HarmonyPatch(typeof(UBOAT.Game.UI.FloatingTooltip), "OpenInternal")]
    internal static class UiFixes_TooltipParent
    {
        private static readonly MethodInfo SetParent2 = AccessTools.Method(typeof(Transform), nameof(Transform.SetParent), new[] { typeof(Transform), typeof(bool) });
        private static readonly MethodInfo Ours = AccessTools.Method(typeof(UiFixes_TooltipParent), nameof(Set));

        public static void Set(Transform child, Transform parent, bool keep)
        {
            if (child == null) return;
            if (!Settings.B("FixTooltipParent")) { child.SetParent(parent, keep); return; }
            if (child.parent == parent) return;
            // Must happen right now: the game sizes and positions the tooltip on the next lines. Moving it a frame
            // later left an empty box (DoMe 2026-11-12, visibility panel). Only the redundant move is skipped.
            child.SetParent(parent, keep);
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            foreach (var c in code)
            {
                if ((c.opcode == OpCodes.Callvirt || c.opcode == OpCodes.Call) && c.operand as MethodInfo == SetParent2) { c.opcode = OpCodes.Call; c.operand = Ours; }
                yield return c;
            }
        }
    }

    // Fix 'Pooled instance of prefab "Character/Other/Newspaper Mirror" has enabled animators ...':
    // some props (newspaper stand) go back into the pool with their animator still running. The game notices
    // that only when it reuses them, logs the error and throws the object away. We do the same at the moment
    // it is handed back, without the error, so no broken object ever sits in the pool.
    [HarmonyPatch(typeof(GlobalPool), nameof(GlobalPool.FreeInstance))]
    internal static class UiFixes_PoolAnimators
    {
        private static readonly List<Animator> anims = new List<Animator>();
        private static readonly List<SkinnedMeshRenderer> skins = new List<SkinnedMeshRenderer>();

        private static void Postfix(GlobalPool __instance, GameObject instance)
        {
            if (!Settings.B("FixPoolAnimators") || instance == null) return;
            var po = instance.GetComponent<PooledObject>();
            if (po == null) return;
            bool bad = false;
            instance.GetComponentsInChildren(true, anims);
            foreach (var a in anims) if (a != null && a.enabled) { bad = true; break; }
            if (!bad) { instance.GetComponentsInChildren(true, skins); foreach (var s in skins) if (s != null && s.enabled) { bad = true; break; } }
            anims.Clear(); skins.Clear();
            if (!bad) return;
            var pool = Traverse.Create(__instance).Method("GetPool", new[] { typeof(object) }).GetValue(po.PrefabPath);
            if (pool != null)
            {
                var tp = Traverse.Create(pool);
                if (tp.Property("Count").GetValue<int>() > 0 && (tp.Method("Peek").GetValue() as GameObject) == instance) tp.Method("Pop").GetValue();
            }
            Object.Destroy(instance);
        }
    }
}

namespace UboatToolkit.Modules
{
    // Fix NullReferenceException in FlamesRegistry.OnFlamesDetached / SpawnParticles: when a burning part is shot
    // off a ship (e.g. a gun "Detached"), the burning surface reports to a fire registry whose flame effect is
    // already gone. The game then throws every time. Now: nothing to move -> skip quietly.
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Scene.Effects.FlamesRegistry), nameof(UBOAT.Game.Scene.Effects.FlamesRegistry.OnFlamesDetached))]
    internal static class UiFixes_FlamesDetached
    {
        private static bool Prefix(UBOAT.Game.Scene.Effects.FlamesRegistry __instance, UBOAT.Game.Scene.Effects.FlammableSurface flames)
        {
            if (!Settings.B("FixFlames")) return true;
            return __instance != null && __instance.FlamesEffect != null && flames != null && flames.transform.parent != null;
        }

        private static System.Exception Finalizer(System.Exception __exception) =>
            __exception is System.NullReferenceException && Settings.B("FixFlames") ? null : __exception;
    }

    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Scene.Effects.FlamesRegistry), "SpawnParticles", new System.Type[0])]
    internal static class UiFixes_FlamesSpawn
    {
        private static bool Prefix(UBOAT.Game.Scene.Effects.FlamesRegistry __instance) =>
            !Settings.B("FixFlames") || (__instance != null && __instance.FlamesEffect != null);

        private static System.Exception Finalizer(System.Exception __exception) =>
            __exception is System.NullReferenceException && Settings.B("FixFlames") ? null : __exception;
    }
}
