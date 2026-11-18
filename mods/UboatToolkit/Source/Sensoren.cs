// UBOAT Toolkit - Sensoren: Sonar, Horcher, Sehrohr, Funk, Ausguck
// Diese Datei fasst 40 frühere Einzeldateien zusammen (0.4.30). Jeder Abschnitt nennt seine alte Datei.

// ====================================================================================================
// Abschnitt: Modules/ActiveSonar.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::HarmonyLib;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Items;
    using global::UnityEngine;

    // Active sonar (ping): the game gives our boat only a passive listening device. A ping sends sound out:
    // for a few seconds every ship/boat within range (and in the arc) is found for sure and its distance,
    // speed and course are exact. Downside: during the ping our boat is much louder, enemies can hear us.
    // Manual by key, or automatically every x seconds under water (not during "quiet" alarm if wanted).
    // Needs a man in the listening room unless switched off.
    public class ActiveSonar : ModuleBase
    {
        internal static float PingUntil = -1f;
        private static float lastAuto = -9999f;
        private Modifier loud;

        internal static bool Active => Time.time < PingUntil;

        public override void Frame()
        {
            if (Settings.B("ActiveSonar") && Keys.Down("KeySonarPing")) Ping(true);
        }

        public override void Tick()
        {
            var ship = Util.PlayerShip;
            if (ship == null) return;
            // louder while pinging
            loud = Util.OneDelta(ship.HydrophoneDetectability, "Toolkit Sonar");   // 0.4.20: exactly one, also after loading
            float want = Settings.B("ActiveSonar") && Active ? Settings.F("SonarLoudness") : 0f;
            if (loud.Value != want) loud.Value = want;

            float every = Settings.F("SonarAutoSeconds");
            if (!Settings.B("ActiveSonar") || every <= 0f || !ship.SubmergedOrGoingToSubmerge) return;
            if (Settings.B("SonarAutoQuiet") && ship.Alarmed) return;
            if (Time.time - lastAuto >= every) { lastAuto = Time.time; Ping(false); }
        }

        internal static void Ping(bool manual)
        {
            var ship = Util.PlayerShip;
            if (ship == null) return;
            if (Settings.B("SonarSubmergedOnly") && !ship.SubmergedOrGoingToSubmerge)
            { if (manual) ToolkitRunner.ShowNotice("Sonar: nur getaucht", 3f); return; }
            if (Settings.B("SonarNeedsOperator") && !CrewSkills.HydrophoneManned)
            { if (manual) ToolkitRunner.ShowNotice("Sonar: Horchraum ist nicht besetzt", 3f); return; }
            PingUntil = Time.time + Mathf.Max(1f, Settings.F("SonarPingSeconds"));
            lastAuto = Time.time;
            // report what is in range now
            float range = Settings.F("SonarRange");
            int n = 0; Entity near = null; float best = float.MaxValue;
            foreach (var s in Object.FindObjectsOfType<Ship>())
            {
                if (s == null || s == ship || !InArc(ship, s)) continue;
                float d = Vector3.Distance(ship.transform.position, s.transform.position);
                if (d > range) continue;
                n++;
                if (d < best) { best = d; near = s; }
            }
            string text = "Sonar-Ping: " + (n == 0 ? "nichts in " + (range / 1000f).ToString("0.0") + " km"
                : n + " Kontakt(e), nächster " + Mathf.RoundToInt(best) + " m, Peilung " + Bearing(ship, near).ToString("000") + "°");
            if (Settings.B("SonarPingText") || !Settings.B("SonarScope")) ToolkitRunner.ShowNotice(text, 5f);
        }

        internal static bool InArc(PlayerShip ship, Entity e)
        {
            float arc = Settings.F("SonarArc");
            if (arc >= 360f) return true;
            Vector3 to = e.transform.position - ship.transform.position;
            float rel = Mathf.Abs(Mathf.DeltaAngle(ship.transform.eulerAngles.y, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg));
            return rel <= arc / 2f;
        }

        private static int Bearing(PlayerShip ship, Entity e)
        {
            Vector3 to = e.transform.position - ship.transform.position;
            return Mathf.RoundToInt((Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg + 360f) % 360f);
        }
    }

    // During the ping our listening device finds everything in range for sure.
    [HarmonyPatch(typeof(Hydrophone), nameof(Hydrophone.GetSpotChance))]
    internal static class ActiveSonar_SpotPatch
    {
        private static void Postfix(Hydrophone __instance, Entity entity, float distance, ref float __result)
        {
            if (!ActiveSonar.Active || !Settings.B("ActiveSonar") || entity == null || !(entity is Ship)) return;
            var ship = Util.PlayerShip;
            if (ship == null || __instance.ParentEntity != ship) return;
            if (distance <= Settings.F("SonarRange") && ActiveSonar.InArc(ship, entity)) __result = 1f;
        }
    }

    // ...and the values (distance, speed, course) are exact while the echo comes back.
    [HarmonyPatch(typeof(Hydrophone), nameof(Hydrophone.AllowOnlyCrudeEstimations), MethodType.Getter)]
    internal static class ActiveSonar_ExactPatch
    {
        internal static DirectObservation Now;
        private static void Postfix(Hydrophone __instance, ref bool __result)
        {
            if (!ActiveSonar.Active || !Settings.B("ActiveSonar")) return;
            // 0.4.22 (option audit): only OUR hydrophone, and only for the contact the ping really reaches (range + arc).
            var ship = Util.PlayerShip;
            if (ship == null || __instance.ParentEntity != ship) return;
            var o = Now;
            if (o == null || o.Entity == null) return;
            float d = (o.Entity.transform.position - ship.transform.position).magnitude;
            if (d <= Settings.F("SonarRange") && ActiveSonar.InArc(ship, o.Entity)) __result = false;
        }
    }
    [HarmonyPatch(typeof(DirectObservation), "AddTrackingProgress")]
    internal static class ActiveSonar_ObsPatch
    {
        private static void Prefix(DirectObservation __instance) { ActiveSonar_ExactPatch.Now = __instance; }
        private static System.Exception Finalizer(System.Exception __exception) { ActiveSonar_ExactPatch.Now = null; return __exception; }
    }
}

