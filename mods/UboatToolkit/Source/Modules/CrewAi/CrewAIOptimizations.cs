// Crew-KI (Teil des UBOAT Toolkit): CrewAIOptimizations. Schalter: F10 > QoL > "Crew-KI".
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using HarmonyLib;
using UBOAT.Game;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Interactions;
using UBOAT.Game.Scene.Characters.Navigation;

namespace UboatToolkit.Modules.CrewAi
{
    internal static class OptToggles
    {
        public static readonly bool ThrottleGoToRetries = true;

        public const int MinRetryFrames = 15;

        public const float SameTargetEpsilon = 0.05f;

        public static readonly bool EarlyOutStopAction = false;

        public static readonly bool BlacklistUnreachableSlots = false;

        public const float BlacklistSeconds = 30f;

        public const int MinDistinctFailures = 2;

        public static readonly bool InstrumentCancel = true;

        public static readonly bool LogCounters = true;

        public static readonly bool DiscoverCharacterState = true;
        public const float CharacterStateInterval = 120f;   // unscaled seconds
        public static readonly bool DiscoverBehaviourProfit = true;
        public static readonly bool DiscoverActionTypes = true;
        public static readonly bool DiscoverBehaviourTypes = true;

        public static readonly bool MeasureBehaviourChurn = true;
    }

    internal static class OptCounters
    {
        public static long GoToTotal, GoToThrottled;
        public static long StopActionTotal, StopActionSkipped;
        public static long SlotsBlacklisted, SlotChecksBlocked, BlacklistExpired;
        public static long SlotPerCharacterBlocks;
        public static long CancelsSeen, Fix3Errors;
        public static long BehaviourStarts, BehaviourSameFrameStops;
        public static long CooldownFailuresSeen, CooldownsStarted, CooldownBehavioursCancelled;
        public static long AttackPeriscopeBlocked, AttackPeriscopeRemoved;
        public static long LoiterDestinationsRejected;
        public static long HatchOrdersPreserved;
        public static long ProximityScaled, ProximityPlayerOrderExempt;
        public static long ProximitySleepExempt, ProximityDeckAsTower;   // v0.31
        public static long PlayerOrdersSeen, PlayerOrdersDropped, PlayerOrdersStalled;
        public static long AiActionsDropped;
        public static long OrdersRefusedQueueFull, PlayerOrdersRefusedQueueFull, OrdersMadeRoom;
        public static long CompartmentTransits, ControlRoomPassThroughs, ControlRoomInAndBack;
        public static long HatchOrdersApplied, EatSuppressedOnAlarm;
        public static long BedChoiceImproved, BedChoiceSticky, BedChoiceHerdAvoided;
        public static long BedPathFailures, BedChoiceBadAvoided, BedChoiceRefused;
        public static long WatchClaimsTaken, WatchRefusedClaimed;
        public static long RestSent, RestEndedForWork, RestEndedOther, RestNeverStarted;
        public static long RestNoBunk, RestNotAllowed, RestSkippedReserve, RestSkippedAlarm;
        public static long RestResumed, WatchFlips;
        public static long BedSharedAssigned, BedSlotsAdded, WaterCheckCorrected;
        public static long RoleDropsRunning, RoleKeeps;
        public static long YieldAsked, YieldVacated, YieldStuck, YieldTakenByTired, RestHeldForTired;
        public static long YieldReservedRefused, YieldReservedSteered, YieldReserveLapsed;   // v1.0.0
        public static long BowRunDives, BowRunSent, BowRunTrimmed, BowRunReleased;   // v0.32
        public static long CloneSlotsTurnedOff, CloneSleepersStoodUp;                // v0.33

        private static float _lastDump;
        private const float DumpInterval = 60f; // unscaled seconds

