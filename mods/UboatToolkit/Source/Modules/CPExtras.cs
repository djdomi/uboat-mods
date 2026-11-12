using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Threading;
using HarmonyLib;
using UnityEngine;
using UBOAT.Game.Core.Data;
using DWS.Common.InjectionFramework;
using UBOAT.Game.Core.Serialization;
using UBOAT.Game.Scene;
using UBOAT.Game.Scene.Camera;
using UBOAT.Game.Scene.Camera.ExternalControllers;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Characters.Navigation;
using UBOAT.Game.Scene.Entities.NavMesh;
using UBOAT.Game.Scene.Environment;
using UBOAT.Game.Scene.Tasks;
using UBOAT.Game.UI.Periscope;

namespace UboatToolkit.Modules
{
    // ---------- Periscope remembers view mode and zoom per periscope type ----------
    internal static class ScopeMemory
    {
        internal static readonly Dictionary<string, int> View = new Dictionary<string, int>(), Zoom = new Dictionary<string, int>();
        internal static PeriscopeController Ctrl(MainCamera cam) => cam?.GenericController?.ExternalController as PeriscopeController;
        internal static bool restoring;
    }
    [HarmonyPatch(typeof(PeriscopeUI), "OnViewModeSelected")]
    internal static class Extras_ScopeViewPatch
    {
        private static void Postfix(int index, MainCamera ___mainCamera)
        {
            var p = ScopeMemory.Ctrl(___mainCamera)?.Periscope;
            if (p != null && !ScopeMemory.restoring) ScopeMemory.View[p.TypeName] = index;
        }
    }
    [HarmonyPatch(typeof(PeriscopeUI), "OnZoomSelected")]
    internal static class Extras_ScopeZoomPatch
    {
        private static void Postfix(int index, MainCamera ___mainCamera)
        {
            var p = ScopeMemory.Ctrl(___mainCamera)?.Periscope;
            if (p != null && !ScopeMemory.restoring) ScopeMemory.Zoom[p.TypeName] = index;
        }
    }
    [HarmonyPatch(typeof(PeriscopeUI), "OnEnable")]
    internal static class Extras_ScopeRestorePatch
    {
        private static void Show(Component dd, int i)
        {
            if (dd == null || i < 0) return;
            var t = Traverse.Create(dd);
            if (t.Field("m_Value").GetValue<int>() == i) return;
            t.Field("m_Value").SetValue(i);
            t.Method("Refresh").GetValue();
        }
        private static void Postfix(PeriscopeUI __instance, MainCamera ___mainCamera, Component ___viewModeDropdown, Component ___zoomDropdown)
        {
            if (!Settings.B("ScopeRemember")) return;
            Util.Safe("Extras.Scope", () =>
            {
                var c = ScopeMemory.Ctrl(___mainCamera);
                var p = c?.Periscope;
                if (p == null) return;
                ScopeMemory.restoring = true;
                try
                {
                    if (!p.IsBinocular && ScopeMemory.View.TryGetValue(p.TypeName, out int v))
                    {
                        AccessTools.Method(typeof(PeriscopeUI), "OnViewModeSelected").Invoke(__instance, new object[] { v });
                        Show(___viewModeDropdown, v);
                    }
                    int n = p.FovPresets?.Length ?? 0;
                    if (n > 0 && ScopeMemory.Zoom.TryGetValue(p.TypeName, out int z))
                    {
                        z = Mathf.Clamp(z, 0, n - 1);
                        c.FovIndex = z;
                        Show(___zoomDropdown, z);
                    }
                }
                finally { ScopeMemory.restoring = false; }
            });
        }
    }

    // ---------- Officers: new or promoted officers get a watch (6 h or 3 h), split evenly in two watches ----------
    // Only officers WITHOUT a schedule yet (all hours "any") are touched; a schedule you set stays.
    internal static class OfficerWatch
    {
        private static readonly System.Reflection.FieldInfo SlotsF = AccessTools.Field(typeof(Schedule), "behaviourByHour");
        private static readonly System.Reflection.FieldInfo ClockF = AccessTools.Field(typeof(Schedule), "gameTime");
        private static readonly System.Reflection.MethodInfo RefreshM = AccessTools.Method(typeof(Schedule), "GameTimeOnHourChanged");
        internal static PlayerCrew Crew;
        private static int lastHourKey = -1;
        private static System.Reflection.PropertyInfo nowP;

        private static int Hours => Mathf.RoundToInt(Settings.F("OfficerWatchHours")) == 3 ? 3 : 6;
        private static byte[] Pattern(int hours, int watch)
        {
            var p = new byte[24];
            for (int h = 0; h < 24; h++) p[h] = (byte)(((h / hours + watch) % 2 == 0) ? Schedule.Behaviour.Work : Schedule.Behaviour.Sleep);
            return p;
        }
        private static byte[] Read(Schedule s) { var b = new byte[24]; for (int h = 0; h < 24; h++) b[h] = (byte)s[h]; return b; }
        private static bool Same(byte[] a, byte[] b) { for (int i = 0; i < 24; i++) if (a[i] != b[i]) return false; return true; }
        private static bool AllAny(byte[] b) { foreach (var x in b) if (x != (byte)Schedule.Behaviour.Any) return false; return true; }

