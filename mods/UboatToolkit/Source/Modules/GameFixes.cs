using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using DWS.Common.InjectionFramework;
using UBOAT.Game.Core.Time;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Sandbox.Missions;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Characters.Actions;
using UBOAT.Game.Scene.Effects;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Stories;
using UBOAT.Game.Scene.Tasks;
using UBOAT.Game.UI;

namespace UboatToolkit.Modules
{
    // Small game fixes, own code. Each part has its own switch.
    public class GameFixes : ModuleBase
    {
        [Inject] internal static StoryFragmentsDatabase fragments;
        private PlayerShip lastShip;

        private bool warned;
        private float nextPortCheck;

        public override void Tick()
        {
            var ship = Util.PlayerShip;
            if (ship != null && Settings.B("FixSkipperDead") && Time.unscaledTime > nextPortCheck)
            {
                nextPortCheck = Time.unscaledTime + 5f;
                Util.Safe("Fixes.PortWarn", () => PortWarning(ship));
            }
            Util.Safe("Fixes.Officers", ExtraOfficers);
            if (ship == null || ship == lastShip) return;
            lastShip = ship;   // new game scene / save loaded
            Util.Safe("Fixes.Inject", () => InjectionFramework.Instance.InjectIntoAssembly(typeof(GameFixes).Assembly));
            if (Settings.B("FixPortDialogue")) Util.Safe("Fixes.Tags", () => CleanTags(ship));
        }

        // Stuck conversation tags make port officers show no options. Remove them once per load.
        private static readonly string[] officerTrees = { "Driver", "Personnel Officer", "Harbor Captain", "Flotilla Commander", "Operations Officer" };
        private static void CleanTags(PlayerShip ship)
        {
            var vars = ship.SandboxEntity?.Variables;
            if (vars == null || vars.Count == 0) return;
            var names = new HashSet<string> { "Activate", "FirstDialogue" };
            var list = fragments == null ? null : Traverse.Create(fragments).Field("storyFragments").GetValue<List<StoryFragment>>();
            if (list != null)
                foreach (var f in list)
                    if (f.Path != null && officerTrees.Any(p => f.Path.StartsWith(p)) && f.Id.Length > f.Path.Length)
                        names.Add(f.Id.Substring(f.Path.Length));
            int n = names.Count(vars.Remove);
            if (n > 0) Util.Log("Hafen-Dialog: " + n + " haengende Gespraechs-Markierungen entfernt");
        }

        // Before reaching a port: warn if the commander is dead or wounded (port talks need him).
        private void PortWarning(PlayerShip ship)
        {
            bool nearPort = !ship.Docked && Util.Find<UBOAT.Game.Scene.Dock.DockArea>(5f)
                .Any(d => d != null && (d.transform.position - ship.transform.position).sqrMagnitude < 3000f * 3000f);
            if (!nearPort) { warned = false; return; }
            if (warned || SkipperOk()) return;
            warned = true;
            string text = "Achtung: Kommandant tot oder verwundet! Vor dem Anlegen heilen (F10 > Cheat > Alle heilen), sonst gehen die Hafen-Gespr\u00e4che nicht.";
            var bar = UnityEngine.Object.FindObjectOfType<NotificationBarUI>();
            if (bar != null) bar.OpenNow("Icons/Notification Bar/Notification - 40 - Danger", text);
            Util.Log(text);
        }

        // More officers allowed on board (runtime only, nothing is written into the save).
        private Modifier officerMod;
        private object officerCareer;
        private void ExtraOfficers()
        {
            var c = Util.Career;
            float want = Settings.F("ExtraOfficers");
            if (c == null) return;
            if (!ReferenceEquals(c, officerCareer)) { officerMod = null; officerCareer = c; }
            if (want <= 0f) { if (officerMod != null) { officerMod.Remove(); officerMod = null; } return; }
            if (officerMod == null) officerMod = c.OfficersOnBoardLimit.AddDeltaModifier("Toolkit Officers");
            if (officerMod.Value != want) officerMod.Value = want;
        }

        internal static bool SkipperOk()
        {
            var crew = Util.Find<PlayerCrew>(30f).FirstOrDefault();
            var s = crew != null ? crew.Skipper : null;
            return crew == null || (s != null && !s.Injury);
        }
    }

    // --- Berlin time: all clocks show German time (CET), no time zone messages.
    [HarmonyPatch(typeof(TimeZoneTracker), "LocalOffsetFromCET", MethodType.Getter)]
    internal static class Fix_BerlinOffset
    {
        private static bool Prefix(ref int __result) { if (!Settings.B("BerlinTime")) return true; __result = 0; return false; }
    }
    [HarmonyPatch(typeof(TimeZoneTracker), "UpdateTimeZone")]
    internal static class Fix_BerlinZone
    {
        private static bool Prefix(ref int ___currentUtcOffsetHours)
        {
            if (!Settings.B("BerlinTime")) return true;
            ___currentUtcOffsetHours = 1;
            return false;
        }
    }

