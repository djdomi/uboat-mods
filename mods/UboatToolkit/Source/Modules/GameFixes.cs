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

        public override void Tick()
        {
            var ship = Util.PlayerShip;
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
}