        public static void MaybeDump()
        {
            if (!OptToggles.LogCounters) return;
            try
            {
                float now = Time.unscaledTime;
                if (now - _lastDump < DumpInterval) return;
                _lastDump = now;
                DiagLog.Write(
                    "[Counters] GoTo " + GoToThrottled + "/" + GoToTotal + " throttled (" + Pct(GoToThrottled, GoToTotal) + ") | " +
                    "StopCurrentAction " + StopActionSkipped + "/" + StopActionTotal + " skipped (" + Pct(StopActionSkipped, StopActionTotal) + ") | " +
                    "cancels=" + CancelsSeen + " | " +
                    "slots blacklisted=" + SlotsBlacklisted + " globalBlocks=" + SlotChecksBlocked +
                    " perCharBlocks=" + SlotPerCharacterBlocks + " expired=" + BlacklistExpired +
                    " fix3errors=" + Fix3Errors + " | " +
                    "behaviourStarts=" + BehaviourStarts + " sameFrameStops=" + BehaviourSameFrameStops +
                    " (" + Pct(BehaviourSameFrameStops, BehaviourStarts) + ") | " +
                    "cooldowns started=" + CooldownsStarted + " cancelled=" + CooldownBehavioursCancelled +
                    " fromFailures=" + CooldownFailuresSeen +
                    " | loiterRejected=" + LoiterDestinationsRejected +
                    " hatchOrdersKept=" + HatchOrdersPreserved +
                    " hatchOrdersApplied=" + HatchOrdersApplied +
                    " eatSuppressed=" + EatSuppressedOnAlarm +
                    " bedChoice improved=" + BedChoiceImproved +
                    " sticky=" + BedChoiceSticky +
                    " herdAvoided=" + BedChoiceHerdAvoided +
                    " pathFailed=" + BedPathFailures +
                    " badAvoided=" + BedChoiceBadAvoided +
                    " refused=" + BedChoiceRefused +
                    " proximityScaled=" + ProximityScaled +
                    " playerOrderExempt=" + ProximityPlayerOrderExempt +
                    " sleepExempt=" + ProximitySleepExempt +
                    " deckAsTower=" + ProximityDeckAsTower +
                    " | playerOrders seen=" + PlayerOrdersSeen +
                    " droppedBeforeStart=" + PlayerOrdersDropped +
                    " stalled=" + PlayerOrdersStalled +
                    " aiDropped=" + AiActionsDropped +
                    " | queueFull refused=" + OrdersRefusedQueueFull +
                    " (player " + PlayerOrdersRefusedQueueFull + ")" +
                    " roomMade=" + OrdersMadeRoom +
                    " | transits=" + CompartmentTransits +
                    " ctrlPassThru=" + ControlRoomPassThroughs +
                    " ctrlInAndBack=" + ControlRoomInAndBack +
                    " periscopeBlocked=" + AttackPeriscopeBlocked +
                    " periscopeRemoved=" + AttackPeriscopeRemoved +
                    " | watch claims=" + WatchClaimsTaken +
                    " refusedHeld=" + WatchRefusedClaimed +
                    " watchFlips=" + WatchFlips +
                    " roleDrops=" + RoleDropsRunning +
                    " roleKeeps=" + RoleKeeps +
                    " | rest sent=" + RestSent +
                    " leftForWork=" + RestEndedForWork +
                    " backInBunk=" + RestResumed +
                    " | sharing assigned=" + BedSharedAssigned +
                    " slotsAdded=" + BedSlotsAdded +
                    " waterFixes=" + WaterCheckCorrected +
                    " shareSlotsOff=" + CloneSlotsTurnedOff +
                    " shareSleepersUp=" + CloneSleepersStoodUp +
                    " leftOther=" + RestEndedOther +
                    " neverStarted=" + RestNeverStarted +
                    " noBunk=" + RestNoBunk +
                    " notAllowed=" + RestNotAllowed +
                    " reserveHeld=" + RestSkippedReserve +
                    " alarmTicks=" + RestSkippedAlarm +
                    " | bedYield asked=" + YieldAsked +
                    " vacated=" + YieldVacated +
                    " stuck=" + YieldStuck +
                    " takenByTired=" + YieldTakenByTired +
                    " restHeldForTired=" + RestHeldForTired +
                    " reservedRefused=" + YieldReservedRefused +
                    " reservedSteered=" + YieldReservedSteered +
                    " reserveLapsed=" + YieldReserveLapsed +
                    " | bowRun dives=" + BowRunDives +
                    " sent=" + BowRunSent +
                    " trimmed=" + BowRunTrimmed +
                    " released=" + BowRunReleased +
                    "");

                Perf.MaybeReport();
                OrderTracker.Sweep();
                CrowdCensus.MaybeReport();
            }
            catch { }
        }