    // --- Commander dead or wounded: no port officer talks and no story dialogues that need him.
    [HarmonyPatch(typeof(PortActivitiesUI), "StartAction")]
    internal static class Fix_SkipperPortTalk
    {
        private static bool Prefix(string destinationOfficerTag)
        {
            if (!Settings.B("FixSkipperDead") || destinationOfficerTag == null || !destinationOfficerTag.StartsWith("Dialogue:")) return true;
            return GameFixes.SkipperOk();
        }
    }
    [HarmonyPatch(typeof(StoryDialogueAction), "GetState")]
    internal static class Fix_SkipperStory
    {
        private static void Postfix(ref ActionStateData __result)
        {
            if (Settings.B("FixSkipperDead") && !GameFixes.SkipperOk()) __result = new ActionStateData(ActionState.Hidden);
        }
    }

    // --- Port dialogue: a click while the dialogue closes must not break it.
    [HarmonyPatch(typeof(GenericStory), "ContinueStory")]
    internal static class Fix_StoryContinue
    {
        private static bool Prefix(GenericStory __instance, ref bool __result, object ___scriptingEnvironment)
        {
            if (!Settings.B("FixPortDialogue") || __instance.Running || ___scriptingEnvironment != null) return true;
            __result = false;
            return false;
        }
    }
    [HarmonyPatch(typeof(GenericStory), "AddTempTag")]
    internal static class Fix_StoryTag
    {
        private static bool Prefix(GenericStory __instance, object ___scriptingEnvironment)
            => !Settings.B("FixPortDialogue") || __instance.Running || ___scriptingEnvironment != null;
    }

    // --- Radio assignments: rewards of earlier finished assignments get paid too.
    [HarmonyPatch(typeof(MissionSummaryAnimation), "StopAnimation")]
    internal static class Fix_RadioRewards
    {
        private static void Prefix(bool ___notableEventsMode, IMission ___mission, out object[] __state) { __state = new object[] { ___notableEventsMode, ___mission }; }
        private static void Finalizer(object[] __state)
        {
            if (!Settings.B("FixRadioRewards") || __state == null || (bool)__state[0]) return;
            Util.Safe("Fix.RadioRewards", () =>
            {
                var reg = Resources.FindObjectsOfTypeAll<MissionsRegister>().FirstOrDefault();
                if (reg == null) return;
                int paid = 0;
                for (int i = 0; i < reg.Count; i++)
                {
                    var m = reg[i];
                    var a = m as IAssignmentMission;
                    if (a == null || ReferenceEquals(m, __state[1]) || m.State != MissionState.Completed) continue;
                    if ((m.CompletionFlags & MissionCompletionFlags.RewardAccepted) != 0 || a.AssignmentObjectiveState != AssignmentObjectiveState.Completed) continue;
                    m.GiveRewardsToPlayer();
                    paid++;
                }
                if (paid == 0) return;
                Util.Log("Funkauftrag: " + paid + " vergessene Belohnung(en) ausgezahlt");
                var ui = UnityEngine.Object.FindObjectOfType<ActiveMissionSummaryUI>(true);
                if (ui != null) AccessTools.Method(typeof(ActiveMissionSummaryUI), "ValidateDropdownOptions")?.Invoke(ui, null);
            });
        }
    }

    // --- Speed & range at high time compression (own approach):
    // 1) physics runs at most x100 instead of x300, the rest is plain time skip
    [HarmonyPatch(typeof(TimeCompressionController), "TimeCompressionScale_Changed")]
    internal static class Fix_SpeedCompression
    {
        private static void Postfix(TimeCompressionController __instance, Parameter timeCompressionScale)
        {
            if (!Settings.B("FixSpeedRange")) return;
            var t = Traverse.Create(__instance);
            if (!t.Field("timeCompression").GetValue<bool>()) return;
            var mod = t.Field("timeScaleModifier").GetValue<ParameterModifier>();
            var gt = t.Field("gameTime").GetValue<GameTime>();
            if (mod == null || gt == null) return;
            float s = timeCompressionScale;
            mod.Value = Mathf.Min(100f, s);
            gt.ArtificialTimeScale = Mathf.Max(1f, s / 100f);
        }
    }
    // 2) spray on the tower slows the boat a lot at high speed-up -> skipped there
    [HarmonyPatch(typeof(ConningTowerSprayEffect), "FixedUpdate")]
    internal static class Fix_SpeedSpray
    {
        private static bool Prefix(ConningTowerSprayEffect __instance)
        {
            if (!Settings.B("FixSpeedRange") || Time.timeScale < 100f) return true;
            var ship = Util.PlayerShip;
            if (ship == null || !__instance.transform.IsChildOf(ship.transform)) return true;
            return ship.SubmergedOrGoingToSubmerge || ship.ActiveEngine == null || ship.ActiveEngine.GearIndex == 0;
        }
    }
    // 3) water drag: learn the real value per boat and gear at normal speed, use it at x100 and more
    [HarmonyPatch(typeof(PlayWay.Water.Extensions.NeuralNetworkPhysics), "SimpleUpdate")]
    internal static class Fix_SpeedDrag
    {
        private static readonly Dictionary<string, float> learned = new Dictionary<string, float>();
        private static bool loaded;
        private static float nextSave;
        private static string File => Path.Combine(Settings.Folder, "toolkit-drag.txt");