        internal static bool AlreadyAboard(PlayerCrew crew, PlayableCharacterData data)
        {
            if (crew == null || data == null) return true;
            for (int i = 0; i < crew.Count; i++) if (crew[i] != null && crew[i].Data == data) return true;
            return false;
        }

        internal static void Assign(PlayerCrew crew, PlayableCharacter c, string why)
        {
            if (!Settings.B("OfficerWatches") || crew == null || c == null || !c.IsOfficer || c.Schedule == null || SlotsF == null) return;
            var s = c.Schedule;
            if (!AllAny(Read(s))) { Util.Log("Wachen: " + c.Name + " (" + why + ") hat schon einen Plan - bleibt so"); return; }
            int hours = Hours;
            byte[] w0 = Pattern(hours, 0), w1 = Pattern(hours, 1);
            int n0 = 0, n1 = 0;
            foreach (var o in crew.Officers ?? new PlayableCharacter[0])
            {
                if (o == null || ReferenceEquals(o, c) || o.Schedule == null) continue;
                var b = Read(o.Schedule);
                if (Same(b, w0)) n0++; else if (Same(b, w1)) n1++;
            }
            int watch = n0 <= n1 ? 0 : 1;
            var pattern = watch == 0 ? w0 : w1;
            var slots = SlotsF.GetValue(s) as Schedule.Behaviour[];
            if (slots == null || slots.Length < 24) return;
            for (int h = 0; h < 24; h++) slots[h] = (Schedule.Behaviour)pattern[h];
            bool now = ClockF != null && ClockF.GetValue(s) != null && RefreshM != null;
            if (now) RefreshM.Invoke(s, null);
            Util.Log("Wachen: " + c.Name + " (" + why + "): " + hours + "-h-Wachen, Wache " + (watch == 0 ? "A" : "B") + " im Dienst " + OnDuty(pattern) + (now ? "" : " (ab nächster Stunde)"));
        }

        private static string OnDuty(byte[] p)
        {
            var sb = new System.Text.StringBuilder();
            int start = -1;
            for (int h = 0; h <= 24; h++)
            {
                bool on = h < 24 && p[h] == (byte)Schedule.Behaviour.Work;
                if (on && start < 0) start = h;
                if (!on && start >= 0) { if (sb.Length > 0) sb.Append(", "); sb.Append(start.ToString("00")).Append('-').Append(h.ToString("00")); start = -1; }
            }
            return sb.ToString();
        }

        // Optional hourly log of the officers (energy and duty), switch in the Tools tab.
        internal static void LogHour(Schedule s)
        {
            if (!Settings.B("OfficerWatchLog") || Crew == null || ClockF == null) return;
            object clock = ClockF.GetValue(s);
            if (clock == null) return;
            if (nowP == null) nowP = AccessTools.Property(clock.GetType(), "CurrentDateTime");
            if (nowP == null) return;
            var now = (DateTime)nowP.GetValue(clock, null);
            int key = now.DayOfYear * 24 + now.Hour;
            if (key == lastHourKey) return;
            lastHourKey = key;
            var sb = new System.Text.StringBuilder("Wachen ").Append(now.Hour.ToString("00")).Append(" Uhr:");
            foreach (var o in Crew.Officers ?? new PlayableCharacter[0])
            {
                if (o == null) continue;
                sb.Append(' ').Append(o.Name).Append(' ').Append(o.Energy.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                if (o.Schedule != null) sb.Append(' ').Append(o.Schedule[now.Hour]);
                sb.Append(';');
            }
            Util.Log(sb.ToString());
        }

        internal static bool Loading()
        {
            try { var sm = ScriptableObjectSingleton.LoadSingleton<SavesManager>(); return sm != null && sm.IsLoading; } catch { return false; }
        }
    }
    [HarmonyPatch(typeof(PlayerCrew), "AddCharacter", new[] { typeof(PlayableCharacterData) })]
    internal static class Extras_OfficerNewPatch
    {
        private static void Prefix(PlayerCrew __instance, PlayableCharacterData characterData, out bool __state)
        {
            __state = false;
            try { __state = Settings.B("OfficerWatches") && !OfficerWatch.Loading() && !OfficerWatch.AlreadyAboard(__instance, characterData); } catch { }
        }
        private static void Postfix(PlayerCrew __instance, PlayableCharacter __result, bool __state)
        {
            if (__state) Util.Safe("Extras.Watch", () => OfficerWatch.Assign(__instance, __result, "neu an Bord"));
        }
    }
    [HarmonyPatch(typeof(PlayerCrew), "Character_Promoted")]
    internal static class Extras_OfficerPromotedPatch
    {
        private static void Postfix(PlayerCrew __instance, PlayableCharacterData characterData)
        {
            if (!Settings.B("OfficerWatches") || characterData == null || OfficerWatch.Loading()) return;
            Util.Safe("Extras.Watch", () =>
            {
                for (int i = 0; i < __instance.Count; i++)
                    if (__instance[i] != null && __instance[i].Data == characterData) { OfficerWatch.Assign(__instance, __instance[i], "befördert"); return; }
            });
        }
    }
    [HarmonyPatch(typeof(PlayerCrew), "AddCharacter", new[] { typeof(PlayableCharacter) })]
    internal static class Extras_OfficerCrewPatch
    {
        private static void Postfix(PlayerCrew __instance) { OfficerWatch.Crew = __instance; }
    }
    [HarmonyPatch(typeof(Schedule), "GameTimeOnHourChanged")]
    internal static class Extras_OfficerHourPatch
    {
        private static void Postfix(Schedule __instance) { if (Settings.B("OfficerWatches")) Util.Safe("Extras.WatchLog", () => OfficerWatch.LogHour(__instance)); }
    }

