using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // Fix: when a crew/port-worker figure fades in while its parent object (e.g. "Port Workers Controller")
    // is being switched on or off, the game moves its "Fade Effect Renderer" into that parent at the wrong
    // moment and Unity refuses with an error. The move is now done one frame later, when it is allowed.
    // If the parent is gone by then, the orphaned renderer is removed so nothing is left behind.
    public class FadeReparent : ModuleBase
    {
        private struct Job { public Transform Child, Parent; public bool Keep; public bool HadParent; }
        private static readonly List<Job> jobs = new List<Job>();
        private static int fixedCount;

        public static void Set(Transform child, Transform parent, bool keep)
        {
            if (child == null) return;
            if (!Settings.B("FixFadeReparent")) { child.SetParent(parent, keep); return; }
            jobs.Add(new Job { Child = child, Parent = parent, Keep = keep, HadParent = parent != null });
        }

        public override void Frame()
        {
            if (jobs.Count == 0) return;
            var list = jobs.ToArray(); jobs.Clear();
            foreach (var j in list)
            {
                if (j.Child == null) continue;
                try
                {
                    if (j.HadParent && j.Parent == null) { Object.Destroy(j.Child.gameObject); continue; }
                    j.Child.SetParent(j.Parent, j.Keep);
                    fixedCount++;
                }
                catch (System.Exception e) { Util.Log("Fade-Fix: " + e.Message); }
            }
        }

        public static int Fixed => fixedCount;
    }

    [HarmonyPatch(typeof(UBOAT.Game.Scene.Characters.InteractiveCharacter), "FadeInOut")]
    internal static class FadeReparent_Patch
    {
        private static readonly MethodInfo SetParent2 = AccessTools.Method(typeof(Transform), nameof(Transform.SetParent), new[] { typeof(Transform), typeof(bool) });
        private static readonly MethodInfo Ours = AccessTools.Method(typeof(FadeReparent), nameof(FadeReparent.Set));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            var list = new List<CodeInstruction>(code);
            int n = 0;
            foreach (var c in list)
                if ((c.opcode == OpCodes.Callvirt || c.opcode == OpCodes.Call) && c.operand as MethodInfo == SetParent2)
                { c.opcode = OpCodes.Call; c.operand = Ours; n++; }
            if (n != 1) Util.Log("Fade-Fix: erwartet 1 Stelle, gefunden " + n + (n == 0 ? " - bleibt unverändert." : " - alle umgeleitet."));
            return list;
        }
    }
}