        private static string Key(PlayerShip s) => (s.SandboxEntity?.Type?.Name ?? "?") + "|" + s.ActiveEngine.GearIndex;

        internal static void Handle(PlayWay.Water.Extensions.NeuralNetworkPhysics p)
        {
            if (!Settings.B("FixSpeedRange")) return;
            var ship = Util.PlayerShip;
            if (ship == null || ship.ActiveEngine == null || ship.SubmergedOrGoingToSubmerge || ship.ActiveEngine.GearIndex == 0) return;
            if (!p.transform.IsChildOf(ship.transform)) return;
            Load();
            var t = Traverse.Create(p);
            string k = Key(ship);
            if (Time.timeScale < 20f)
            {
                float now = t.Field("areaFractionSmoothed").GetValue<float>();
                if (now <= 0.01f || now > 0.6f) return;
                learned[k] = learned.TryGetValue(k, out var old) ? Mathf.Lerp(old, now, 0.01f) : now;
                if (Time.unscaledTime > nextSave) { nextSave = Time.unscaledTime + 60f; Save(); }
            }
            else if (Time.timeScale >= 100f)
            {
                if (!learned.TryGetValue(k, out var v))
                {
                    string type = k.Substring(0, k.IndexOf('|') + 1);
                    var any = learned.Where(e => e.Key.StartsWith(type)).Select(e => e.Value).ToList();
                    v = any.Count > 0 ? any.Average() : 0.12f;
                }
                t.Field("areaFractionSmoothed").SetValue(v);
                t.Field("submergedAreaFraction").SetValue(v);
            }
        }

        private static void Load()
        {
            if (loaded) return;
            loaded = true;
            try
            {
                if (!System.IO.File.Exists(File)) return;
                foreach (var line in System.IO.File.ReadAllLines(File))
                {
                    int i = line.LastIndexOf('=');
                    if (i > 0 && float.TryParse(line.Substring(i + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) learned[line.Substring(0, i)] = v;
                }
            }
            catch { }
        }

        private static void Save()
        {
            try { System.IO.File.WriteAllLines(File, learned.Select(e => e.Key + "=" + e.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))); } catch { }
        }

        private static void Prefix(PlayWay.Water.Extensions.NeuralNetworkPhysics __instance) => Handle(__instance);
    }
    [HarmonyPatch(typeof(PlayWay.Water.Extensions.NeuralNetworkPhysics), "Execute")]
    internal static class Fix_SpeedDragFull
    {
        private static void Prefix(List<PlayWay.Water.Extensions.NeuralNetworkPhysics> list) { if (Time.timeScale >= 100f) Each(list); }
        private static void Postfix(List<PlayWay.Water.Extensions.NeuralNetworkPhysics> list) => Each(list);
        private static void Each(List<PlayWay.Water.Extensions.NeuralNetworkPhysics> list)
        {
            if (list == null || !Settings.B("FixSpeedRange")) return;
            var ship = Util.PlayerShip;
            if (ship == null) return;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].transform.IsChildOf(ship.transform)) { Fix_SpeedDrag.Handle(list[i]); break; }
        }
    }

    // --- Pause without blur and without the dark overlay.
    [HarmonyPatch(typeof(PauseEffects), "SetPostProcessingWeight")]
    internal static class Fix_PauseBlur
    {
        private static void Prefix(ref float f) { if (Settings.B("NoPauseBlur")) f = 0f; }
    }
    [HarmonyPatch(typeof(PauseOverlayUI), "ValidateState")]
    internal static class Fix_PauseOverlay
    {
        private static void Postfix(PauseOverlayUI __instance) { if (Settings.B("NoPauseBlur") && __instance.gameObject.activeSelf) __instance.gameObject.SetActive(false); }
    }

}

