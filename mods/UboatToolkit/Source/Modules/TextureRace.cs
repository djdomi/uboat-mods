using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UBOAT.Game.Core.Serialization;

namespace UboatToolkit.Modules
{
    // Crash fix: five game methods destroy textures the graphics thread may still use. Those destroys
    // are queued (thread-safe) and done 10 s / 120 frames later on the main thread.
    [NonSerializedInGameState]
    public sealed class LateDestroy : MonoBehaviour
    {
        private struct Pending { public UnityEngine.Object Obj; public float Due; public int DueFrame; }
        private static readonly List<UnityEngine.Object> incoming = new List<UnityEngine.Object>();
        private static readonly object gate = new object();
        private static readonly List<Pending> pending = new List<Pending>();
        private static LateDestroy host;
        private static int deferred, destroyed, logged;
        private static float nextSummary;

        public static void Queue(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (host == null || !Settings.B("FixTextureRace") || !(obj is Texture)) { UnityEngine.Object.Destroy(obj); return; }
            lock (gate) incoming.Add(obj);
        }

        public static void QueueRef(ref Texture2D obj, bool immediate) { var t = obj; obj = null; Queue(t); }

        // Called on the main thread once the patches are in.
        public static void EnsureHost()
        {
            if (host != null) return;
            var go = new GameObject("Toolkit Late Destroy");
            DontDestroyOnLoad(go);
            host = go.AddComponent<LateDestroy>();
        }

        private void Update()
        {
            try
            {
                float now = Time.unscaledTime;
                int frame = Time.frameCount;
                lock (gate)
                {
                    foreach (var o in incoming) pending.Add(new Pending { Obj = o, Due = now + 10f, DueFrame = frame + 120 });
                    deferred += incoming.Count;
                    incoming.Clear();
                }
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    if (now < pending[i].Due || frame < pending[i].DueFrame) continue;
                    var o = pending[i].Obj;
                    pending.RemoveAt(i);
                    if (o != null) Destroy(o);
                    destroyed++;
                }
                if (deferred > 0 && logged == 0) { logged = deferred; nextSummary = now + 60f; Util.Log("Textur-Fix: erstes spätes Löschen - Fix ist aktiv."); }
                else if (deferred != logged && now >= nextSummary)
                {
                    logged = deferred; nextSummary = now + 60f;
                    if (Settings.B("TextureRaceLog")) Util.Log("Textur-Fix: " + deferred + " Texturen spät gelöscht (" + destroyed + " erledigt, " + pending.Count + " wartend).");
                }
            }
            catch (Exception e) { Util.Log("Textur-Fix: Durchlauf fehlgeschlagen: " + e.Message); }
        }
    }

    [HarmonyPatch]
    internal static class TextureRace_Patch
    {
        private static readonly MethodInfo Destroy1 = typeof(UnityEngine.Object).GetMethod("Destroy", new[] { typeof(UnityEngine.Object) });
        private static readonly MethodInfo QueueM = AccessTools.Method(typeof(LateDestroy), "Queue");
        private static readonly MethodInfo QueueRefM = AccessTools.Method(typeof(LateDestroy), "QueueRef");
        private static readonly Dictionary<string, int> Expected = new Dictionary<string, int>
        {
            { "CleanRecentTask", 3 }, { "ConvertTexture", 1 }, { "StopView", 1 }, { "UpdateView", 1 }, { "AssignTexturesToMaterials", 3 }
        };
        private static int rewritten;

        private static IEnumerable<MethodBase> TargetMethods()
        {
            var sites = new[] { "UBOAT.Game.Scene.Effects.CharacterBatcher:CleanRecentTask", "UBOAT.Game.Scene.Effects.LayeredMaterialComposer:ConvertTexture",
                                "UBOAT.Game.Scene.Items.Periscope:StopView", "UBOAT.Game.Scene.Items.Periscope:UpdateView", "UBOAT.Game.Scene.Effects.CharacterBatcher:AssignTexturesToMaterials" };
            foreach (var s in sites)
            {
                var p = s.Split(':');
                var t = AccessTools.TypeByName(p[0]);
                var m = t != null ? AccessTools.Method(t, p[1]) : null;
                if (m != null) yield return m;
                else Util.Log("Textur-Fix: " + s + " nicht gefunden.");
            }
        }

        private static bool IsDestroyIfExists(CodeInstruction c)
        {
            if (c.opcode != OpCodes.Call && c.opcode != OpCodes.Callvirt) return false;
            var m = c.operand as MethodInfo;
            if (m == null || m.Name != "DestroyIfExists" || !m.IsGenericMethod) return false;
            var a = m.GetGenericArguments();
            return a.Length == 1 && a[0] == typeof(Texture2D);
        }

        private static bool IsDestroy(CodeInstruction c) => (c.opcode == OpCodes.Call || c.opcode == OpCodes.Callvirt) && c.operand as MethodInfo == Destroy1;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code, MethodBase original)
        {
            var list = new List<CodeInstruction>(code);
            bool assign = original.Name == "AssignTexturesToMaterials";
            int found = 0;
            foreach (var c in list) if (assign ? IsDestroyIfExists(c) : IsDestroy(c)) found++;
            if (!Expected.TryGetValue(original.Name, out int want) || found != want || QueueM == null || QueueRefM == null)
            {
                Util.Log("Textur-Fix: " + original.DeclaringType.Name + "." + original.Name + " erwartet " + want + ", gefunden " + found + " - bleibt unverändert.");
                return list;
            }
            foreach (var c in list) if (assign ? IsDestroyIfExists(c) : IsDestroy(c)) c.operand = assign ? QueueRefM : QueueM;
            rewritten++;
            return list;
        }

        private static void Cleanup() { if (rewritten > 0) Util.Safe("TextureRace.Host", LateDestroy.EnsureHost); }
    }
}