// ====================================================================================================
// Abschnitt: Modules/DetectCheat.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Sandbox;
    using global::UBOAT.Game.Sandbox.Missions;

    // Cheat (DoMe 2026-11-16: "detect cheat dauerhaft aktiv, Missionsziel direkt anfahren"): keeps contacts on the map,
    // the same way the game's console command "Detect" does it, but every 3 s instead of 12 times a second,
    // also after loading, and without the console. 1 = only targets of missions, 2 = every group.
    public class DetectCheat : ModuleBase
    {
        private float next;
        private int lastCount = -1;
        public override void Tick()
        {
            int mode = Mathf.RoundToInt(Settings.F("CheatDetect"));
            if (mode <= 0 || Time.unscaledTime < next) return;
            next = Time.unscaledTime + 3f;
            var w = Game.Wolfpack;
            var sb = Traverse.Create(typeof(UBOAT.Game.UI.DevConsole.DevConsoleCommands)).Field("sandbox").GetValue<Sandbox>();
            if (w == null || sb == null) return;
            float km = Settings.F("CheatDetectKm");
            float max2 = km > 0f ? km * km : float.MaxValue;
            List<SandboxGroup> groups = sb.GetGroupsDirect();
            int n = 0;
            for (int i = 0; i < groups.Count; i++)
            {
                var g = groups[i];
                if (!g || g.IsGroupPublic || g == w) continue;
                if (g.DistanceToPlayerSquared > max2) continue;
                if (mode == 1 && !IsTarget(g)) continue;
                w.AddObservation(g, GroupDetectionMethod.NotSpecified).AddKnownData(GroupData.All, 0f, true);
                n++;
            }
            if (n != lastCount) { lastCount = n; Util.Log("[Kontakte] " + n + " Gruppen auf der Karte gehalten (Modus " + mode + ")"); }
        }

        private static bool IsTarget(SandboxGroup g)
        {
            var es = g.Entities;
            for (int i = 0; i < es.Count; i++)
            {
                var st = es[i]?.Statuses;
                if (st == null) continue;
                for (int j = 0; j < st.Count; j++) if (st[j] is IMissionTarget) return true;
            }
            return false;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Lookout/LrlAcceptancePatch.cs
// ====================================================================================================
// Ausguck (Teil des UBOAT Toolkit): LrlAcceptancePatch. Schalter: F10 > QoL > "Ausguck".

namespace UboatToolkit.Modules.Lrl
{
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::System.Reflection.Emit;
    using global::HarmonyLib;

    [HarmonyPatch(typeof(UBOAT.Game.Sandbox.Sandbox), "GetRandomGroupInRangeWithHorizonOcclusion")]
    internal static class LrlAcceptancePatch
    {
        private static int _rolls;
        private static int _vanillaAccepts;
        private static int _fixedAccepts;

        internal static bool Applied { get; private set; }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions);
            var getValue = AccessTools.PropertyGetter(typeof(UnityEngine.Random), "value");
            var decide = AccessTools.Method(typeof(LrlAcceptancePatch), "Decide");

            var found = -1;
            var matches = 0;
            for (var i = 0; i + 2 < list.Count; i++)
            {
                if (list[i].opcode == OpCodes.Call && Equals(list[i].operand, getValue) &&
                    list[i + 1].opcode == OpCodes.Ldloc_0 &&
                    (list[i + 2].opcode == OpCodes.Bge_Un_S || list[i + 2].opcode == OpCodes.Bge_Un))
                {
                    found = i + 2;
                    matches++;
                }
            }

            if (matches != 1 || decide == null)
            {
                LrlLog.Warn("acceptance fix NOT applied - expected one 'Random.value >= summed' branch, found " +
                            matches + "; the search is left exactly as vanilla");
                return list;
            }

            var target = list[found].operand;
            var labels = list[found].labels;

            var loadFlag = new CodeInstruction(OpCodes.Ldarg_S, (byte)6);   // updateCandidates
            loadFlag.labels.AddRange(labels);

            list[found] = loadFlag;
            list.Insert(found + 1, new CodeInstruction(OpCodes.Call, decide));
            list.Insert(found + 2, new CodeInstruction(OpCodes.Brtrue, target));

            Applied = true;
            return list;
        }

        private static bool Decide(float random, float summed, bool isPlayer)
        {
            var vanilla = !(random < summed);   // == bge.un, unordered included

            if (!isPlayer || !LrlSettings.Enabled || !LrlSettings.FixInvertedAcceptance)
                return vanilla;

            var pick = random < summed;   // accept with probability = total visibility weight

            _rolls++;
            if (vanilla) _vanillaAccepts++;
            if (pick) _fixedAccepts++;

            return pick;
        }

        internal static string Report()
        {
            var s = string.Format(
                "acceptance fix {0}: player rolls={1} vanilla would accept={2} accepted={3}",
                Applied ? (LrlSettings.FixInvertedAcceptance ? "ON" : "off") : "NOT APPLIED",
                _rolls, _vanillaAccepts, _fixedAccepts);
            _rolls = 0;
            _vanillaAccepts = 0;
            _fixedAccepts = 0;
            return s;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Lookout/LrlApparentHeightPatch.cs
// ====================================================================================================
// Ausguck (Teil des UBOAT Toolkit): LrlApparentHeightPatch. Schalter: F10 > QoL > "Ausguck".

namespace UboatToolkit.Modules.Lrl
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::HarmonyLib;
    using global::UBOAT.Game.Sandbox;

    [HarmonyPatch]
    internal static class LrlApparentHeightPatch
    {
        private static bool _warned;

        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(SandboxGroup), "GetApparentHeight");
            yield return AccessTools.Method(typeof(SandboxMobileGroup), "GetApparentHeight");
        }

        internal static bool HasRun { get; private set; }

        private static void Postfix(SandboxGroup __instance, ref float __result)
        {
            try
            {
                // 0.3.38 (DoMe): only helps the player see others; our own group never gets taller for enemies
                if (!LrlSettings.Enabled || !LrlSettings.SmokeHeightEnabled || __instance is SandboxPlayerWolfpack)
                    return;

                if (float.IsInfinity(__result))
                {
                    if (LrlLog.ShouldLog("apparent-infinite", 300f))
                    {
                        LrlLog.Info(
                            "vanilla GetApparentHeight returned " +
                            (__result > 0f ? "+Infinity (Velocity > 0" : "-Infinity (Velocity < 0") +
                            ", MaxVelocity 0) - left untouched; occlusion resolves to 0 either way");
                    }
                    return;
                }

                var smoke = LrlSmokeModel.GetSmokeHeight(__instance);

                if (float.IsNaN(__result))
                {
                    var mast = __instance.GetMaximumMastHeight();
                    if (float.IsNaN(mast) || float.IsInfinity(mast) || mast <= 0f)
                        return;

                    __result = mast + smoke;
                    HasRun = true;

                    if (LrlLog.ShouldLog("apparent-nan", 60f))
                    {
                        LrlLog.Info(string.Format(
                            "vanilla GetApparentHeight returned NaN (velocity and MaxVelocity both 0) - " +
                            "substituted mast {0:0.0}m + smoke {1:0.0}m; without this the group is undetectable",
                            mast, smoke));
                    }
                    return;
                }

                if (smoke <= 0f)
                    return;

                __result += smoke;
                HasRun = true;
            }
            catch (Exception ex)
            {
                if (!_warned)
                {
                    _warned = true;
                    LrlLog.Error("apparent-height postfix failed - smoke height disabled for this session", ex);
                }
            }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Lookout/LrlCalloutRetry.cs
// ====================================================================================================
// Ausguck (Teil des UBOAT Toolkit): LrlCalloutRetry. Schalter: F10 > QoL > "Ausguck".

namespace UboatToolkit.Modules.Lrl
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::HarmonyLib;
    using global::UBOAT.Game.Sandbox;
    using global::UBOAT.Game.Scene.Entities;
    using global::UnityEngine;

    internal static class LrlCalloutRetry
    {
        private sealed class Entry
        {
            public SandboxGroup Observator;
            public GroupObservation Observation;
            public object ContactType;
            public string Line;
            public float FirstDrop;
            public float NextAttempt;
            public int Attempts;
        }

        internal static readonly MethodInfo SayReport =
            AccessTools.Method(typeof(SandboxPlayerWolfpack), "SayGroupDetectionReport");

        internal static readonly FieldInfo PerceivedField =
            AccessTools.Field(typeof(GroupObservation), "perceivedContactType");

        private static readonly List<Entry> Queue = new List<Entry>();

        internal static void Clear() { Queue.Clear(); }

        internal static SandboxGroup CurrentObservator;
        internal static GroupObservation CurrentObservation;

        internal static bool Retrying;
        internal static bool LastRetryVoiced;

        private static bool _warned;

        internal static bool Active
        {
            get { return LrlSettings.Enabled && LrlSettings.RetryDroppedCallouts; }
        }

        internal static void Enqueue(string line)
        {
            if (!Active || CurrentObservation == null || CurrentObservator == null)
                return;

            for (var i = 0; i < Queue.Count; i++)
                if (ReferenceEquals(Queue[i].Observation, CurrentObservation))
                    return;

            object type = null;
            try
            {
                if (PerceivedField != null)
                    type = PerceivedField.GetValue(CurrentObservation);
            }
            catch (Exception)
            {
                type = null;
            }

            var now = Time.unscaledTime;
            Queue.Add(new Entry
            {
                Observator = CurrentObservator,
                Observation = CurrentObservation,
                ContactType = type,
                Line = line,
                FirstDrop = now,
                NextAttempt = now + Mathf.Max(1f, LrlSettings.RetryIntervalSeconds),
                Attempts = 0
            });

            LrlLog.Info("callout queued for retry: \"" + line + "\" - re-attempting every " +
                        LrlSettings.RetryIntervalSeconds.ToString("0") + " s for up to " +
                        LrlSettings.RetryWindowSeconds.ToString("0") + " s");
        }

        internal static void Tick(float now)
        {
            if (Queue.Count == 0)
                return;

            for (var i = Queue.Count - 1; i >= 0; i--)
            {
                var e = Queue[i];
                if (now < e.NextAttempt)
                    continue;

                if (!StillValid(e))
                {
                    Queue.RemoveAt(i);
                    LrlLog.Info("callout retry abandoned - contact no longer held: \"" + e.Line + "\"");
                    continue;
                }

                if (now - e.FirstDrop > LrlSettings.RetryWindowSeconds)
                {
                    Queue.RemoveAt(i);
                    LrlLog.Info("callout retry gave up after " + e.Attempts + " attempts / " +
                                (now - e.FirstDrop).ToString("0") + " s: \"" + e.Line + "\"");
                    continue;
                }

                e.Attempts++;
                e.NextAttempt = now + Mathf.Max(1f, LrlSettings.RetryIntervalSeconds);

                if (Attempt(e))
                {
                    Queue.RemoveAt(i);
                    LrlLog.Info("callout VOICED on retry " + e.Attempts + ", " +
                                (now - e.FirstDrop).ToString("0") + " s after it was dropped: \"" + e.Line + "\"");
                }
            }
        }

        private static bool StillValid(Entry e)
        {
            if (e.Observator == null || e.Observation == null)
                return false;
            if (e.Observation.IsLost)
                return false;

            var group = e.Observation.Group;
            if (group == null)
                return false;

            return ReferenceEquals(e.Observator.GetObservation(group), e.Observation);
        }

        private static bool Attempt(Entry e)
        {
            object original = null;
            var swapped = false;

            Retrying = true;
            LastRetryVoiced = false;

            try
            {
                if (SayReport == null)
                    return true;   // cannot retry at all - drop the entry

                if (e.ContactType != null && PerceivedField != null)
                {
                    original = PerceivedField.GetValue(e.Observation);
                    PerceivedField.SetValue(e.Observation, e.ContactType);
                    swapped = true;
                }

                SayReport.Invoke(null, new object[] { e.Observator, e.Observation });
                return LastRetryVoiced;
            }
            catch (Exception ex)
            {
                if (!_warned)
                {
                    _warned = true;
                    LrlLog.Error("callout retry failed - entry dropped", ex);
                }
                return true;
            }
            finally
            {
                Retrying = false;
                if (swapped)
                {
                    try { PerceivedField.SetValue(e.Observation, original); }
                    catch (Exception) { }
                }
            }
        }
    }

    [HarmonyPatch(typeof(SandboxPlayerWolfpack), "SayGroupDetectionReport")]
    internal static class LrlCalloutContextPatch
    {
        private static void Prefix(SandboxGroup observator, GroupObservation observation)
        {
            LrlCalloutRetry.CurrentObservator = observator;
            LrlCalloutRetry.CurrentObservation = observation;
        }

        private static void Finalizer()
        {
            LrlCalloutRetry.CurrentObservator = null;
            LrlCalloutRetry.CurrentObservation = null;
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "DetectionReportInternal")]
    internal static class LrlCalloutRetryHook
    {
        private static void Postfix(string statementId1, bool __result)
        {
            if (statementId1 == null || !statementId1.StartsWith("Group Contact"))
                return;

            if (LrlCalloutRetry.Retrying)
            {
                if (__result)
                    LrlCalloutRetry.LastRetryVoiced = true;
                return;
            }

            if (!__result)
                LrlCalloutRetry.Enqueue(statementId1);
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Lookout/LrlCompressionLadder.cs
// ====================================================================================================
// Ausguck (Teil des UBOAT Toolkit): LrlCompressionLadder. Schalter: F10 > QoL > "Ausguck".

namespace UboatToolkit.Modules.Lrl
{
    using global::System;
    using global::System.Reflection;
    using global::HarmonyLib;

    internal static class LrlCompressionLadder
    {
        private const int RungCount = 5;

        private static bool _resolved;
        private static string _whyNot = "not resolved yet";
        private static readonly FieldInfo[] RungFields = new FieldInfo[RungCount];
        private static MethodInfo _toFloat;

        private static readonly float[] Rungs = new float[RungCount];

        private static void Resolve()
        {
            if (_resolved)
                return;

            _resolved = true;

            try
            {
                var type = UboatToolkit.Util.FindType("UBOAT.Game.Core.Time.TimeCompressionController");
                if (type == null)
                {
                    _whyNot = "TimeCompressionController type not found";
                    return;
                }

                for (var i = 0; i < RungCount; i++)
                {
                    RungFields[i] = AccessTools.Field(type, "timeCompression" + (i + 1));
                    if (RungFields[i] == null)
                    {
                        _whyNot = "timeCompression" + (i + 1) + " field not found";
                        return;
                    }
                }

                var refType = RungFields[0].FieldType;
                _toFloat = null;
                foreach (var m in refType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name != "op_Implicit" || m.ReturnType != typeof(float))
                        continue;
                    var ps = m.GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType == refType)
                    {
                        _toFloat = m;
                        break;
                    }
                }
                if (_toFloat == null)
                {
                    _whyNot = "GameDataReference.op_Implicit(float) not found";
                    return;
                }

                _whyNot = null;
            }
            catch (Exception ex)
            {
                _whyNot = "resolve threw: " + ex.GetType().Name;
            }
        }

        internal static bool TryRead(out float[] rungs)
        {
            rungs = Rungs;

            Resolve();
            if (_whyNot != null)
                return false;

            try
            {
                for (var i = 0; i < RungCount; i++)
                {
                    var reference = RungFields[i].GetValue(null);
                    if (reference == null)
                    {
                        _whyNot = "timeCompression" + (i + 1) + " is null";
                        return false;
                    }

                    Rungs[i] = (float)_toFloat.Invoke(null, new[] { reference });
                }

                return true;
            }
            catch (Exception ex)
            {
                _whyNot = "read threw: " + ex.GetType().Name;
                return false;
            }
        }

        internal static string WhyNot { get { return _whyNot; } }

        internal static float Top
        {
            get
            {
                float[] rungs;
                if (!TryRead(out rungs))
                    return float.NaN;

                var top = rungs[0];
                for (var i = 1; i < RungCount; i++)
                    if (rungs[i] > top)
                        top = rungs[i];

                return top;
            }
        }

        internal static float EffectiveCompression(float cap)
        {
            float[] rungs;
            if (!TryRead(out rungs))
                return float.NaN;

            var best = 1f;
            for (var i = 0; i < RungCount; i++)
                if (rungs[i] <= cap && rungs[i] > best)
                    best = rungs[i];

            return best;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Lookout/LrlContactNoticeAudit.cs
// ====================================================================================================
// Ausguck (Teil des UBOAT Toolkit): LrlContactNoticeAudit. Schalter: F10 > QoL > "Ausguck".

namespace UboatToolkit.Modules.Lrl
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::HarmonyLib;
    using global::UBOAT.Game.Sandbox;
    using global::UBOAT.Game.Scene.Entities;
    using global::UnityEngine;

    [HarmonyPatch]
    internal static class LrlContactNoticeAudit
    {
        private const int IndirectObservation = 2;   // DynamicContactType
        private const int FunnelSmoke = 9;           // PerceivedDynamicContactType
        private const int FunnelSmokeSingle = 19;
        private const int ShipMasts = 10;
        private const int ShipMastSingle = 20;
        private const int MovementInTheDark = 21;

        private static readonly MethodInfo MakesVisibleSmoke =
            AccessTools.Method(typeof(CompoundContactInfo), "MakesVisibleSmoke");

        private static bool _warned;

        private const float RepeatWindow = 2f;
        private static string _lastGroupName;
        private static float _lastLoggedAt = -100f;
        private static int _folded;

        private static MethodBase TargetMethod()
        {
            var t = UboatToolkit.Util.FindType("UBOAT.Game.Scene.Entities.DynamicContactsController");
            return t != null ? AccessTools.Method(t, "AddContact") : null;
        }

        private static void Postfix(CompoundContactInfo contact)
        {
            try
            {
                if (!LrlSettings.Enabled || !LrlSettings.DebugLogging || contact == null)
                    return;
                if ((int)contact.Type != IndirectObservation)
                    return;

                var perceived = (int)contact.PerceivedType;
                var airborne = contact.IsAirborne;

                var smoke = "?";
                if (MakesVisibleSmoke != null)
                {
                    var r = MakesVisibleSmoke.Invoke(contact, null);
                    if (r is bool)
                        smoke = (bool)r ? "yes" : "NO";
                }

                var weather = LrlPlayer.Weather;
                var dark = weather != null && weather.IsDarkWeather;
                var vis = weather != null ? weather.VisualDetectabilityFactors : float.NaN;

                var infos = contact.EntityInfos;
                var ships = infos != null ? infos.Count : 0;
                var groupName = "?";
                SandboxGroup group = null;
                if (ships > 0 && infos[0] != null && infos[0].Entity != null && infos[0].Entity.Group != null)
                {
                    group = infos[0].Entity.Group;
                    groupName = group.name;
                }

                var now = Time.unscaledTime;
                if (groupName == _lastGroupName && now - _lastLoggedAt < RepeatWindow)
                {
                    _folded++;
                    return;
                }
                _lastGroupName = groupName;
                _lastLoggedAt = now;

                var where = "dist=? bearing=?";
                var wolfpack = LrlRuntime.CurrentWolfpack;
                if (group != null && wolfpack != null)
                {
                    var delta = group.Position - wolfpack.Position;
                    var b = Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg;
                    if (b < 0f)
                        b += 360f;
                    where = string.Format("dist={0:0.0}km bearing={1:000}", delta.magnitude, b);
                }

                var folded = "";
                if (_folded > 0)
                {
                    folded = string.Format("  [{0} repeat add(s) of the previous contact folded]", _folded);
                    _folded = 0;
                }

                var accepted = !airborne && (perceived == FunnelSmoke || perceived == FunnelSmokeSingle);

                string why;
                if (accepted) why = "smoke notice SHOWN";
                else if (airborne) why = "no notice - airborne";
                else if (perceived == MovementInTheDark) why = "no notice - map type is MovementInTheDark (dark weather and visibility < 0.4 at the moment it was added)";
                else if (perceived == ShipMasts || perceived == ShipMastSingle) why = "no notice - map type is ShipMasts (MakesVisibleSmoke=" + smoke + ")";
                else why = "no notice - map type " + perceived;

                LrlLog.Info(string.Format(
                    "map contact added: {0} ({1} ship(s)) {2} perceived={3} dark={4} visibility={5:0.00} makesVisibleSmoke={6} -> {7}{8}",
                    groupName, ships, where, perceived, dark ? "yes" : "no", vis, smoke, why, folded));
            }
            catch (Exception ex)
            {
                if (!_warned)
                {
                    _warned = true;
                    LrlLog.Error("map contact audit failed - disabled for this session", ex);
                }
            }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Lookout/LrlConvoyNightBonus.cs
// ====================================================================================================
// Ausguck (Teil des UBOAT Toolkit): LrlConvoyNightBonus. Schalter: F10 > QoL > "Ausguck".

namespace UboatToolkit.Modules.Lrl
{
    using global::System;
    using global::System.Collections.Generic;
    using global::HarmonyLib;
    using global::UBOAT.Game.Sandbox;
    using global::UnityEngine;

    internal static class LrlConvoyNightBonus
    {
        internal static float NightWeight
        {
            get
            {
                if (!LrlSettings.Enabled || !LrlSettings.ConvoyNightBonusEnabled)
                    return 0f;

                var v = LrlSightRangePatch.CachedVisibility;
                if (float.IsNaN(v) || float.IsInfinity(v))
                    return 0f;

                var full = LrlSettings.ConvoyNightBonusFullVisibility;
                var end = LrlSettings.ConvoyNightBonusEndVisibility;
                if (end <= full)
                    return v <= full ? 1f : 0f;

                return Mathf.Clamp01((end - v) / (end - full));
            }
        }

        internal static float MaxFactor
        {
            get { return Mathf.Lerp(1f, Mathf.Max(1f, LrlSettings.ConvoyNightBonusMax), NightWeight); }
        }

        internal static float Factor(SandboxGroup group)
        {
            var weight = NightWeight;
            if (weight <= 0f || group == null)
                return 1f;
            if (group.IsGroupPublic || group.IsAirborne || !(group is SandboxMobileGroup) ||
                group is SandboxPlayerWolfpack)
                return 1f;

            var entities = group.Entities;
            var ships = entities != null ? entities.Count : 0;
            if (ships < 2)
                return 1f;

            var size = Mathf.Min(1f + LrlSettings.ConvoyNightBonusPerExtraShip * (ships - 1),
                                 Mathf.Max(1f, LrlSettings.ConvoyNightBonusMax));
            return Mathf.Lerp(1f, size, weight);
        }

        internal static readonly List<SandboxGroup> Pool = new List<SandboxGroup>();

        private const float PoolRadiusKm = 120f;
        private static readonly List<SandboxGroup> Scratch = new List<SandboxGroup>();

        internal static void RefreshPool(SandboxPlayerWolfpack wolfpack)
        {
            Pool.Clear();
            if (wolfpack == null || NightWeight <= 0f)
                return;

            if (!LrlSandboxGroups.GetNearest(wolfpack.Position, PoolRadiusKm, Scratch))
                return;

            for (var i = 0; i < Scratch.Count; i++)
            {
                if (Factor(Scratch[i]) > 1.0001f)
                    Pool.Add(Scratch[i]);
            }
            Scratch.Clear();
        }

        internal static int Rolls;
        internal static int Accepted;
        internal static string LastAccepted;

        internal static string Report()
        {
            var max = MaxFactor;
            if (max <= 1.0001f && Rolls == 0)
                return null;

            var s = string.Format(
                "convoy night bonus: up to x{0:0.00} (night {1:0%}) pool={5} bonus-only rolls={2} accepted={3}{4}",
                max, NightWeight, Rolls, Accepted,
                LastAccepted != null ? " last=" + LastAccepted : "", Pool.Count);
            Rolls = 0;
            Accepted = 0;
            LastAccepted = null;
            return s;
        }
    }

    [HarmonyPatch(typeof(UBOAT.Game.Sandbox.Sandbox), "GetRandomGroupInRangeWithHorizonOcclusion")]
    internal static class LrlConvoyNightBonusPatch
    {
        private static readonly SandboxGroup[] Candidates = new SandboxGroup[64];
        private static readonly float[] Weights = new float[64];

        private static bool _failed;

        private static bool Prefix(Vector2 point, float range,
            Func<SandboxGroup, float> detectabilityFunc, float observerHorizonDistance,
            bool updateCandidates, ref SandboxGroup __result)
        {
            if (!updateCandidates || _failed || detectabilityFunc == null || range <= 0f)
                return true;

            try
            {
                var maxFactor = LrlConvoyNightBonus.MaxFactor;
                if (maxFactor <= 1.0001f)
                    return true;

                var pool = LrlConvoyNightBonus.Pool;
                var count = 0;
                var summed = 0f;
                var rangeSq = range * range;

                for (var i = 0; i < pool.Count && count < Candidates.Length; i++)
                {
                    var group = pool[i];
                    if (group == null)   // Unity null: destroyed since the pool was built
                        continue;

                    var factor = LrlConvoyNightBonus.Factor(group);
                    if (factor <= 1.0001f)
                        continue;

                    group.UpdateGroup();
                    var distSq = (group.Position - point).sqrMagnitude;
                    if (distSq >= rangeSq * factor * factor)
                        continue;

                    var det = detectabilityFunc(group);
                    var weight = det * det;
                    if (!(weight > 0f))
                        continue;

                    weight *= 1f - group.ComputeOcclusionByHorizonFactor(observerHorizonDistance, distSq * 1000000f);
                    if (!(weight > 0f))
                        continue;

                    if (distSq < rangeSq * weight)
                        continue;

                    if (distSq >= rangeSq * factor * factor * weight)
                        continue;

                    Candidates[count] = group;
                    Weights[count] = weight;
                    count++;
                    summed += weight;
                }

                if (count == 0)
                    return true;

                LrlConvoyNightBonus.Rolls++;

                var roll = UnityEngine.Random.value;
                var accept = LrlSettings.FixInvertedAcceptance
                    ? roll < summed
                    : (summed >= 1f || roll >= summed);

                SandboxGroup picked = null;
                if (accept)
                {
                    var pick = UnityEngine.Random.value * summed;
                    for (var i = 0; i < count; i++)
                    {
                        pick -= Weights[i];
                        if (pick < 0f || i == count - 1)
                        {
                            picked = Candidates[i];
                            break;
                        }
                    }
                }

                for (var i = 0; i < count; i++)
                    Candidates[i] = null;

                if (picked == null)
                    return true;

                LrlConvoyNightBonus.Accepted++;
                LrlConvoyNightBonus.LastAccepted = picked.name;
                __result = picked;
                return false;
            }
            catch (Exception ex)
            {
                _failed = true;
                LrlLog.Error("convoy night bonus failed - off for this session, search left vanilla", ex);
                return true;
            }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Lookout/LrlCrewStations.cs
// ====================================================================================================
// Ausguck (Teil des UBOAT Toolkit): LrlCrewStations. Schalter: F10 > QoL > "Ausguck".

namespace UboatToolkit.Modules.Lrl
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::System.Text;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Interactions;
    using global::UBOAT.Game.Scene.Items;

    internal static class LrlCrewStations
    {
        private static readonly List<ObservationSpot> Buffer = new List<ObservationSpot>();
        private static readonly StringBuilder DetailBuilder = new StringBuilder();

        private static readonly FieldInfo SubtitlesField = FindSubtitlesField();
        private static readonly PropertyInfo DisplayedProperty = FindDisplayedProperty();

        private static FieldInfo FindSubtitlesField()
        {
            var t = UboatToolkit.Util.FindType("UBOAT.Game.Scene.Tasks.SpeakController");
            return t != null ? AccessTools.Field(t, "subtitlesUI") : null;
        }

        private static PropertyInfo FindDisplayedProperty()
        {
            var t = UboatToolkit.Util.FindType("UBOAT.Game.UI.ISubtitlesUI");
            return t != null ? t.GetProperty("Displayed") : null;
        }

        private static string SubtitleShown()
        {
            try
            {
                if (SubtitlesField == null || DisplayedProperty == null)
                    return "?";
                var ui = SubtitlesField.GetValue(null);
                if (ui == null)
                    return "?";
                var shown = DisplayedProperty.GetValue(ui, null);
                return shown is bool ? ((bool)shown ? "yes" : "no") : "?";
            }
            catch (Exception)
            {
                return "?";
            }
        }

        private static bool Inspect(ObservationSpot spot, out int workers, out int crew, out bool officer)
        {
            workers = 0;
            crew = 0;
            officer = false;

            if (spot == null)
                return false;

            var interactable = spot.GetComponent<Interactable>();
            if (interactable == null)
                return false;

            var list = interactable.Workers;
            workers = list != null ? list.Count : 0;

            for (var i = 0; i < workers; i++)
            {
                var pc = list[i] as PlayableCharacter;
                if (pc == null)
                    continue;

                crew++;
                if (pc.IsOfficer)
                    officer = true;
            }

            var range = spot.DetectionRange != null ? spot.DetectionRange.Value : 0f;
            return crew > 0 && (officer || range >= 1f);
        }

        internal static string Describe(ObservationSpot spot)
        {
            if (spot == null)
                return "n/a";

            if (spot.GetComponent<Interactable>() == null)
                return "no station (cannot voice a report)";

            int workers, crew;
            bool officer;
            var canVoice = Inspect(spot, out workers, out crew, out officer);

            return string.Format("workers={0} crew={1} officer={2} canVoice={3}",
                workers, crew, officer ? "yes" : "no", canVoice ? "yes" : "NO");
        }

        internal static string Summarise(PlayerShip ship, string statementId)
        {
            if (ship == null)
                return "no player ship";

            Buffer.Clear();
            LrlProviders.GetAll(ship, Buffer);

            var voicers = 0;
            var detail = DetailBuilder;
            detail.Length = 0;

            for (var i = 0; i < Buffer.Count; i++)
            {
                int workers, crew;
                bool officer;
                if (!Inspect(Buffer[i], out workers, out crew, out officer))
                    continue;

                voicers++;
                detail.Append(voicers > 1 ? "; " : "").Append(Buffer[i].name).Append(": ");

                var list = Buffer[i].GetComponent<Interactable>().Workers;
                for (var w = 0; w < list.Count; w++)
                {
                    var pc = list[w] as PlayableCharacter;
                    if (pc == null)
                        continue;

                    detail.Append(pc.Name)
                          .Append(pc.IsOfficer ? " (officer" : " (crew")
                          .Append(pc.IsImpersonated ? ", you" : "")
                          .Append(") speaking=").Append(pc.IsSpeaking ? "yes" : "no")
                          .Append(" canSpeakFreely=").Append(pc.CanSpeakFreely ? "yes" : "no")
                          .Append(" canSay=").Append(pc.CanSay(statementId) ? "yes" : "NO")
                          .Append(' ');
                }
            }

            Buffer.Clear();

            if (voicers == 0)
                return "NOBODY was manning a lookout station that can voice it";

            return voicers + " station(s) could have voiced it [" + detail.ToString().TrimEnd() +
                   "] subtitleOnScreen=" + SubtitleShown();
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Lookout/LrlDarkContactPatch.cs
// ====================================================================================================
// Ausguck (Teil des UBOAT Toolkit): LrlDarkContactPatch. Schalter: F10 > QoL > "Ausguck".

namespace UboatToolkit.Modules.Lrl
{
    using global::System;
    using global::System.Reflection;
    using global::HarmonyLib;
    using global::UBOAT.Game.Sandbox;

    [HarmonyPatch]
    internal static class LrlDarkContactPatch
    {
        private const int Masts = 3;
        private const int Ships = 7;
        private const int Ship = 8;

        private const int IndirectObservation = 4;

        private static readonly FieldInfo PerceivedField =
            AccessTools.Field(typeof(GroupObservation), "perceivedContactType");

        private static readonly FieldInfo DarknessField =
            AccessTools.Field(typeof(GroupObservation), "wasSpottedInDarkness");

        private static bool _warned;

        internal static int Rescued { get; private set; }

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(SandboxPlayerWolfpack), "SayGroupDetectionReport");
        }

        private static void Prefix(GroupObservation observation, out object __state)
        {
            __state = null;

            try
            {
                if (!LrlSettings.Enabled || !LrlSettings.ReportDarkContacts)
                    return;
                if (observation == null || PerceivedField == null || DarknessField == null)
                    return;

                if (((int)observation.DetectionMethod & IndirectObservation) == 0)
                    return;

                if (!(bool)DarknessField.GetValue(observation))
                    return;

                var current = (int)observation.PerceivedContactType;
                if (current != Ships && current != Ship)
                    return;

                __state = PerceivedField.GetValue(observation);
                PerceivedField.SetValue(observation, Enum.ToObject(PerceivedField.FieldType, Masts));

                Rescued++;
                LrlLog.Throttled("dark-contact", 30f,
                    "dark contact would have been reported silently (PerceivedContactType=" +
                    (current == Ships ? "Ships" : "Ship") +
                    ", no voice line exists) - speaking it as Masts instead");
            }
            catch (Exception ex)
            {
                __state = null;
                if (!_warned)
                {
                    _warned = true;
                    LrlLog.Error("dark-contact prefix failed - dark contacts stay silent", ex);
                }
            }
        }

        private static void Finalizer(GroupObservation observation, object __state)
        {
            if (__state == null || observation == null || PerceivedField == null)
                return;

            try
            {
                PerceivedField.SetValue(observation, __state);
            }
            catch (Exception ex)
            {
                if (!_warned)
                {
                    _warned = true;
                    LrlLog.Error("dark-contact finalizer failed to restore the contact type", ex);
                }
            }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Lookout/LrlDaylightTrim.cs
// ====================================================================================================
// Ausguck (Teil des UBOAT Toolkit): LrlDaylightTrim. Schalter: F10 > QoL > "Ausguck".

namespace UboatToolkit.Modules.Lrl
{
    using global::UnityEngine;

    internal static class LrlDaylightTrim
    {
        internal static float Weight(float visibility)
        {
            if (!LrlSettings.DaylightTrimEnabled)
                return 0f;
            if (float.IsNaN(visibility) || float.IsInfinity(visibility))
                return 0f;

            var start = LrlSettings.DaylightTrimStartVisibility;
            var full = LrlSettings.DaylightTrimFullVisibility;
            if (full <= start)
                return visibility >= full ? 1f : 0f;

            return Mathf.Clamp01((visibility - start) / (full - start));
        }

        internal static float RangeFactor(float visibility)
        {
            return Mathf.Lerp(1f, LrlSettings.DaylightRangeScale, Weight(visibility));
        }

        internal static float SmokeFactor(float visibility)
        {
            return Mathf.Lerp(1f, LrlSettings.DaylightSmokeScale, Weight(visibility));
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Lookout/LrlDetectionAudit.cs
// ====================================================================================================
// Ausguck (Teil des UBOAT Toolkit): LrlDetectionAudit. Schalter: F10 > QoL > "Ausguck".

namespace UboatToolkit.Modules.Lrl
{
    using global::System;
    using global::System.Reflection;
    using global::HarmonyLib;
    using global::UBOAT.Game.Sandbox;
    using global::UnityEngine;

    internal static class LrlDetectionAudit
    {
        internal const int IndirectObservation = 4;
        internal const int DirectObservation = 2;
        internal const int Hydrophone = 8;

        internal static bool InSpawner;
        internal static bool SpawnerVisual;

        private static bool _warned;

        internal static void Warn(string where, Exception ex)
        {
            if (_warned)
                return;
            _warned = true;
            LrlLog.Error("detection audit " + where + " failed - disabled for this session", ex);
        }

        internal static bool Disabled
        {
            get { return _warned; }
        }

        internal static void Report(SandboxPlayerWolfpack wolfpack, SandboxGroup group,
            int methodsBefore, int methodsAfter)
        {
            var delta = group.Position - wolfpack.Position;
            var distKm = delta.magnitude;
            var bearing = Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg;
            if (bearing < 0f)
                bearing += 360f;

            string source;
            if (InSpawner)
                source = SpawnerVisual ? "spawner(visual)" : "spawner(hydrophone)";
            else
                source = "search";

            var weather = LrlPlayer.InjectedWeather;
            var vis = weather != null ? weather.VisualDetectabilityFactors : float.NaN;
            var searchKm = float.NaN;
            if (!float.IsNaN(vis) && LrlRuntime.GroupIsReady(wolfpack))
                searchKm = wolfpack.SightRange * vis * vis;

            var det = LrlRuntime.TargetDetectability(group);
            var apparent = group.GetApparentHeight();
            var occl = LrlRuntime.OcclusionFactor(distKm * 1000f, wolfpack.GetObserverHorizonDistance(), apparent);

            var bonus = LrlConvoyNightBonus.Factor(group);

            var reachKm = float.NaN;
            if (!float.IsNaN(searchKm) && !float.IsNaN(det) && !float.IsNaN(occl))
                reachKm = searchKm * bonus * det * Mathf.Sqrt(Mathf.Max(0f, 1f - occl));

            var nearKm = float.NaN;
            var ship = LrlPlayer.Ship;
            if (ship != null && !float.IsNaN(vis))
            {
                var baseKm = (float)ship.DirectSightRange * 0.001f * vis;
                nearKm = baseKm + group.GetEstimatedOccupiedRadius() * 0.5f * Mathf.InverseLerp(0f, 0.3f, baseKm);
            }

            string verdict;
            if (InSpawner) verdict = "placed by the spawner, no sighting test";
            else if (!float.IsNaN(reachKm) && distKm <= reachKm * 1.02f) verdict = "within reach";
            else if (!float.IsNaN(nearKm) && distKm <= nearKm * 1.02f) verdict = "within close-range sight";
            else if (float.IsNaN(reachKm)) verdict = "reach unknown";
            else verdict = "BEYOND REACH";

            LrlLog.Info(string.Format(
                "new lookout observation: {0} dist={1:0.0}km bearing={2:000} source={3} obs={4}->{5}  " +
                "vis={6:0.00} searchRadius={7:0.0}km det={8:0.00} apparent={9:0.0}m occl={10:0.00} reach={11:0.0}km " +
                "closeSight={12:0.0}km convoyNightBonus=x{14:0.00} -> {13}",
                group.name, distKm, bearing, source,
                LrlRuntime.DescribeMethods(methodsBefore), LrlRuntime.DescribeMethods(methodsAfter),
                vis, searchKm, det, apparent, occl, reachKm, nearKm, verdict, bonus));
        }
    }

    [HarmonyPatch]
    internal static class LrlEncounterSpawnerTag
    {
        private static MethodBase _target;

        private static bool Prepare()
        {
            var t = UboatToolkit.Util.FindType("UBOAT.Game.Scene.Tasks.AdditionalEncountersController");
            _target = t != null ? AccessTools.Method(t, "TrySpawnGroup", new[] { typeof(bool) }) : null;
            if (_target == null && LrlSettings.DebugLogging)
                LrlLog.Warn("AdditionalEncountersController.TrySpawnGroup not found - spawned contacts will read as source=search");
            return _target != null;
        }

        private static MethodBase TargetMethod()
        {
            return _target;
        }

        private static void Prefix(bool visualContact)
        {
            LrlDetectionAudit.InSpawner = true;
            LrlDetectionAudit.SpawnerVisual = visualContact;
        }

        private static void Finalizer()
        {
            LrlDetectionAudit.InSpawner = false;
        }
    }

    [HarmonyPatch(typeof(SandboxGroup), "AddObservation")]
    internal static class LrlDetectionAuditPatch
    {
        private static void Prefix(SandboxGroup __instance, SandboxGroup group,
            GroupDetectionMethod detectionMethod, out int __state)
        {
            __state = -1;

            try
            {
                if (!LrlSettings.Enabled || !LrlSettings.DebugLogging || LrlDetectionAudit.Disabled)
                    return;
                if (!(__instance is SandboxPlayerWolfpack) || group == null)
                    return;

                var adding = (int)detectionMethod;
                if (!LrlDetectionAudit.InSpawner && (adding & LrlDetectionAudit.IndirectObservation) == 0)
                    return;

                var existing = __instance.GetObservation(group);
                var before = existing == null ? 0 : (int)existing.DetectionMethod;

                if (!LrlDetectionAudit.InSpawner &&
                    (before & (LrlDetectionAudit.IndirectObservation | LrlDetectionAudit.DirectObservation)) != 0)
                    return;

                __state = before;
            }
            catch (Exception ex)
            {
                __state = -1;
                LrlDetectionAudit.Warn("prefix", ex);
            }
        }

        private static void Postfix(SandboxGroup __instance, SandboxGroup group,
            GroupObservation __result, int __state)
        {
            if (__state < 0 || __result == null)
                return;

            try
            {
                LrlDetectionAudit.Report((SandboxPlayerWolfpack)__instance, group,
                    __state, (int)__result.DetectionMethod);
            }
            catch (Exception ex)
            {
                LrlDetectionAudit.Warn("postfix", ex);
            }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Lookout/LrlHiddenFloor.cs
// ====================================================================================================
// Ausguck (Teil des UBOAT Toolkit): LrlHiddenFloor. Schalter: F10 > QoL > "Ausguck".

namespace UboatToolkit.Modules.Lrl
{
    using global::System;
    using global::System.Collections.Generic;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Items;
    using global::UnityEngine;

    internal static class LrlHiddenFloor
    {
        private const string HiddenModifierName = "Hidden";

        private static readonly List<ObservationSpot> Buffer = new List<ObservationSpot>();

        private static bool _warned;

        internal static float LastRawKm { get; private set; }
        internal static float LastFlooredKm { get; private set; }

        internal static bool Active
        {
            get { return LrlSettings.Enabled && LrlSettings.FloorHiddenForGroupDetection && LrlSettings.HiddenFloor > 0f; }
        }

        private static int _cachedFrame = -1;
        private static PlayerShip _cachedShip;
        private static float _cachedKm;

        internal static float MaxGroupRangeKm(PlayerShip ship)
        {
            if (ship == null)
                return 0f;

            if (_cachedFrame == Time.frameCount && ReferenceEquals(_cachedShip, ship))
                return _cachedKm;

            var floor = Active ? Mathf.Clamp01(LrlSettings.HiddenFloor) : 0f;

            try
            {
                Buffer.Clear();
                LrlProviders.GetAll(ship, Buffer);

                var raw = 0f;
                var floored = 0f;

                for (var i = 0; i < Buffer.Count; i++)
                {
                    var spot = Buffer[i];
                    if (spot == null)
                        continue;

                    var p = spot.GroupDetectionRange;
                    if (p == null)
                        continue;

                    var value = p.Value;
                    if (value > raw)
                        raw = value;

                    var lifted = value;

                    if (floor > 0f)
                    {
                        var hidden = p.GetScaleModifier(HiddenModifierName, false);
                        if (hidden != null)
                        {
                            var h = hidden.Value;
                            if (h <= 0.0001f)
                                continue;          // optic fully under: stays blind
                            if (h < floor)
                                lifted = p.GetValueWithModifierOverride(hidden, floor);
                        }
                    }

                    if (lifted > floored)
                        floored = lifted;
                }

                Buffer.Clear();

                LastRawKm = raw;
                LastFlooredKm = floored;

                _cachedShip = ship;
                _cachedFrame = Time.frameCount;
                _cachedKm = floored;
                return floored;
            }
            catch (Exception ex)
            {
                Buffer.Clear();
                if (!_warned)
                {
                    _warned = true;
                    LrlLog.Error("Hidden floor failed - falling back to vanilla provider ranges", ex);
                }

                _cachedShip = ship;
                _cachedFrame = Time.frameCount;
                _cachedKm = 0f;
                return 0f;
            }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Lookout/LrlHost.cs
// ====================================================================================================
// Ausguck (Teil des UBOAT Toolkit): reagiert, wenn der Hauptschalter "Ausguck" im F10-Menü umgelegt wird
// (wie im Original sofort, ohne Neustart): beim Ausschalten wird eine gesetzte Zeitraffer-Bremse gelöst
// und die wartenden Meldungen werden verworfen.
namespace UboatToolkit.Modules.Lrl
{
    public class LrlHost : ModuleBase
    {
        private bool? last;

        public override void Tick()
        {
            bool on = Settings.B("Lookout");
            if (last.HasValue && last.Value != on) LrlInstall.SetEnabled(on);
            last = on;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Lookout/LrlInstall.cs
// ====================================================================================================
// Ausguck (Teil des UBOAT Toolkit): LrlInstall. Schalter: F10 > QoL > "Ausguck".

namespace UboatToolkit.Modules.Lrl
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::DWS.Common.InjectionFramework;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UnityEngine.SceneManagement;

    internal static class LrlInstall
    {
        internal const string ModuleId = "LongRangeLookout";

        private static GameObject _host;
        private static bool _subscribed;

        private static readonly Type[] PatchClasses =
        {
            typeof(LrlSightRangePatch),
            typeof(LrlApparentHeightPatch),
            typeof(LrlAcceptancePatch),
            typeof(LrlConvoyNightBonusPatch),
            typeof(LrlKnownContactCalloutPatch),
            typeof(LrlDarkContactPatch),
            typeof(LrlCalloutContextPatch),
            typeof(LrlCalloutRetryHook),
            typeof(LrlEncounterSpawnerTag),
            typeof(LrlDetectionAuditPatch),
            typeof(LrlReportAudit),
            typeof(LrlContactNoticeAudit),
        };

        internal static void Install(Harmony harmony)
        {
            LrlSettings.Load();
            try
            {
                var existing = UnityEngine.Object.FindObjectOfType<LrlRuntime>();
                if (existing != null)
                {
                    _host = existing.gameObject;
                }
                else
                {
                    _host = new GameObject("UboatToolkitLookoutRuntime");
                    UnityEngine.Object.DontDestroyOnLoad(_host);
                    _host.AddComponent<LrlRuntime>();
                }
            }
            catch (Exception ex) { LrlLog.Error("could not start the runtime", ex); }

            int patched = 0;
            foreach (Type t in PatchClasses)
            {
                try
                {
                    List<MethodInfo> done = harmony.CreateClassProcessor(t).Patch();
                    if (done == null) continue;
                    LrlLog.Info(PatchedLine(t, done));
                    patched += done.Count;
                }
                catch (Exception ex) { LrlLog.Error("could not patch " + t.Name + " - that feature stays vanilla", ex); }
            }
            if (patched == 0) LrlLog.Warn("no methods were patched - the sight range will be vanilla");

            LrlLog.Info(string.Format(
                "geladen (v{0}) - {1}, group sight range x{2:0.000}, smoke height {3}, dark contact reports {4}",
                UboatToolkit.Modules.CrewAi.DiagLog.ManifestVersion(),
                LrlSettings.Enabled ? "enabled" : "disabled",
                LrlSettings.GroupSightRangeMultiplier,
                LrlSettings.SmokeHeightEnabled ? "on (all groups, player included)" : "off",
                LrlSettings.ReportDarkContacts ? "on" : "off"));

            if (!_subscribed)
            {
                _subscribed = true;
                SceneEventsListener.OnSceneAwake += OnScene;
                SceneEventsListener.OnSceneStart += OnScene;
            }
        }

        internal static void SetEnabled(bool on)
        {
            LrlSettings.Load();
            if (on) return;
            try { LrlTimeCompressionGuard.Release(LrlPlayer.TimeCompression); }
            catch (Exception ex) { LrlLog.Error("could not release the time compression cap on switch-off", ex); }
            LrlCalloutRetry.Clear();
        }

        internal static string PatchedLine(Type patchClass, IList<MethodInfo> done)
        {
            int n = done == null ? 0 : done.Count;
            return "patched " + patchClass.Name + " (" + n + " method" + (n == 1 ? "" : "s") + ")";
        }

        internal static bool Verbose { get { return UboatToolkit.Settings.B("LookoutLog"); } }

        private static void OnScene(Scene scene)
        {
            LrlPlayer.TryInject();
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Lookout/LrlKnownContactCalloutPatch.cs
// ====================================================================================================
// Ausguck (Teil des UBOAT Toolkit): LrlKnownContactCalloutPatch. Schalter: F10 > QoL > "Ausguck".

namespace UboatToolkit.Modules.Lrl
{
    using global::System;
    using global::System.Reflection;
    using global::HarmonyLib;
    using global::UBOAT.Game.Sandbox;

    [HarmonyPatch(typeof(SandboxGroup), "AddObservation")]
    internal static class LrlKnownContactCalloutPatch
    {
        private const int DirectObservation = 2;
        private const int IndirectObservation = 4;

        private const int FunnelSmoke = 2;
        private const int Masts = 3;
        private const int Contrail = 4;

        private static readonly MethodInfo SayReport =
            AccessTools.Method(typeof(SandboxPlayerWolfpack), "SayGroupDetectionReport");

        private static readonly FieldInfo PerceivedField =
            AccessTools.Field(typeof(GroupObservation), "perceivedContactType");

        private static readonly FieldInfo DarknessField =
            AccessTools.Field(typeof(GroupObservation), "wasSpottedInDarkness");

        private static bool _warned;
        private static bool _speaking;

        private static void Prefix(SandboxGroup __instance, SandboxGroup group,
            GroupDetectionMethod detectionMethod, out GroupObservation __state)
        {
            __state = null;

            try
            {
                if (!LrlSettings.Enabled || !LrlSettings.CalloutKnownContacts || _speaking)
                    return;
                if (!(__instance is SandboxPlayerWolfpack) || group == null)
                    return;
                if (((int)detectionMethod & IndirectObservation) == 0)
                    return;

                var existing = __instance.GetObservation(group);
                if (existing == null)
                    return;   // brand new: vanilla calls this one out itself

                if (((int)existing.DetectionMethod & (DirectObservation | IndirectObservation)) != 0)
                    return;   // already seen by eye

                __state = existing;
            }
            catch (Exception ex)
            {
                __state = null;
                Warn("prefix", ex);
            }
        }

        private static void Postfix(SandboxGroup __instance, SandboxGroup group,
            GroupObservation __result, GroupObservation __state)
        {
            if (__state == null || _speaking || !ReferenceEquals(__result, __state))
                return;
            if (((int)__state.DetectionMethod & IndirectObservation) == 0)
                return;
            if (SayReport == null || PerceivedField == null)
                return;

            object original = null;
            var swapped = false;

            try
            {
                var type = LookoutContactType(group, __state);
                if (type < 0)
                    return;

                original = PerceivedField.GetValue(__state);
                var forced = Enum.ToObject(PerceivedField.FieldType, type);
                PerceivedField.SetValue(__state, forced);
                swapped = true;

                _speaking = true;
                SayReport.Invoke(null, new object[] { __instance, __state });

                LrlLog.Info("known contact first seen by lookouts - calling it out as " + forced +
                            " (it was previously known only as " + original + ")");
            }
            catch (Exception ex)
            {
                Warn("postfix", ex);
            }
            finally
            {
                _speaking = false;
                if (swapped)
                {
                    try { PerceivedField.SetValue(__state, original); }
                    catch (Exception ex) { Warn("restore", ex); }
                }
            }
        }

        private static int LookoutContactType(SandboxGroup group, GroupObservation observation)
        {
            if (group == null)
                return -1;
            if (group.IsAirborne)
                return Contrail;

            var mobile = group as SandboxMobileGroup;
            if (mobile == null || group.Type == "Wreck")
                return -1;

            var dark = DarknessField != null && (bool)DarknessField.GetValue(observation);
            if (dark)
                return Masts;   // a silhouette, as LrlDarkContactPatch does

            return mobile.Velocity > 0f ? FunnelSmoke : Masts;
        }

        private static void Warn(string where, Exception ex)
        {
            if (_warned)
                return;
            _warned = true;
            LrlLog.Error("known-contact callout " + where + " failed - callouts for HQ-known ships off this session", ex);
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Lookout/LrlLog.cs
// ====================================================================================================
// Ausguck (Teil des UBOAT Toolkit): LrlLog. Schalter: F10 > QoL > "Ausguck".

namespace UboatToolkit.Modules.Lrl
{
    using global::System;
    using global::System.Collections.Generic;
    using global::UnityEngine;

    internal static class LrlLog
    {
        private const string Prefix = "[UboatToolkit Ausguck] ";

        private static readonly Dictionary<string, float> LastLogged = new Dictionary<string, float>();

        internal static void Info(string message)
        {
            Debug.Log(Prefix + message);
        }

        internal static void Warn(string message)
        {
            Debug.LogWarning(Prefix + message);
        }

        internal static void Error(string message, Exception ex)
        {
            Debug.LogError(Prefix + message);
            if (ex != null)
                Debug.LogException(ex);
        }

        internal static void Throttled(string key, float seconds, string message)
        {
            if (!ShouldLog(key, seconds))
                return;

            Debug.Log(Prefix + message);
        }

        internal static bool ShouldLog(string key, float seconds)
        {
            var now = Time.unscaledTime;

            float last;
            if (LastLogged.TryGetValue(key, out last) && now - last < seconds)
                return false;

            LastLogged[key] = now;
            return true;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Lookout/LrlOrphanScrub.cs
// ====================================================================================================
// Ausguck (Teil des UBOAT Toolkit): LrlOrphanScrub. Schalter: F10 > QoL > "Ausguck".

namespace UboatToolkit.Modules.Lrl
{
    using global::System;
    using global::System.Collections.Generic;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Items;
    using global::UnityEngine;

    internal static class LrlOrphanScrub
    {
        private const string LegacyModifierName = "LongRangeLookout.GroupSightRange";

        private static readonly List<ObservationSpot> SpotBuffer = new List<ObservationSpot>();
        private static readonly List<Modifier> Doomed = new List<Modifier>();

        internal static int RemovedCount { get; private set; }

        internal static void Sweep(PlayerShip ship)
        {
            if (ship == null)
                return;

            LrlProviders.GetAll(ship, SpotBuffer);

            for (var i = 0; i < SpotBuffer.Count; i++)
            {
                var spot = SpotBuffer[i];

                SweepParameter(spot, spot.GroupDetectionRange);
            }

            SpotBuffer.Clear();
        }

        private static void SweepParameter(ObservationSpot spot, Parameter parameter)
        {
            if (parameter == null)
                return;

            var scales = parameter.ScaleModifiers;
            if (scales == null || scales.Count == 0)
                return;

            Doomed.Clear();

            for (var i = 0; i < scales.Count; i++)
            {
                if (IsOurs(scales[i]))
                    Doomed.Add(scales[i]);
            }

            for (var i = 0; i < Doomed.Count; i++)
            {
                var modifier = Doomed[i];

                LrlLog.Info(string.Format(
                    "scrub: removing legacy modifier from {0}.GroupSightRange - name=\"{1}\" value={2:0.000}",
                    spot.Name, modifier.Name, modifier.Value));

                modifier.Value = 1f;
                scales.Remove(modifier);

                RemovedCount++;
            }

            Doomed.Clear();
        }

        private static bool IsOurs(Modifier modifier)
        {
            if (modifier == null)
                return false;

            if (LegacyModifierName.Equals(modifier.Name))
                return true;

            if (modifier.GetType() != typeof(Modifier))
                return false;

            return Mathf.Abs(modifier.Value - LrlSettings.LegacyMultiplier) < 0.0005f;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Lookout/LrlPlayer.cs
// ====================================================================================================
// Ausguck (Teil des UBOAT Toolkit): LrlPlayer. Schalter: F10 > QoL > "Ausguck".

namespace UboatToolkit.Modules.Lrl
{
    using global::System;
    using global::System.Reflection;
    using global::DWS.Common.InjectionFramework;
    using global::UBOAT.Game.Core.Time;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Environment;
    using global::UnityEngine;

    internal static class LrlPlayer
    {
#pragma warning disable 649
        [Inject]
        private static PlayerShip injectedPlayerShip;

        [Inject]
        private static Weather injectedWeather;

        [Inject]
        private static TimeCompressionController injectedTimeCompression;
#pragma warning restore 649

        internal static TimeCompressionController TimeCompression
        {
            get { return injectedTimeCompression; }
        }

        internal static void TryInject()
        {
            try
            {
                InjectionFramework.Instance.InjectIntoAssembly(Assembly.GetExecutingAssembly());

                if (injectedPlayerShip != null)
                    LrlLog.Info("player ship bound via injection");
                else
                    LrlLog.Throttled("ship-unbound", 30f, "injection ran but PlayerShip is still null");
            }
            catch (Exception ex)
            {
                LrlLog.Error("injection failed", ex);
            }
        }

        internal static PlayerShip Ship
        {
            get
            {
                if (injectedPlayerShip != null)
                    return injectedPlayerShip;

                return UnityEngine.Object.FindObjectOfType<PlayerShip>();
            }
        }

        internal static Weather Weather
        {
            get
            {
                if (injectedWeather != null)
                    return injectedWeather;

                return UnityEngine.Object.FindObjectOfType<Weather>();
            }
        }

        internal static Weather InjectedWeather
        {
            get { return injectedWeather; }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Lookout/LrlPlayerStation.cs
// ====================================================================================================
// Ausguck (Teil des UBOAT Toolkit): LrlPlayerStation. Schalter: F10 > QoL > "Ausguck".

namespace UboatToolkit.Modules.Lrl
{
    using global::System;
    using global::System.Reflection;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Items;
    using global::UnityEngine;

    internal static class LrlPlayerStation
    {
        private const string GenericModeName = "Generic";

        private static bool _resolved;
        private static bool _usable;
        private static string _whyNot = "not resolved yet";

        private static PropertyInfo _cameraInstance;
        private static FieldInfo _cameraInstanceField;
        private static PropertyInfo _currentMode;
        private static PropertyInfo _externalController;
        private static Type _genericControllerType;
        private static FieldInfo _cameraGenericController;
        private static bool _externalControllerIsStatic;

        private static int _cachedFrame = -1;
        private static ObservationSpot _cachedSpot;
        private static Component _cameraComponent;
        private static readonly System.Collections.Generic.List<ObservationSpot> HintBuffer =
            new System.Collections.Generic.List<ObservationSpot>();
        private static string _cachedText = "unknown";

        private static void Resolve()
        {
            if (_resolved)
                return;

            _resolved = true;

            try
            {
                var cameraType = UboatToolkit.Util.FindType("UBOAT.Game.Scene.Camera.MainCamera");
                if (cameraType == null)
                {
                    _whyNot = "MainCamera type not found";
                    return;
                }

                _cameraInstance = AccessTools.Property(cameraType, "Instance");
                _cameraInstanceField = AccessTools.Field(cameraType, "Instance");
                _currentMode = AccessTools.Property(cameraType, "CurrentMode");

                _genericControllerType =
                    UboatToolkit.Util.FindType("UBOAT.Game.Scene.Camera.GenericCameraController");
                if (_genericControllerType == null)
                {
                    _whyNot = "GenericCameraController type not found";
                    return;
                }

                _cameraGenericController = AccessTools.Field(cameraType, "genericController");

                _externalController = AccessTools.Property(_genericControllerType, "ExternalController");
                if (_externalController == null)
                {
                    _whyNot = "ExternalController property not found";
                    return;
                }

                var getter = _externalController.GetGetMethod(true);
                _externalControllerIsStatic = getter != null && getter.IsStatic;

                if (_currentMode == null)
                {
                    _whyNot = "MainCamera.CurrentMode not found";
                    return;
                }

                _usable = true;
                _whyNot = null;
            }
            catch (Exception ex)
            {
                _whyNot = "resolve threw: " + ex.GetType().Name;
            }
        }

        private static object CameraInstance()
        {
            if (_cameraInstance != null)
            {
                var getter = _cameraInstance.GetGetMethod(true);
                if (getter != null && getter.IsStatic)
                    return _cameraInstance.GetValue(null, null);
            }

            if (_cameraInstanceField != null && _cameraInstanceField.IsStatic)
                return _cameraInstanceField.GetValue(null);

            return null;
        }

        private static void Refresh()
        {
            if (_cachedFrame == Time.frameCount)
                return;

            _cachedFrame = Time.frameCount;
            _cachedSpot = null;
            _cameraComponent = null;
            _cachedText = "unknown";

            Resolve();

            if (!_usable)
            {
                _cachedText = "unknown (" + _whyNot + ")";
                return;
            }

            var stage = "start";

            try
            {
                stage = "MainCamera.Instance";
                var camera = CameraInstance();

                if (camera == null || IsDeadUnityObject(camera))
                {
                    _cachedText = "unknown (no live MainCamera instance)";
                    return;
                }

                _cameraComponent = camera as Component;

                stage = "MainCamera.CurrentMode";
                var mode = _currentMode.GetValue(camera, null);
                var modeText = mode == null ? "null" : mode.ToString();

                if (!GenericModeName.Equals(modeText))
                {
                    _cachedText = "none (camera mode " + modeText + ")";
                    return;
                }

                stage = "GenericCameraController.ExternalController";
                object controller = null;
                try
                {
                    if (_externalControllerIsStatic)
                    {
                        controller = _externalController.GetValue(null, null);
                    }
                    else
                    {
                        object instance = null;

                        if (_cameraGenericController != null)
                            instance = _cameraGenericController.GetValue(camera);

                        if (instance == null || IsDeadUnityObject(instance))
                            instance = UnityEngine.Object.FindObjectOfType(_genericControllerType);

                        if (instance != null && !IsDeadUnityObject(instance))
                            controller = _externalController.GetValue(instance, null);
                    }
                }
                catch (Exception inner)
                {
                    var realInner = inner is TargetInvocationException && inner.InnerException != null
                        ? inner.InnerException
                        : inner;
                    _cachedText = "none (camera mode Generic; ExternalController unreadable: " +
                                  realInner.GetType().Name + ")";
                    return;
                }

                if (controller == null)
                {
                    _cachedText = "none (camera mode Generic, no external controller)";
                    return;
                }

                var component = controller as Component;
                if (component == null)
                {
                    _cachedText = "unrecognised (" + controller.GetType().Name + ")";
                    return;
                }

                stage = "GetComponentInParent<ObservationSpot>";
                _cachedSpot = component.GetComponentInParent<ObservationSpot>();
                _cachedText = _cachedSpot != null
                    ? _cachedSpot.Name
                    : "a station that is not an ObservationSpot (" + component.GetType().Name + ")";
            }
            catch (Exception ex)
            {
                var real = ex is TargetInvocationException && ex.InnerException != null
                    ? ex.InnerException
                    : ex;
                _cachedText = "unknown (" + real.GetType().Name + " at " + stage + ")";
            }
        }

        private static bool IsDeadUnityObject(object o)
        {
            var unityObject = o as UnityEngine.Object;
            return unityObject != null ? unityObject == null : false;
        }

        internal static string CameraProximityHint(PlayerShip ship)
        {
            if (ship == null)
                return null;

            Refresh();

            var cam = _cameraComponent;
            if (cam == null)
                return null;

            try
            {
                var camPos = cam.transform.position;

                HintBuffer.Clear();
                LrlProviders.GetAll(ship, HintBuffer);

                ObservationSpot best = null;
                var bestDist = float.MaxValue;

                for (var i = 0; i < HintBuffer.Count; i++)
                {
                    var spot = HintBuffer[i];
                    if (spot == null)
                        continue;

                    var d = (spot.transform.position - camPos).magnitude;
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = spot;
                    }
                }

                HintBuffer.Clear();

                if (best == null)
                    return null;

                return string.Format("nearestSpotToCamera={0} ({1:0.0}m)", best.Name, bestDist);
            }
            catch (Exception ex)
            {
                HintBuffer.Clear();
                return "nearestSpotToCamera=unknown (" + ex.GetType().Name + ")";
            }
        }

        internal static string Describe()
        {
            Refresh();
            return _cachedText;
        }

        internal static bool NamedAStation
        {
            get
            {
                Refresh();
                return _cachedSpot != null;
            }
        }

        internal static bool IsAt(ObservationSpot spot)
        {
            if (spot == null)
                return false;

            Refresh();
            return ReferenceEquals(_cachedSpot, spot);
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Lookout/LrlProviders.cs
// ====================================================================================================
// Ausguck (Teil des UBOAT Toolkit): LrlProviders. Schalter: F10 > QoL > "Ausguck".

namespace UboatToolkit.Modules.Lrl
{
    using global::System.Collections.Generic;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Items;

    internal static class LrlProviders
    {
        internal static void GetAll(PlayerShip ship, List<ObservationSpot> results)
        {
            results.Clear();

            if (ship == null)
                return;

            var equipment = ship.Equipment;
            if (equipment == null)
                return;

            for (var i = 0; i < equipment.Count; i++)
            {
                var spot = equipment[i] as ObservationSpot;
                if (spot != null)
                    results.Add(spot);
            }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Lookout/LrlReportAudit.cs
// ====================================================================================================
// Ausguck (Teil des UBOAT Toolkit): LrlReportAudit. Schalter: F10 > QoL > "Ausguck".

namespace UboatToolkit.Modules.Lrl
{
    using global::System;
    using global::HarmonyLib;
    using global::UBOAT.Game.Sandbox;
    using global::UBOAT.Game.Scene.Entities;

    [HarmonyPatch(typeof(PlayerShip), "DetectionReportInternal")]
    internal static class LrlReportAudit
    {
        private static bool _warned;

        private static void Postfix(PlayerShip __instance, string statementId1,
            GroupDetectionMethod observationType, bool __result)
        {
            try
            {
                if (!LrlSettings.Enabled || !LrlSettings.DebugLogging || LrlCalloutRetry.Retrying)
                    return;
                if (statementId1 == null || !statementId1.StartsWith("Group Contact"))
                    return;

                if (__result)
                {
                    LrlLog.Info("callout VOICED: \"" + statementId1 + "\" (method " + (int)observationType + ")");
                }
                else
                {
                    LrlLog.Info("callout DROPPED: \"" + statementId1 + "\" (method " + (int)observationType +
                                ") - " + LrlCrewStations.Summarise(__instance, statementId1) +
                                (LrlCalloutRetry.Active
                                    ? ". Queued for retry."
                                    : ". Retry is off, so it is lost: the contact now exists, and a later sighting adds nothing that speaks."));
                }
            }
            catch (Exception ex)
            {
                if (!_warned)
                {
                    _warned = true;
                    LrlLog.Error("callout audit failed - disabled for this session", ex);
                }
            }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Lookout/LrlRuntime.cs
// ====================================================================================================
// Ausguck (Teil des UBOAT Toolkit): LrlRuntime. Schalter: F10 > QoL > "Ausguck".

namespace UboatToolkit.Modules.Lrl
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Text;
    using global::UBOAT.Game.Core.Serialization;
    using global::UBOAT.Game.Sandbox;
    using global::UBOAT.Game.Scene.Items;
    using global::UnityEngine;

    [NonSerializedInGameState]
    internal class LrlRuntime : MonoBehaviour
    {
        private const float ScrubInterval = 5.1f;

        private static readonly List<ObservationSpot> Spots = new List<ObservationSpot>();

        private float _nextScrub;
        private float _nextDebug;
        private int _lastRemovedCount;
        private bool _announcedPatchLive;
        private bool _announcedSmokeLive;
        private float _nextCacheClear;

        private void Update()
        {
            var now = Time.unscaledTime;

            if (!LrlSettings.Enabled)
            {
                LrlTimeCompressionGuard.Release(LrlPlayer.TimeCompression);
                return;
            }

            LrlWeatherResponse.RefreshLive(LrlPlayer.InjectedWeather);

            RunTimeCompressionGuard();

            LrlCalloutRetry.Tick(now);

            if (now >= _nextScrub)
            {
                _nextScrub = now + ScrubInterval;
                RunScrub();
            }

            if (LrlSettings.DebugLogging && LrlInstall.Verbose && now >= _nextDebug)
            {
                _nextDebug = now + Mathf.Max(1f, LrlSettings.DebugInterval);
                RunDiagnostics();
            }
        }

        private SandboxPlayerWolfpack _wolfpack;
        private float _nextWolfpackScan;
        private float _cachedSearchRadiusKm;
        private float _cachedMaxProviderKm;
        private SandboxGroup _repairedGroup;

        internal static SandboxPlayerWolfpack CurrentWolfpack;

        private static float GuardSearchRadiusKm(float maxProviderKm)
        {
            var v = LrlSightRangePatch.CachedVisibility;
            var comp = LrlWeatherResponse.RangeMultiplier(v);
            return maxProviderKm * LrlSettings.GroupSightRangeMultiplier * comp * v * v;
        }

        internal static bool GroupIsReady(SandboxGroup group)
        {
            if (group == null)
                return false;

            var entities = group.Entities;
            if (entities == null)
                return false;

            for (var i = 0; i < entities.Count; i++)
            {
                if (entities[i] != null && entities[i].Enabled)
                    return true;
            }

            return false;
        }

        private static bool RevalidateSightListener(SandboxGroup group)
        {
            if (GroupSightListenerField == null || group == null)
                return false;

            var listener = GroupSightListenerField.GetValue(group);
            if (listener == null)
                return false;

            var validate = HarmonyLib.AccessTools.Method(listener.GetType(), "ValidateEntities");
            if (validate == null)
                return false;

            validate.Invoke(listener, null);
            return true;
        }

        private void RepairSightListener(SandboxGroup group)
        {
            if (_repairedGroup == group)
                return;

            _repairedGroup = group;

            try
            {
                if (GroupSightListenerField == null)
                    return;

                var listener = GroupSightListenerField.GetValue(group);
                if (listener == null)
                    return;

                var validate = HarmonyLib.AccessTools.Method(listener.GetType(), "ValidateEntities");
                if (validate == null)
                    return;

                validate.Invoke(listener, null);
                LrlLog.Info("sight listener revalidated for the current player group");
            }
            catch (Exception ex)
            {
                LrlLog.Error("sight listener repair failed", ex);
            }
        }

        private static float MaxProviderGroupRangeKm(UBOAT.Game.Scene.Entities.PlayerShip ship)
        {
            if (ship == null)
                return 0f;

            LrlProviders.GetAll(ship, ProviderBuffer);

            var best = 0f;
            for (var i = 0; i < ProviderBuffer.Count; i++)
            {
                var p = ProviderBuffer[i].GroupDetectionRange;
                if (p != null && p.Value > best)
                    best = p.Value;
            }

            ProviderBuffer.Clear();
            return best;
        }

        private static readonly List<ObservationSpot> ProviderBuffer = new List<ObservationSpot>();

        private static string DiagnoseBlindness(UBOAT.Game.Scene.Entities.PlayerShip ship)
        {
            if (ship == null)
                return "no player ship";

            LrlProviders.GetAll(ship, ProviderBuffer);

            var manned = 0;
            var mannedButHidden = 0;
            var withRange = 0;

            for (var i = 0; i < ProviderBuffer.Count; i++)
            {
                var spot = ProviderBuffer[i];
                var perf = spot.Performance;
                var group = spot.GroupDetectionRange;

                if (perf == null || perf.Value <= 0.001f)
                    continue;

                manned++;

                if (group == null)
                    continue;

                if (group.Value > 0.001f)
                    withRange++;
                else
                    mannedButHidden++;
            }

            ProviderBuffer.Clear();

            if (withRange > 0)
                return "listener latched - " + withRange + " provider(s) have range but the group does not";
            if (mannedButHidden > 0)
                return "optics submerged - " + mannedButHidden + " manned provider(s) with Hidden=0 (below periscope depth)";
            if (manned == 0)
                return "no manned observation provider";

            return "providers manned but contributing nothing";
        }

        private void RunTimeCompressionGuard()
        {
            if (!LrlSettings.TimeCompressionGuard)
            {
                LrlTimeCompressionGuard.Release(LrlPlayer.TimeCompression);
                return;
            }

            if (_wolfpack == null && Time.unscaledTime >= _nextWolfpackScan)
            {
                _nextWolfpackScan = Time.unscaledTime + 2f;
                _wolfpack = UnityEngine.Object.FindObjectOfType<SandboxPlayerWolfpack>();
                _cachedSearchRadiusKm = 0f;
                _cachedMaxProviderKm = 0f;
            }

            CurrentWolfpack = _wolfpack;

            if (!GroupIsReady(_wolfpack))
            {
                LrlTimeCompressionGuard.Release(LrlPlayer.TimeCompression);
                return;
            }

            RepairSightListener(_wolfpack);

            if (_cachedMaxProviderKm > 0f)
                _cachedSearchRadiusKm = GuardSearchRadiusKm(_cachedMaxProviderKm);

            LrlTimeCompressionGuard.Evaluate(_wolfpack, _cachedSearchRadiusKm);
        }

        private void RunScrub()
        {
            try
            {
                LrlOrphanScrub.Sweep(LrlPlayer.Ship);

                if (LrlOrphanScrub.RemovedCount != _lastRemovedCount)
                {
                    _lastRemovedCount = LrlOrphanScrub.RemovedCount;
                    LrlLog.Info(string.Format(
                        "scrub: {0} legacy modifier(s) removed so far this session", _lastRemovedCount));
                }

                if (!_announcedPatchLive && LrlSightRangePatch.HasRun)
                {
                    _announcedPatchLive = true;
                    LrlLog.Info(string.Format(
                        "group sight range patch is live - player group range x{0:0.000}",
                        LrlSettings.GroupSightRangeMultiplier));
                }

                if (!_announcedSmokeLive && LrlApparentHeightPatch.HasRun)
                {
                    _announcedSmokeLive = true;
                    LrlLog.Info("smoke-height patch is live - apparent height is being raised");
                }

                var w = LrlPlayer.Weather;
                if (w != null)
                    LrlSightRangePatch.CachedVisibility = w.VisualDetectabilityFactors;

                LrlWeatherResponse.Refresh(w);

                LrlConvoyNightBonus.RefreshPool(GroupIsReady(_wolfpack) ? _wolfpack : null);

                var maxProvider = LrlHiddenFloor.MaxGroupRangeKm(LrlPlayer.Ship);
                _cachedMaxProviderKm = maxProvider;
                if (maxProvider > 0f)
                {
                    _cachedSearchRadiusKm = GuardSearchRadiusKm(maxProvider);

                    if (GroupIsReady(_wolfpack) && _wolfpack.SightRange <= 0f)
                    {
                        if (RevalidateSightListener(_wolfpack))
                        {
                            LrlLog.Throttled("sight-relatch", 10f, string.Format(
                                "sight listener latched at 0 while providers read {0:0.00}km - revalidated, now {1:0.00}km",
                                maxProvider, _wolfpack.SightRange));
                        }
                    }
                }

                if (Time.unscaledTime >= _nextCacheClear)
                {
                    _nextCacheClear = Time.unscaledTime + 60f;
                    LrlSmokeModel.ClearCache();
                    LrlSightRangePatch.ClearCaches();
                }
            }
            catch (Exception ex)
            {
                LrlLog.Error("scrub pass failed", ex);
            }
        }

        private void RunDiagnostics()
        {
            try
            {
                var ship = LrlPlayer.Ship;
                if (ship == null)
                {
                    LrlLog.Throttled("diag-no-ship", 60f, "no player ship yet - diagnostics idle");
                    return;
                }

                var weather = LrlPlayer.Weather;
                var visibility = weather != null ? weather.VisualDetectabilityFactors : 1f;

                LrlWeatherDiagnostics.Log(weather);

                LrlProviders.GetAll(ship, Spots);

                if (Spots.Count == 0)
                {
                    LrlLog.Throttled("diag-no-spots", 60f, "player ship has no ObservationSpot equipment");
                    return;
                }

                var sb = DiagBuilder;
                sb.Length = 0;
                sb.Append("diagnostics  visibility=").Append(visibility.ToString("0.00"));
                sb.Append("  visibility^2=").Append((visibility * visibility).ToString("0.00"));

                var visEff = LrlWeatherResponse.EffectiveVisibility(visibility);
                if (visEff > visibility)
                    sb.Append("  [floored to ").Append(visEff.ToString("0.00"))
                      .Append(", x").Append(LrlWeatherResponse.RangeMultiplier(visibility).ToString("0.0"))
                      .Append(" on range]");

                var trimWeight = LrlDaylightTrim.Weight(visibility);
                if (trimWeight > 0f)
                    sb.Append("  [daylight trim ").Append((trimWeight * 100f).ToString("0"))
                      .Append("%: range x").Append(LrlDaylightTrim.RangeFactor(visibility).ToString("0.000"))
                      .Append(", smoke x").Append(LrlDaylightTrim.SmokeFactor(visibility).ToString("0.00"))
                      .Append("]");

                var wolfpack = UnityEngine.Object.FindObjectOfType<SandboxPlayerWolfpack>();

                if (wolfpack != null && !GroupIsReady(wolfpack))
                {
                    sb.Append("  resolvedGroupRange=<group not populated yet>");
                    wolfpack = null;
                }

                if (wolfpack != null)
                {
                    sb.Append("  resolvedGroupRange=").Append(wolfpack.SightRange.ToString("0.00")).Append("km");
                    sb.Append("  searchRadius=")
                      .Append((wolfpack.SightRange * visibility * visibility).ToString("0.00")).Append("km");
                    sb.Append("  observerHorizon=")
                      .Append((wolfpack.GetObserverHorizonDistance() / 1000f).ToString("0.00")).Append("km");

                    if (wolfpack.SightRange <= 0.01f)
                        sb.Append("  [BLIND: ").Append(DiagnoseBlindness(ship)).Append(']');

                    sb.Append("  playerStation=").Append(LrlPlayerStation.Describe());

                    if (!LrlPlayerStation.NamedAStation)
                    {
                        var hint = LrlPlayerStation.CameraProximityHint(ship);
                        if (!string.IsNullOrEmpty(hint))
                            sb.Append("  ").Append(hint);
                    }

                    LrlLog.Info(sb.ToString());
                    sb.Length = 0;

                    LogNearbyTargets(wolfpack, wolfpack.SightRange * visibility * visibility);
                    LrlLog.Info("  " + LrlAcceptancePatch.Report());
                    var convoyBonus = LrlConvoyNightBonus.Report();
                    if (convoyBonus != null)
                        LrlLog.Info("  " + convoyBonus);

                    var guardBand = wolfpack.SightRange * visibility * visibility * LrlSettings.GuardRadiusFactor;
                    LrlStepAudit.CrossCheckCoverage(wolfpack, guardBand);
                    LrlLog.Info("  " + LrlStepAudit.Describe());

                    LogSightListener(wolfpack);

                    string tier;
                    var smoke = LrlSmokeModel.GetSmokeHeight(wolfpack, out tier);
                    var mast = wolfpack.GetMaximumMastHeight();
                    var apparent = wolfpack.GetApparentHeight();

                    sb.Append("  own signature: mast=").Append(mast.ToString("0.0")).Append("m");
                    sb.Append(" smoke=").Append(smoke.ToString("0.0")).Append("m(").Append(tier).Append(')');
                    sb.Append(" apparent=").Append(apparent.ToString("0.0")).Append("m");
                    sb.Append(" maxSeenFrom=")
                      .Append(((wolfpack.GetObserverHorizonDistance() + Mathf.Sqrt(2f * 6371000f * apparent)) / 1000f)
                          .ToString("0.0")).Append("km");
                }
                else
                {
                    sb.Append("  resolvedGroupRange=<no sandbox wolfpack>");
                }

                LrlLog.Info(sb.ToString());

                for (var i = 0; i < Spots.Count; i++)
                {
                    var spot = Spots[i];

                    var groupRange = spot.GroupDetectionRange;
                    var directRange = spot.DetectionRange;
                    var performance = spot.Performance;

                    LrlLog.Info(string.Format(
                        "  provider={0} type={1} performance={2} groupRange={3}km directRange={4}m playerHere={5}",
                        spot.Name,
                        spot.GetType().Name,
                        performance != null ? performance.Value.ToString("0.000") : "n/a",
                        groupRange != null ? groupRange.Value.ToString("0.00") : "n/a",
                        directRange != null ? directRange.Value.ToString("0") : "n/a",
                        LrlPlayerStation.IsAt(spot) ? "YES" : "no"));

                    LrlLog.Info("    crew   " + LrlCrewStations.Describe(spot));

                    LrlLog.Info("    group  " + DescribeModifiers(groupRange));
                    LrlLog.Info("    direct " + DescribeModifiers(directRange));
                }

                Spots.Clear();
            }
            catch (Exception ex)
            {
                LrlLog.Error("diagnostic pass failed", ex);
            }
        }

        private const float EarthRadius = 6371000f;

        internal static float OcclusionFactor(float distM, float observerHorizonM, float apparentM)
        {
            if (float.IsNaN(apparentM) || float.IsInfinity(apparentM) || apparentM <= 0f)
                return float.NaN;

            var beyond = distM - observerHorizonM;
            if (beyond <= 0f)
                return 0f;

            var hidden = Mathf.Sqrt(beyond * beyond + EarthRadius * EarthRadius) - EarthRadius;
            return Mathf.Clamp01(hidden / apparentM);
        }

        internal static float TargetDetectability(SandboxGroup g)
        {
            if (g == null || !GroupIsReady(g))
                return float.NaN;

            if (g.IsGroupPublic)
                return 0f;

            var listener = g.VisualDetectability;
            if (listener == null)
                return float.NaN;

            return listener.Value * g.NpcDetectionModifier;
        }

        private static readonly string[] ListenerFieldNames =
        {
            "parameterName", "mode", "max",
            "parametersListIsValid", "resolvedValueIsValid", "resolvedValue"
        };

        private const int MaxTargetsLogged = 12;

        private static readonly StringBuilder MethodsBuilder = new StringBuilder();
        private static readonly StringBuilder ModifiersBuilder = new StringBuilder();
        private static readonly StringBuilder ListenerBuilder = new StringBuilder();
        private static readonly StringBuilder DiagBuilder = new StringBuilder();

        internal static string DescribeMethods(int m)
        {
            if (m == 0) return "none";
            var sb = MethodsBuilder;
            sb.Length = 0;
            if ((m & 1) != 0) sb.Append("HQ+");
            if ((m & 2) != 0) sb.Append("direct+");
            if ((m & 4) != 0) sb.Append("lookout+");
            if ((m & 8) != 0) sb.Append("hydrophone+");
            if ((m & 16) != 0) sb.Append("radioDF+");
            if ((m & 32) != 0) sb.Append("radar+");
            if ((m & 64) != 0) sb.Append("radarDetector+");
            if ((m & 128) != 0) sb.Append("console+");
            sb.Length--;
            return sb.ToString();
        }

        private static readonly List<SandboxGroup> Candidates = new List<SandboxGroup>();

        private const float TargetScanRadiusKm = 60f;

        private static void LogNearbyTargets(SandboxPlayerWolfpack wolfpack, float searchRadiusKm)
        {
            try
            {
                var origin = wolfpack.Position;
                var observerHorizon = wolfpack.GetObserverHorizonDistance();
                var logged = 0;

                var nearby = wolfpack.NearbyGroups;
                var nearbyCount = nearby == null ? 0 : nearby.Count;

                var ok = LrlSandboxGroups.GetNearest(origin, TargetScanRadiusKm, Candidates);

                if (!ok)
                {
                    LrlLog.Info("  targets: sandbox group list unreachable (" +
                                LrlSandboxGroups.LastError + ") - falling back to NearbyGroups, which holds " +
                                nearbyCount);
                    Candidates.Clear();
                    if (nearby != null)
                    {
                        for (var n = 0; n < nearby.Count; n++)
                            if (nearby[n] != null)
                                Candidates.Add(nearby[n]);
                    }
                }
                else
                {
                    LrlLog.Info(string.Format(
                        "  targets: {0} within {1:0}km of {2} groups in the world ({3} public); " +
                        "NearbyGroups holds {4}; logging up to {5}",
                        Candidates.Count, TargetScanRadiusKm, LrlSandboxGroups.LastTotal,
                        LrlSandboxGroups.LastPublic, nearbyCount, MaxTargetsLogged));
                }

                for (var i = 0; i < Candidates.Count && logged < MaxTargetsLogged; i++)
                {
                    var g = Candidates[i];
                    if (g == null || ReferenceEquals(g, wolfpack))
                        continue;

                    var distKm = (g.Position - origin).magnitude;

                    string tier;
                    var smoke = LrlSmokeModel.GetSmokeHeight(g, out tier);
                    var mast = g.GetMaximumMastHeight();
                    var apparent = g.GetApparentHeight();

                    var maxDistKm = float.IsNaN(apparent) || float.IsInfinity(apparent) || apparent <= 0f
                        ? float.NaN
                        : (observerHorizon + Mathf.Sqrt(2f * EarthRadius * apparent)) / 1000f;

                    LrlLog.Info(string.Format(
                        "  target{7} dist={0:0.0}km mast={1:0.0}m smoke={2:0.0}m({3}) apparent={4:0.0}m maxDist={5:0.0}km detected={6}",
                        distKm, mast, smoke, tier, apparent, maxDistKm, g.DetectedByPlayer,
                        (g.IsGroupPublic ? " [public]" : "") +
                        (nearby != null && nearby.Contains(g) ? " [nearby]" : "")));

                    var det = TargetDetectability(g);
                    var occl = OcclusionFactor(distKm * 1000f, observerHorizon, apparent);
                    var areaRadiusKm = g.AreaRadius;

                    var bonus = LrlConvoyNightBonus.Factor(g);
                    var groupRadiusKm = searchRadiusKm * bonus;

                    var reachKm = float.NaN;
                    if (!float.IsNaN(det) && !float.IsNaN(occl))
                        reachKm = groupRadiusKm * det * Mathf.Sqrt(Mathf.Max(0f, 1f - occl));

                    var verdict = "?";
                    if (!float.IsNaN(reachKm))
                    {
                        if (searchRadiusKm < 0.05f) verdict = "no range - see [BLIND] on the diagnostics line";
                        else if (det <= 0.001f) verdict = "det=0 (not visible to the sandbox at all)";
                        else if (distKm < reachKm) verdict = "QUALIFIES";
                        else if (groupRadiusKm < distKm) verdict = "radius short";
                        else if (occl >= 0.999f) verdict = "hull down";
                        else verdict = "detectability cut";
                    }

                    var observation = wolfpack.GetObservation(g);
                    var obsText = observation == null ? "none" : DescribeMethods((int)observation.DetectionMethod);

                    LrlLog.Info(string.Format(
                        "     areaRadius={0:0.0}km det={1:0.000} occl={2:0.000} " +
                        "searchRadius={3:0.0}km reach={4:0.0}km -> {5}  obs={6}{7}",
                        areaRadiusKm, det, occl, searchRadiusKm, reachKm, verdict, obsText,
                        bonus > 1.0001f ? string.Format("  convoyNightBonus=x{0:0.00}", bonus) : ""));

                    logged++;
                }

                if (logged == 0)
                    LrlLog.Info("  targets: no groups at all within " + TargetScanRadiusKm + " km");
            }
            catch (Exception ex)
            {
                LrlLog.Error("target dump failed", ex);
            }
        }

        private static readonly System.Reflection.FieldInfo GroupSightListenerField =
            HarmonyLib.AccessTools.Field(typeof(SandboxGroup), "sightRange");

        private static void LogSightListener(SandboxGroup wolfpack)
        {
            try
            {
                if (GroupSightListenerField == null)
                {
                    LrlLog.Throttled("no-sightrange-field", 120f,
                        "SandboxGroup.sightRange field not found - listener dump unavailable");
                    return;
                }

                var listener = GroupSightListenerField.GetValue(wolfpack);
                if (listener == null)
                {
                    LrlLog.Info("  sightRange listener is NULL on the player group");
                    return;
                }

                var t = listener.GetType();
                var sb = ListenerBuilder;
                sb.Length = 0;
                sb.Append("  listener");

                foreach (var name in ListenerFieldNames)
                {
                    var f = HarmonyLib.AccessTools.Field(t, name);
                    sb.Append(' ').Append(name).Append('=')
                      .Append(f == null ? "<missing>" : (f.GetValue(listener) ?? "null").ToString());
                }

                var sourcesField = HarmonyLib.AccessTools.Field(t, "sourceParameters");
                var sources = sourcesField != null
                    ? sourcesField.GetValue(listener) as System.Collections.IList
                    : null;

                sb.Append("  sources=").Append(sources == null ? "<null>" : sources.Count.ToString());
                LrlLog.Info(sb.ToString());

                if (sources != null)
                {
                    for (var i = 0; i < sources.Count && i < 12; i++)
                    {
                        var p = sources[i] as UBOAT.Game.Core.Data.Parameter;
                        LrlLog.Info("    source[" + i + "] " +
                            (p == null ? "<null or not a Parameter>"
                                       : p.Name + "=" + p.Value.ToString("0.000")));
                    }
                }
            }
            catch (Exception ex)
            {
                LrlLog.Error("listener dump failed", ex);
            }
        }

        private static string DescribeModifiers(UBOAT.Game.Core.Data.Parameter parameter)
        {
            if (parameter == null)
                return "n/a";

            var sb = ModifiersBuilder;
            sb.Length = 0;
            sb.Append("value=").Append(parameter.Value.ToString("0.000"));

            var deltas = parameter.DeltaModifiers;
            if (deltas != null && deltas.Count > 0)
            {
                sb.Append("  deltas[");
                for (var i = 0; i < deltas.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(deltas[i].Name).Append('=').Append(deltas[i].Value.ToString("0.000"));
                    if (deltas[i].Simulated) sb.Append("(sim)");
                }
                sb.Append(']');
            }

            var scales = parameter.ScaleModifiers;
            if (scales != null && scales.Count > 0)
            {
                sb.Append("  scales[");
                for (var i = 0; i < scales.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(scales[i].Name).Append('=').Append(scales[i].Value.ToString("0.000"));
                    if (scales[i].Simulated) sb.Append("(sim)");
                }
                sb.Append(']');
            }

            return sb.ToString();
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Lookout/LrlSandboxGroups.cs
// ====================================================================================================
// Ausguck (Teil des UBOAT Toolkit): LrlSandboxGroups. Schalter: F10 > QoL > "Ausguck".

namespace UboatToolkit.Modules.Lrl
{
    using global::System;
    using global::System.Collections;
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::HarmonyLib;
    using global::UBOAT.Game.Sandbox;
    using global::UnityEngine;

    internal static class LrlSandboxGroups
    {
        private static bool _resolved;
        private static PropertyInfo _instanceProp;
        private static FieldInfo _instanceField;
        private static MethodInfo _getGroups;
        private static MethodInfo _getPublicGroups;
        private static string _whyNot = "not resolved yet";

        internal static int LastTotal { get; private set; }
        internal static int LastPublic { get; private set; }
        internal static string LastError { get; private set; }

        private static void Resolve()
        {
            if (_resolved)
                return;

            _resolved = true;

            try
            {
                var t = typeof(Sandbox);
                _instanceProp = AccessTools.Property(t, "Instance");
                _instanceField = AccessTools.Field(t, "Instance");
                _getGroups = AccessTools.Method(t, "GetGroupsDirect");
                _getPublicGroups = AccessTools.Method(t, "GetPublicGroupsDirect");

                if (_getGroups == null)
                    _whyNot = "Sandbox.GetGroupsDirect not found";
                else if (_instanceProp == null && _instanceField == null)
                    _whyNot = "Sandbox.Instance not found";
                else
                    _whyNot = null;
            }
            catch (Exception ex)
            {
                _whyNot = "resolve threw: " + ex.GetType().Name;
            }
        }

        private static object Instance()
        {
            if (_instanceProp != null)
            {
                var getter = _instanceProp.GetGetMethod(true);
                if (getter != null && getter.IsStatic)
                    return _instanceProp.GetValue(null, null);
            }

            if (_instanceField != null && _instanceField.IsStatic)
                return _instanceField.GetValue(null);

            return null;
        }

        private static void Harvest(MethodInfo method, object instance, Vector2 origin,
                                    float maxKm, List<SandboxGroup> into, ref int counted)
        {
            if (method == null)
                return;

            var result = method.IsStatic
                ? method.Invoke(null, null)
                : method.Invoke(instance, null);

            var list = result as IEnumerable;
            if (list == null)
                return;

            foreach (var item in list)
            {
                var g = item as SandboxGroup;
                if (g == null)
                    continue;

                counted++;

                if ((g.Position - origin).magnitude <= maxKm)
                    into.Add(g);
            }
        }

        internal static bool GetNearest(Vector2 origin, float maxKm, List<SandboxGroup> results)
        {
            results.Clear();
            LastTotal = 0;
            LastPublic = 0;
            LastError = null;

            Resolve();

            if (_whyNot != null)
            {
                LastError = _whyNot;
                return false;
            }

            try
            {
                var instance = Instance();
                if (instance == null)
                {
                    LastError = "no Sandbox instance";
                    return false;
                }

                var total = 0;
                Harvest(_getGroups, instance, origin, maxKm, results, ref total);
                LastTotal = total;

                var pub = 0;
                Harvest(_getPublicGroups, instance, origin, maxKm, results, ref pub);
                LastPublic = pub;
                LastTotal += pub;

                results.Sort(delegate(SandboxGroup a, SandboxGroup b)
                {
                    var da = (a.Position - origin).sqrMagnitude;
                    var db = (b.Position - origin).sqrMagnitude;
                    return da.CompareTo(db);
                });

                return true;
            }
            catch (Exception ex)
            {
                LastError = "read threw: " + ex.GetType().Name;
                results.Clear();
                return false;
            }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Lookout/LrlSettings.cs
// ====================================================================================================
// Ausguck (Teil des UBOAT Toolkit): LrlSettings. Schalter: F10 > QoL > "Ausguck".
namespace UboatToolkit.Modules.Lrl
{
    internal static class LrlSettings
    {
        private static bool _offEnabled;
        internal static bool Enabled { get { return UboatToolkit.Settings.B("Lookout") && !_offEnabled; } set { _offEnabled = !value; } }

        internal static float GroupSightRangeMultiplier { get { float v = UboatToolkit.Settings.F("LookoutGroupRange"); return v > 0f ? v : 1.6923f; } }

        internal static float LegacyMultiplier = 1.6923f;

        private static bool _offCancelWeatherSquaring;
        internal static bool CancelWeatherSquaring { get { return UboatToolkit.Settings.B("LookoutWeatherOnce") && !_offCancelWeatherSquaring; } set { _offCancelWeatherSquaring = !value; } }

        internal static float MaxWeatherCompensation = 2f;

        internal static float VisibilityFloor = 0.40f;

        internal static float VisibilityFloorSoftness = 0.5f;

        private static bool _offStrengthenFog;
        internal static bool StrengthenFog { get { return UboatToolkit.Settings.B("LookoutFog") && !_offStrengthenFog; } set { _offStrengthenFog = !value; } }

        internal static float FogExtraExponent = 1f;

        private static bool _offTimeCompressionGuard;
        internal static bool TimeCompressionGuard { get { return UboatToolkit.Settings.B("LookoutTimeGuard") && !_offTimeCompressionGuard; } set { _offTimeCompressionGuard = !value; } }

        internal static float TimeCompressionCap = 150f;

        internal static float GuardRadiusFactor = 1.0f;

        internal static bool GuardIgnoresAllies = true;

        private static bool _offReportDarkContacts;
        internal static bool ReportDarkContacts { get { return UboatToolkit.Settings.B("LookoutDarkReports") && !_offReportDarkContacts; } set { _offReportDarkContacts = !value; } }

        private static bool _offFixInvertedAcceptance;
        internal static bool FixInvertedAcceptance { get { return UboatToolkit.Settings.B("LookoutFixRoll") && !_offFixInvertedAcceptance; } set { _offFixInvertedAcceptance = !value; } }

        private static bool _offCalloutKnownContacts;
        internal static bool CalloutKnownContacts { get { return UboatToolkit.Settings.B("LookoutKnownAgain") && !_offCalloutKnownContacts; } set { _offCalloutKnownContacts = !value; } }

        private static bool _offFloorHiddenForGroupDetection;
        internal static bool FloorHiddenForGroupDetection { get { return UboatToolkit.Settings.B("LookoutAwashView") && !_offFloorHiddenForGroupDetection; } set { _offFloorHiddenForGroupDetection = !value; } }

        internal static float HiddenFloor = 0.85f;

        private static bool _offRetryDroppedCallouts;
        internal static bool RetryDroppedCallouts { get { return UboatToolkit.Settings.B("LookoutRetry") && !_offRetryDroppedCallouts; } set { _offRetryDroppedCallouts = !value; } }

        internal static float RetryIntervalSeconds = 4f;

        internal static float RetryWindowSeconds = 90f;

        private static bool _offSmokeHeightEnabled;
        internal static bool SmokeHeightEnabled { get { return UboatToolkit.Settings.B("LookoutSmoke") && !_offSmokeHeightEnabled; } set { _offSmokeHeightEnabled = !value; } }

        internal static float MinimumSmokingSpeed = 2.0f;

        internal static float SpeedSlow = 6.0f;
        internal static float SpeedNormal = 12.0f;
        internal static float SpeedFast = 18.0f;

        internal static float SmokeBarelyMoving = 6f;
        internal static float SmokeSlow = 10f;
        internal static float SmokeNormal = 18f;
        internal static float SmokeFast = 28f;

        internal static float SmokePerExtraShip = 2f;

        internal static float SmokeCrippledBonus = 8f;

        internal static float SmokeGroupCap = 40f;

        internal static float SmokeCacheSeconds = 2f;

        private static bool _offDaylightTrimEnabled;
        internal static bool DaylightTrimEnabled { get { return UboatToolkit.Settings.B("LookoutDaylightTrim") && !_offDaylightTrimEnabled; } set { _offDaylightTrimEnabled = !value; } }

        internal static float DaylightTrimStartVisibility = 0.70f;

        internal static float DaylightTrimFullVisibility = 0.90f;

        internal static float DaylightRangeScale = 0.93f;

        internal static float DaylightSmokeScale = 0.60f;

        private static bool _offConvoyNightBonusEnabled;
        internal static bool ConvoyNightBonusEnabled { get { return UboatToolkit.Settings.B("LookoutConvoyNight") && !_offConvoyNightBonusEnabled; } set { _offConvoyNightBonusEnabled = !value; } }

        internal static float ConvoyNightBonusPerExtraShip = 0.03f;

        internal static float ConvoyNightBonusMax = 1.5f;

        internal static float ConvoyNightBonusFullVisibility = 0.20f;

        internal static float ConvoyNightBonusEndVisibility = 0.40f;

        internal static bool DebugLogging { get { return UboatToolkit.Settings.B("LookoutLog"); } }

        internal static float DebugInterval = 15f;

        private static bool _offWeatherFloorEnabled;
        internal static bool WeatherFloorEnabled { get { return UboatToolkit.Settings.B("LookoutWeatherFloor") && !_offWeatherFloorEnabled; } set { _offWeatherFloorEnabled = !value; } }

        internal static void Load()
        {
            _offTimeCompressionGuard = false;
        }

        internal static string Key(string field) { return LrlInstall.ModuleId + "." + field; }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Lookout/LrlSightRangePatch.cs
// ====================================================================================================
// Ausguck (Teil des UBOAT Toolkit): LrlSightRangePatch. Schalter: F10 > QoL > "Ausguck".

namespace UboatToolkit.Modules.Lrl
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::HarmonyLib;
    using global::UBOAT.Game.Sandbox;
    using global::UnityEngine;

    [HarmonyPatch(typeof(GroupParameterListener))]
    [HarmonyPatch("Value", MethodType.Getter)]
    internal static class LrlSightRangePatch
    {
        private const string SightParameterName = "GroupSightRange";

        private static readonly FieldInfo GroupField =
            AccessTools.Field(typeof(GroupParameterListener), "group");

        private static readonly FieldInfo ParameterNameField =
            AccessTools.Field(typeof(GroupParameterListener), "parameterName");

        private static bool _warned;

        internal static bool HasRun { get; private set; }

        private static GroupParameterListener _match;
        private static readonly HashSet<GroupParameterListener> Rejected =
            new HashSet<GroupParameterListener>();

        internal static float CachedVisibility = 1f;

        internal static void ClearCaches()
        {
            _match = null;
            Rejected.Clear();
        }

        private static void Postfix(GroupParameterListener __instance, ref float __result)
        {
            try
            {
                if (!LrlSettings.Enabled)
                    return;

                if (GroupField == null || ParameterNameField == null)
                {
                    if (!_warned)
                    {
                        _warned = true;
                        LrlLog.Warn("GroupParameterListener field layout changed - range unmodified");
                    }
                    return;
                }

                if (!ReferenceEquals(__instance, _match))
                {
                    if (Rejected.Contains(__instance))
                        return;

                    if (!(GroupField.GetValue(__instance) is SandboxPlayerWolfpack) ||
                        !SightParameterName.Equals(ParameterNameField.GetValue(__instance) as string))
                    {
                        Rejected.Add(__instance);
                        return;
                    }

                    _match = __instance;
                }

                if (LrlHiddenFloor.Active)
                {
                    var floored = LrlHiddenFloor.MaxGroupRangeKm(LrlPlayer.Ship);
                    if (floored > __result)
                    {
                        if (LrlLog.ShouldLog("hidden-floor", 30f))
                        {
                            LrlLog.Info(string.Format(
                                "Hidden floor lifted the group range {0:0.00} -> {1:0.00} km (awash optic)",
                                __result, floored));
                        }
                        __result = floored;
                    }
                }

                __result *= LrlSettings.GroupSightRangeMultiplier;

                __result *= LrlWeatherResponse.RangeMultiplier(CachedVisibility);

                HasRun = true;
            }
            catch (Exception ex)
            {
                if (!_warned)
                {
                    _warned = true;
                    LrlLog.Error("sight range postfix failed - range left unmodified", ex);
                }
            }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Lookout/LrlSmokeModel.cs
// ====================================================================================================
// Ausguck (Teil des UBOAT Toolkit): LrlSmokeModel. Schalter: F10 > QoL > "Ausguck".

namespace UboatToolkit.Modules.Lrl
{
    using global::System.Collections.Generic;
    using global::UBOAT.Game.Sandbox;
    using global::UBOAT.Game.Scene.Entities;
    using global::UnityEngine;

    internal static class LrlSmokeModel
    {
        private const EntityTypeCategory SmokeProducers =
            EntityTypeCategory.Submarine |
            EntityTypeCategory.Freighter |
            EntityTypeCategory.Destroyer |
            EntityTypeCategory.Cruiser |
            EntityTypeCategory.Battleship |
            EntityTypeCategory.EscortCarrier |
            EntityTypeCategory.FastAttackCraft |
            EntityTypeCategory.Tanker |
            EntityTypeCategory.Corvette |
            EntityTypeCategory.FishingBoat |
            EntityTypeCategory.Coaster |
            EntityTypeCategory.Trawler |
            EntityTypeCategory.PatrolBoat;

        private static readonly Dictionary<int, Entry> Cache = new Dictionary<int, Entry>();

        private struct Entry
        {
            public float Time;
            public float Height;
            public string Tier;
        }

        internal static void ClearCache()
        {
            Cache.Clear();
        }

        internal static float GetSmokeHeight(SandboxGroup group)
        {
            string tier;
            return GetSmokeHeight(group, out tier);
        }

        internal static float GetSmokeHeight(SandboxGroup group, out string tier)
        {
            tier = "none";

            if (group == null || !LrlSettings.SmokeHeightEnabled)
                return 0f;

            var id = group.GetInstanceID();
            var now = Time.unscaledTime;

            var trim = LrlDaylightTrim.SmokeFactor(LrlSightRangePatch.CachedVisibility);

            Entry cached;
            if (Cache.TryGetValue(id, out cached) && now - cached.Time < LrlSettings.SmokeCacheSeconds)
            {
                tier = cached.Tier;
                return cached.Height * trim;
            }

            var height = Compute(group, out tier);

            Cache[id] = new Entry { Time = now, Height = height, Tier = tier };
            return height * trim;
        }

        private static float Compute(SandboxGroup group, out string tier)
        {
            tier = "none";

            var entities = group.Entities;
            if (entities == null || entities.Count == 0)
                return 0f;

            var best = 0f;
            var smokingShips = 0;
            var anyCrippled = false;

            for (var i = 0; i < entities.Count; i++)
            {
                var entity = entities[i];
                if (entity == null)
                    continue;

                var type = entity.Type;
                if (type == null || (type.Category & SmokeProducers) == 0)
                    continue;

                var crippled = entity.IsCrippled;

                var speed = entity.Velocity != null ? Mathf.Abs(entity.Velocity.Value) : 0f;

                if (!crippled && speed < LrlSettings.MinimumSmokingSpeed)
                    continue;

                smokingShips++;
                if (crippled)
                    anyCrippled = true;

                var h = crippled
                    ? Mathf.Max(LrlSettings.SmokeSlow, TierHeight(speed))
                    : TierHeight(speed);

                if (h > best)
                {
                    best = h;
                    tier = TierName(speed, crippled);
                }
            }

            if (smokingShips == 0)
                return 0f;

            if (smokingShips > 1)
            {
                best += LrlSettings.SmokePerExtraShip * (smokingShips - 1);
                tier += "+convoy" + smokingShips;
            }

            if (anyCrippled)
                best += LrlSettings.SmokeCrippledBonus;

            return Mathf.Min(best, LrlSettings.SmokeGroupCap);
        }

        private static float TierHeight(float speed)
        {
            if (speed < LrlSettings.SpeedSlow) return LrlSettings.SmokeBarelyMoving;
            if (speed < LrlSettings.SpeedNormal) return LrlSettings.SmokeSlow;
            if (speed < LrlSettings.SpeedFast) return LrlSettings.SmokeNormal;
            return LrlSettings.SmokeFast;
        }

        private static string TierName(float speed, bool crippled)
        {
            var name = speed < LrlSettings.SpeedSlow ? "barely"
                     : speed < LrlSettings.SpeedNormal ? "slow"
                     : speed < LrlSettings.SpeedFast ? "normal"
                     : "fast";

            return crippled ? name + "+crippled" : name;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Lookout/LrlStepAudit.cs
// ====================================================================================================
// Ausguck (Teil des UBOAT Toolkit): LrlStepAudit. Schalter: F10 > QoL > "Ausguck".

namespace UboatToolkit.Modules.Lrl
{
    using global::System;
    using global::System.Collections.Generic;
    using global::UBOAT.Game.Sandbox;
    using global::UnityEngine;

    internal static class LrlStepAudit
    {
        private static readonly Dictionary<SandboxGroup, Vector2> LastPosition =
            new Dictionary<SandboxGroup, Vector2>();

        private static readonly List<SandboxGroup> CrossCheckBuffer = new List<SandboxGroup>();

        private static float _nextClear;

        internal static float MaxJumpKm { get; private set; }
        internal static int BandSkips { get; private set; }
        internal static int CoverageGaps { get; private set; }

        internal static void Reset()
        {
            LastPosition.Clear();
            MaxJumpKm = 0f;
            BandSkips = 0;
            CoverageGaps = 0;
        }

        internal static void Observe(SandboxGroup group, Vector2 playerPos, float thresholdKm)
        {
            if (group == null || thresholdKm <= 0f)
                return;

            var now = Time.unscaledTime;
            if (now >= _nextClear)
            {
                _nextClear = now + 60f;
                LastPosition.Clear();
            }

            var position = group.Position;

            Vector2 previous;
            if (!LastPosition.TryGetValue(group, out previous))
            {
                LastPosition[group] = position;
                return;
            }

            LastPosition[group] = position;

            var jump = (position - previous).magnitude;
            if (jump > MaxJumpKm)
                MaxJumpKm = jump;

            if (group.DetectedByPlayer)
                return;

            var beforeKm = (previous - playerPos).magnitude;
            var afterKm = (position - playerPos).magnitude;

            if (beforeKm <= thresholdKm || afterKm <= thresholdKm)
                return;

            var closestKm = DistanceToSegment(playerPos, previous, position);
            if (closestKm > thresholdKm)
                return;

            BandSkips++;

            if (LrlLog.ShouldLog("band-skip", 20f))
            {
                LrlLog.Warn(string.Format(
                    "OVERSHOOT: an undetected group crossed the {0:0.0}km detection band without being " +
                    "sampled inside it - {1:0.0}km -> {2:0.0}km in one step ({3:0.0}km of travel, closest " +
                    "approach {4:0.0}km). This is the failure the time-compression guard exists to prevent.",
                    thresholdKm, beforeKm, afterKm, jump, closestKm));
            }
        }

        internal static void CrossCheckCoverage(SandboxPlayerWolfpack wolfpack, float thresholdKm)
        {
            if (wolfpack == null || thresholdKm <= 0f)
                return;

            try
            {
                var origin = wolfpack.Position;
                if (!LrlSandboxGroups.GetNearest(origin, thresholdKm, CrossCheckBuffer))
                    return;

                var nearby = wolfpack.NearbyGroups;
                var missed = 0;

                for (var i = 0; i < CrossCheckBuffer.Count; i++)
                {
                    var group = CrossCheckBuffer[i];
                    if (group == null || ReferenceEquals(group, wolfpack))
                        continue;
                    if (group.DetectedByPlayer || group.IsGroupPublic)
                        continue;
                    if (nearby != null && nearby.Contains(group))
                        continue;

                    missed++;
                }

                CrossCheckBuffer.Clear();

                if (missed <= 0)
                    return;

                CoverageGaps += missed;

                if (LrlLog.ShouldLog("guard-coverage", 30f))
                {
                    LrlLog.Warn(string.Format(
                        "GUARD BLIND SPOT: {0} undetected group(s) inside the {1:0.0}km band are absent from " +
                        "NearbyGroups, which the guard searches. NearbyGroups is a ~32km circle, so a band " +
                        "wider than that cannot be fully covered by it.",
                        missed, thresholdKm));
                }
            }
            catch (Exception ex)
            {
                CrossCheckBuffer.Clear();
                if (LrlLog.ShouldLog("guard-coverage-failed", 300f))
                    LrlLog.Error("guard coverage cross-check failed", ex);
            }
        }

        private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var lengthSq = ab.sqrMagnitude;

            if (lengthSq <= 1e-9f)
                return (point - a).magnitude;

            var t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSq);
            return (point - (a + ab * t)).magnitude;
        }

        internal static string Describe()
        {
            return string.Format(
                "stepAudit maxGroupJump={0:0.00}km bandSkips={1} coverageGaps={2}",
                MaxJumpKm, BandSkips, CoverageGaps);
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Lookout/LrlTimeCompressionGuard.cs
// ====================================================================================================
// Ausguck (Teil des UBOAT Toolkit): LrlTimeCompressionGuard. Schalter: F10 > QoL > "Ausguck".

namespace UboatToolkit.Modules.Lrl
{
    using global::System;
    using global::UBOAT.Game.Sandbox;

    internal static class LrlTimeCompressionGuard
    {
        private static readonly object BlockadeKey = new object();

        private static bool _engaged;
        private static bool _ladderReported;
        private static bool _warned;

        internal static bool Engaged { get { return _engaged; } }

        internal static void Evaluate(SandboxPlayerWolfpack wolfpack, float searchRadiusKm)
        {
            var controller = LrlPlayer.TimeCompression;
            if (controller == null)
                return;

            try
            {
                if (!LrlSettings.Enabled || !LrlSettings.TimeCompressionGuard || wolfpack == null)
                {
                    Release(controller);
                    return;
                }

                var threshold = searchRadiusKm * LrlSettings.GuardRadiusFactor;
                if (threshold <= 0f)
                {
                    Release(controller);
                    return;
                }

                if (IsAnyUndetectedGroupWithin(wolfpack, threshold))
                {
                    if (!_engaged)
                    {
                        controller.SetTimeCompressionBlockade(BlockadeKey, LrlSettings.TimeCompressionCap);
                        _engaged = true;
                        ReportLadderOnce();

                        var effective = LrlCompressionLadder.EffectiveCompression(LrlSettings.TimeCompressionCap);
                        LrlLog.Info(string.Format(
                            "time compression capped: cap={0:0} -> rung {1} - undetected group within {2:0.0}km band",
                            LrlSettings.TimeCompressionCap,
                            float.IsNaN(effective) ? "unknown (ladder unreadable)" : effective.ToString("0"),
                            threshold));
                    }
                }
                else
                {
                    Release(controller);
                }
            }
            catch (Exception ex)
            {
                if (!_warned)
                {
                    _warned = true;
                    LrlLog.Error("time compression guard failed - releasing and standing down", ex);
                }

                try { Release(controller); } catch { }
                LrlSettings.TimeCompressionGuard = false;
            }
        }

        internal static void Release(UBOAT.Game.Core.Time.TimeCompressionController controller)
        {
            if (!_engaged || controller == null)
                return;

            controller.RemoveTimeCompressionBlockade(BlockadeKey);
            _engaged = false;
            LrlLog.Info("time compression cap released");
        }

        private static bool IsAnyUndetectedGroupWithin(SandboxPlayerWolfpack wolfpack, float thresholdKm)
        {
            var nearby = wolfpack.NearbyGroups;
            if (nearby == null || nearby.Count == 0)
                return false;

            var origin = wolfpack.Position;
            var thresholdSq = thresholdKm * thresholdKm;
            var playerCountry = wolfpack.Country;
            var found = false;

            for (var i = 0; i < nearby.Count; i++)
            {
                var group = nearby[i];
                if (group == null)
                    continue;

                if (group.IsGroupPublic)
                    continue;

                LrlStepAudit.Observe(group, origin, thresholdKm);

                if (group.DetectedByPlayer)
                    continue;

                if (LrlSettings.GuardIgnoresAllies && playerCountry != null && group.Country != null)
                {
                    if (playerCountry.GetRelationWith(group.Country) == Country.Relation.Ally)
                        continue;
                }

                var delta = group.Position - origin;
                var bonus = LrlConvoyNightBonus.Factor(group);
                if (delta.sqrMagnitude <= thresholdSq * bonus * bonus)
                    found = true;
            }

            return found;
        }

        private static void ReportLadderOnce()
        {
            if (_ladderReported)
                return;

            _ladderReported = true;

            float[] rungs;
            if (!LrlCompressionLadder.TryRead(out rungs))
            {
                LrlLog.Warn("compression ladder unreadable (" + LrlCompressionLadder.WhyNot +
                            ") - the cap may or may not bind, and this line cannot say which");
                return;
            }

            var top = LrlCompressionLadder.Top;
            var cap = LrlSettings.TimeCompressionCap;
            var effective = LrlCompressionLadder.EffectiveCompression(cap);

            LrlLog.Info(string.Format(
                "compression ladder (live): {0:0} / {1:0} / {2:0} / {3:0} / {4:0}; cap {5:0} selects rung {6:0}",
                rungs[0], rungs[1], rungs[2], rungs[3], rungs[4], cap, effective));

            if (cap >= top)
            {
                LrlLog.Warn(string.Format(
                    "THE COMPRESSION CAP IS INERT: cap {0:0} is at or above the top rung {1:0}, so the " +
                    "blockade admits full compression and this guard does nothing. Set " +
                    "LrlSettings.TimeCompressionCap below {1:0}.", cap, top));
            }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Lookout/LrlWeatherDiagnostics.cs
// ====================================================================================================
// Ausguck (Teil des UBOAT Toolkit): LrlWeatherDiagnostics. Schalter: F10 > QoL > "Ausguck".

namespace UboatToolkit.Modules.Lrl
{
    using global::System;
    using global::System.Reflection;
    using global::System.Text;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Environment;
    using global::UnityEngine;

    internal static class LrlWeatherDiagnostics
    {
        private static readonly StringBuilder LineBuilder = new StringBuilder();

        private static readonly FieldInfo Total = Field("totalVisibilityModifier");
        private static readonly FieldInfo TotalLit = Field("totalVisibilityModifierIfArtificiallyIlluminated");
        private static readonly FieldInfo Sun = Field("sunPositionVisibilityFactor");
        private static readonly FieldInfo Moon = Field("moonStateVisibilityFactor");
        private static readonly FieldInfo Cloud = Field("cloudinessVisibilityFactor");
        private static readonly FieldInfo Fog = Field("fogVisibilityFactor");
        private static readonly FieldInfo Rain = Field("rainVisibilityFactor");
        private static readonly FieldInfo Lightings = Field("lightingsVisibilityFactor");
        private static readonly FieldInfo Radar = Field("radarDetectabilityFactors");
        private static readonly FieldInfo Hydro = Field("passiveHydrophoneDetectabilityFactor");
        private static readonly FieldInfo MinFog = Field("minFogDensity");

        private static bool _warned;

        private static FieldInfo Field(string name)
        {
            return AccessTools.Field(typeof(Weather), name);
        }

        private static float Read(FieldInfo f, Weather w)
        {
            if (f == null || w == null)
                return float.NaN;

            var boxed = f.GetValue(w);
            return boxed is float ? (float)boxed : float.NaN;
        }

        internal static void Log(Weather weather)
        {
            if (weather == null)
                return;

            try
            {
                var total = Read(Total, weather);
                var sun = Read(Sun, weather);
                var moon = Read(Moon, weather);
                var cloud = Read(Cloud, weather);
                var fog = Read(Fog, weather);
                var rain = Read(Rain, weather);
                var lightings = Read(Lightings, weather);

                var sb = LineBuilder;
                sb.Length = 0;
                sb.Append("weather  visual=").Append(total.ToString("0.0000"));
                sb.Append(" [sun=").Append(sun.ToString("0.000"));
                sb.Append(" moon=").Append(moon.ToString("0.000"));
                sb.Append(" cloud=").Append(cloud.ToString("0.000"));
                sb.Append(" fog=").Append(fog.ToString("0.000"));
                sb.Append(" rain=").Append(rain.ToString("0.000"));
                sb.Append(" lightings=").Append(lightings.ToString("0.000")).Append(']');
                sb.Append("  lit=").Append(Read(TotalLit, weather).ToString("0.0000"));
                sb.Append("  radar=").Append(Read(Radar, weather).ToString("0.000"));
                sb.Append("  hydrophone=").Append(Read(Hydro, weather).ToString("0.000"));
                sb.Append("  minFogDensity=").Append(Read(MinFog, weather).ToString("0.00000"));
                LrlLog.Info(sb.ToString());

                var frc = fog * rain * cloud;
                var lightingsOverFrc = frc > 0f ? lightings / frc : float.NaN;

                sb.Length = 0;
                sb.Append("weather  fog*rain*cloud=").Append(frc.ToString("0.00000"));
                sb.Append("  lightings/frc=").Append(lightingsOverFrc.ToString("0.000"));
                sb.Append("  visual^2=").Append((total * total).ToString("0.00000"));
                sb.Append("  squaring costs x").Append((total > 0f ? 1f / total : float.NaN).ToString("0.00"));
                sb.Append("  (radius uses visual^2; hydrophone and radar use theirs once)");

                var dark = weather.IsDarkWeather;
                sb.Append("  darkness[isDark=").Append(dark ? "yes" : "no");
                sb.Append(" visual<0.4=").Append(total < 0.4f ? "yes" : "no");
                sb.Append(" -> newContactsSilent=")
                  .Append(dark && total < 0.4f
                      ? (LrlSettings.Enabled && LrlSettings.ReportDarkContacts ? "no (patched)" : "YES")
                      : "no")
                  .Append(']');

                LrlLog.Info(sb.ToString());
            }
            catch (Exception ex)
            {
                if (!_warned)
                {
                    _warned = true;
                    LrlLog.Error("weather diagnostics failed", ex);
                }
            }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Lookout/LrlWeatherResponse.cs
// ====================================================================================================
// Ausguck (Teil des UBOAT Toolkit): LrlWeatherResponse. Schalter: F10 > QoL > "Ausguck".

namespace UboatToolkit.Modules.Lrl
{
    using global::System;
    using global::System.Reflection;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Environment;
    using global::UnityEngine;

    internal static class LrlWeatherResponse
    {
        private const string HistoricalWeatherHarmonyId = "com.kugler.historicalweather";

        private static readonly FieldInfo FogField =
            AccessTools.Field(typeof(Weather), "fogVisibilityFactor");

        internal static float CachedFog = 1f;

        internal static bool HistoricalWeatherPresent { get; private set; }

        private static bool _warned;

        internal static void Refresh(Weather weather)
        {
            try
            {
                HistoricalWeatherPresent = Harmony.HasAnyPatches(HistoricalWeatherHarmonyId);

                var fog = 1f;
                if (weather != null && FogField != null)
                {
                    var boxed = FogField.GetValue(weather);
                    if (boxed is float)
                        fog = (float)boxed;
                }

                CachedFog = float.IsNaN(fog) || float.IsInfinity(fog) ? 1f : Mathf.Clamp01(fog);
            }
            catch (Exception ex)
            {
                CachedFog = 1f;
                if (!_warned)
                {
                    _warned = true;
                    LrlLog.Error("fog read failed - treating fog as clear, weather correction unchanged", ex);
                }
            }
        }

        private static readonly AccessTools.FieldRef<Weather, float> FogRef = MakeFogRef();

        private static AccessTools.FieldRef<Weather, float> MakeFogRef()
        {
            try
            {
                return FogField != null
                    ? AccessTools.FieldRefAccess<Weather, float>("fogVisibilityFactor")
                    : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool _warnedLive;

        internal static void RefreshLive(Weather weather)
        {
            if (weather == null || _warnedLive)
                return;

            try
            {
                LrlSightRangePatch.CachedVisibility = weather.VisualDetectabilityFactors;

                if (FogRef != null)
                {
                    var fog = FogRef(weather);
                    CachedFog = float.IsNaN(fog) || float.IsInfinity(fog) ? 1f : Mathf.Clamp01(fog);
                }
            }
            catch (Exception ex)
            {
                if (!_warnedLive)
                {
                    _warnedLive = true;
                    LrlLog.Error("per-frame weather read failed - falling back to the 5 s refresh", ex);
                }
            }
        }

        internal static float ActiveFogExtra
        {
            get
            {
                if (!LrlSettings.StrengthenFog || HistoricalWeatherPresent)
                    return 0f;
                return Mathf.Max(0f, LrlSettings.FogExtraExponent);
            }
        }

        internal static float EffectiveRest(float rest)
        {
            var floor = LrlSettings.WeatherFloorEnabled ? LrlSettings.VisibilityFloor : 0f;
            if (floor <= 0f || rest >= floor)
                return rest;

            var softness = Mathf.Clamp01(LrlSettings.VisibilityFloorSoftness);
            var restEff = softness <= 0f ? floor : floor * Mathf.Pow(rest / floor, softness);
            return restEff < rest ? rest : restEff;
        }

        internal static float RangeMultiplier(float visibility)
        {
            if (float.IsNaN(visibility) || float.IsInfinity(visibility) || visibility <= 0.0001f)
                return 1f;

            var fog = CachedFog;
            if (fog <= 0.0001f)
                return 1f;   // total fog: vanilla already gives ~zero, leave it

            var rest = Mathf.Clamp01(visibility / fog);
            if (rest <= 0.0001f)
                return 1f;

            var restEff = rest;
            var multiplier = 1f;

            restEff = EffectiveRest(rest);
            if (restEff > rest)
            {
                var lift = restEff / rest;
                multiplier *= lift * lift;   // squared, because the game squares the term
            }

            if (LrlSettings.CancelWeatherSquaring)
                multiplier *= Mathf.Min(1f / restEff, LrlSettings.MaxWeatherCompensation);

            var extra = ActiveFogExtra;
            if (extra > 0f && fog < 1f)
                multiplier *= Mathf.Pow(fog, extra);

            multiplier *= LrlDaylightTrim.RangeFactor(visibility);

            return multiplier;
        }

        internal static float EffectiveVisibility(float visibility)
        {
            if (float.IsNaN(visibility) || float.IsInfinity(visibility) || visibility <= 0.0001f)
                return visibility;

            var fog = CachedFog;
            if (fog <= 0.0001f)
                return visibility;

            var rest = Mathf.Clamp01(visibility / fog);
            return fog * EffectiveRest(rest);
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/NightView.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UnityEngine.Rendering.PostProcessing;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Scene;
    using global::UBOAT.Game.Scene.Camera;
    using global::UBOAT.Game.Scene.Camera.ExternalControllers;
    using global::UBOAT.Game.Scene.Effects;
    using global::UBOAT.Game.Scene.Items;
    using global::UBOAT.Game.Scene.Tasks;
    using global::UBOAT.Game.UI;

    // Night view, hooked on the game's own night adaptation (BrighterNights.DoUpdate):
    //  - brighter nights outside (strengthens the game's adaptation, with a safety ceiling)
    //  - brighter boat interior (own post-exposure volume over the interior one)
    //  - per-optic exposure: removes the observation periscope's extra brightness, gives every
    //    sight (attack / observation periscope, UZO, binoculars) its own exposure and glass look
    //    (haze by day, dimming and colour loss at night), plus a per-sight night gain.
    internal enum NightSight { None, Attack, Observation, Uzo, Binoculars, UnknownPeriscope }

    internal struct NightOptic
    {
        public float Ev, Contrast, Saturation, Lift, Temperature, Tint;
        public static NightOptic Lerp(NightOptic a, NightOptic b, float t) => new NightOptic
        {
            Ev = Mathf.Lerp(a.Ev, b.Ev, t), Contrast = Mathf.Lerp(a.Contrast, b.Contrast, t), Saturation = Mathf.Lerp(a.Saturation, b.Saturation, t),
            Lift = Mathf.Lerp(a.Lift, b.Lift, t), Temperature = Mathf.Lerp(a.Temperature, b.Temperature, t), Tint = Mathf.Lerp(a.Tint, b.Tint, t)
        };
        public NightOptic Scale(float k) => new NightOptic { Ev = Ev * k, Contrast = Contrast * k, Saturation = Saturation * k, Lift = Lift * k, Temperature = Temperature * k, Tint = Tint * k };
    }

    internal static class NightCfg
    {
        public static bool Optics => Settings.B("NightOptics");
        public static NightOptic Day => new NightOptic { Ev = -0.25f, Contrast = -10f, Saturation = -12f, Lift = Settings.F("NightOpticDayLift") / 1000f };
        public static NightOptic Night => new NightOptic { Ev = -1.10f, Contrast = -4f, Saturation = -Settings.F("NightOpticColourLoss"), Lift = -0.020f };

        public static float ExposureFor(NightSight s)
        {
            switch (s)
            {
                case NightSight.Attack: return Settings.F("NightAttackEv");
                case NightSight.Observation: return Settings.F("NightObservationEv");
                case NightSight.Uzo: return Settings.B("NightUzoBinoGrading") ? Settings.F("NightUzoEv") : float.NaN;
                case NightSight.Binoculars: return Settings.B("NightUzoBinoGrading") ? Settings.F("NightBinoEv") : float.NaN;
                default: return float.NaN;
            }
        }
        public static float GlassFor(NightSight s)
        {
            switch (s)
            {
                case NightSight.Attack: return Settings.F("NightAttackGlass") / 100f;
                case NightSight.Observation: return Settings.F("NightObservationGlass") / 100f;
                case NightSight.Uzo: case NightSight.Binoculars: return 1f;
                default: return 0f;
            }
        }
        // Night adaptation gain per view (100 % = game).
        public static float GainFor(NightSight s)
        {
            if (!Optics) return 1f;
            switch (s)
            {
                case NightSight.Attack: return Settings.F("NightGainAttack") / 100f;
                case NightSight.Observation: return Settings.F("NightGainObservation") / 100f;
                case NightSight.Uzo: return Settings.F("NightGainUzo") / 100f;
                case NightSight.Binoculars: return Settings.F("NightGainBino") / 100f;
                default: return Settings.F("NightGainOther") / 100f;
            }
        }
        public static float ExteriorGain => 1f + Settings.F("NightBright") / 100f;
        public static float InteriorEv { get { float p = Settings.F("InteriorBright"); return p <= 0f ? 0f : Mathf.Log(1f + p / 100f, 2f); } }
    }

    internal static class NightViewRead
    {
        public static NightSight Classify(string overlay)
        {
            if (string.IsNullOrEmpty(overlay)) return NightSight.UnknownPeriscope;
            if (overlay.StartsWith("Observation", StringComparison.OrdinalIgnoreCase)) return NightSight.Observation;
            if (overlay.StartsWith("Targeting", StringComparison.OrdinalIgnoreCase)) return NightSight.Uzo;
            if (overlay.StartsWith("Binocular", StringComparison.OrdinalIgnoreCase)) return NightSight.Binoculars;
            if (overlay.StartsWith("Periscope", StringComparison.OrdinalIgnoreCase)) return NightSight.Attack;
            return NightSight.UnknownPeriscope;
        }

        public static void Read(MainCamera cam, out CameraMode mode, out bool active, out NightSight sight)
        {
            mode = CameraMode.Generic; active = false; sight = NightSight.None;
            if (cam == null) return;
            try
            {
                mode = cam.CurrentMode;
                active = cam.isActiveAndEnabled;
                var pc = cam.GenericController?.ExternalController as PeriscopeController;
                var p = pc?.Periscope;
                if (p == null) return;
                string name = p.ScreenOverlayName;
                if (string.IsNullOrEmpty(name)) name = p.FallbackScreenOverlayName;
                if (mode == CameraMode.Generic) sight = Classify(name);
            }
            catch (Exception e) { Util.Log("Nacht: Ansicht nicht lesbar: " + e.Message); }
        }
    }

    // Per-sight grading volume (layer 16 = periscope volumes, priority above their 128).
    internal static class NightGrading
    {
        private static GameObject host;
        private static PostProcessVolume volume;
        private static ColorGrading grading;
        private static bool failed, clampWarned;
        private static bool baseValid;
        private static float basePe, baseCon, baseSat, baseTemp, baseTint, baseAt = -999f;
        private static Vector4 baseLift;
        private static float lastEv = float.NaN;
        private static NightSight lastSight = NightSight.None;
        public static float PublishedEvDelta, PublishedSatBase, PublishedSatDelta;
        public static int PublishedFrame = -1;

        public static void Tick(NightSight sight, CameraMode mode, float nightFactor)
        {
            try
            {
                float sightEv = NightCfg.ExposureFor(sight);
                if (!NightCfg.Optics || mode != CameraMode.Generic || sight == NightSight.None || float.IsNaN(sightEv)) { Off(); return; }
                if (!Ensure()) return;
                ReadBase();
                if (!baseValid) { Off(); return; }
                float f = float.IsNaN(nightFactor) ? 0f : Mathf.Clamp01(nightFactor);
                var optic = NightOptic.Lerp(NightCfg.Day, NightCfg.Night, f).Scale(Mathf.Max(0f, Settings.F("NightOpticStrength") / 100f) * Mathf.Max(0f, NightCfg.GlassFor(sight)));
                float target = basePe + sightEv + optic.Ev;
                float ev = Mathf.Clamp(target, -8f, 4f);
                if (Mathf.Abs(ev - target) > 1e-4f && !clampWarned) { clampWarned = true; Util.Log("Nacht: Belichtung " + target.ToString("F2") + " EV außerhalb -8..+4, nehme " + ev.ToString("F2")); }
                float comp = Mathf.Clamp(Settings.F("NightReticleComp") / 100f, 0f, 2f);
                PublishedEvDelta = (ev - basePe) * comp; PublishedSatBase = baseSat; PublishedSatDelta = optic.Saturation * comp; PublishedFrame = Time.frameCount;
                grading.postExposure.overrideState = true;
                grading.postExposure.value = ev;
                SetF(grading.contrast, baseCon + optic.Contrast, optic.Contrast);
                SetF(grading.saturation, baseSat + optic.Saturation, optic.Saturation);
                SetF(grading.temperature, baseTemp + optic.Temperature, optic.Temperature);
                SetF(grading.tint, baseTint + optic.Tint, optic.Tint);
                if (Mathf.Abs(optic.Lift) > 1e-5f) { grading.lift.overrideState = true; var l = baseLift; l.w += optic.Lift; grading.lift.value = l; }
                else grading.lift.overrideState = false;
                volume.weight = 1f;
                if (sight != lastSight || float.IsNaN(lastEv) || Mathf.Abs(ev - lastEv) > 1e-3f)
                {
                    lastSight = sight;
                    if (Settings.B("NightLog")) Util.Log("Nacht: " + sight + " Nacht-Anteil " + f.ToString("F2") + " -> Belichtung " + ev.ToString("F3") + " EV, Sättigung " + (baseSat + optic.Saturation).ToString("F1"));
                }
                lastEv = ev;
            }
            catch (Exception e) { failed = true; Off(); Util.Log("Nacht: Sicht-Belichtung abgeschaltet: " + e.Message); }
        }

        private static void SetF(FloatParameter p, float abs, float delta)
        {
            if (Mathf.Abs(delta) > 1e-5f) { p.overrideState = true; p.value = abs; } else p.overrideState = false;
        }

        public static void Off()
        {
            PublishedFrame = -1; lastEv = float.NaN; lastSight = NightSight.None;
            if (volume != null) volume.weight = 0f;
        }

        private static bool Ensure()
        {
            if (failed) return false;
            if (volume != null && grading != null) return true;
            try
            {
                if (host == null) { host = new GameObject("Toolkit Sight Grading") { layer = 16, hideFlags = HideFlags.DontSave }; UnityEngine.Object.DontDestroyOnLoad(host); }
                var prof = ScriptableObject.CreateInstance<PostProcessProfile>();
                prof.hideFlags = HideFlags.DontSave;
                grading = prof.AddSettings<ColorGrading>();
                grading.active = true; grading.enabled.Override(true); grading.postExposure.Override(0f);
                volume = host.AddComponent<PostProcessVolume>();
                volume.isGlobal = true; volume.priority = 200f; volume.weight = 0f; volume.sharedProfile = prof;
                return true;
            }
            catch (Exception e) { failed = true; Util.Log("Nacht: Sicht-Volumen nicht erstellbar: " + e.Message); return false; }
        }

        // The game's exterior grading ("Base - Exterior"), refreshed every 5 s.
        private static void ReadBase()
        {
            if (baseValid && Time.unscaledTime - baseAt < 5f) return;
            baseAt = Time.unscaledTime;
            baseValid = false;
            foreach (var v in UnityEngine.Object.FindObjectsOfType<PostProcessVolume>())
            {
                if (v == null || v.gameObject == null || v.gameObject.name != "Base - Exterior") continue;
                var prof = v.HasInstantiatedProfile() ? v.profile : v.sharedProfile;
                if (prof == null || !prof.TryGetSettings(out ColorGrading cg) || cg == null) continue;
                baseValid = true;
                basePe = cg.postExposure.value; baseCon = cg.contrast.value; baseSat = cg.saturation.value;
                baseLift = cg.lift.value; baseTemp = cg.temperature.value; baseTint = cg.tint.value;
                return;
            }
        }
    }

    // Brighter interior: own volume on the interior layer, base exposure + log2(1 + %).
    internal static class NightInterior
    {
        private static GameObject host;
        private static PostProcessVolume volume;
        private static ColorGrading grading;
        private static bool failed;
        private static float basePe, baseAt = -999f;
        private static PostProcessVolume baseFrom;
        private static float lastEv = float.NaN;

        public static void Tick(MainCamera cam)
        {
            try
            {
                float ev = NightCfg.InteriorEv;
                var inner = cam != null ? cam.InteriorPostProcessVolume : null;
                if (ev <= 0f || inner == null) { Off(); return; }
                if (failed) return;
                if (volume == null)
                {
                    host = new GameObject("Toolkit Interior Light") { hideFlags = HideFlags.DontSave };
                    UnityEngine.Object.DontDestroyOnLoad(host);
                    var prof = ScriptableObject.CreateInstance<PostProcessProfile>();
                    prof.hideFlags = HideFlags.DontSave;
                    grading = prof.AddSettings<ColorGrading>();
                    grading.active = true; grading.enabled.Override(true); grading.postExposure.Override(0f);
                    volume = host.AddComponent<PostProcessVolume>();
                    volume.isGlobal = true; volume.priority = 210f; volume.weight = 0f; volume.sharedProfile = prof;
                }
                if (host.layer != inner.gameObject.layer) host.layer = inner.gameObject.layer;
                if (inner != baseFrom || Time.unscaledTime - baseAt >= 5f)
                {
                    baseFrom = inner; baseAt = Time.unscaledTime; basePe = 0f;
                    var prof = inner.HasInstantiatedProfile() ? inner.profile : inner.sharedProfile;
                    if (prof != null && prof.TryGetSettings(out ColorGrading cg) && cg != null && cg.postExposure.overrideState) basePe = cg.postExposure.value;
                }
                grading.postExposure.overrideState = true;
                grading.postExposure.value = basePe + ev;
                volume.weight = inner.weight;
                if (float.IsNaN(lastEv) || Mathf.Abs(ev - lastEv) > 1e-3f) { lastEv = ev; Util.Log("Nacht: Innenraum +" + ev.ToString("F2") + " EV über " + basePe.ToString("F2")); }
            }
            catch (Exception e) { failed = true; Off(); Util.Log("Nacht: Innenraum-Aufhellung abgeschaltet: " + e.Message); }
        }

        public static void Off() { lastEv = float.NaN; if (volume != null) volume.weight = 0f; }
    }

    [HarmonyPatch(typeof(BrighterNights), "DoUpdate")]
    internal static class Night_AdaptationPatch
    {
        private static bool clampWarned;
        private static float nextLog;

        private static void Postfix(AutoExposure ___autoExposure, float ___minExposure, float ___shelterFactor,
                                    GameDataParameter ___adaptation, GameDataParameter ___adaptationUnrealisticNights, GameDataParameter ___adaptationNightLights,
                                    MainCamera ___mainCamera, PlayerShipInteriorLighting ___submarineLighting, UserSettings ___userSettings, IStoryPlayerUI ___storyPlayerUI)
        {
            try
            {
                // which adaptation value the game used this frame
                float param = 0f;
                if (___userSettings != null && ___userSettings.GameplaySettings != null)
                {
                    if (!___userSettings.GameplaySettings.realisticNights) param = Val(___adaptationUnrealisticNights);
                    else if (___submarineLighting != null && ___submarineLighting.CurrentPreset == LightController.Preset.Alarm && (___storyPlayerUI == null || !___storyPlayerUI.IsOpened)) param = Val(___adaptationNightLights);
                    else param = Val(___adaptation);
                }
                float written = ___autoExposure != null ? ___autoExposure.minLuminance.value : float.NaN;
                float f = float.NaN;
                if (___autoExposure != null && Mathf.Abs(param) > 1e-6f) f = (___minExposure - written) / param;
                NightViewRead.Read(___mainCamera, out var mode, out var active, out var sight);
                bool zeroExpected = mode == CameraMode.Map || !active;
                // only build on what the game really wrote this frame
                bool wrote = ___submarineLighting != null
                             && !(!float.IsNaN(f) && (f < -0.001f || f > 1.001f))
                             && !(zeroExpected && Mathf.Abs(___minExposure - written) > 1e-4f);
                if (wrote && ___autoExposure != null)
                {
                    float gain = NightCfg.GainFor(sight) * NightCfg.ExteriorGain;
                    if (Mathf.Abs(gain - 1f) >= 1e-5f)
                    {
                        float ff = float.IsNaN(f) ? 0f : Mathf.Clamp01(f);
                        float target = ___minExposure - gain * ff * param;
                        float ceiling = ___autoExposure.maxLuminance.value - 0.01f;
                        if (target > ceiling)
                        {
                            target = ceiling;
                            if (!clampWarned) { clampWarned = true; Util.Log("Nacht: Aufhellung an der Obergrenze " + ___autoExposure.maxLuminance.value.ToString("F3") + " gekappt (Faktor " + gain.ToString("F2") + ")"); }
                        }
                        ___autoExposure.minLuminance.value = target;
                    }
                }
                NightGrading.Tick(sight, mode, wrote ? f : float.NaN);
                NightInterior.Tick(___mainCamera);
                if (Settings.B("NightLog") && Time.unscaledTime >= nextLog)
                {
                    nextLog = Time.unscaledTime + 5f;
                    Util.Log("Nacht: Ansicht " + mode + "/" + sight + ", Nacht-Anteil " + (float.IsNaN(f) ? "-" : f.ToString("F2")) + ", Spiel schrieb " + (wrote ? "ja" : "nein") + ", minLuminance " + (___autoExposure != null ? ___autoExposure.minLuminance.value.ToString("F3") : "-"));
                }
            }
            catch (Exception e) { Util.Log("Nacht: " + e.Message); }
        }

        private static float Val(GameDataParameter p) { try { return p != null ? p.Value : 0f; } catch { return 0f; } }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Optics.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Scene.Items;

    // QoL #18 more zoom, #19 calm picture, #20 periscope extends higher (QoL up to 12 m, cheat up to 100 m).
    // Binoculars use the same game class as the periscopes, so they get zoom + calm picture too.
    public class Optics : ModuleBase
    {
        public override void Tick()
        {
            float zoom = Mathf.Max(1f, Settings.F("OpticsZoom"));
            float calm = Mathf.Clamp01(Settings.F("OpticsStabilization"));
            float height = Mathf.Max(Mathf.Min(Settings.F("PeriscopeHeight"), 12f), Settings.F("CheatPeriscopeHeight"));

            foreach (var p in Util.Find<Periscope>())
            {
                ApplyFov(p, "fovPresets", "fovSensitivities", zoom);
                ApplyFov(p, "fovPresetsRealistic", "fovSensitivitiesRealistic", zoom);
                ApplyCalm(p, "fovStabilization", calm);
                ApplyCalm(p, "fovStabilizationRealistic", calm);

                var length = Traverse.Create(p).Field("length").GetValue<Parameter>();
                if (length != null)
                {
                    float game = length.GetValueExcludingModifier(Util.ModifierName);
                    Util.SetParameter(length, height > game ? height : 0f);
                }
            }
        }

        // Only the strongest zoom step gets stronger; the wide view stays as it is.
        private static void ApplyFov(Periscope p, string fovField, string sensField, float zoom)
        {
            var orig = Util.Original<float[]>(p, fovField);
            var cur = Traverse.Create(p).Field(fovField).GetValue<float[]>();
            if (orig == null || cur == null || cur.Length != orig.Length || cur.Length == 0) return;
            int last = cur.Length - 1;
            cur[last] = orig[last] / zoom;

            var origSens = Util.Original<float[]>(p, sensField);
            var sens = Traverse.Create(p).Field(sensField).GetValue<float[]>();
            if (origSens != null && sens != null && sens.Length == origSens.Length && sens.Length > last)
                sens[last] = origSens[last] / zoom; // slower mouse when zoomed in further
        }

        private static void ApplyCalm(Periscope p, string field, float calm)
        {
            var orig = Util.Original<float[]>(p, field);
            var cur = Traverse.Create(p).Field(field).GetValue<float[]>();
            if (orig == null || cur == null || cur.Length != orig.Length) return;
            for (int i = 0; i < cur.Length; i++) cur[i] = Mathf.Lerp(orig[i], 1f, calm);
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/PeriscopeUI.cs
// ====================================================================================================
namespace UboatToolkit.Modules.ScopeUi
{
    using global::HarmonyLib;
    using ScopeItem = global::UBOAT.Game.Scene.Items.Periscope;
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::System;
    using global::UBOAT.Game.Scene.Camera.ExternalControllers;
    using global::UBOAT.Game.Scene.Camera;
    using global::UBOAT.Game.Scene.Effects;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.UI.Periscope;
    using global::UBOAT.Game.UI;
    using global::UnityEngine.UI;
    using global::UnityEngine;

    // Periscope screen fixes: view mode and zoom are remembered per periscope and re-applied when it
    // opens again (dropdowns show the real state), the station button lights for the observation
    // periscope and takes you to the periscope you used last, colour-filter mismatch is logged.
    internal static class PuiSettings
    {
        public static bool Enabled => Settings.B("ScopeRemember");
        public static bool RememberViewAndZoom => Settings.B("ScopeRememberView");
        public static bool StationButtonLastPeriscope => Settings.B("ScopeStationLast");
    }

    internal static class PuiMemory
    {
        internal const string AttackKind = "Attack Periscope";
        internal const string ObservationKind = "Observation Periscope";
        private static readonly Dictionary<string, int> ViewModes = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly Dictionary<string, int> Zooms = new Dictionary<string, int>(StringComparer.Ordinal);
        private static string _lastKind;
        internal static string LastKind { get { return _lastKind; } }
        internal static int ViewModeOf(bool fullScreen, bool closer)
        {
            if (fullScreen) return 2;
            return closer ? 1 : 0;
        }
        internal static int ClampZoom(int index, int presetCount)
        {
            if (presetCount <= 0) return -1;
            if (index < 0) return 0;
            return index >= presetCount ? presetCount - 1 : index;
        }
        internal static string KindOf(string typeName)
        {
            if (typeName == null) return null;
            if (typeName.StartsWith(AttackKind, StringComparison.Ordinal)) return AttackKind;
            if (typeName.StartsWith(ObservationKind, StringComparison.Ordinal)) return ObservationKind;
            return null;
        }
        internal static void RecordViewMode(string typeName, int index)
        {
            if (string.IsNullOrEmpty(typeName) || index < 0 || index > 2) return;
            ViewModes[typeName] = index;
        }
        internal static void RecordZoom(string typeName, int index)
        {
            if (string.IsNullOrEmpty(typeName) || index < 0) return;
            Zooms[typeName] = index;
        }
        internal static int ViewModeFor(string typeName)
        {
            int v;
            return typeName != null && ViewModes.TryGetValue(typeName, out v) ? v : -1;
        }
        internal static int ZoomFor(string typeName)
        {
            int v;
            return typeName != null && Zooms.TryGetValue(typeName, out v) ? v : -1;
        }
        internal static void NoteOpened(string typeName)
        {
            string kind = KindOf(typeName);
            if (kind != null) _lastKind = kind;
        }
        internal static void Clear()
        {
            ViewModes.Clear();
            Zooms.Clear();
            _lastKind = null;
        }
    }


    internal static class PuiPatches
    {
        internal static readonly MethodInfo SetToggle =
            AccessTools.Method(typeof(TorpedoAttackStationsViewSwitchUI), "SetToggle");
        internal static readonly FieldInfo UiValidation =
            AccessTools.Field(typeof(TorpedoAttackStationsViewSwitchUI), "uiValidation");
        internal static readonly MethodInfo SetController =
            AccessTools.Method(typeof(TorpedoAttackStationsViewSwitchUI), "SetExternalCameraController");
        
        internal static IEnumerable<string> MissingStationMembers()
        {
            if (SetToggle == null) yield return "TorpedoAttackStationsViewSwitchUI.SetToggle";
            if (UiValidation == null) yield return "TorpedoAttackStationsViewSwitchUI.uiValidation";
            if (SetController == null) yield return "TorpedoAttackStationsViewSwitchUI.SetExternalCameraController";
        }
    }
    [HarmonyPatch(typeof(PeriscopeUI), "OnViewModeSelected")]
    internal static class RecordViewModePatch
    {
        private static void Postfix(int index, MainCamera ___mainCamera)
        {
            if (!PuiSettings.Enabled) return;
            try
            {
                if (___mainCamera == null || ___mainCamera.GenericController == null) return;
                var pc = ___mainCamera.GenericController.ExternalController as PeriscopeController;
                if (pc != null && pc.Periscope != null) PuiMemory.RecordViewMode(pc.Periscope.TypeName, index);
            }
            catch (Exception ex) { PuiRestore.Fail("recording the view mode", ex); }
        }
    }
    [HarmonyPatch(typeof(PeriscopeUI), "OnZoomSelected")]
    internal static class RecordZoomPatch
    {
        private static void Postfix(int index, MainCamera ___mainCamera)
        {
            if (!PuiSettings.Enabled) return;
            try
            {
                if (___mainCamera == null || ___mainCamera.GenericController == null) return;
                var pc = ___mainCamera.GenericController.ExternalController as PeriscopeController;
                if (pc != null && pc.Periscope != null) PuiMemory.RecordZoom(pc.Periscope.TypeName, index);
            }
            catch (Exception ex) { PuiRestore.Fail("recording the zoom", ex); }
        }
    }
    [HarmonyPatch(typeof(PeriscopeUI), "OnEnable")]
    internal static class RestoreOnOpenPatch
    {
        private static void Postfix(PeriscopeUI __instance, MainCamera ___mainCamera,
                                    TextMeshProDropdown ___viewModeDropdown, TextMeshProDropdown ___zoomDropdown)
        {
            if (!PuiSettings.Enabled) return;
            try { PuiRestore.OnOpened(__instance, ___mainCamera, ___viewModeDropdown, ___zoomDropdown); }
            catch (Exception ex) { PuiRestore.Fail("restoring the periscope view", ex); }
        }
    }
    [HarmonyPatch(typeof(TorpedoAttackStationsViewSwitchUI), "ValidateToggleState")]
    internal static class LightStationButtonPatch
    {
        private static void Postfix(TorpedoAttackStationsViewSwitchUI __instance, MainCamera ___mainCamera,
                                    Toggle ___periscopeToggle)
        {
            if (!PuiSettings.Enabled || !PuiSettings.StationButtonLastPeriscope) return;
            try
            {
                if (___periscopeToggle == null || PuiPatches.SetToggle == null || PuiPatches.UiValidation == null) return;
                var pc = PuiRestore.ExternalOf(___mainCamera) as PeriscopeController;
                if (pc == null || pc.Periscope == null) return;
                if (PuiMemory.KindOf(pc.Periscope.TypeName) != PuiMemory.ObservationKind) return;
                bool was = (bool)PuiPatches.UiValidation.GetValue(__instance);
                PuiPatches.UiValidation.SetValue(__instance, true);
                try { PuiPatches.SetToggle.Invoke(__instance, new object[] { ___periscopeToggle }); }
                finally { PuiPatches.UiValidation.SetValue(__instance, was); }
            }
            catch (Exception ex) { PuiRestore.Fail("lighting the station button", ex); }
        }
    }
    [HarmonyPatch(typeof(TorpedoAttackStationsViewSwitchUI), "OnPeriscopeToggleValueChanged")]
    internal static class StationButtonTargetPatch
    {
        private static bool Prefix(bool value, bool ___uiValidation, PlayerShip ___playerShip)
        {
            if (!PuiSettings.Enabled || !PuiSettings.StationButtonLastPeriscope) return true;
            if (___uiValidation || !value) return true;
            try
            {
                string kind = PuiMemory.LastKind;
                if (kind == null || ___playerShip == null || PuiPatches.SetController == null) return true;
                ScopeItem scope = ___playerShip.GetEquipment<ScopeItem>(kind);
                if (scope == null || !scope.enabled) return true;
                PuiPatches.SetController.Invoke(null, new object[] { scope });
                if (PuiRestore.Verbose) Util.Log("Sehrohr-Anzeige: station button -> " + kind);
                return false;
            }
            catch (Exception ex)
            {
                PuiRestore.Fail("choosing the station button's periscope", ex);
                return true;
            }
        }
    }


    internal static class PuiRestore
    {
        private const string ModuleId = "PeriscopeUIFix";
        private static readonly MethodInfo ViewHandler =
            AccessTools.Method(typeof(PeriscopeUI), "OnViewModeSelected", new[] { typeof(int) });
        private static readonly MethodInfo ZoomHandler =
            AccessTools.Method(typeof(PeriscopeUI), "OnZoomSelected", new[] { typeof(int) });
        private static readonly FieldInfo DropdownValue =
            AccessTools.Field(typeof(TextMeshProDropdown), "m_Value");
        private static readonly MethodInfo DropdownRefresh =
            AccessTools.Method(typeof(TextMeshProDropdown), "Refresh");
        private static readonly HashSet<string> WarnedFilters = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> FailedSites = new HashSet<string>(StringComparer.Ordinal);
        internal static bool Verbose { get { return Settings.B("ScopeUiLog"); } }
        internal static string MemberReport()
        {
            var missing = new List<string>();
            if (ViewHandler == null) missing.Add("PeriscopeUI.OnViewModeSelected");
            if (ZoomHandler == null) missing.Add("PeriscopeUI.OnZoomSelected");
            if (DropdownValue == null) missing.Add("TextMeshProDropdown.m_Value");
            if (DropdownRefresh == null) missing.Add("TextMeshProDropdown.Refresh");
            missing.AddRange(PuiPatches.MissingStationMembers());
            if (missing.Count == 0) return "all 7 members resolved";
            return "MISSING " + missing.Count + ": " + string.Join(", ", missing.ToArray());
        }
        internal static void Fail(string where, Exception ex)
        {
            if (!FailedSites.Add(where)) return;
            Util.Log("Sehrohr-Anzeige: " + where + " failed (further failures here are not logged): " + ex);
        }
        internal static IExternalController ExternalOf(MainCamera cam)
        {
            if (cam == null) return null;
            IExternalController ec = null;
            try { ec = cam.TransitionTargetModeDescriptor.ExternalController; }
            catch (Exception) { ec = null; }
            if (ec == null && cam.GenericController != null) ec = cam.GenericController.ExternalController;
            return ec;
        }
        internal static void OnOpened(PeriscopeUI ui, MainCamera cam,
                                      TextMeshProDropdown viewDropdown, TextMeshProDropdown zoomDropdown)
        {
            if (cam == null || cam.GenericController == null) return;
            var pc = cam.GenericController.ExternalController as PeriscopeController;
            if (pc == null) return;
            ScopeItem scope = pc.Periscope;
            if (scope == null) return;
            string name = scope.TypeName;
            PuiMemory.NoteOpened(name);
            CheckColourFilter(scope);
            if (!PuiSettings.RememberViewAndZoom) return;
            if (!scope.IsBinocular)
            {
                int want = PuiMemory.ViewModeFor(name);
                int live = PuiMemory.ViewModeOf(scope.FullScreenView, pc.ForceCloserView);
                if (want < 0)
                {
                    ShowSilently(viewDropdown, live);
                }
                else
                {
                    if (want != live && ViewHandler != null)
                    {
                        ViewHandler.Invoke(ui, new object[] { want });
                        if (Verbose)
                            Util.Log("Sehrohr-Anzeige: re-applied view mode " + want + " (was " + live + ") on " + name);
                    }
                    ShowSilently(viewDropdown, want);
                }
            }
            float[] presets = scope.FovPresets;
            int count = presets == null ? 0 : presets.Length;
            if (count == 0) return;
            int wantZoom = PuiMemory.ZoomFor(name);
            if (wantZoom >= 0)
            {
                wantZoom = PuiMemory.ClampZoom(wantZoom, count);
                int liveZoom = scope.FovIndex;
                if (wantZoom != liveZoom && ZoomHandler != null)
                {
                    ZoomHandler.Invoke(null, new object[] { wantZoom });
                    if (Verbose)
                        Util.Log("Sehrohr-Anzeige: re-applied zoom " + wantZoom + " (was " + liveZoom + ") on " + name);
                }
            }
            ShowSilently(zoomDropdown, PuiMemory.ClampZoom(scope.FovIndex, count));
        }
        private static void ShowSilently(TextMeshProDropdown dropdown, int index)
        {
            if (dropdown == null || index < 0 || DropdownValue == null || DropdownRefresh == null) return;
            if ((int)DropdownValue.GetValue(dropdown) == index) return;
            DropdownValue.SetValue(dropdown, index);
            DropdownRefresh.Invoke(dropdown, null);
        }
        private static void CheckColourFilter(ScopeItem scope)
        {
            string[] names = scope.FilterNames;
            if (names == null || names.Length == 0 || scope.PeriscopeCamera == null) return;
            PeriscopeIME ime = scope.PeriscopeCamera.GetComponent<PeriscopeIME>();
            if (ime == null) return;
            string current = ime.ColorFilterName;
            if (current != null && Array.IndexOf(names, current) >= 0) return;
            string shown = current ?? "<null>";
            if (!WarnedFilters.Add(scope.TypeName + "|" + shown)) return;
            Util.Log("Sehrohr-Anzeige: colour filter '" + shown + "' on " + scope.TypeName +
                             " is not in its list (" + string.Join(", ", names) +
                             "); the filter dropdown may not match the view");
        }
    }

}

// ====================================================================================================
// Abschnitt: Modules/Radio.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::HarmonyLib;
    using global::UBOAT.Game.Sandbox;
    using global::UBOAT.Game.Sandbox.Messages;

    // QoL #11 + #12: faster radio messages, enemies cannot direction-find the player's transmissions.
    public class Radio : ModuleBase { }

    [HarmonyPatch(typeof(SandboxEntity), nameof(SandboxEntity.StartTransmission))]
    internal static class Radio_FasterPatch
    {
        private static void Postfix(SandboxEntity __instance, RadioTransmission transmission, bool __result)
        {
            if (!__result || transmission == null || !__instance.IsPlayerShip) return;
            float speed = Settings.F("RadioSpeed");
            if (speed > 1f) transmission.RemainingTransmissionDuration /= speed;
        }
    }

    // SandboxEntity.UpdateTransmission lets nearby enemy groups detect a transmission (direction finding).
    // For the player's boat the method does nothing else, so it is skipped entirely
    // (the "can transmit" check is kept so a transmission still stops when it has to).
    [HarmonyPatch(typeof(SandboxEntity), "UpdateTransmission")]
    internal static class Radio_UndetectablePatch
    {
        private static bool Prefix(SandboxEntity __instance, ref float __result)
        {
            if (!Settings.B("RadioUndetectable") || !__instance.IsPlayerShip) return true;
            if (!Traverse.Create(__instance).Method("CanTransmit").GetValue<bool>()) return true;
            __result = 2f;
            return false;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/RadioDecrypt.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::HarmonyLib;
    using global::UBOAT.Game.Sandbox;
    using global::UBOAT.Game.Sandbox.Messages;

    // QoL (DoMe 2026-11-13): every radio message is readable. The game shows "Message Encrypted" when the message's
    // EncryptionMethod is neither Raw nor the player's country's method (RadioStory.GetFormattedMessageContents and
    // JournalMessageUI.Start). While those two methods run, the player's country temporarily uses the message's method.
    internal static class RadioDecrypt
    {
        private static readonly System.Reflection.FieldInfo EncF = AccessTools.Field(typeof(Country), "encryptionMethod");
        private static bool logged;

        internal static object Swap(Country c, IMessage m)
        {
            if (c == null || m == null || EncF == null || !Settings.B("RadioDecryptAll")) return null;
            var own = EncF.GetValue(c);
            if (Equals(own, m.EncryptionMethod) || m.EncryptionMethod == EncryptionMethod.Raw) return null;
            EncF.SetValue(c, m.EncryptionMethod);
            if (!logged) { logged = true; Util.Log("Funk: fremd verschlüsselter Spruch entschlüsselt (" + m.EncryptionMethod + ")"); }
            return own;
        }

        internal static void Restore(Country c, object own)
        {
            if (c != null && own != null && EncF != null) EncF.SetValue(c, own);
        }

        internal static Country PlayerCountry(object proxy)
        {
            var c = proxy != null ? Traverse.Create(proxy).Property("Country").GetValue<Country>() : null;
            if (c == null && Util.PlayerShip != null) c = Util.PlayerShip.Country;
            return c;
        }
    }

    [HarmonyPatch(typeof(UBOAT.Game.Scene.Stories.RadioStory), "GetFormattedMessageContents")]
    internal static class RadioDecrypt_StoryPatch
    {
        private static void Prefix(object __instance, IMessage message, out object[] __state)
        {
            __state = null;
            try
            {
                var c = RadioDecrypt.PlayerCountry(Traverse.Create(__instance).Field("playerShip").GetValue());
                var own = RadioDecrypt.Swap(c, message);
                if (own != null) __state = new object[] { c, own };
            }
            catch { }
        }

        private static System.Exception Finalizer(System.Exception __exception, object[] __state)
        {
            if (__state != null) RadioDecrypt.Restore((Country)__state[0], __state[1]);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(UBOAT.Game.UI.Journal.JournalMessageUI), "Start")]
    internal static class RadioDecrypt_JournalPatch
    {
        private static void Prefix(object __instance, out object[] __state)
        {
            __state = null;
            try
            {
                var t = Traverse.Create(__instance);
                var c = RadioDecrypt.PlayerCountry(t.Field("playerShip").GetValue());
                var own = RadioDecrypt.Swap(c, t.Field("message").GetValue<IMessage>());
                if (own != null) __state = new object[] { c, own };
            }
            catch { }
        }

        private static System.Exception Finalizer(System.Exception __exception, object[] __state)
        {
            if (__state != null) RadioDecrypt.Restore((Country)__state[0], __state[1]);
            return __exception;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Reticle.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Collections;
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Core.Input;
    using global::UBOAT.Game.Scene.Audio;
    using global::UBOAT.Game.Scene.Camera;
    using global::UBOAT.Game.Scene.Camera.ExternalControllers;
    using global::UBOAT.Game.Scene.Effects;
    using global::UBOAT.Game.Scene.Items;

    // Reticle light at periscopes / UZO / binoculars:
    //  - key (default F) switches it on/off while looking through, with a click sound
    //  - own colour + brightness, a factor per optic
    //  - bearing scale balanced to the reticle (mask copy with scaled alpha, labels lit too,
    //    weak masks normalised) and compensated for the per-sight night grading
    public class Reticle : ModuleBase
    {
        internal static bool Lit = true;
        private static readonly FieldInfo GameInputF = AccessTools.Field(typeof(MainCamera), "gameInput");
        private static readonly FieldInfo AudioLibF = AccessTools.Field(typeof(Periscope), "audioLib");
        private static readonly FieldInfo AudioTargetF = AccessTools.Field(typeof(Periscope), "audioTarget");
        private static readonly FieldInfo TagsF = AccessTools.Field(typeof(AudioLib), "tags");
        private static readonly HashSet<int> loggedLibs = new HashSet<int>();
        private static readonly string[] SoundTags = { "Switch", "Click", "Button", "Zoom Swap" };
        private static GameInput input;
        private static string stamp;

        public override void Frame()
        {
            // re-balance the masks when a reticle setting changed
            string s = Settings.F("ReticleBearing") + "|" + Settings.B("ReticleLabels") + "|" + Settings.B("ReticleCustom");
            if (s != stamp) { bool first = stamp == null; stamp = s; if (!first) Util.Safe("Reticle.Reapply", ReticleMask.ReapplyAll); }

            if (!Keys.Down("KeyReticleLight")) return;
            if (input == null && GameInputF != null) input = GameInputF.GetValue(null) as GameInput;
            if (input != null && input.HasActiveBlockades()) return;
            var p = Current();
            if (p == null) return;
            Lit = !Lit;
            ToolkitRunner.ShowNotice(Lit ? "Fadenkreuz-Licht an" : "Fadenkreuz-Licht aus", 2f);
            if (Settings.B("ReticleSound")) Util.Safe("Reticle.Sound", () => Sound(p));
        }

        private static Periscope Current()
        {
            var cam = MainCamera.Instance;
            var pc = cam?.GenericController?.ExternalController as PeriscopeController;
            return pc != null && pc.Periscope != null && pc.Periscope.ManualControl ? pc.Periscope : null;
        }

        private static void Sound(Periscope p)
        {
            var lib = AudioLibF?.GetValue(p) as AudioLib;
            if (lib == null) return;
            var target = AudioTargetF?.GetValue(p) as Transform ?? p.transform;
            string played = null;
            foreach (var tag in SoundTags)
            {
                var src = lib.GetClip3D(tag, target);
                if (src == null) continue;
                src.volume *= Settings.F("ReticleSoundVolume");
                src.Play();
                played = tag;
                break;
            }
            if (TagsF != null && loggedLibs.Add(lib.GetInstanceID()) && TagsF.GetValue(lib) is IDictionary tags && tags.Count > 0)
            {
                var names = new List<string>();
                foreach (var k in tags.Keys) names.Add(Convert.ToString(k));
                names.Sort(StringComparer.OrdinalIgnoreCase);
                Util.Log("Fadenkreuz: Töne in '" + lib.name + "': " + string.Join(", ", names.ToArray()));
            }
            if (played == null) Util.Log("Fadenkreuz: kein passender Klick-Ton bei " + p.name);
        }

        // ---- colour ----
        private static readonly Color VanillaGreen = new Color(0.001456f, 0.014f, 0.001665f, 1f);
        private static readonly FieldInfo OverlayNameF = AccessTools.Field(typeof(Periscope), "screenOverlayName");
        private static float Lum(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        private static Color Configured()
        {
            var tint = new Color(Settings.F("ReticleR") / 255f, Settings.F("ReticleG") / 255f, Settings.F("ReticleB") / 255f, 1f);
            float step = Mathf.Clamp(Settings.F("ReticleStep"), 0f, 100f);
            float brightness = 0.5f * Mathf.Pow(16f / 0.5f, step / 100f);
            float y = Lum(tint);
            if (!(y > 1e-6f)) return new Color(0f, 0f, 0f, 1f);
            float k = Lum(VanillaGreen) * brightness / y;
            return new Color(tint.r * k, tint.g * k, tint.b * k, 1f);
        }

        private static float ScopeFactor(Periscope p)
        {
            string o = p != null && OverlayNameF != null ? OverlayNameF.GetValue(p) as string : null;
            switch (o)
            {
                case "Targeting Sight": return Settings.F("ReticleMulUzo");
                case "Periscope": return Settings.F("ReticleMulAttack");
                case "Observation Periscope": return Settings.F("ReticleMulObservation");
                case "Binoculars": return Settings.F("ReticleMulBino");
                default: return 1f;
            }
        }

        internal static Color ColourFor(Periscope p)
        {
            var c = Configured();
            if (!Lit) return new Color(0f, 0f, 0f, c.a);
            float m = ScopeFactor(p) * ReticleMask.ColourMultiplierFor(p) * ReticleMask.MaskGainFor(p);
            return NightCompensate(new Color(c.r * m, c.g * m, c.b * m, c.a));
        }

        // The reticle is drawn before colour grading, so the per-sight grading would dim it: undo that.
        internal static Color NightCompensate(Color c)
        {
            int age = Time.frameCount - NightGrading.PublishedFrame;
            if (NightGrading.PublishedFrame < 0 || age < 0 || age > 1) return c;
            float ev = NightGrading.PublishedEvDelta, satBase = NightGrading.PublishedSatBase, satDelta = NightGrading.PublishedSatDelta;
            if (ev == 0f && satDelta == 0f) return c;
            float gain = Mathf.Pow(2f, -ev);
            float r = c.r * gain, g = c.g * gain, b = c.b * gain;
            float with = (satBase + satDelta) / 100f + 1f, without = satBase / 100f + 1f;
            if (with > 1e-3f && without > 0f)
            {
                float ratio = without / with, l = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                r = l + (r - l) * ratio; g = l + (g - l) * ratio; b = l + (b - l) * ratio;
            }
            return new Color(Mathf.Max(0f, r), Mathf.Max(0f, g), Mathf.Max(0f, b), c.a);
        }
    }

    [HarmonyPatch(typeof(Periscope), "ReticleIlluminationColor", MethodType.Getter)]
    internal static class Reticle_ColourPatch
    {
        private static bool Prefix(Periscope __instance, ref Color __result)
        {
            if (!Settings.B("ReticleCustom")) return true;
            __result = Reticle.ColourFor(__instance);
            return false;
        }
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Periscope __instance, ref Color __result)
        {
            if (Settings.B("ReticleCustom")) { __result = Reticle.ColourFor(__instance); return; }
            float m = Reticle.Lit ? Settings.F("ReticleBright") : 0f;
            if (!Mathf.Approximately(m, 1f)) __result = new Color(__result.r * m, __result.g * m, __result.b * m, __result.a);
        }
    }

    // Bearing scale: the game's illumination mask is far brighter than the reticle. A copy with
    // scaled alpha (and the scale's numbers merged in) evens it out; the reticle colour is raised to match.
    internal static class ReticleMask
    {
        private const float ShaderGain = 4.6f, MaxMaskGain = 16f;
        private const int MaxCopies = 8;
        private static readonly FieldInfo MaterialF = AccessTools.Field(typeof(PeriscopeIME), "material");
        private static int illumId = -1, overlayId = -1;
        private static int IllumId => illumId < 0 ? (illumId = Shader.PropertyToID("_IlluminationTex")) : illumId;
        private static int OverlayId => overlayId < 0 ? (overlayId = Shader.PropertyToID("_OverlayTex")) : overlayId;
        private static bool missingLogged;

        private sealed class Copy { public Texture2D Scaled; public float Multiplier, PeakAlpha; }
        private sealed class Applied { public PeriscopeIME Ime; public Texture Original; public Copy Copy; }
        private static readonly Dictionary<int, Copy> copies = new Dictionary<int, Copy>();
        private static readonly List<Applied> live = new List<Applied>();
        private static readonly Dictionary<int, float> multByScope = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> gainByScope = new Dictionary<int, float>();

        public static float ColourMultiplierFor(Periscope p) => p != null && multByScope.TryGetValue(p.GetInstanceID(), out var m) ? m : 1f;
        public static float MaskGainFor(Periscope p) => Settings.B("ReticleNormalize") && p != null && gainByScope.TryGetValue(p.GetInstanceID(), out var g) ? g : 1f;
        private static float MaskGain(float peak) { if (float.IsNaN(peak) || peak <= 0f) return 1f; float g = 1f / (peak * peak); return Mathf.Clamp(g, 1f, MaxMaskGain); }

        public static void Apply(PeriscopeIME ime)
        {
            try
            {
                if (!Settings.B("ReticleCustom") || ime == null) return;
                if (MaterialF == null) { if (!missingLogged) { missingLogged = true; Util.Log("Fadenkreuz: Masken-Material nicht gefunden (Spiel-Update?) - Peilskala bleibt wie im Spiel"); } return; }
                var mat = MaterialF.GetValue(ime) as Material;
                if (mat == null) return;
                var scope = ime.Periscope;
                var current = mat.GetTexture(IllumId);
                var existing = Find(ime);
                var source = existing != null && existing.Copy != null && ReferenceEquals(current, existing.Copy.Scaled) ? existing.Original : current;
                if (source == null || source.width <= 4 || source.height <= 4)
                {
                    Forget(ime, false);
                    if (scope != null) gainByScope.Remove(scope.GetInstanceID());
                    return;
                }
                var overlay = Settings.B("ReticleLabels") ? mat.GetTexture(OverlayId) : null;
                if (overlay != null && (overlay.width <= 4 || overlay.height <= 4)) overlay = null;
                var copy = CopyFor(source, overlay);
                if (copy == null) return;
                if (scope != null) gainByScope[scope.GetInstanceID()] = MaskGain(copy.PeakAlpha);
                if (copy.Scaled == null)
                {
                    if (existing != null) Forget(ime, true);
                    if (scope != null) multByScope.Remove(scope.GetInstanceID());
                    return;
                }
                mat.SetTexture(IllumId, copy.Scaled);
                if (existing == null) { existing = new Applied { Ime = ime }; live.Add(existing); }
                existing.Original = source; existing.Copy = copy;
                if (scope != null) multByScope[scope.GetInstanceID()] = copy.Multiplier;
            }
            catch (Exception e) { Util.Log("Fadenkreuz: Peilskala nicht angepasst, Spiel-Maske bleibt: " + e.Message); }
        }

        private static Applied Find(PeriscopeIME ime)
        {
            for (int i = live.Count - 1; i >= 0; i--) { if (live[i].Ime == null) { live.RemoveAt(i); continue; } if (ReferenceEquals(live[i].Ime, ime)) return live[i]; }
            return null;
        }
        private static void Forget(PeriscopeIME ime, bool restore)
        {
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var a = live[i];
                if (a.Ime == null) { live.RemoveAt(i); continue; }
                if (!ReferenceEquals(a.Ime, ime)) continue;
                if (restore) Restore(a);
                live.RemoveAt(i);
            }
        }
        private static void Restore(Applied a)
        {
            try
            {
                var mat = a.Ime != null ? MaterialF.GetValue(a.Ime) as Material : null;
                if (mat != null && a.Copy != null && ReferenceEquals(mat.GetTexture(IllumId), a.Copy.Scaled)) mat.SetTexture(IllumId, a.Original);
            }
            catch (Exception e) { Util.Log("Fadenkreuz: Zurücksetzen fehlgeschlagen: " + e.Message); }
        }
        public static void RestoreAll()
        {
            for (int i = live.Count - 1; i >= 0; i--) Restore(live[i]);
            live.Clear(); multByScope.Clear(); gainByScope.Clear();
            foreach (var kv in copies) if (kv.Value.Scaled != null) UnityEngine.Object.Destroy(kv.Value.Scaled);
            copies.Clear();
        }
        public static void ReapplyAll()
        {
            var imes = new List<PeriscopeIME>();
            foreach (var a in live) if (a.Ime != null) imes.Add(a.Ime);
            if (imes.Count == 0) foreach (var i in UnityEngine.Object.FindObjectsOfType<PeriscopeIME>()) imes.Add(i);
            RestoreAll();
            if (!Settings.B("ReticleCustom")) return;
            foreach (var i in imes) Apply(i);
        }

        private static Texture2D ReadBack(Texture src, string name)
        {
            int w = src.width, h = src.height;
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var prev = RenderTexture.active;
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                var t = new Texture2D(w, h, TextureFormat.RGBA32, true, true) { name = name, hideFlags = HideFlags.HideAndDontSave };
                t.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                return t;
            }
            finally { RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt); }
        }

        // Ink of the overlay that is NOT connected to the edge (= the numbers inside the scale).
        private static byte[] IsolatedInk(Texture overlay, int w, int h)
        {
            Texture2D ov = null;
            try
            {
                ov = ReadBack(overlay, "overlay readback");
                int ow = ov.width, oh = ov.height;
                var op = ov.GetPixels32();
                var surround = new byte[op.Length];
                var queue = new int[op.Length];
                int head = 0, tail = 0;
                const byte Ink = 8;
                void Seed(int i) { if (op[i].a >= Ink && surround[i] == 0) { surround[i] = 1; queue[tail++] = i; } }
                for (int x = 0; x < ow; x++) { Seed(x); Seed((oh - 1) * ow + x); }
                for (int y = 0; y < oh; y++) { Seed(y * ow); Seed(y * ow + ow - 1); }
                while (head < tail)
                {
                    int i = queue[head++], x = i % ow, y = i / ow;
                    if (x > 0) Seed(i - 1);
                    if (x < ow - 1) Seed(i + 1);
                    if (y > 0) Seed(i - ow);
                    if (y < oh - 1) Seed(i + ow);
                }
                var ink = new byte[w * h];
                if (ow == w && oh == h) for (int i = 0; i < ink.Length; i++) ink[i] = surround[i] == 0 ? op[i].a : (byte)0;
                else
                    for (int y = 0; y < h; y++)
                    {
                        int oy = Mathf.Min(oh - 1, y * oh / h);
                        for (int x = 0; x < w; x++) { int o = oy * ow + Mathf.Min(ow - 1, x * ow / w); ink[y * w + x] = surround[o] == 0 ? op[o].a : (byte)0; }
                    }
                return ink;
            }
            catch (Exception e) { Util.Log("Fadenkreuz: Zahlen der Skala nicht lesbar: " + e.Message); return null; }
            finally { if (ov != null) UnityEngine.Object.Destroy(ov); }
        }

        private static Copy CopyFor(Texture source, Texture overlay)
        {
            int id = source.GetInstanceID() * 31 + (overlay == null ? 0 : overlay.GetInstanceID());
            if (copies.TryGetValue(id, out var copy)) return copy;
            if (copies.Count >= MaxCopies) { foreach (var kv in copies) if (kv.Value.Scaled != null) UnityEngine.Object.Destroy(kv.Value.Scaled); copies.Clear(); }
            int w = source.width, h = source.height;
            var scaled = ReadBack(source, source.name + " (Toolkit)");
            var px = scaled.GetPixels32();
            int peak = 0;
            foreach (var p in px) if (p.a > peak) peak = p.a;
            float peakAlpha = peak / 255f;
            int labels = 0;
            if (overlay != null && peak > 0)
            {
                var ink = IsolatedInk(overlay, w, h);
                if (ink != null)
                    for (int i = 0; i < px.Length; i++) { int lit = ink[i] * peak / 255; if (lit > px[i].a) { var c = px[i]; c.a = (byte)lit; px[i] = c; labels++; } }
            }
            float m = Mathf.Max(1f, Mathf.Clamp(Settings.F("ReticleBearing"), 0.2f, 2f) * ShaderGain * peakAlpha * peakAlpha);
            float k = 1f / Mathf.Sqrt(m);
            if (m > 1.0001f || labels > 0)
            {
                if (m > 1.0001f) for (int i = 0; i < px.Length; i++) { var c = px[i]; c.a = (byte)Mathf.Clamp(Mathf.RoundToInt(c.a * k), 0, 255); px[i] = c; }
                scaled.SetPixels32(px);
                scaled.Apply(true, true);
            }
            else { UnityEngine.Object.Destroy(scaled); scaled = null; }
            copy = new Copy { Scaled = scaled, Multiplier = m, PeakAlpha = peakAlpha };
            copies[id] = copy;
            return copy;
        }
    }

    [HarmonyPatch(typeof(PeriscopeIME), "ValidateOverlay")]
    internal static class Reticle_OverlayPatch
    {
        private static void Postfix(PeriscopeIME __instance) => ReticleMask.Apply(__instance);
    }
}

// ====================================================================================================
// Abschnitt: Modules/ScopeStall.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Globalization;
    using global::System.IO;
    using global::System.Reflection;
    using global::System.Text;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::DWS.Common.InjectionFramework;
    using global::UBOAT.Game.Core.Serialization;
    using global::UBOAT.Game.Scene.Camera;
    using global::UBOAT.Game.Scene.Camera.ExternalControllers;
    using global::UBOAT.Game.Scene.Effects;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Items;

    // Periscope/UZO zoom without stutter: overlay textures stay loaded (up to a memory cap),
    // overlays seen before are preloaded in the background after a load, the UZO zoom dropdown of
    // a realistic-UZO mod refreshes the overlay instead of restarting the view, slow loads are logged.
    internal static class ScopeCfg
    {
        public static bool On => Settings.B("FixScopeStall");
        public static bool Retain => On && Settings.B("ScopeRetain");
        public static bool Preload => Settings.B("ScopePreload");
        public static bool ReplaceUzo => Settings.B("ScopeUzoRefresh");
        public static float MaxMb => Mathf.Max(0f, Settings.F("ScopeMaxMb"));
        public static float LogSlowMs => Settings.B("ScopeLog") ? Mathf.Max(0f, Settings.F("ScopeLogSlowMs")) : float.MaxValue;
        public static void Log(string m) { if (Settings.B("ScopeLog")) Util.Log("Sehrohr-Ruckeln: " + m); }
    }

    internal static class ScopeClock
    {
        public static long Now() => LoadClock.Now();
        public static double MsSince(long t) => LoadClock.Seconds(LoadClock.Now() - t) * 1000.0;
    }

    [HarmonyPatch(typeof(PeriscopeIME), "ValidateOverlay")]
    internal static class ScopeRetainer
    {
        public static readonly object Referent = new object();
        private static FieldInfo[] textureFields;
        private static FieldInfo resourceField, assetField;
        private static MethodInfo addUsage, removeUsage;
        private static readonly Dictionary<object, long> retained = new Dictionary<object, long>();
        private static long retainedBytes;
        private static bool capLogged, errorLogged, inited, lastRetain;
        public static PeriscopeIME LastIme;

        private static void Init()
        {
            if (inited) return;
            inited = true;
            var found = new List<FieldInfo>();
            foreach (var f in typeof(PeriscopeIME).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var t = f.FieldType;
                if (t.IsGenericType && t.GetGenericTypeDefinition().Name == "Resource`1" && t.GetGenericArguments()[0] == typeof(Texture2D)) found.Add(f);
            }
            textureFields = found.ToArray();
            if (textureFields.Length > 0)
            {
                var rt = textureFields[0].FieldType;
                const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                resourceField = rt.GetField("resource", any);
                assetField = rt.GetField("asset", any);
                if (resourceField != null)
                {
                    var iface = resourceField.FieldType;
                    addUsage = iface.GetMethod("AddUsage", new[] { typeof(object), typeof(bool) });
                    removeUsage = iface.GetMethod("RemoveUsage", new[] { typeof(object) });
                }
            }
            ScopeCfg.Log("Overlay-Felder gefunden: " + textureFields.Length + (addUsage == null || removeUsage == null ? " - Festhalten nicht möglich" : ""));
        }

        public static void Adopt(object resource) { if (resource != null && !retained.ContainsKey(resource)) retained[resource] = 0; }

        private static void Prefix(out long __state) { __state = ScopeClock.Now(); }

        private static void Postfix(PeriscopeIME __instance, long __state)
        {
            try
            {
                double ms = ScopeClock.MsSince(__state);
                Init();
                LastIme = __instance;
                bool retain = ScopeCfg.Retain;
                if (lastRetain && !retain) ReleaseAll("ausgeschaltet");
                lastRetain = retain;
                if (!ScopeCfg.On) return;
                if (retain) Retain(__instance);
                if (ms >= ScopeCfg.LogSlowMs)
                    ScopeCfg.Log("Overlay (" + (__instance.Periscope != null ? __instance.Periscope.Name : "?") + ") brauchte " + ms.ToString("0.0", CultureInfo.InvariantCulture) + " ms.");
            }
            catch (Exception e) { if (!errorLogged) { errorLogged = true; Util.Log("Sehrohr-Ruckeln: Fehler (einmalig) " + e); } }
        }

        private static void Retain(PeriscopeIME ime)
        {
            if (addUsage == null || resourceField == null) return;
            foreach (var tf in textureFields)
            {
                object boxed = tf.GetValue(ime);
                if (boxed == null) continue;
                object res = resourceField.GetValue(boxed);
                if (res == null) continue;
                bool known = retained.ContainsKey(res);
                Texture tex = null;
                long bytes = 0;
                if (!known)
                {
                    tex = assetField != null ? assetField.GetValue(boxed) as Texture : null;
                    bytes = EstimateBytes(tex);
                    if ((retainedBytes + bytes) / (1024.0 * 1024.0) > ScopeCfg.MaxMb)
                    {
                        if (!capLogged) { capLogged = true; ScopeCfg.Log("Obergrenze erreicht (" + Mb(retainedBytes) + " MB) - weitere Overlays wie im Spiel."); }
                        continue;
                    }
                }
                addUsage.Invoke(res, new object[] { Referent, false });
                ScopePreloader.Remember(ScopeResources.PathOf(res));
                if (!known)
                {
                    retained[res] = bytes; retainedBytes += bytes;
                    ScopeCfg.Log("halte Overlay geladen: " + Describe(tex) + ", ~" + Mb(bytes) + " MB (gesamt ~" + Mb(retainedBytes) + " MB, " + retained.Count + " Texturen).");
                }
            }
        }

        public static void ReleaseAll(string why)
        {
            if (removeUsage == null || retained.Count == 0) return;
            int n = retained.Count;
            foreach (var r in retained.Keys) { try { removeUsage.Invoke(r, new object[] { Referent }); } catch { } }
            retained.Clear(); retainedBytes = 0; capLogged = false;
            ScopeCfg.Log(n + " Overlay-Textur(en) freigegeben (" + why + ").");
        }

        private static string Describe(Texture t)
        {
            if (t == null) return "(noch nicht geladen)";
            var t2 = t as Texture2D;
            return "'" + t.name + "' " + t.width + "x" + t.height + (t2 != null ? " " + t2.format : "");
        }

        private static long EstimateBytes(Texture t)
        {
            if (t == null) return 0;
            double bpp = 4.0;
            if (t is Texture2D t2)
            {
                switch (t2.format)
                {
                    case TextureFormat.DXT1: case TextureFormat.BC4: bpp = 0.5; break;
                    case TextureFormat.DXT5: case TextureFormat.BC5: case TextureFormat.BC6H: case TextureFormat.BC7: case TextureFormat.Alpha8: case TextureFormat.R8: bpp = 1.0; break;
                    case TextureFormat.RGBAHalf: bpp = 8.0; break;
                    case TextureFormat.RGBAFloat: bpp = 16.0; break;
                }
                if (t2.mipmapCount > 1) bpp *= 4.0 / 3.0;
            }
            return (long)((double)t.width * t.height * bpp);
        }

        private static string Mb(long b) => (b / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture);
    }

    // Remembers every overlay ever kept (file toolkit-scope-preload.txt) and loads them in the
    // background 20 s after a career is ready, one every 0.25 s.
    internal static class ScopePreloader
    {
        private static readonly List<string> paths = new List<string>();
        private static readonly HashSet<string> known = new HashSet<string>(StringComparer.Ordinal);
        private static readonly List<object> requested = new List<object>();
        private static bool dirty, inited;
        public static int Count => paths.Count;
        public static int RequestedCount => requested.Count;
        private static string FilePath => Path.Combine(Settings.Folder, "toolkit-scope-preload.txt");

        public static void Init()
        {
            if (inited) return;
            inited = true;
            try
            {
                if (File.Exists(FilePath))
                    foreach (var raw in File.ReadAllLines(FilePath))
                    {
                        string line = raw.Trim().TrimStart('﻿');
                        if (line.Length == 0 || line[0] == '#') continue;
                        if (known.Add(line)) paths.Add(line);
                    }
                ScopeCfg.Log(paths.Count + " Overlay(s) für Vorladen gemerkt.");
            }
            catch (Exception e) { Util.Log("Sehrohr-Ruckeln: Vorladen nicht möglich: " + e.Message); }
        }

        public static void Remember(string path)
        {
            if (string.IsNullOrEmpty(path) || !known.Add(path)) return;
            paths.Add(path); dirty = true;
        }

        public static void FlushIfDirty()
        {
            if (!dirty) return;
            dirty = false;
            try
            {
                Directory.CreateDirectory(Settings.Folder);
                var lines = new List<string> { "# Sehrohr-Overlays, die im Hintergrund vorgeladen werden. Wird automatisch gefüllt;", "# Datei löschen = alles vergessen." };
                lines.AddRange(paths);
                File.WriteAllLines(FilePath, lines.ToArray(), new UTF8Encoding(false));
            }
            catch (Exception e) { Util.Log("Sehrohr-Ruckeln: Vorlade-Liste nicht speicherbar: " + e.Message); }
        }

        public static void Request(int i)
        {
            var r = ScopeResources.RetrieveAsync(paths[i], ScopeRetainer.Referent);
            if (r == null) return;
            requested.Add(r);
            ScopeRetainer.Adopt(r);
        }

        public static int LoadedCount() { int n = 0; foreach (var r in requested) if (ScopeResources.IsLoaded(r)) n++; return n; }
    }

    public class ScopePreloadHost : ModuleBase
    {
        private float nextCheck, nextFlush, readySince = -1f, nextRequest, verifyAt = -1f;
        private int next;
        private bool done;

        public override void Frame()
        {
            ScopePreloader.Init();
            float now = Time.unscaledTime;
            if (now >= nextFlush) { nextFlush = now + 6.7f; ScopePreloader.FlushIfDirty(); }
            if (verifyAt > 0f && now >= verifyAt) { verifyAt = -1f; ScopeCfg.Log("Vorladen: " + ScopePreloader.LoadedCount() + "/" + ScopePreloader.RequestedCount + " Overlays geladen."); }
            if (done || !ScopeCfg.Retain || !ScopeCfg.Preload || ScopePreloader.Count == 0) return;
            if (readySince < 0f)
            {
                if (now < nextCheck) return;
                nextCheck = now + 3f;
                if (Util.PlayerShip == null || ScopeResources.IsLoading()) return;
                readySince = now;
                return;
            }
            if (now - readySince < 20f || now < nextRequest) return;
            if (next >= ScopePreloader.Count || !ScopeResources.Ready)
            {
                done = true; verifyAt = now + 30f;
                ScopeCfg.Log("Vorladen angefordert für " + ScopePreloader.RequestedCount + " Overlay(s)" + (ScopeResources.Ready ? "." : " - Ressourcen-Schnittstelle fehlt."));
                return;
            }
            ScopePreloader.Request(next++);
            nextRequest = now + 0.25f;
        }
    }

    internal static class ScopeResources
    {
        private static bool tried;
        private static object manager;
        private static MethodInfo retrieveAsync;
        private static PropertyInfo isLoadingP, pathP, isLoadedP;

        public static bool Ready { get { Init(); return manager != null && retrieveAsync != null; } }

        public static string PathOf(object r) { Init(); if (r == null || pathP == null) return null; try { return pathP.GetValue(r, null) as string; } catch { return null; } }
        public static bool IsLoaded(object r) { Init(); if (r == null || isLoadedP == null) return false; try { return (bool)isLoadedP.GetValue(r, null); } catch { return false; } }
        public static bool IsLoading() { Init(); if (manager == null || isLoadingP == null) return false; try { return (bool)isLoadingP.GetValue(manager, null); } catch { return false; } }

        public static object RetrieveAsync(string path, object referent)
        {
            if (!Ready) return null;
            try { return retrieveAsync.Invoke(manager, new object[] { path, referent, null, false }); }
            catch (Exception e) { ScopeCfg.Log("Vorladen von '" + path + "' fehlgeschlagen: " + e.Message); return null; }
        }

        private static void Init()
        {
            if (tried) return;
            tried = true;
            try
            {
                const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var iface = UboatToolkit.Util.FindType("DWS.Common.Resources.IResource");
                if (iface != null) { pathP = iface.GetProperty("Path"); isLoadedP = iface.GetProperty("IsLoaded"); }
                var mt = UboatToolkit.Util.FindType("DWS.Common.Resources.ResourceManager");
                if (mt == null) return;
                isLoadingP = mt.GetProperty("IsLoading", inst);
                foreach (var m in typeof(ScriptableObjectSingleton).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    if (m.Name == "LoadSingleton" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0) { manager = m.MakeGenericMethod(mt).Invoke(null, null); break; }
                foreach (var m in mt.GetMethods(inst))
                    if (m.Name == "RetrieveAssetAsync" && m.GetParameters().Length == 4) { retrieveAsync = m.IsGenericMethodDefinition ? m.MakeGenericMethod(typeof(Texture2D)) : m; break; }
            }
            catch (Exception e) { Util.Log("Sehrohr-Ruckeln: Ressourcen-Schnittstelle fehlt: " + e.Message); }
        }
    }

    // A realistic-UZO mod restarts the whole view on its zoom dropdown; refresh the overlay instead.
    [HarmonyPatch]
    internal static class ScopeUzoZoom
    {
        private static MethodInfo validate;
        private static bool lastDirect, errorLogged;
        private static int direct, restarts;

        private static MethodBase Target()
        {
            var t = UboatToolkit.Util.FindType("RealisticUZOSight.PeriscopeUIPatches+OnZoomSelectedPeriscopeUIPatch");
            return t == null ? null : AccessTools.Method(t, "Postfix");
        }

        private static bool Prepare() { validate = AccessTools.Method(typeof(PeriscopeIME), "ValidateOverlay"); return validate != null && Target() != null; }
        private static MethodBase TargetMethod() => Target();

        private static bool Prefix(object[] __args, out long __state)
        {
            __state = ScopeClock.Now();
            lastDirect = false;
            try
            {
                if (!ScopeCfg.On || !ScopeCfg.ReplaceUzo) return true;
                var main = __args != null && __args.Length > 0 ? __args[0] as MainCamera : null;
                if (main == null || main.GenericController == null) return true;
                var ctl = main.GenericController.ExternalController as PeriscopeController;
                var scope = ctl != null ? ctl.Periscope : null;
                var ime = ScopeRetainer.LastIme;
                if (scope == null || ime == null || !ime.isActiveAndEnabled || ime.Periscope != scope) return true;
                validate.Invoke(ime, null);
                lastDirect = true;
                return false;
            }
            catch (Exception e) { if (!errorLogged) { errorLogged = true; Util.Log("Sehrohr-Ruckeln: UZO-Ersatz fehlgeschlagen, nehme Original (einmalig): " + e); } return true; }
        }

        private static void Postfix(long __state)
        {
            double ms = ScopeClock.MsSince(__state);
            if (lastDirect) direct++; else restarts++;
            ScopeCfg.Log("UZO-Zoom: " + ms.ToString("0.0", CultureInfo.InvariantCulture) + " ms über " + (lastDirect ? "Overlay-Auffrischen" : "Neustart der Ansicht") + " (Sitzung: " + direct + " Auffrischen, " + restarts + " Neustarts).");
        }
    }

    // Logs synchronous asset-bundle opens (explains any stall that is left).
    [HarmonyPatch]
    internal static class ScopeBundleTimer
    {
        private static readonly HashSet<string> opened = new HashSet<string>(StringComparer.Ordinal);
        private static int count;
        private static double total;

        private static MethodBase Target()
        {
            var t = UboatToolkit.Util.FindType("DWS.Common.Resources.AssetBundleResource");
            return t == null ? null : AccessTools.Method(t, "LoadAsset", Type.EmptyTypes);
        }

        private static bool Prepare() => Target() != null;
        private static MethodBase TargetMethod() => Target();
        private static void Prefix(out long __state) { __state = ScopeClock.Now(); }

        private static void Postfix(object __instance, long __state)
        {
            try
            {
                double ms = ScopeClock.MsSince(__state);
                string path = ScopeResources.PathOf(__instance) ?? "?";
                bool again = !opened.Add(path);
                count++; total += ms;
                if (ScopeCfg.On && (ms >= ScopeCfg.LogSlowMs || (again && Settings.B("ScopeLog"))))
                    ScopeCfg.Log("Asset-Bundle " + (again ? "ERNEUT" : "") + " geöffnet: " + path + " in " + ms.ToString("0.0", CultureInfo.InvariantCulture) + " ms (Sitzung: " + count + ", " + total.ToString("0", CultureInfo.InvariantCulture) + " ms).");
            }
            catch { }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/SensorCheats.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Items;
    using global::UBOAT.Game.Scene.Utilities;

    // Cheats:
    // - Radio reception at any depth.
    // - Hydrophone hears fully when surfaced and at high speed.
    // - Exact target data (speed, course, distance) instead of rough estimates.
    // - Hull armour: damage to your boat and crew is reduced (explosions, shells, torpedoes, depth charges, ramming).
    public class SensorCheats : ModuleBase
    {
        // "Hydrophone hears fully surfaced / fast": see HydroSame below (no per-tick overwrite any more).
        private readonly System.Collections.Generic.Dictionary<Hydrophone, bool> state = new System.Collections.Generic.Dictionary<Hydrophone, bool>();
        public override void Tick()
        {
            foreach (var h in Util.Find<Hydrophone>())
            {
                if (h == null) continue;
                bool want = HydroSame.Wanted(h);
                state.TryGetValue(h, out var was);
                if (want == was) continue;
                state[h] = want;
                if (want) HydroSame.Force(h); else HydroSame.Restore(h);
            }
        }
    }

    [HarmonyPatch(typeof(Antenna), "LowFrequencyUpdate")]
    internal static class Sensor_RadioDepthPatch
    {
        private static void Postfix(Antenna __instance)
        {
            if (Settings.B("CheatRadioAnyDepth") && __instance.ReceiveRangeDepthModifier != null)
                __instance.ReceiveRangeDepthModifier.Value = 1f;
        }
    }

    [HarmonyPatch(typeof(Hydrophone), nameof(Hydrophone.AllowOnlyCrudeEstimations), MethodType.Getter)]
    internal static class Sensor_ExactTargetsPatch
    {
        // 0.4.29 (player-only rule): only the player's own hydrophone, never an enemy one.
        private static void Postfix(Hydrophone __instance, ref bool __result)
        {
            if (Settings.B("CheatExactTargets") && Util.IsPlayers(__instance)) __result = false;
        }
    }

    // Cheat (DoMe 2026-11-16: "instant Schusslösung"): the game builds the firing solution from "tracking progress" that
    // grows while a contact is watched (accuracy = 1 - e^(-0.1 x progress)). With the cheat every sighting by the
    // player's boat counts as fully tracked at once, so type, speed, course and distance are there immediately.
    [HarmonyPatch(typeof(DirectObservation), nameof(DirectObservation.UpdateObservation))]
    internal static class Sensor_InstantSolutionPatch
    {
        private static void Prefix(DirectObservation __instance, ref float trackingProgress)
        {
            if (!Settings.B("CheatInstantSolution") || !(__instance.Observator is PlayerShip)) return;
            if (__instance.TrackingProgress < 100f) trackingProgress = Mathf.Max(trackingProgress, 100f - __instance.TrackingProgress);
        }
    }

    [HarmonyPatch(typeof(DamageUtility), nameof(DamageUtility.ApplyDamage))]
    internal static class Sensor_ArmourPatch
    {
        private static float nextNote;
        private static readonly Collider[] buf = new Collider[256];
        private static bool OthersInBlast(PlayerShip ship, Vector3 position, float radius)
        {
            int n = Physics.OverlapSphereNonAlloc(position, radius * 1.35f, buf, 1090049, QueryTriggerInteraction.Collide);
            if (n >= buf.Length) return true;                       // too many to check: be safe, no armour
            for (int i = 0; i < n; i++)
            {
                var e = buf[i] != null ? buf[i].GetComponentInParent<Entity>() : null;
                if (e != null && e != ship) return true;
            }
            return false;
        }
        private static void Prefix(Entity appliedBy, Vector3 position, float damageRadius, ref float damage, ref float crewDamage, ref float fireChance, Ship target)
        {
            float hull = Settings.F("CheatHullArmour");
            bool crew = Settings.B("CheatCrewProtection");
            if (hull >= 1f && !crew) return;
            var ship = Util.PlayerShip;
            if (ship == null || appliedBy == ship) return;
            if (target != null && target != ship) return;
            if ((position - ship.transform.position).magnitude > 80f + damageRadius) return;
            // 0.4.22 (option audit, player-only rule): a blast without a fixed target (bomb, depth charge, shell burst) has ONE
            // damage value for everything in its radius. If anything else than our boat is inside, the value stays as the
            // game made it, so no enemy next to us is ever protected by our armour.
            if (hull < 1f && target == null && OthersInBlast(ship, position, damageRadius))
            {
                if (Time.unscaledTime > nextNote) { nextNote = Time.unscaledTime + 30f; Util.Log("Rumpfpanzerung: Explosion trifft auch andere Schiffe, Schaden bleibt Spielwert (Panzerung gilt nur allein für dein Boot)"); }
                hull = 1f;
            }
            if (hull < 1f) { damage *= hull; fireChance *= hull; }
            if (crew) crewDamage = 0f;
        }
    }
}

namespace UboatToolkit.Modules
{
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Items;
    using global::UBOAT.Game.Scene.Utilities;

    // Own shells / flak must not damage our own boat. The game fires player shells with friendlyFire=true,
    // so a shell that bursts on our own tower or deck hurts hull, devices and crew - and the armour cheat
    // skipped those hits (appliedBy == our boat). Here our boat's parts are removed from the hit list.
    [HarmonyPatch(typeof(DamageUtility), "DoApplyDamage")]
    internal static class Sensor_OwnShellsPatch
    {
        internal static bool Active;
        private static bool logged;

        private static void Prefix(Entity appliedBy, DamageType damageType, ref float crewDamage)
        {
            Active = false;
            if (!Settings.B("CheatOwnShellsSafe") || damageType != DamageType.Projectile) return;
            var ship = Util.PlayerShip;
            if (ship == null || appliedBy != ship) return;
            Active = true;
            crewDamage = 0f;
            if (!logged) { logged = true; Util.Log("Eigene Granaten: Treffer am eigenen Boot werden ignoriert."); }
        }

        private static System.Exception Finalizer(System.Exception __exception) { Active = false; return __exception; }
    }

    [HarmonyPatch(typeof(DamageUtility), "CollectComponents")]
    internal static class Sensor_OwnShellsCollectPatch
    {
        private static readonly System.Reflection.FieldInfo compsF = AccessTools.Field(typeof(DamageUtility), "components");
        private static readonly System.Reflection.FieldInfo rendF = AccessTools.Field(typeof(DamageUtility), "damageRenderers");
        private static readonly System.Collections.Generic.List<UBOAT.Game.Scene.Effects.HullEffectsRenderer> drop = new System.Collections.Generic.List<UBOAT.Game.Scene.Effects.HullEffectsRenderer>();

        private static void Postfix()
        {
            if (!Sensor_OwnShellsPatch.Active) return;
            var ship = Util.PlayerShip;
            if (ship == null) return;
            var comps = compsF != null ? compsF.GetValue(null) as System.Collections.Generic.SortedList<float, Equipment> : null;
            if (comps != null)
                for (int i = comps.Count - 1; i >= 0; i--)
                {
                    var eq = comps.Values[i];
                    if (eq != null && eq.ParentEntity == ship) comps.RemoveAt(i);
                }
            var rend = rendF != null ? rendF.GetValue(null) as System.Collections.Generic.Dictionary<UBOAT.Game.Scene.Effects.HullEffectsRenderer, bool> : null;
            if (rend != null)
            {
                drop.Clear();
                foreach (var k in rend.Keys) if (k != null && k.ParentEntity == ship) drop.Add(k);
                for (int i = 0; i < drop.Count; i++) rend.Remove(drop[i]);
                drop.Clear();
            }
        }
    }
}

namespace UboatToolkit.Modules
{
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Items;
    using global::UBOAT.Game.Scene.Utilities;

    // Hydrophone "as good surfaced and fast as submerged and slow" (cheat CheatHydrophoneAlways, Warthog WhPassiveSame).
    // The game eases the speed penalty EVERY FRAME (Hydrophone.UpdateShipVelocityModifier) and fades the surface penalty
    // over 0.5 s. Overwriting the modifiers once per tick made the range jump between two values = the two green range
    // circles on the map flickered. The old fix patched SetShipVelocityModifiers, a 6-line method the JIT inlines, so it
    // never ran (no log line in DoMe's log 2026-11-13). Now the game's TARGET values are set to 1 in the bigger methods
    // that decide them, so the game itself settles on 1 and there is only one value.
    internal static class HydroSame
    {
        private static readonly System.Reflection.FieldInfo targetF = AccessTools.Field(typeof(Hydrophone), "shipVelocityModifierTargetValue");
        private static readonly System.Reflection.FieldInfo tweenF = AccessTools.Field(typeof(Hydrophone), "rangeModifierTween");
        private static readonly string[] Mods = { "surfaceRangeModifier", "surfaceGroupRangeModifier", "shipVelocityRangeModifier", "shipVelocityGroupRangeModifier" };
        private static bool logged;

        internal static bool Wanted(Hydrophone h)
        {
            if (h == null || !Util.IsPlayers(h)) return false;
            if (Settings.B("CheatHydrophoneAlways") || Settings.B("CheatHydrophoneFast")) return true;
            return Warthog.WarthogSonar.On && Settings.B("WhPassive") && Settings.B("WhPassiveSame");
        }

        internal static void Force(Hydrophone h)
        {
            try
            {
                // 0.4.29: two separate switches (surfaced / high speed); the Warthog passive sonar switch still means both
                bool wh = Warthog.WarthogSonar.On && Settings.B("WhPassive") && Settings.B("WhPassiveSame");
                bool surf = wh || Settings.B("CheatHydrophoneAlways"), fast = wh || Settings.B("CheatHydrophoneFast");
                if (fast && targetF != null) targetF.SetValue(h, 1f);
                // The game's range tween is NOT aborted any more: the map draws the green range circles from it (gone since 0.2.54).
                var t = Traverse.Create(h);
                foreach (var f in Mods) { if (!(f.StartsWith("surface") ? surf : fast)) continue; var m = t.Field(f).GetValue<Modifier>(); if (m != null && m.Value != 1f) m.Value = 1f; }
                if (!logged) { logged = true; Util.Log("Horcher (nur eigenes Gerät): Abzug aufgetaucht " + (surf ? "aufgehoben" : "wie im Spiel") + ", Abzug hohe Fahrt " + (fast ? "aufgehoben" : "wie im Spiel") + " (" + h.name + ")"); }
            }
            catch (System.Exception e) { Util.Log("Horcher: " + e.Message); }
        }

        // switched off again: let the game compute its own values
        internal static void Restore(Hydrophone h)
        {
            try
            {
                var t = Traverse.Create(h);
                t.Method("ValidateVelocityModifier").GetValue();
                if (h.ParentEntity is UBOAT.Game.Scene.Entities.PlayableSubmarine) t.Method("SurfaceRangeModifierParameter_Changed", new[] { typeof(Parameter) }, new object[] { null }).GetValue();
            }
            catch (System.Exception e) { Util.Log("Horcher zurück: " + e.Message); }
        }
    }

    [HarmonyPatch]
    internal static class HydroSame_Patches
    {
        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var n in new[] { "ValidateVelocityModifier", "UpdateShipVelocityModifier", "Submarine_SubmergeStateChanged", "SurfaceRangeModifierParameter_Changed", "OnSandboxRepresentationSet" })
            {
                var m = AccessTools.Method(typeof(Hydrophone), n);
                if (m != null) yield return m; else Util.Log("Horcher: Spiel-Methode " + n + " fehlt");
            }
        }
        private static void Postfix(Hydrophone __instance) { if (HydroSame.Wanted(__instance)) HydroSame.Force(__instance); }
    }
}

// ====================================================================================================
// Abschnitt: Modules/SonarScope.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::UBOAT.Game.Scene.Entities;
    using global::UnityEngine;

    // DoMe 2026-11-18: "ein Dial ... mit einem realen Sonar + Radar, so wie man es kennt, mit diesem laufenden Zeiger".
    // Round scope, bow up. A sweep line runs around; a contact lights up when the line passes it and fades after.
    // Shown is ONLY what our own boat really has as a contact right now (the game's own observation list of the
    // player boat): sonar/ping = green dot at its distance, radar = amber dot, hydrophone only = green mark on the
    // outer ring (bearing known, distance not). Nothing is read from enemies' sensors and nothing is changed in the game.
    // The window can be dragged with the mouse; the place is stored.
    public static class SonarScope
    {
        private struct Blip { public float Bearing, Frac; public int Kind; }   // Kind 0 sonar/ping, 1 radar, 2 hydrophone (bearing only)
        private static readonly List<Blip> blips = new List<Blip>();
        private static float nextScan, nextLog;
        private static bool dragging; private static Vector2 grab;
        private static int sonarN, radarN, hydroN;

        private static void Scan(PlayerShip ship, float rangeM)
        {
            blips.Clear(); sonarN = radarN = hydroN = 0;
            float head = ship.transform.eulerAngles.y;
            var list = ship.GetObservationsDirect();
            if (list == null) return;
            bool ping = ActiveSonar.Active && Settings.B("ActiveSonar");
            for (int i = 0; i < list.Count; i++)
            {
                var o = list[i];
                if (o == null || !o.IsValid || o.Entity == null) continue;
                Vector3 to = o.Entity.transform.position - ship.transform.position;
                float d = new Vector2(to.x, to.z).magnitude;
                float rel = Mathf.DeltaAngle(head, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);
                bool sonar = o.IsObservedVia(DirectObservationType.Sonar) || (ping && d <= Settings.F("SonarRange") && ActiveSonar.InArc(ship, o.Entity));
                bool radar = o.IsObservedVia(DirectObservationType.Radar);
                bool hydro = o.IsObservedVia(DirectObservationType.Hydrophone);
                if (!sonar && !radar && !hydro) continue;      // seen by eye only: not a scope contact
                int kind = sonar ? 0 : radar ? 1 : 2;
                if (kind == 0) sonarN++; else if (kind == 1) radarN++; else hydroN++;
                blips.Add(new Blip { Bearing = rel, Frac = kind == 2 ? 1f : Mathf.Clamp01(d / rangeM), Kind = kind });
            }
        }

        public static void Draw()
        {
            if (!Settings.B("SonarScope")) return;
            var ship = Util.PlayerShip;
            if (ship == null) return;
            float size = Mathf.Clamp(Settings.F("SonarScopeSize"), 120f, 600f);
            float w = size + 16f, h = size + 44f;
            var r = new Rect(Mathf.Clamp(Screen.width * Settings.F("SonarScopeX") / 100f, 0f, Screen.width - w),
                             Mathf.Clamp(Screen.height * Settings.F("SonarScopeY") / 100f, 0f, Screen.height - h), w, h);

            // drag with the left mouse button
            var mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            if (Input.GetMouseButtonDown(0) && r.Contains(mouse)) { dragging = true; grab = mouse - new Vector2(r.x, r.y); }
            if (dragging)
            {
                if (Input.GetMouseButton(0))
                {
                    r.x = Mathf.Clamp(mouse.x - grab.x, 0f, Screen.width - w); r.y = Mathf.Clamp(mouse.y - grab.y, 0f, Screen.height - h);
                    Settings.Set("SonarScopeX", r.x * 100f / Screen.width); Settings.Set("SonarScopeY", r.y * 100f / Screen.height);
                }
                else { dragging = false; Settings.Save(); Util.Log("Sonar-Anzeige verschoben nach " + Settings.F("SonarScopeX").ToString("0") + " % / " + Settings.F("SonarScopeY").ToString("0") + " %"); }
            }
            if (Imgui.EventKind() != 7) return;                 // draw only in the repaint pass

            float rangeM = Mathf.Max(500f, Settings.F("SonarScopeKm") * 1000f);
            if (Time.unscaledTime >= nextScan) { nextScan = Time.unscaledTime + 0.5f; Util.Safe("SonarScope.Scan", () => Scan(ship, rangeM)); }
            if (Time.unscaledTime >= nextLog)
            {
                nextLog = Time.unscaledTime + 60f;
                Util.Log("Sonar-Anzeige (nur eigene Kontakte): Sonar/Ping " + sonarN + ", Radar " + radarN + ", nur Horcher " + hydroN + ", Bereich " + (rangeM / 1000f).ToString("0.#") + " km");
            }

            Imgui.Backdrop(r, 0.8f);
            float rad = size / 2f;
            var c = new Vector2(r.x + w / 2f, r.y + 24f + rad);
            var green = new Color(0.35f, 1f, 0.45f, 1f);
            Imgui.TextAt(new Rect(r.x + 6, r.y + 3, w - 12, 20), "Sonar/Radar  " + (rangeM / 1000f).ToString("0.#") + " km  Bug oben");

            // range rings (dots) and cross
            for (int ring = 1; ring <= 3; ring++)
            {
                float rr = rad * ring / 3f; int n = 24 * ring;
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2f / n;
                    Imgui.Fill(new Rect(c.x + Mathf.Sin(a) * rr - 1f, c.y - Mathf.Cos(a) * rr - 1f, 2f, 2f), new Color(0.2f, 0.6f, 0.3f, 0.55f));
                }
            }
            Imgui.Fill(new Rect(c.x - rad, c.y, rad * 2f, 1f), new Color(0.2f, 0.6f, 0.3f, 0.3f));
            Imgui.Fill(new Rect(c.x, c.y - rad, 1f, rad * 2f), new Color(0.2f, 0.6f, 0.3f, 0.3f));
            Imgui.Fill(new Rect(c.x - 2f, c.y - 2f, 4f, 4f), Color.white);                       // own boat

            // sweep line with a short fading tail
            float period = Mathf.Clamp(Settings.F("SonarScopeSweep"), 1f, 30f);
            float sweep = Time.unscaledTime * 360f / period % 360f;
            for (int tail = 0; tail < 6; tail++)
            {
                float a = (sweep - tail * 2.5f) * Mathf.Deg2Rad, alpha = 0.9f - tail * 0.15f;
                int steps = Mathf.RoundToInt(rad / 4f);
                for (int s = 1; s <= steps; s++)
                {
                    float rr = rad * s / steps;
                    Imgui.Fill(new Rect(c.x + Mathf.Sin(a) * rr - 1f, c.y - Mathf.Cos(a) * rr - 1f, 2f, 2f), new Color(green.r, green.g, green.b, alpha));
                }
            }

            // contacts: brightest right after the line has passed, then fading until it comes round again
            for (int i = 0; i < blips.Count; i++)
            {
                var b = blips[i];
                float since = Mathf.Repeat(sweep - b.Bearing, 360f) / 360f;      // 0 = just swept, 1 = a full turn ago
                float alpha = Mathf.Lerp(1f, 0.15f, since);
                float a = b.Bearing * Mathf.Deg2Rad, rr = rad * b.Frac;
                var col = b.Kind == 1 ? new Color(1f, 0.75f, 0.2f, alpha) : new Color(green.r, green.g, green.b, alpha);
                float sz = b.Kind == 2 ? 4f : 6f;
                Imgui.Fill(new Rect(c.x + Mathf.Sin(a) * rr - sz / 2f, c.y - Mathf.Cos(a) * rr - sz / 2f, sz, sz), col);
            }
            Imgui.TextAt(new Rect(r.x + 6, r.yMax - 20, w - 12, 20), "grün Sonar " + sonarN + "   gelb Radar " + radarN + "   Rand Horcher " + hydroN);
        }
    }
}