namespace UboatToolkit.Modules
{
    // New career: skip the intro tutorial (switch "SkipIntroTutorial", default on).
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Sandbox.Missions.StartScript), "StartIntroTutorial")]
    internal static class GameFixes_SkipIntroTutorialPatch
    {
        private static bool Prefix()
        {
            if (!Settings.B("SkipIntroTutorial")) return true;
            Util.Log("Einführungs-Tutorial übersprungen");
            return false;
        }
    }
}

namespace UboatToolkit.Modules
{
    // QoL: "Mesh assigned to a collider is not readable: Bin / Clock / Folder_01".
    // Cause (game code, Entity.CollectColliders): some small props in the boat/port have a collision
    // shape whose 3D data is locked. The game then cannot compute its volume, writes a red error and
    // counts the part with volume 0 (it is left out of weight/buoyancy spreading).
    // Fix: the red line is replaced by one quiet toolkit log line per object, and the volume is
    // taken from the shape's bounding box instead (always readable), so the part counts again.
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Scene.Entities.Entity), "CollectColliders")]
    internal static class GameFixes_UnreadableColliderPatch
    {
        private static readonly System.Collections.Generic.HashSet<string> told = new System.Collections.Generic.HashSet<string>();

        private static System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction> Transpiler(System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction> code)
        {
            var logError = HarmonyLib.AccessTools.Method(typeof(UnityEngine.Debug), "LogError", new[] { typeof(object) });
            var quiet = HarmonyLib.AccessTools.Method(typeof(GameFixes_UnreadableColliderPatch), nameof(Quiet));
            foreach (var ci in code)
            {
                if (ci.Calls(logError)) yield return new HarmonyLib.CodeInstruction(System.Reflection.Emit.OpCodes.Call, quiet) { labels = ci.labels, blocks = ci.blocks };
                else yield return ci;
            }
        }

        public static void Quiet(object msg)
        {
            if (!Settings.B("FixUnreadableColliders")) { UnityEngine.Debug.LogError(msg); return; }
            string s = msg as string ?? "";
            if (told.Add(s)) Util.Log("Kollision ohne lesbare Form, Volumen aus Umriss berechnet: " + s.Replace("Mesh assigned to a collider is not readable: ", ""));
        }

        private static void Postfix(ref UnityEngine.Collider[] colliders, ref float[] colliderVolumes, ref float totalVolume)
        {
            if (!Settings.B("FixUnreadableColliders") || colliders == null || colliderVolumes == null) return;
            try
            {
                for (int i = 0; i < colliders.Length && i < colliderVolumes.Length; i++)
                {
                    var mc = colliders[i] as UnityEngine.MeshCollider;
                    if (mc == null || mc.sharedMesh == null || mc.sharedMesh.isReadable || colliderVolumes[i] > 0f) continue;
                    var size = mc.sharedMesh.bounds.size;
                    var sc = mc.transform.lossyScale;
                    float v = UnityEngine.Mathf.Abs(size.x * sc.x * size.y * sc.y * size.z * sc.z);
                    colliderVolumes[i] = v;
                    totalVolume += v;
                }
            }
            catch (System.Exception e) { Util.Log("Kollisions-Fix: " + e.Message); }
        }
    }

    // QoL: "Couldn't sample navmesh for position (-65.7, 2.1, -41.4)".
    // Cause (game code, GoToBehaviour.SnapTarget): a crew member is sent to a spot that lies a bit
    // off the walkable area (here in port). The game only searches 1.5 m around it, finds nothing,
    // writes the red error and the man gives up / stands around. Fix: search wider (up to 5 m)
    // and send him to the nearest walkable point instead.
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Core.AI.Behaviours.GoToBehaviour), "SnapTarget")]
    internal static class GameFixes_NavmeshSnapPatch
    {
        private static bool Prefix(UBOAT.Game.Core.AI.Behaviours.GoToBehaviour __instance, ref UnityEngine.Vector3 ___target, ref bool __result)
        {
            if (!Settings.B("FixNavmeshSnap")) return true;
            try
            {
                var nav = HarmonyLib.Traverse.Create(__instance).Field("navigator").GetValue<UBOAT.Game.Scene.Characters.Navigation.NavMeshNavigator>();
                if (nav == null) return true;
                int mask = nav.WalkableAreasMask;
                if (UnityEngine.AI.NavMesh.SamplePosition(___target, out var hit, 1.5f, mask)) return true;   // normal case: game does it
                if (!UnityEngine.AI.NavMesh.SamplePosition(___target, out hit, 5f, mask)) return true;      // really nowhere: game handles it
                ___target = hit.position;
                __result = true;
                return false;
            }
            catch { return true; }
        }
    }
}