        private static string Pct(long a, long b)
        {
            return b > 0 ? ((a * 100.0 / b)).ToString("F0") + "%" : "n/a";
        }
    }

    internal static class GoToThrottleState
    {
        private class Entry
        {
            public Vector3 Target;
            public float StopDistance;
            public bool ForceRun;
            public int LastCallFrame;
        }

        private static readonly Dictionary<int, Entry> _last = new Dictionary<int, Entry>();
        private static readonly object _lock = new object();

        public static bool ShouldThrottle(NavMeshNavigator nav, Vector3 target, float stopDistance, bool forceRun)
        {
            if (nav == null) return false;
            int id = nav.GetInstanceID();
            int frame = Time.frameCount;
            lock (_lock)
            {
                Entry e;
                if (!_last.TryGetValue(id, out e))
                {
                    _last[id] = new Entry { Target = target, StopDistance = stopDistance, ForceRun = forceRun, LastCallFrame = frame };
                    return false;
                }

                bool sameTarget = (e.Target - target).sqrMagnitude <=
                                  OptToggles.SameTargetEpsilon * OptToggles.SameTargetEpsilon;
                bool sameParams = Mathf.Approximately(e.StopDistance, stopDistance) && e.ForceRun == forceRun;
                bool tooSoon = (frame - e.LastCallFrame) < OptToggles.MinRetryFrames;

                if (sameTarget && sameParams && tooSoon)
                {
                    return true;
                }

                e.Target = target;
                e.StopDistance = stopDistance;
                e.ForceRun = forceRun;
                e.LastCallFrame = frame;
                return false;
            }
        }

        public static void Forget(NavMeshNavigator nav)
        {
            if (nav == null) return;
            try { lock (_lock) { _last.Remove(nav.GetInstanceID()); } }
            catch { }
        }
    }

    [HarmonyPatch(typeof(NavMeshNavigator), "GoTo", new Type[] { typeof(Vector3), typeof(float), typeof(bool) })]
    internal static class Patch_Opt_GoToThrottle
    {
        static bool Prefix(NavMeshNavigator __instance, Vector3 navSpacePosition, float stopDistance, bool forceRun)
        {
            GoToLogState.LastCallThrottled = false;

            if (!OptToggles.ThrottleGoToRetries) return true;
            try
            {
                OptCounters.GoToTotal++;
                OptCounters.MaybeDump();

                if (GoToThrottleState.ShouldThrottle(__instance, navSpacePosition, stopDistance, forceRun))
                {
                    OptCounters.GoToThrottled++;
                    GoToLogState.LastCallThrottled = true;
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                DiagLog.WriteError("Opt.GoToThrottle", e);
                return true; // fail open
            }
        }
    }

    internal static class GoToLogState
    {
        public static bool LastCallThrottled;
    }

    [HarmonyPatch(typeof(NavMeshNavigator), "Stop")]
    internal static class Patch_Opt_GoToThrottleReset
    {
        static void Postfix(NavMeshNavigator __instance)
        {
            if (!OptToggles.ThrottleGoToRetries) return;
            try { GoToThrottleState.Forget(__instance); }
            catch (Exception e) { DiagLog.WriteError("Opt.GoToThrottleReset", e); }
        }
    }

    [HarmonyPatch(typeof(PlayableCharacter), "StopCurrentAction", new Type[] { typeof(bool) })]
    internal static class Patch_Opt_StopCurrentActionEarlyOut
    {
        static bool Prepare() { return OptToggles.EarlyOutStopAction; }   // v1.0.0: not installed while off
        static bool Prefix(PlayableCharacter __instance, bool ignoreCanBeStoppedFlag)
        {
            if (!OptToggles.EarlyOutStopAction) return true;
            try
            {
                OptCounters.StopActionTotal++;
                if (__instance == null) return true;
                if (__instance.Action != null) return true;
                OptCounters.StopActionSkipped++;
                return false;
            }
            catch (Exception e)
            {
                DiagLog.WriteError("Opt.StopActionEarlyOut", e);
                return true; // fail open
            }
        }
    }