    // ---------- Keep the chosen crew voice language (game resets it after some updates) ----------
    [HarmonyPatch(typeof(UserSettings), "DetermineDefaultVoice")]
    internal static class Extras_VoicePatch
    {
        private static bool Prefix(ref bool ___applyDefaultVoiceLanguage)
        {
            if (!Settings.B("KeepVoiceLanguage")) return true;
            ___applyDefaultVoiceLanguage = false;
            return false;
        }
    }

    // ---------- Stutter fix: main thread no longer waits for a running background texture job ----------
    [HarmonyPatch]
    internal static class Extras_AbortStallPatch
    {
        private static System.Reflection.FieldInfo current;
        private static bool Prepare()
        {
            var t = AccessTools.TypeByName("DWS.Common.Tasks.TaskExecutor");
            current = t != null ? AccessTools.Field(t, "currentRunnable") : null;
            return current != null && TargetMethod() != null;
        }
        private static System.Reflection.MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("DWS.Common.Tasks.TaskExecutor");
            var r = AccessTools.TypeByName("DWS.Common.Tasks.IRunnable");
            return t == null || r == null ? null : AccessTools.Method(t, "Abort", new[] { r });
        }
        private static bool Prefix(object __instance, object runnable, out object __state)
        {
            __state = null;
            if (runnable == null || ReferenceEquals(current.GetValue(__instance), runnable)) return true;
            if (Monitor.TryEnter(runnable)) { __state = runnable; return true; }
            return !Settings.B("FixAbortStall");   // busy in the background: skip instead of freezing the game
        }
        private static Exception Finalizer(Exception __exception, object __state)
        {
            if (__state != null) try { Monitor.Exit(__state); } catch { }
            return __exception;
        }
    }

    // ---------- Crash fix: textures are destroyed a little later, after the graphics thread is done with them ----------
    internal class LateDestroy : MonoBehaviour
    {
        private struct Item { public UnityEngine.Object O; public float T; public int F; }
        private static readonly List<Item> list = new List<Item>();
        private static LateDestroy host;

        public static void Queue(UnityEngine.Object o)
        {
            if (o == null) return;
            if (!(o is Texture) || !Settings.B("FixTextureRace")) { UnityEngine.Object.Destroy(o); return; }
            if (host == null) { var go = new GameObject("Toolkit Late Destroy"); DontDestroyOnLoad(go); host = go.AddComponent<LateDestroy>(); }
            list.Add(new Item { O = o, T = Time.realtimeSinceStartup + 10f, F = Time.frameCount + 120 });
        }
        public static void QueueRef(ref Texture2D o, bool immediate) { var t = o; o = null; Queue(t); }

        private void Update()
        {
            float now = Time.realtimeSinceStartup; int f = Time.frameCount;
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i].T <= now && list[i].F <= f) { if (list[i].O != null) Destroy(list[i].O); list.RemoveAt(i); }
        }
    }
    [HarmonyPatch]
    internal static class Extras_TextureRacePatch
    {
        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            var sites = new[] { "UBOAT.Game.Scene.Effects.CharacterBatcher:CleanRecentTask", "UBOAT.Game.Scene.Effects.LayeredMaterialComposer:ConvertTexture",
                                "UBOAT.Game.Scene.Items.Periscope:StopView", "UBOAT.Game.Scene.Items.Periscope:UpdateView", "UBOAT.Game.Scene.Effects.CharacterBatcher:AssignTexturesToMaterials" };
            foreach (var s in sites)
            {
                var p = s.Split(':');
                var t = AccessTools.TypeByName(p[0]);
                var m = t != null ? AccessTools.Method(t, p[1]) : null;
                if (m != null) yield return m;
            }
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            var destroy = typeof(UnityEngine.Object).GetMethod("Destroy", new[] { typeof(UnityEngine.Object) });
            foreach (var c in code)
            {
                if (c.opcode == OpCodes.Call && c.operand is System.Reflection.MethodInfo m)
                {
                    if (m == destroy) c.operand = AccessTools.Method(typeof(LateDestroy), "Queue");
                    else if (m.Name == "DestroyIfExists" && m.IsGenericMethod && m.GetGenericArguments()[0] == typeof(Texture2D) && m.GetParameters().Length == 2)
                        c.operand = AccessTools.Method(typeof(LateDestroy), "QueueRef");
                }
                yield return c;
            }
        }
    }
}
