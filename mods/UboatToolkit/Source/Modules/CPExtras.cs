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

    // ---------- Watch crew wear rain gear in strong wind / spray too (Beaufort 6+) ----------
    [HarmonyPatch(typeof(EquipRainCoats), "DoUpdate")]
    internal static class Extras_RainGearPatch
    {
        private static bool Prefix(ref float __result, Weather ___weather, PlayerCrew ___playerCrew, UBOAT.Game.Scene.Entities.PlayerShip ___playerShip, MainCamera ___mainCamera)
        {
            if (!Settings.B("RainGearWind") || ___weather == null || ___playerShip == null || ___playerCrew == null) return true;
            if ((float)___weather.Rain >= 0.05f || ___weather.SeaCondition < 6f || ___playerShip.IsSheltered) return true;   // game handles rain and calm
            if (___mainCamera != null && ___mainCamera.CurrentMode == CameraMode.Map) { __result = 0f; return false; }
            for (int i = ___playerCrew.Count - 1; i >= 0; i--)
            {
                var c = ___playerCrew[i];
                if (c == null || !c.isActiveAndEnabled || c.Data == null || c.Data.CharacterType == PlayableCharacterData.Type.Captive || c.IsImpersonated) continue;
                var layer = c.Navigator.Layer;
                bool outside = layer == NavMeshLayer.UboatExterior || layer == NavMeshLayer.World;
                if (outside && c.IsSlotFree("Rain Coat"))
                {
                    c.LoadWearableIfNotAlreadyLoadedAsync("Rain Coat", UnityEngine.Random.value < 0.5f ? "Character/Other/Rain Coat Black" : "Character/Other/Rain Coat Gray");
                    if (!c.IsSkipper) c.LoadWearableIfNotAlreadyLoadedAsync("Rain Coat Cap", "Character/Other/Rain Coat Cap");
                }
                else if (!outside)
                {
                    var w = c.GetWearableAt("Rain Coat"); if (w != null) c.RemoveWearable(w);
                    var w2 = c.GetWearableAt("Rain Coat Cap"); if (w2 != null) c.RemoveWearable(w2);
                }
            }
            __result = 5f;
            return false;
        }
    }

    // ---------- Officers: new or promoted officers get a 6-hour watch, half on duty, half resting ----------
    internal static class OfficerWatch
    {
        internal static void Assign(PlayerCrew crew, PlayableCharacter c)
        {
            if (!Settings.B("OfficerWatches") || crew == null || c == null || !c.IsOfficer || c.IsSkipper || c.Schedule == null) return;
            int[] count = new int[2];
            foreach (var o in crew.Officers ?? new PlayableCharacter[0])
            {
                if (o == null || o == c || o.Schedule == null) continue;
                int w = WatchOf(o.Schedule);
                if (w >= 0) count[w]++;
            }
            int mine = count[1] < count[0] ? 1 : 0;
            for (int h = 0; h < 24; h++) c.Schedule[h] = (h / 6) % 2 == mine ? Schedule.Behaviour.Work : Schedule.Behaviour.Sleep;
        }
        private static int WatchOf(Schedule s)
        {
            for (int w = 0; w < 2; w++)
            {
                bool ok = true;
                for (int h = 0; h < 24 && ok; h++) ok = s[h] == ((h / 6) % 2 == w ? Schedule.Behaviour.Work : Schedule.Behaviour.Sleep);
                if (ok) return w;
            }
            return -1;
        }
        internal static bool Loading()
        {
            try { var sm = ScriptableObjectSingleton.LoadSingleton<SavesManager>(); return sm != null && sm.IsLoading; } catch { return false; }
        }
    }
    [HarmonyPatch(typeof(PlayerCrew), "AddCharacter", new[] { typeof(PlayableCharacterData) })]
    internal static class Extras_OfficerNewPatch
    {
        private static void Postfix(PlayerCrew __instance, PlayableCharacter __result)
        {
            if (OfficerWatch.Loading()) return;
            Util.Safe("Extras.Watch", () => OfficerWatch.Assign(__instance, __result));
        }
    }
    [HarmonyPatch(typeof(PlayerCrew), "Character_Promoted")]
    internal static class Extras_OfficerPromotedPatch
    {
        private static void Postfix(PlayerCrew __instance, PlayableCharacterData characterData)
        {
            if (OfficerWatch.Loading()) return;
            Util.Safe("Extras.Watch", () => OfficerWatch.Assign(__instance, __instance.GetCharacter(characterData)));
        }
    }

    // ---------- First person: running through hatches is faster (hold Shift) ----------
    [HarmonyPatch(typeof(FPPCameraController), "ParseInputsFPPMode")]
    internal static class Extras_HatchPatch
    {
        private static void Postfix(NavMeshNavigator ___characterNavigator, float ___offMeshLinkTravelDirection)
        {
            if (!Settings.B("FastHatch") || !Keys.Shift) return;
            var n = ___characterNavigator;
            if (n == null || !n.IsOnOffMeshLink || !(Traverse.Create(n).Field("currentLinkEventHandler").GetValue() is NavMeshHatch)) return;
            var a = n.Animator;
            if (a == null || (___offMeshLinkTravelDirection < 0f && !a.IsInTransition(0))) return;
            float vanilla = 0.0001f + Mathf.Max(0f, ___offMeshLinkTravelDirection) * 1.25f;
            float want = Mathf.Min(vanilla * 1.6f, 3f);
            if (want > vanilla) a.speed = want;
        }
    }

    // ---------- Mute the "lights" voice callouts ----------
    [HarmonyPatch]
    internal static class Extras_QuietPatch
    {
        private static System.Reflection.MethodBase TargetMethod() =>
            AccessTools.GetDeclaredMethods(typeof(InteractiveCharacter)).FirstOrDefault(m => m.Name == "SayNow" && m.GetParameters().Length == 6 && m.GetParameters()[0].ParameterType == typeof(string));
        private static bool Prefix(string statementId) =>
            !(Settings.B("MuteLightCallouts") && (statementId == "Voice 158" || statementId == "Voice 159" || statementId == "Voice 160"));
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

namespace UboatToolkit.Modules
{
    // Periscope/UZO/binocular reticle light: on/off with a key while looking through, plus brightness.
    public class Reticle : ModuleBase
    {
        internal static bool Lit = true;
        public override void Frame()
        {
            if (!Keys.Down("KeyReticleLight")) return;
            var cam = UnityEngine.Object.FindObjectOfType<UBOAT.Game.Scene.Camera.MainCamera>();
            if (ScopeMemory.Ctrl(cam) == null) return;
            Lit = !Lit;
            ToolkitRunner.ShowNotice(Lit ? "Fadenkreuz-Licht an" : "Fadenkreuz-Licht aus", 2f);
        }
    }
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Scene.Items.Periscope), "ReticleIlluminationColor", HarmonyLib.MethodType.Getter)]
    internal static class Extras_ReticlePatch
    {
        [HarmonyLib.HarmonyPriority(HarmonyLib.Priority.Last)]
        private static void Postfix(ref UnityEngine.Color __result)
        {
            float m = Settings.F("ReticleBright");
            if (!Reticle.Lit) m = 0f;
            if (UnityEngine.Mathf.Approximately(m, 1f)) return;
            __result = new UnityEngine.Color(__result.r * m, __result.g * m, __result.b * m, __result.a);
        }
    }
}