    internal static class SlotBlacklist
    {
        private class Entry
        {
            public float FirstSeen;                 // unscaled
            public float LastSeen;                  // unscaled
            public readonly HashSet<string> Failers = new HashSet<string>();
            public bool Global;
        }

        private static readonly Dictionary<int, Entry> _blocked = new Dictionary<int, Entry>();
        private static readonly object _lock = new object();

        public static void NoteFailure(InteractableSlot slot, string who, string reason)
        {
            if (slot == null) return;
            try
            {
                int id = slot.GetInstanceID();
                float now = Time.unscaledTime;
                bool becameGlobal = false;
                int distinct;

                lock (_lock)
                {
                    Entry e;
                    if (!_blocked.TryGetValue(id, out e))
                    {
                        e = new Entry { FirstSeen = now };
                        _blocked[id] = e;
                    }
                    e.LastSeen = now;
                    e.Failers.Add(who ?? "unknown");
                    distinct = e.Failers.Count;
                    if (!e.Global && distinct >= OptToggles.MinDistinctFailures)
                    {
                        e.Global = true;
                        becameGlobal = true;
                    }
                }

                OptCounters.SlotsBlacklisted++;
                if (becameGlobal)
                {
                    DiagLog.Write("[Fix3] GLOBAL block on slot '" + SafeName(slot) + "' for " +
                                  OptToggles.BlacklistSeconds + "s - " + distinct +
                                  " distinct characters failed to reach it (latest=" + who + ", " + reason + ")");
                }
                else
                {
                    DiagLog.Write("[Fix3] per-character block on slot '" + SafeName(slot) + "' for " +
                                  who + " (" + distinct + "/" + OptToggles.MinDistinctFailures +
                                  " distinct failures so far, " + reason + ")");
                }
            }
            catch (Exception e) { OptCounters.Fix3Errors++; DiagLog.WriteError("Opt.Blacklist.Note", e); }
        }

        public static bool IsBlocked(InteractableSlot slot, string who)
        {
            if (slot == null) return false;
            try
            {
                int id = slot.GetInstanceID();
                float now = Time.unscaledTime;
                lock (_lock)
                {
                    Entry e;
                    if (!_blocked.TryGetValue(id, out e)) return false;
                    if (now - e.LastSeen >= OptToggles.BlacklistSeconds)
                    {
                        _blocked.Remove(id);
                        OptCounters.BlacklistExpired++;
                        DiagLog.Write("[Fix3] expired slot '" + SafeName(slot) + "' - available again");
                        return false;
                    }
                    if (e.Global)
                    {
                        OptCounters.SlotChecksBlocked++;
                        return true;
                    }
                    if (who != null && e.Failers.Contains(who))
                    {
                        OptCounters.SlotPerCharacterBlocks++;
                        return true;
                    }
                    return false;
                }
            }
            catch (Exception e)
            {
                OptCounters.Fix3Errors++;
                DiagLog.WriteError("Opt.Blacklist.IsBlocked", e);
                return false; // fail open - never block because of our own error
            }
        }

        public static string SafeName(UnityEngine.Object o)
        {
            try { return o != null ? o.name : "null"; } catch { return "?"; }
        }
    }

    [HarmonyPatch(typeof(UBOAT.Game.Core.AI.Behaviours.GoToBehaviour), "Cancel")]
    internal static class Patch_Opt_GoToBehaviourCancel
    {
        static readonly System.Reflection.FieldInfo AttemptsField =
            AccessTools.Field(typeof(UBOAT.Game.Core.AI.Behaviours.GoToBehaviour), "setPathAttempts");
        static readonly System.Reflection.FieldInfo PathValidField =
            AccessTools.Field(typeof(UBOAT.Game.Core.AI.Behaviours.GoToBehaviour), "wasPathValid");
        static readonly System.Reflection.FieldInfo PathSetField =
            AccessTools.Field(typeof(UBOAT.Game.Core.AI.Behaviours.GoToBehaviour), "pathSet");
        static readonly System.Reflection.FieldInfo LoggingTargetField =
            AccessTools.Field(typeof(UBOAT.Game.Core.AI.Behaviours.GoToBehaviour), "loggingTarget");
        static readonly System.Reflection.FieldInfo UnreachableActionField =
            AccessTools.Field(typeof(UBOAT.Game.Core.AI.Behaviours.GoToBehaviour), "targetUnreachableAction");

        static float _lastProbeLog;

        static void Prefix(UBOAT.Game.Core.AI.Behaviours.GoToBehaviour __instance)
        {
            if (!OptToggles.BlacklistUnreachableSlots && !OptToggles.InstrumentCancel) return;
            try
            {
                if (__instance == null) return;
                OptCounters.CancelsSeen++;

                int attempts = AttemptsField != null ? (int)AttemptsField.GetValue(__instance) : -1;
                bool wasValid = PathValidField != null && (bool)PathValidField.GetValue(__instance);
                bool pathSet = PathSetField != null && (bool)PathSetField.GetValue(__instance);
                object unreachable = UnreachableActionField != null ? UnreachableActionField.GetValue(__instance) : null;
                object rawTarget = LoggingTargetField != null ? LoggingTargetField.GetValue(__instance) : null;
                string unreachableStr = unreachable != null ? unreachable.ToString() : "null";

                string who = "unknown";
                try { who = __instance.AI != null ? __instance.AI.name : "unknown"; } catch { }

                var tr = rawTarget as Transform;
                InteractableSlot slot = null;
                if (tr != null)
                {
                    try
                    {
                        slot = tr.GetComponent<InteractableSlot>();
                        if (slot == null) slot = tr.GetComponentInParent<InteractableSlot>();
                    }
                    catch { }
                }

                bool confirmedShape = pathSet && !wasValid && unreachableStr == "CancelBehaviour";

                if (confirmedShape)
                {
                    string failedTarget = "?";
                    if (tr != null)
                        failedTarget = (tr.parent != null ? tr.parent.name : "?") + "/" + tr.name;
                    BehaviourCooldown.NoteUnreachable(who, failedTarget);
                }

                if (OptToggles.BlacklistUnreachableSlots && confirmedShape && slot != null)
                {
                    SlotBlacklist.NoteFailure(slot, who, "attempts=" + attempts + ", action=" + unreachableStr);
                    return;
                }

                if (!OptToggles.InstrumentCancel || !DiagToggles.Verbose) return;   // v1.0.0: the line is verbose-only; NoteUnreachable above always runs
                float now = Time.unscaledTime;
                if (now - _lastProbeLog < 1.0f) return;
                _lastProbeLog = now;

                string targetDesc = "null";
                if (tr != null)
                {
                    string parentName = tr.parent != null ? tr.parent.name : "(no parent)";
                    targetDesc = "Transform '" + tr.name + "' parent '" + parentName + "'";
                }
                else if (rawTarget != null) targetDesc = rawTarget.GetType().Name;

                string slotName = slot != null ? slot.name : "none";
                string why = confirmedShape ? "matched shape but no slot found" : "shape did not match";
                DiagLog.Write("[CancelProbe] " + who + " | attempts=" + attempts + " wasPathValid=" + wasValid +
                              " pathSet=" + pathSet + " unreachableAction=" + unreachableStr +
                              " | target=" + targetDesc + " | slot=" + slotName + " | " + why);
            }
            catch (Exception e)
            {
                OptCounters.Fix3Errors++;
                DiagLog.WriteError("Opt.CancelProbe", e);
            }
        }
    }

    [HarmonyPatch(typeof(InteractableSlot), "get_IsBlockedForAI")]
    internal static class Patch_Opt_IsBlockedForAI
    {
        static bool Prepare() { return OptToggles.BlacklistUnreachableSlots; }   // v1.0.0: hot getter; not installed while off
        static void Postfix(InteractableSlot __instance, ref bool __result)
        {
            if (!OptToggles.BlacklistUnreachableSlots) return;
            if (__result) return; // already blocked by the game - leave it alone
            try
            {
                if (SlotBlacklist.IsBlocked(__instance, null)) __result = true;
            }
            catch (Exception e)
            {
                OptCounters.Fix3Errors++;
                DiagLog.WriteError("Opt.IsBlockedForAI", e);
            }
        }
    }
}
