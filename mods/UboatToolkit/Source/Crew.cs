// UBOAT Toolkit - Crew: Mannschaft, Wachen, Stationen, Crew-KI
// Diese Datei fasst 45 frühere Einzeldateien zusammen (0.4.30). Jeder Abschnitt nennt seine alte Datei.

// ====================================================================================================
// Abschnitt: Modules/Crew.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::UnityEngine;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Navigation;
    using global::UBOAT.Game.Scene.Interactions;
    using global::UBOAT.Game.Scene.Items;

    // QoL #21: crew walks through each other; doors are not touched.
    // QoL #14: sailors may man the navigation table.
    public class Crew : ModuleBase
    {
        private PlayerCrew crew;
        private readonly System.Collections.Generic.Dictionary<int, AgentMode> changed = new System.Collections.Generic.Dictionary<int, AgentMode>();

        public override void Tick()
        {
            if (crew == null) crew = Object.FindObjectOfType<PlayerCrew>();
            if (crew != null && crew.Characters != null)
            {
                bool through = Settings.B("NoCrewCollisions");
                foreach (var c in crew.Characters)
                {
                    if (c == null || c.Navigator == null) continue;
                    var nav = c.Navigator;
                    int id = c.GetInstanceID();
                    if (through && (nav.AgentMode == AgentMode.Obstacle || nav.AgentMode == AgentMode.CarvedObstacle))
                    {
                        changed[id] = nav.AgentMode;
                        nav.AgentMode = AgentMode.Disabled;
                    }
                    else if (!through && changed.TryGetValue(id, out var before))
                    {
                        if (nav.AgentMode == AgentMode.Disabled) nav.AgentMode = before;
                        changed.Remove(id);
                    }
                }
            }

            foreach (var table in Util.Find<NavigationTable>())
            {
                var i = table.GetComponent<Interactable>();
                if (i == null) continue;
                bool orig = Util.Original<bool>(i, "disallowSailors");
                i.DisallowSailors = Settings.B("SailorsNavTable") ? false : orig;
            }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/CrewAi/CrewAIAlarmBehaviour.cs
// ====================================================================================================
// Crew-KI (Teil des UBOAT Toolkit): CrewAIAlarmBehaviour. Schalter: F10 > QoL > "Crew-KI".

namespace UboatToolkit.Modules.CrewAi
{
    using global::System;
    using global::System.Reflection;
    using global::HarmonyLib;
    using global::UBOAT.Game.Core.AI.Behaviours;
    using global::UBOAT.Game.Scene.Entities;

    internal static class AlarmToggles
    {
        public static bool NoEatingDuringAlarm => UboatToolkit.Settings.B("CrewNoEatAlarm");

        public const float CriticalHunger = 0.85f;

        public static readonly bool NoSleepingDuringAlarm = false;

        public static readonly bool LogSuppression = true;
    }

    [HarmonyPatch(typeof(EatBehaviour), "OnBehaviourBackgroundUpdate")]
    internal static class Patch_Alarm_NoEating
    {
        static readonly FieldInfo ShipField = AccessTools.Field(typeof(EatBehaviour), "playerShip");
        static readonly FieldInfo HungerField = AccessTools.Field(typeof(EatBehaviour), "hunger");
        static readonly FieldInfo CharField = AccessTools.Field(typeof(EatBehaviour), "character");

        static bool _loggedOnce;

        static void Postfix(EatBehaviour __instance)
        {
            if (!AlarmToggles.NoEatingDuringAlarm) return;
            try
            {
                if (__instance == null || __instance.Profit <= 0f) return;

                bool alarmed = false;
                if (ShipField != null)
                {
                    var ship = ShipField.GetValue(__instance) as PlayerShip;
                    if (ship == null) return;
                    try { alarmed = ship.Alarmed; } catch { return; }
                }
                if (!alarmed) return;

                float hunger = 0f;
                if (HungerField != null)
                {
                    var need = HungerField.GetValue(__instance);
                    if (need != null)
                    {
                        try
                        {
                            var p = AccessTools.Property(need.GetType(), "BiasedStress");
                            if (p != null) hunger = (float)p.GetValue(need, null);
                        }
                        catch { }
                    }
                }
                if (hunger >= AlarmToggles.CriticalHunger) return;

                __instance.Profit = 0f;
                OptCounters.EatSuppressedOnAlarm++;

                if (AlarmToggles.LogSuppression && !_loggedOnce)
                {
                    _loggedOnce = true;
                    string who = "?";
                    try
                    {
                        var c = CharField != null
                            ? CharField.GetValue(__instance) as UBOAT.Game.Scene.Characters.PlayableCharacter
                            : null;
                        if (c != null) who = c.Name ?? "?";
                    }
                    catch { }
                    DiagLog.Write("[Alarm] suppressing EatBehaviour while alarmed (first: " + who +
                                  ", hunger=" + hunger.ToString("F2") +
                                  ", critical=" + AlarmToggles.CriticalHunger +
                                  ") - vanilla EatBehaviour has no alarm gate at all");
                }
            }
            catch (Exception e)
            {
                DiagLog.WriteError("Alarm.NoEating", e);
            }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/CrewAi/CrewAIAttackPeriscope.cs
// ====================================================================================================
// Crew-KI (Teil des UBOAT Toolkit): CrewAIAttackPeriscope. Schalter: F10 > QoL > "Crew-KI".

namespace UboatToolkit.Modules.CrewAi
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UBOAT.Game.Scene.Items;
    using Activator = global::UBOAT.Game.Scene.Characters.Activators.Activator;

    internal static class AttackPeriscopeGate
    {
        private static readonly HashSet<string> _seen = new HashSet<string>();
        private static readonly object _lock = new object();

        public static bool IsPeriscope(Activator activator)
        {
            if (activator == null) return false;
            try { return activator.HasTag(PeriscopeToggles.PeriscopeActivatorTag); }
            catch { return false; }
        }

        public static bool IsAttackPeriscope(Activator activator)
        {
            if (activator == null) return false;
            try
            {
                var p = activator.GetComponent<Periscope>();
                return p != null && p.name == PeriscopeToggles.AttackPeriscopeObjectName;
            }
            catch { return false; }
        }

        private static readonly HashSet<string> _spared = new HashSet<string>();
        public static void NoteSpared(Activator activator)
        {
            try
            {
                if (activator == null || !IsPeriscope(activator) || IsAttackPeriscope(activator)) return;
                string n = activator.name ?? "?";
                bool first;
                lock (_lock) { first = _spared.Add(n); }
                if (first)
                    DiagLog.Write("[AttackPeriscope] '" + n + "' carries the Periscope tag but is not the " +
                                  "attack periscope - v0.19 zeroed AI profit here, v0.20 does not");
            }
            catch { }
        }

        public static void NoteOnce(CharacterAction action, Activator activator,
                                    PlayableCharacter c, string what)
        {
            if (c == null) return;
            try
            {
                string name = c.Name ?? "unknown";
                string act = activator != null ? activator.name : "null";
                lock (_lock) { if (!_seen.Add(what + "|" + name + "|" + act)) return; }

                string tags = "?";
                try
                {
                    if (activator != null && activator.Tags != null)
                        tags = string.Join(", ", new List<string>(activator.Tags).ToArray());
                }
                catch { }

                string hard = "?";
                try
                {
                    if (action != null && action.HardRequirements != null)
                        hard = string.Join(", ", action.HardRequirements);
                }
                catch { }

                DiagLog.Write("[AttackPeriscope] " + what + " | character=" + name +
                              " skipper=" + SafeSkipper(c) + " officer=" + SafeOfficer(c) +
                              " | activator=" + act + " tags=[" + tags + "]" +
                              " | actionHardReq=[" + hard + "]");
            }
            catch (Exception e) { DiagLog.WriteError("AttackPeriscope.NoteOnce", e); }
        }

        private static string SafeSkipper(PlayableCharacter c)
        { try { return c.IsSkipper.ToString(); } catch { return "?"; } }

        private static string SafeOfficer(PlayableCharacter c)
        { try { return c.IsOfficer.ToString(); } catch { return "?"; } }

        private static readonly Dictionary<string, int> _blockCounts =
            new Dictionary<string, int>();

        public static void NoteBlockedOnce(CharacterAction action, PlayableCharacter c,
                                           bool viaAttackFlag, string optic)
        {
            try
            {
                string name = "unknown";
                try { name = c != null ? (c.Name ?? "unknown") : "unknown"; } catch { }

                int n;
                bool first;
                lock (_lock)
                {
                    _blockCounts.TryGetValue(name, out n);
                    n++;
                    _blockCounts[name] = n;
                    first = (n == 1);
                }

                if (!first && (n & (n - 1)) != 0) return;

                string hard = "?", soft = "?", role = "?", job = "?";
                try
                {
                    if (action != null && action.HardRequirements != null)
                        hard = string.Join(", ", action.HardRequirements);
                }
                catch { }
                try
                {
                    if (action != null && action.SoftRequirements != null)
                        soft = string.Join(", ", action.SoftRequirements);
                }
                catch { }
                try
                {
                    if (action != null)
                        role = action.Role != null ? action.Role.ToString() : "null";
                }
                catch { }
                try
                {
                    if (action != null)
                        job = action.SourceJob != null ? action.SourceJob.ToString() : "null";
                }
                catch { }

                string order = "?";
                try { if (action != null) order = action.IsDirectPlayerOrder.ToString(); }
                catch { }

                DiagLog.Write("[AttackPeriscope] BLOCKED start #" + n + " | character=" + name +
                              " | optic='" + optic + "'" +
                              " | identifiedBy=" + (viaAttackFlag ? "isAttackPeriscope(name match)" : "unknown") +
                              " | actionHardReq=[" + hard + "] softReq=[" + soft + "]" +
                              " | role=" + role + " sourceJob=" + job +
                              " directPlayerOrder=" + order);
            }
            catch (Exception e) { DiagLog.WriteError("AttackPeriscope.NoteBlockedOnce", e); }
        }

        public static bool Denied(CharacterAction action, PlayableCharacter c)
        {
            if (!PeriscopeToggles.AttackPeriscopeSkipperOnly) return false;
            if (c == null) return false;
            try
            {
                if (c.IsSkipper) return false;
                if (PeriscopeToggles.AllowDirectPlayerOrder && action != null &&
                    action.IsDirectPlayerOrder) return false;
                return true;
            }
            catch { return false; } // fail open
        }
    }

    [HarmonyPatch(typeof(ObserveAction), "GetActionProfit",
        new Type[] { typeof(Activator), typeof(PlayableCharacter) })]
    internal static class Patch_ObserveAction_GetActionProfit
    {
        static void Postfix(ObserveAction __instance, Activator activator,
                            PlayableCharacter character, ref ActionProfit __result)
        {
            if (!PeriscopeToggles.AttackPeriscopeSkipperOnly) return;
            try
            {
                AttackPeriscopeGate.NoteSpared(activator);
                if (character == null || !AttackPeriscopeGate.IsAttackPeriscope(activator)) return;
                AttackPeriscopeGate.NoteOnce(__instance, activator, character, "scored");

                if (AttackPeriscopeGate.Denied(__instance, character))
                {
                    __result = new ActionProfit(__result.ProfitForPlayer, 0f);
                    OptCounters.AttackPeriscopeBlocked++;
                }
            }
            catch (Exception e)
            {
                DiagLog.WriteError("AttackPeriscope.GetActionProfit", e);
            }
        }
    }

    [HarmonyPatch(typeof(ObserveAction), "GetActionProfit",
        new Type[] { typeof(Activator), typeof(PlayableCharacter), typeof(float) })]
    internal static class Patch_ObserveAction_GetActionProfitFloat
    {
        static void Postfix(ObserveAction __instance, Activator activator,
                            PlayableCharacter character, ref float __result)
        {
            if (!PeriscopeToggles.AttackPeriscopeSkipperOnly) return;
            try
            {
                AttackPeriscopeGate.NoteSpared(activator);
                if (character == null || !AttackPeriscopeGate.IsAttackPeriscope(activator)) return;
                if (AttackPeriscopeGate.Denied(__instance, character) && __result > 0f)
                {
                    __result = 0f;
                    OptCounters.AttackPeriscopeBlocked++;
                }
            }
            catch (Exception e) { DiagLog.WriteError("AttackPeriscope.GetActionProfitFloat", e); }
        }
    }

    [HarmonyPatch(typeof(ObserveAction), "StartMainCharacter",
        new Type[] { typeof(PlayableCharacter) })]
    internal static class Patch_ObserveAction_StartMainCharacter
    {
        static bool Prefix(ObserveAction __instance, PlayableCharacter character)
        {
            if (!PeriscopeToggles.AttackPeriscopeSkipperOnly) return true;
            try
            {
                if (__instance == null || character == null) return true;
                if (!AttackPeriscopeGate.Denied(__instance, character)) return true;

                bool isPeriscope = false;
                bool isAttackFlag = false;
                string optic = "none";
                try
                {
                    var act = __instance.Activator;
                    var p = act != null ? act.GetComponent<Periscope>() : null;
                    if (p != null)
                    {
                        optic = p.name;
                        isAttackFlag = (p.name == PeriscopeToggles.AttackPeriscopeObjectName);
                        isPeriscope = isAttackFlag;
                    }
                }
                catch { }

                if (!isPeriscope)
                {
                    AttackPeriscopeGate.NoteOnce(__instance, null, character,
                                                 "allowed-start (optic='" + optic + "')");
                    return true;
                }

                AttackPeriscopeGate.NoteBlockedOnce(__instance, character, isAttackFlag, optic);
                OptCounters.AttackPeriscopeBlocked++;
                AttackPeriscopeStop.MarkRefused(__instance);
                return true;
            }
            catch (Exception e)
            {
                DiagLog.WriteError("AttackPeriscope.StartMainCharacter", e);
            }
            return true;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/CrewAi/CrewAIAttackPeriscopeStop.cs
// ====================================================================================================
// Crew-KI (Teil des UBOAT Toolkit): CrewAIAttackPeriscopeStop. Schalter: F10 > QoL > "Crew-KI".

namespace UboatToolkit.Modules.CrewAi
{
    using global::System;
    using global::System.Collections.Generic;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Actions;

    internal static class AttackPeriscopeStop
    {
        private static readonly HashSet<ObserveAction> _pending = new HashSet<ObserveAction>();
        private static readonly object _lock = new object();

        public static void MarkRefused(ObserveAction a)
        {
            if (a == null) return;
            lock (_lock)
            {
                if (_pending.Count > 64) _pending.Clear();   // bounded, belt and braces
                _pending.Add(a);
            }
        }

        public static bool TakeRefused(ObserveAction a)
        {
            if (a == null) return false;
            lock (_lock) { return _pending.Remove(a); }
        }

        public static void Forget(ObserveAction a)
        {
            if (a == null) return;
            lock (_lock) { _pending.Remove(a); }
        }
    }

    [HarmonyPatch(typeof(ObserveAction), "UpdateAction")]
    internal static class Patch_AttackPeriscopeStop_UpdateAction
    {
        static bool Prefix(ObserveAction __instance)
        {
            try
            {
                if (!AttackPeriscopeStop.TakeRefused(__instance)) return true;
                PlayableCharacter c = null;
                try { c = __instance.Character; } catch { }

                bool removed = false;
                try
                {
                    var q = c != null ? c.ActionQueue : null;
                    var acts = q != null ? q.Actions : null;
                    if (acts != null && acts.Contains(__instance)) { q.Remove(__instance, true); removed = true; }
                }
                catch (Exception e) { DiagLog.WriteError("AttackPeriscopeStop.Remove", e); }
                if (!removed) { try { __instance.CompleteAction(); } catch { } }
                if (c != null && c.Action == __instance) c.StopCurrentAction(true);
                OptCounters.AttackPeriscopeRemoved++;
                return false;                                  // skip this one frame's update
            }
            catch (Exception e)
            {
                DiagLog.WriteError("AttackPeriscopeStop.UpdateAction", e);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(ObserveAction), "StopMainCharacter", new Type[] { typeof(PlayableCharacter) })]
    internal static class Patch_AttackPeriscopeStop_StopMainCharacter
    {
        static void Postfix(ObserveAction __instance)
        {
            try { AttackPeriscopeStop.Forget(__instance); } catch { }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/CrewAi/CrewAIBedChoice.cs
// ====================================================================================================
// Crew-KI (Teil des UBOAT Toolkit): CrewAIBedChoice. Schalter: F10 > QoL > "Crew-KI".

namespace UboatToolkit.Modules.CrewAi
{
    using global::System;
    using global::System.Collections.Generic;
    using global::UnityEngine;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Navigation;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Items;

    internal static class BedChoiceToggles
    {
        public static bool PreferNearestBed => UboatToolkit.Settings.B("CrewNearestBed");

        public const float ClaimSeconds = 45f;

        public static readonly bool StickyChoice = true;

        public static readonly bool RandomiseWithinTier = true;

        public static readonly bool LogSamples = true;
        public const int MaxSampleLines = 30;

        public const float MaxStickySeconds = 45f;

        public const float BadBedSeconds = 30f;
        public const int MaxFailureLines = 30;
    }

    internal static class NearestBed
    {
        private sealed class Claim
        {
            public Bed Bed;
            public int CharId;
            public float Stamp;      // unscaled, refreshed while he keeps asking
            public float FirstGiven; // unscaled, NOT refreshed - caps stickiness
        }

        private static readonly Dictionary<long, float> _bad = new Dictionary<long, float>();
        private static int _failureLines;

        private static long BadKey(int charId, int bedId) { return ((long)charId << 32) ^ (uint)bedId; }

        private static bool IsBadFor(int charId, Bed bed, float now)
        {
            if (bed == null) return false;
            float until;
            return _bad.TryGetValue(BadKey(charId, bed.GetInstanceID()), out until) && now < until;
        }

        private static readonly Dictionary<int, Claim> _byBed = new Dictionary<int, Claim>();
        private static readonly Dictionary<int, Claim> _byChar = new Dictionary<int, Claim>();
        private static readonly object _lock = new object();
        private static float _lastPrune;

        private static readonly System.Random _rng = new System.Random(20260907);

        private static int _logged;

        private static bool AvailableCheap(Bed bed, bool includeUpperBeds)
        {
            try
            {
                if (bed == null) return false;
                if (!bed.isActiveAndEnabled) return false;
                if (!includeUpperBeds && bed.Upper) return false;

                var reserving = bed.ReservingCharacters;
                if (reserving != null && reserving.Count != 0) return false;

                if (!bed.HasAnyCrewSlotsAboveWater) return false;

                var slots = bed.Slots;
                if (slots == null || slots.Length == 0) return false;
                if (slots[0] == null || !slots[0].isActiveAndEnabled) return false;

                var comp = bed.Compartment;
                if (comp != null && comp.Evacuation) return false;

                return true;
            }
            catch { return false; }
        }

        private static bool Reachable(Bed bed, NavMeshNavigator nav)
        {
            try
            {
                if (nav == null) return true;          // cannot test - fail open
                var slots = bed.Slots;
                if (slots == null || slots.Length == 0 || slots[0] == null) return false;
                return nav.IsDestinationReachable(slots[0].transform.position, false);
            }
            catch { return false; }
        }

        private static void Prune(float now)
        {
            if (now - _lastPrune < 15f) return;
            _lastPrune = now;
            try
            {
                List<int> dead = null;
                foreach (var kv in _byBed)
                {
                    if (now - kv.Value.Stamp > BedChoiceToggles.ClaimSeconds)
                    {
                        if (dead == null) dead = new List<int>();
                        dead.Add(kv.Key);
                    }
                }
                if (dead != null)
                    for (int i = 0; i < dead.Count; i++) _byBed.Remove(dead[i]);

                List<long> deadBad = null;
                foreach (var kv in _bad)
                {
                    if (now >= kv.Value)
                    {
                        if (deadBad == null) deadBad = new List<long>();
                        deadBad.Add(kv.Key);
                    }
                }
                if (deadBad != null)
                    for (int i = 0; i < deadBad.Count; i++) _bad.Remove(deadBad[i]);

                dead = null;
                foreach (var kv in _byChar)
                {
                    if (now - kv.Value.Stamp > BedChoiceToggles.ClaimSeconds)
                    {
                        if (dead == null) dead = new List<int>();
                        dead.Add(kv.Key);
                    }
                }
                if (dead != null)
                    for (int i = 0; i < dead.Count; i++) _byChar.Remove(dead[i]);
            }
            catch { }
        }

        private static bool ClaimedByOther(Bed bed, int charId, float now)
        {
            Claim c;
            if (!_byBed.TryGetValue(bed.GetInstanceID(), out c)) return false;
            if (c.CharId == charId) return false;
            return now - c.Stamp <= BedChoiceToggles.ClaimSeconds;
        }

        private static void Take(Bed bed, int charId, float now)
        {
            Claim old;
            if (_byChar.TryGetValue(charId, out old) && old.Bed != null)
            {
                int oldId = old.Bed.GetInstanceID();
                Claim owner;
                if (_byBed.TryGetValue(oldId, out owner) && owner.CharId == charId)
                    _byBed.Remove(oldId);
            }
            var c = new Claim();
            c.Bed = bed; c.CharId = charId; c.Stamp = now;
            c.FirstGiven = (old != null && old.Bed == bed) ? old.FirstGiven : now;
            _byChar[charId] = c;
            _byBed[bed.GetInstanceID()] = c;
        }

        private static void Release(int charId, Bed bed)
        {
            _byChar.Remove(charId);
            if (bed == null) return;
            int id = bed.GetInstanceID();
            Claim owner;
            if (_byBed.TryGetValue(id, out owner) && owner.CharId == charId)
                _byBed.Remove(id);
        }

        public static string WhyNotFor(Bed bed, PlayableCharacter c)
        {
            if (bed == null || c == null) return null;
            try
            {
                int charId = c.GetInstanceID();
                float now = Time.unscaledTime;
                lock (_lock)
                {
                    if (IsBadFor(charId, bed, now)) return "his path to it failed recently";
                    Claim cl;
                    if (_byBed.TryGetValue(bed.GetInstanceID(), out cl) && cl.CharId != charId &&
                        now - cl.Stamp <= BedChoiceToggles.ClaimSeconds)
                        return "claimed for another man " + (now - cl.Stamp).ToString("F0") + "s ago";
                }
                return null;
            }
            catch { return null; }
        }

        public static void ReleaseFor(PlayableCharacter c)
        {
            if (c == null) return;
            try
            {
                int charId = c.GetInstanceID();
                lock (_lock)
                {
                    Claim mine;
                    if (_byChar.TryGetValue(charId, out mine)) Release(charId, mine.Bed);
                }
            }
            catch { }
        }

        public static void NoteBedPathFailed(PlayableCharacter c, Bed bed)
        {
            if (c == null || bed == null) return;
            try
            {
                int charId = c.GetInstanceID();
                float now = Time.unscaledTime;
                lock (_lock)
                {
                    _bad[BadKey(charId, bed.GetInstanceID())] = now + BedChoiceToggles.BadBedSeconds;
                    Claim mine;
                    if (_byChar.TryGetValue(charId, out mine) && mine.Bed == bed) Release(charId, bed);
                }
                OptCounters.BedPathFailures++;
                if (_failureLines < BedChoiceToggles.MaxFailureLines)
                {
                    _failureLines++;
                    DiagLog.Write("[BedChoice] path to '" + SafeName(bed) + "' failed for " + SafeName(c) +
                                  " - not offering it to him for " + BedChoiceToggles.BadBedSeconds + "s");
                }
            }
            catch (Exception e) { DiagLog.WriteError("BedChoice.NoteBedPathFailed", e); }
        }

        public static Bed Improve(Bed chosen, Bed[] candidates, NavMeshNavigator nav,
                                  bool includeUpperBeds, string label, out bool refuse)
        {
            refuse = false;
            if (!BedChoiceToggles.PreferNearestBed) return null;
            if (chosen == null || candidates == null || candidates.Length == 0) return null;

            long t0 = Perf.Begin();
            try
            {
                PlayableCharacter c = null;
                try { c = nav != null ? nav.Character as PlayableCharacter : null; } catch { }
                if (c == null) return null;

                Compartment from = null;
                try { from = c.CurrentCompartment; } catch { }
                if (from == null) return null;

                int charId = c.GetInstanceID();
                float now = Time.unscaledTime;

                if (BedChoiceToggles.StickyChoice)
                {
                    Bed held = null;
                    lock (_lock)
                    {
                        Prune(now);
                        Claim mine;
                        if (_byChar.TryGetValue(charId, out mine) &&
                            now - mine.Stamp <= BedChoiceToggles.ClaimSeconds &&
                            now - mine.FirstGiven <= BedChoiceToggles.MaxStickySeconds &&
                            mine.Bed != null &&
                            !IsBadFor(charId, mine.Bed, now))
                        {
                            held = mine.Bed;
                            mine.Stamp = now;       // refresh while he still wants it
                        }
                    }
                    if (held != null)
                    {
                        if (AvailableCheap(held, includeUpperBeds))
                        {
                            OptCounters.BedChoiceSticky++;
                            return held == chosen ? null : held;
                        }
                        lock (_lock) { Release(charId, held); }
                    }
                }
                else
                {
                    lock (_lock) { Prune(now); }
                }

                bool chosenBad;
                lock (_lock) { chosenBad = IsBadFor(charId, chosen, now); }
                if (chosenBad)
                {
                    var alt = new List<Bed>(8);
                    int altBest = int.MaxValue;
                    lock (_lock)
                    {
                        for (int i = 0; i < candidates.Length; i++)
                        {
                            var b = candidates[i];
                            if (b == null || b == chosen) continue;
                            if (IsBadFor(charId, b, now) || ClaimedByOther(b, charId, now)) continue;
                            if (!AvailableCheap(b, includeUpperBeds)) continue;
                            int h = CompartmentGraph.Hops(from, SafeComp(b));
                            if (h < 0) h = 1000;
                            if (h > altBest) continue;
                            if (h < altBest) { altBest = h; alt.Clear(); }
                            alt.Add(b);
                        }
                    }
                    int s0 = 0;
                    if (alt.Count > 1) { lock (_rng) { s0 = _rng.Next(alt.Count); } }
                    for (int k = 0; k < alt.Count; k++)
                    {
                        var b = alt[(s0 + k) % alt.Count];
                        if (!Reachable(b, nav)) continue;
                        lock (_lock) { Take(b, charId, now); }
                        OptCounters.BedChoiceBadAvoided++;
                        return b;
                    }
                    refuse = true;
                    OptCounters.BedChoiceRefused++;
                    return null;
                }

                int vanillaHops = CompartmentGraph.Hops(from, SafeComp(chosen));
                if (vanillaHops == 0)
                {
                    lock (_lock)
                    {
                        if (!ClaimedByOther(chosen, charId, now)) Take(chosen, charId, now);
                    }
                    return null;
                }
                if (vanillaHops < 0) vanillaHops = int.MaxValue;   // unknown - anything known beats it

                int bestHops = int.MaxValue;
                var tier = new List<Bed>(8);
                lock (_lock)
                {
                    for (int i = 0; i < candidates.Length; i++)
                    {
                        var b = candidates[i];
                        if (b == null || b == chosen) continue;
                        var bc = SafeComp(b);
                        if (bc == null) continue;

                        int h = CompartmentGraph.Hops(from, bc);
                        if (h < 0 || h >= vanillaHops) continue;   // not strictly nearer
                        if (h > bestHops) continue;                // already have nearer

                        if (IsBadFor(charId, b, now)) continue;
                        if (ClaimedByOther(b, charId, now))
                        {
                            OptCounters.BedChoiceHerdAvoided++;
                            continue;
                        }
                        if (!AvailableCheap(b, includeUpperBeds)) continue;

                        if (h < bestHops) { bestHops = h; tier.Clear(); }
                        tier.Add(b);
                        if (h == 0 && tier.Count >= 4) break;     // plenty next door
                    }
                }

                if (tier.Count == 0)
                {
                    lock (_lock)
                    {
                        if (!ClaimedByOther(chosen, charId, now)) Take(chosen, charId, now);
                    }
                    return null;
                }

                int start = 0;
                if (BedChoiceToggles.RandomiseWithinTier && tier.Count > 1)
                {
                    lock (_rng) { start = _rng.Next(tier.Count); }
                }

                for (int k = 0; k < tier.Count; k++)
                {
                    var b = tier[(start + k) % tier.Count];
                    if (!Reachable(b, nav)) continue;

                    lock (_lock) { Take(b, charId, now); }
                    OptCounters.BedChoiceImproved++;

                    if (BedChoiceToggles.LogSamples && _logged < BedChoiceToggles.MaxSampleLines)
                    {
                        _logged++;
                        DiagLog.Write("[BedChoice] " + label + ": " + SafeName(c) +
                                      " in " + SafeName(from) +
                                      " - vanilla picked '" + SafeName(chosen) + "' in " +
                                      SafeName(SafeComp(chosen)) +
                                      ", using '" + SafeName(b) + "' in " +
                                      SafeName(SafeComp(b)) + " (" + bestHops + " hop" +
                                      (bestHops == 1 ? "" : "s") +
                                      ", tier=" + tier.Count + ")");
                    }
                    return b;
                }

                lock (_lock)
                {
                    if (!ClaimedByOther(chosen, charId, now)) Take(chosen, charId, now);
                }
                return null;
            }
            catch (Exception e)
            {
                DiagLog.WriteError("BedChoice.Improve", e);
                return null;
            }
            finally { Perf.End("BedChoice.Improve", t0); }
        }

        private static Compartment SafeComp(Bed b)
        {
            try { return b != null ? b.Compartment : null; } catch { return null; }
        }
        private static string SafeName(UnityEngine.Object o)
        {
            try { return o != null ? o.name : "null"; } catch { return "?"; }
        }
        private static string SafeName(PlayableCharacter c)
        {
            try { return c != null ? (c.Name ?? "?") : "null"; } catch { return "?"; }
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "FindFreeBed",
        new Type[] { typeof(NavMeshNavigator), typeof(bool), typeof(bool) })]
    internal static class Patch_BedChoice_FindFreeBed
    {
        static void Postfix(PlayerShip __instance, NavMeshNavigator navigator,
                            bool includeUpperBeds, ref Bed __result)
        {
            Choose(__instance, navigator, includeUpperBeds, ref __result);
            try { BedYield.EnforceReservation(__instance, navigator, includeUpperBeds, ref __result); }
            catch (Exception e) { DiagLog.WriteError("BedYield.Enforce", e); }
        }

        private static void Choose(PlayerShip __instance, NavMeshNavigator navigator,
                                   bool includeUpperBeds, ref Bed __result)
        {
            if (__result == null)
            {
                try { __result = BedSharing.FindShared(__instance, navigator, includeUpperBeds); }
                catch (Exception e) { DiagLog.WriteError("BedChoice.ShareFallback", e); }
                return;
            }
            if (!BedChoiceToggles.PreferNearestBed) return;
            try
            {
                bool refuse;
                var better = NearestBed.Improve(__result, __instance.Beds, navigator,
                                                includeUpperBeds, "crew", out refuse);
                if (refuse) __result = null;
                else if (better != null) __result = better;
            }
            catch (Exception e) { DiagLog.WriteError("BedChoice.FindFreeBed", e); }
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "FindFreeOfficerBed",
        new Type[] { typeof(NavMeshNavigator), typeof(bool), typeof(bool) })]
    internal static class Patch_BedChoice_FindFreeOfficerBed
    {
        static void Postfix(PlayerShip __instance, NavMeshNavigator navigator,
                            bool includeUpperBeds, ref Bed __result)
        {
            if (!BedChoiceToggles.PreferNearestBed || __result == null) return;
            try
            {
                bool refuse;
                var better = NearestBed.Improve(__result, __instance.OfficerBeds, navigator,
                                                includeUpperBeds, "officer", out refuse);
                if (refuse) __result = null;
                else if (better != null) __result = better;
            }
            catch (Exception e) { DiagLog.WriteError("BedChoice.FindFreeOfficerBed", e); }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/CrewAi/CrewAIBedFeedback.cs
// ====================================================================================================
// Crew-KI (Teil des UBOAT Toolkit): CrewAIBedFeedback. Schalter: F10 > QoL > "Crew-KI".

namespace UboatToolkit.Modules.CrewAi
{
    using global::System;
    using global::System.Reflection;
    using global::UnityEngine;
    using global::HarmonyLib;
    using global::UBOAT.Game.Core.AI.Behaviours;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Navigation;
    using global::UBOAT.Game.Scene.Items;

    [HarmonyPatch(typeof(GoToBehaviour), "Cancel")]
    internal static class Patch_BedChoice_PathFailureFeedback
    {
        static readonly FieldInfo NavigatorField = AccessTools.Field(typeof(GoToBehaviour), "navigator");
        static readonly FieldInfo TargetField = AccessTools.Field(typeof(GoToBehaviour), "loggingTarget");
        static readonly FieldInfo PathValidField = AccessTools.Field(typeof(GoToBehaviour), "wasPathValid");

        static void Prefix(GoToBehaviour __instance)
        {
            if (!BedChoiceToggles.PreferNearestBed) return;
            try
            {
                if (__instance == null || NavigatorField == null || TargetField == null) return;

                if (PathValidField != null && (bool)PathValidField.GetValue(__instance)) return;

                var tr = TargetField.GetValue(__instance) as Transform;
                if (tr == null) return;
                var bed = tr.GetComponentInParent<Bed>();
                if (bed == null) return;

                var nav = NavigatorField.GetValue(__instance) as NavMeshNavigator;
                if (nav == null) return;
                var c = nav.Character as PlayableCharacter;
                if (c == null) return;

                NearestBed.NoteBedPathFailed(c, bed);
            }
            catch (Exception e)
            {
                DiagLog.WriteError("BedChoice.PathFailureFeedback", e);
            }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/CrewAi/CrewAIBedSharing.cs
// ====================================================================================================
// Crew-KI (Teil des UBOAT Toolkit): CrewAIBedSharing. Schalter: F10 > QoL > "Crew-KI".

namespace UboatToolkit.Modules.CrewAi
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::UnityEngine;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Navigation;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Interactions;
    using global::UBOAT.Game.Scene.Items;

    internal static class BedSharingToggles
    {
        public static bool Enabled => UboatToolkit.Settings.B("CrewBedSharing");

        public static int MenPerBunk => Mathf.Clamp(Mathf.RoundToInt(UboatToolkit.Settings.F("CrewBedMen")), 1, 50);

        public static readonly bool FixCrewSlotWaterCheck = true;

        public const float PendingSeconds = 30f;

        public const string CloneSuffix = " (CrewAI share ";
        public const int MaxLogLines = 40;
    }

    internal static class BedSharing
    {
        private static readonly FieldInfo SlotsField = AccessTools.Field(typeof(Interactable), "slots");
        private static readonly FieldInfo CrewWorkSlotsField = AccessTools.Field(typeof(Interactable), "crewWorkSlots");
        private static readonly FieldInfo MaxCharactersField = AccessTools.Field(typeof(Interactable), "maxCharacters");
        private static readonly MethodInfo PrepareSlotsMethod = AccessTools.Method(typeof(Interactable), "PrepareSlots");
        private static readonly MethodInfo InstantiateMethod = typeof(UnityEngine.Object).GetMethod("Instantiate",
            new[] { typeof(UnityEngine.Object), typeof(Vector3), typeof(Quaternion), typeof(Transform) });

        private static readonly string[] ResetFields =
        {
            "character", "reserved", "lastCharacter", "isCharacterInteracting",
            "isSecondaryReservation", "occupiedByNPC", "positionTween",
            "rotationTween", "synchronizeAnimators", "initialized"
        };
        private static readonly string[] CopyFields =
        {
            "executionQueue", "gameState", "mainCamera", "compartment",
            "interactable", "xRay"
        };

        private static readonly HashSet<int> _expanded = new HashSet<int>();

        private static readonly Dictionary<int, InteractableSlot> _templateOf = new Dictionary<int, InteractableSlot>();
        private static readonly Dictionary<int, List<InteractableSlot>> _clonesOf = new Dictionary<int, List<InteractableSlot>>();
        private static readonly List<InteractableSlot> _clones = new List<InteractableSlot>();
        private static readonly HashSet<int> _mirroredOff = new HashSet<int>();
        private static int _lastShipId;
        private static int _logLines;

        private static readonly Dictionary<int, List<KeyValuePair<int, float>>> _pending =
            new Dictionary<int, List<KeyValuePair<int, float>>>();
        private static readonly object _lock = new object();

        private static void Log(string s)
        {
            if (_logLines >= BedSharingToggles.MaxLogLines) return;
            _logLines++;
            DiagLog.Write(s);
        }

        public static void EnsureExpanded(PlayerShip ship)
        {
            if (!BedSharingToggles.Enabled || ship == null) return;
            try
            {
                int sid = ship.GetInstanceID();
                if (sid == _lastShipId) return;
                var beds = ship.Beds;
                if (beds == null || beds.Length == 0) return;
                _templateOf.Clear(); _clonesOf.Clear(); _clones.Clear(); _mirroredOff.Clear();

                int done = 0, skipped = 0;
                for (int i = 0; i < beds.Length; i++)
                {
                    if (Expand(beds[i])) done++; else skipped++;
                }
                _lastShipId = sid;
                Log("[BedSharing] " + done + " sailor bunk(s) now take " + BedSharingToggles.MenPerBunk +
                    " men each (" + skipped + " skipped); officer and skipper bunks untouched");
            }
            catch (Exception e) { DiagLog.WriteError("BedSharing.EnsureExpanded", e); }
        }

        private static bool Expand(Bed bed)
        {
            if (bed == null || SlotsField == null || PrepareSlotsMethod == null || MaxCharactersField == null) return false;
            int id = bed.GetInstanceID();
            try
            {
                var slots = SlotsField.GetValue(bed) as InteractableSlot[];
                if (slots == null || slots.Length == 0) return false;

                InteractableSlot template = null;
                List<InteractableSlot> existing = null;
                for (int i = 0; i < slots.Length; i++)
                {
                    var s = slots[i];
                    if (s == null) continue;
                    if (s.name.IndexOf(BedSharingToggles.CloneSuffix, StringComparison.Ordinal) >= 0)
                    { if (existing == null) existing = new List<InteractableSlot>(); existing.Add(s); continue; }
                    if (template == null && s.name.Contains("Work")) template = s;
                }
                if (existing != null)                                     // already done
                {
                    if (template != null) for (int i = 0; i < existing.Count; i++) RegisterClone(existing[i], template);
                    _expanded.Add(id);
                    return true;
                }
                if (_expanded.Contains(id)) return true;
                if (template == null) { Log("[BedSharing] '" + bed.name + "' has no Work slot - skipped"); return false; }

                var grown = new List<InteractableSlot>(slots);
                for (int n = 1; n < BedSharingToggles.MenPerBunk; n++)
                {
                    var clone = CloneSlot(template, n);
                    if (clone != null) { grown.Add(clone); RegisterClone(clone, template); }
                }
                if (grown.Count == slots.Length) return false;

                int maxBefore = bed.MaxCharacters;
                SlotsField.SetValue(bed, grown.ToArray());
                PrepareSlotsMethod.Invoke(bed, null);        // the game rebuilds crewWorkSlots

                int cap = Math.Max((int)MaxCharactersField.GetValue(bed), BedSharingToggles.MenPerBunk);
                MaxCharactersField.SetValue(bed, cap);       // setter clamps to this, so first
                bed.MaxCharacters = cap;

                _expanded.Add(id);
                OptCounters.BedSlotsAdded += grown.Count - slots.Length;

                if (_logLines < 8)
                {
                    var names = new System.Text.StringBuilder();
                    for (int i = 0; i < grown.Count; i++)
                    {
                        if (i > 0) names.Append(", ");
                        names.Append(grown[i] != null ? grown[i].name : "null");
                        try { if (grown[i] != null && grown[i].IsBelowWater) names.Append(" [below water]"); } catch { }
                    }
                    Log("[BedSharing] '" + bed.name + "': slots {" + names + "}, max " + maxBefore + " -> " + bed.MaxCharacters);
                }
                return true;
            }
            catch (Exception e)
            {
                DiagLog.WriteError("BedSharing.Expand", e);
                return false;
            }
        }

        private static void RegisterClone(InteractableSlot clone, InteractableSlot template)
        {
            if (clone == null || template == null) return;
            int cid = clone.GetInstanceID();
            if (_templateOf.ContainsKey(cid)) return;
            _templateOf[cid] = template;
            _clones.Add(clone);
            List<InteractableSlot> l;
            int tid = template.GetInstanceID();
            if (!_clonesOf.TryGetValue(tid, out l)) { l = new List<InteractableSlot>(); _clonesOf[tid] = l; }
            l.Add(clone);
        }

        internal static bool IsShared(Interactable it) { return it != null && _expanded.Contains(it.GetInstanceID()); }

        internal static bool TemplateAllows(InteractableSlot s)
        {
            if (s == null || _templateOf.Count == 0) return true;
            InteractableSlot t;
            if (!_templateOf.TryGetValue(s.GetInstanceID(), out t)) return true;
            return t != null && t.isActiveAndEnabled;
        }

        public static void SyncClones()
        {
            if (_clones.Count == 0) return;   // not gated on Enabled: clones outlive a switch-off
            for (int i = 0; i < _clones.Count; i++)
            {
                var c = _clones[i];
                if (c == null) continue;
                InteractableSlot t;
                if (!_templateOf.TryGetValue(c.GetInstanceID(), out t)) continue;
                Mirror(c, t);
            }
        }

        public static void OnSlotDisabled(InteractableSlot slot)
        {
            if (slot == null || _clonesOf.Count == 0) return;
            List<InteractableSlot> l;
            if (!_clonesOf.TryGetValue(slot.GetInstanceID(), out l)) return;
            for (int i = 0; i < l.Count; i++) if (l[i] != null) Mirror(l[i], slot);
        }

        private static int _mirrorLines;

        private static void Mirror(InteractableSlot clone, InteractableSlot template)
        {
            try
            {
                int cid = clone.GetInstanceID();
                bool off = template == null || !template.isActiveAndEnabled;
                if (off && !_mirroredOff.Contains(cid))
                {
                    string who = null;
                    try { var ch = clone.Character as PlayableCharacter; if (ch != null) who = ch.Name; } catch { }
                    _mirroredOff.Add(cid);
                    clone.AddDisableReason(template, null);   // enabled = false -> vanilla OnDisable stands him up
                    OptCounters.CloneSlotsTurnedOff++;
                    if (who != null) OptCounters.CloneSleepersStoodUp++;
                    if (_mirrorLines < 40)
                    {
                        _mirrorLines++;
                        string bed = "?";
                        try { bed = clone.transform.parent != null ? clone.transform.parent.name : clone.name; } catch { }
                        DiagLog.Write("[BedSharing] '" + bed + "' sleeping slot turned off (stowed or blocked) - share slot off too" +
                                      (who != null ? ", " + who + " got out" : ", it was empty"));
                    }
                }
                else if (!off && _mirroredOff.Contains(cid))
                {
                    _mirroredOff.Remove(cid);
                    clone.RemoveDisableReason(template);
                }
            }
            catch (Exception e) { DiagLog.WriteError("BedSharing.Mirror", e); }
        }

        private static InteractableSlot CloneSlot(InteractableSlot template, int index)
        {
            try
            {
                var go = InstantiateMethod != null
                    ? InstantiateMethod.Invoke(null, new object[] { template.gameObject, template.transform.position,
                                                                    template.transform.rotation, template.transform.parent }) as GameObject
                    : null;
                if (go == null) return null;
                go.name = template.name + BedSharingToggles.CloneSuffix + index + ")";
                var clone = go.GetComponent<InteractableSlot>();
                if (clone == null) { UnityEngine.Object.Destroy(go); return null; }

                var t = typeof(InteractableSlot);
                for (int i = 0; i < CopyFields.Length; i++)
                {
                    var f = AccessTools.Field(t, CopyFields[i]);
                    if (f != null) f.SetValue(clone, f.GetValue(template));
                }
                for (int i = 0; i < ResetFields.Length; i++)
                {
                    var f = AccessTools.Field(t, ResetFields[i]);
                    if (f == null) continue;
                    if (f.FieldType == typeof(bool)) f.SetValue(clone, false);
                    else if (!f.FieldType.IsValueType) f.SetValue(clone, null);
                }
                var dr = AccessTools.Field(t, "disableReasons");
                if (dr != null) { try { dr.SetValue(clone, System.Activator.CreateInstance(dr.FieldType)); } catch { } }
                var ds = AccessTools.Field(t, "disabledSlots");
                if (ds != null) ds.SetValue(clone, new InteractableSlot[0]);
                var di = AccessTools.Field(t, "disabledInteractables");
                if (di != null) di.SetValue(clone, new Interactable[0]);
                return clone;
            }
            catch (Exception e)
            {
                DiagLog.WriteError("BedSharing.CloneSlot", e);
                return null;
            }
        }

        private static InteractableSlot[] CrewSlots(Interactable bed)
        {
            try { return CrewWorkSlotsField != null ? CrewWorkSlotsField.GetValue(bed) as InteractableSlot[] : null; }
            catch { return null; }
        }

        private static bool SlotFree(InteractableSlot s)
        {
            try
            {
                return s != null && s.isActiveAndEnabled && TemplateAllows(s) && s.ReservingCharacter == null &&
                       s.Character == null && !s.IsBelowWater;
            }
            catch { return false; }
        }

        private static int PendingFor(int bedId, int exceptChar, float now)
        {
            List<KeyValuePair<int, float>> l;
            if (!_pending.TryGetValue(bedId, out l)) return 0;
            l.RemoveAll(p => now - p.Value > BedSharingToggles.PendingSeconds);
            int n = 0;
            for (int i = 0; i < l.Count; i++) if (l[i].Key != exceptChar) n++;
            return n;
        }

        public static void ForgetPending(int charId)
        {
            try
            {
                lock (_lock)
                {
                    foreach (var kv in _pending) kv.Value.RemoveAll(p => p.Key == charId);
                }
            }
            catch { }
        }

        public static int FreeSpots(Bed b, int exceptChar = 0)
        {
            try
            {
                if (b == null || !b.isActiveAndEnabled) return 0;
                var comp = b.Compartment;
                if (comp != null && comp.Evacuation) return 0;
                int reserving = b.ReservingCharacters != null ? b.ReservingCharacters.Count : 0;
                int byLimit = b.MaxCharacters - reserving;
                if (byLimit <= 0) return 0;
                int bySlots = 0;
                var cs = CrewSlots(b);
                if (cs != null) for (int i = 0; i < cs.Length; i++) if (SlotFree(cs[i])) bySlots++;
                int free = Math.Min(byLimit, bySlots);
                lock (_lock) { free -= PendingFor(b.GetInstanceID(), exceptChar, Time.unscaledTime); }
                return free < 0 ? 0 : free;
            }
            catch { return 0; }
        }

        public static int FreeSpots(PlayerShip ship)
        {
            int total = 0;
            try
            {
                var beds = ship != null ? ship.Beds : null;
                if (beds == null) return 0;
                for (int i = 0; i < beds.Length; i++) total += FreeSpots(beds[i]);
            }
            catch { }
            return total;
        }

        public static Bed FindShared(PlayerShip ship, NavMeshNavigator nav, bool includeUpperBeds)
        {
            if (!BedSharingToggles.Enabled || ship == null) return null;
            long t0 = Perf.Begin();
            try
            {
                EnsureExpanded(ship);
                PlayableCharacter c = null;
                try { c = nav != null ? nav.Character as PlayableCharacter : null; } catch { }
                if (c == null) return null;
                Compartment from = null;
                try { from = c.CurrentCompartment; } catch { }
                int charId = c.GetInstanceID();

                var beds = ship.Beds;
                if (beds == null) return null;
                Bed best = null;
                int bestHops = int.MaxValue;
                for (int i = 0; i < beds.Length; i++)
                {
                    var b = beds[i];
                    if (b == null) continue;
                    if (!includeUpperBeds && b.Upper) continue;
                    if (FreeSpots(b, charId) <= 0) continue;
                    if (NearestBed.WhyNotFor(b, c) != null) continue;       // failed path / claimed for another
                    if (BedYield.ReservedForOther(b, c)) continue;          // v1.0.0: freed for a tired man
                    int h = from != null ? CompartmentGraph.Hops(from, b.Compartment) : 0;
                    if (h < 0) h = 1000;
                    if (h >= bestHops) continue;
                    var cs = CrewSlots(b);
                    InteractableSlot spot = null;
                    if (cs != null) for (int k = 0; k < cs.Length && spot == null; k++) if (SlotFree(cs[k])) spot = cs[k];
                    if (spot == null) continue;
                    bool reach = true;
                    try { reach = nav.IsDestinationReachable(spot.transform.position, false); } catch { }
                    if (!reach) continue;
                    best = b; bestHops = h;
                    if (h == 0) break;
                }
                if (best == null) return null;

                lock (_lock)
                {
                    List<KeyValuePair<int, float>> l;
                    int bid = best.GetInstanceID();
                    if (!_pending.TryGetValue(bid, out l)) { l = new List<KeyValuePair<int, float>>(); _pending[bid] = l; }
                    l.RemoveAll(p => p.Key == charId);
                    l.Add(new KeyValuePair<int, float>(charId, Time.unscaledTime));
                }
                OptCounters.BedSharedAssigned++;
                int occ = 0;
                try { occ = best.ReservingCharacters != null ? best.ReservingCharacters.Count : 0; } catch { }
                Log("[BedSharing] " + (c.Name ?? "?") + " -> shared bunk '" + best.name + "' (" + occ +
                    " already in it, " + (bestHops >= 1000 ? "?" : bestHops.ToString()) + " hop(s)) - no empty bunk left");
                return best;
            }
            catch (Exception e)
            {
                DiagLog.WriteError("BedSharing.FindShared", e);
                return null;
            }
            finally { Perf.End("BedSharing.FindShared", t0); }
        }

        private static readonly HashSet<int> _waterLogged = new HashSet<int>();

        public static bool CorrectHasAnyCrewSlotsAboveWater(Interactable it, out bool vanilla)
        {
            vanilla = false;
            var cs = CrewSlots(it);
            InteractableSlot[] slots = null;
            try { slots = SlotsField != null ? SlotsField.GetValue(it) as InteractableSlot[] : null; } catch { }
            bool ours = false;
            if (cs != null)
            {
                for (int i = cs.Length - 1; i >= 0; i--)
                {
                    var s = cs[i];
                    try
                    {
                        if (!vanilla && s != null && s.isActiveAndEnabled && slots != null && i < slots.Length &&
                            slots[i] != null && !slots[i].IsBelowWater) vanilla = true;
                        if (!ours && s != null && s.isActiveAndEnabled && TemplateAllows(s) && !s.IsBelowWater) ours = true;
                    }
                    catch { }
                }
            }
            if (ours != vanilla)
            {
                OptCounters.WaterCheckCorrected++;
                int id = it.GetInstanceID();
                if (!_waterLogged.Contains(id) && _waterLogged.Count < 30)
                {
                    _waterLogged.Add(id);
                    Log("[BedSharing] water check on '" + it.name + "': vanilla says " +
                        (vanilla ? "usable" : "FLOODED") + ", its sleeping slot(s) say " + (ours ? "usable" : "flooded") +
                        " - vanilla reads slots[i] for crewWorkSlots[i]");
                }
            }
            return ours;
        }
    }

    [HarmonyPatch(typeof(InteractableSlot), "AddDisableReason")]
    internal static class Patch_BedSharing_MirrorDisable
    {
        static void Postfix(InteractableSlot __instance)
        {
            try { BedSharing.OnSlotDisabled(__instance); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(Interactable), "get_HasAnyCrewSlotsAboveWater")]
    internal static class Patch_BedSharing_WaterCheck
    {
        static bool Prefix(Interactable __instance, ref bool __result)
        {
            if (!BedSharingToggles.FixCrewSlotWaterCheck) return true;
            try
            {
                bool vanilla;
                __result = BedSharing.CorrectHasAnyCrewSlotsAboveWater(__instance, out vanilla);
                return false;
            }
            catch { return true; }   // fall back to vanilla on any error
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/CrewAi/CrewAIBedSharingWounded.cs
// ====================================================================================================
// Crew-KI (Teil des UBOAT Toolkit): Verletzte und geteilte Kojen.
// Das Spiel legt Verletzte immer auf den ersten Platz einer Koje (Slots[0]) und prüft dafür nur den
// ERSTEN Mann in der Reservierungsliste der Koje. Bei geteilten Kojen ist das nicht immer der Mann auf
// dem ersten Platz: dann hielt das Spiel eine Koje für frei, auf der schon ein Verletzter liegt, und der
// Träger blieb mit dem nächsten Verletzten hängen.
// Fix: bei geteilten Kojen steht der Mann auf dem ersten Platz immer vorne in der Liste. So sieht das
// Spiel genau das, was es ohne Teilen sehen würde: liegt dort ein Verletzter, ist die Koje belegt;
// schläft dort ein gesunder Mann, wird er wie im Spiel für den Verletzten geweckt.
// Verletzte bekommen nie einen Zusatzplatz, nur den echten ersten Platz.
// Platz 1 ist für Verletzte da: bei geteilten Kojen legen sich Gesunde auf Platz 2 bis x, solange dort
// etwas frei ist. Ein Verletzter, der selbst zur Koje geht, nimmt Platz 1, wenn der frei ist.

namespace UboatToolkit.Modules.CrewAi
{
    using global::System;
    using global::System.Collections.Generic;
    using global::HarmonyLib;
    using global::UBOAT.Game.Core.AI;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Interactions;
    using global::UBOAT.Game.Scene.Items;

    [HarmonyPatch(typeof(Interactable), "OnSlotReservationChanged")]
    internal static class Patch_BedSharingWoundedOrder
    {
        private static int _logLines;

        static void Postfix(Interactable __instance, List<InteractiveCharacter> ___reservingWorkers)
        {
            try
            {
                if (!(__instance is Bed) || ___reservingWorkers == null || ___reservingWorkers.Count < 2) return;
                if (!BedSharing.IsShared(__instance)) return;
                var slots = __instance.Slots;
                if (slots == null || slots.Length == 0 || slots[0] == null) return;
                var first = slots[0].ReservingCharacter;
                if (first == null) return;
                int i = ___reservingWorkers.IndexOf(first);
                if (i <= 0) return;
                ___reservingWorkers.RemoveAt(i);
                ___reservingWorkers.Insert(0, first);
                if (_logLines < 20)
                {
                    _logLines++;
                    DiagLog.Write("[BedSharing] '" + __instance.name + "': " + DiagLog.NameOf(first) +
                                  " (first place" + (first.Injury ? ", injured" : "") + ") moved to the front of the reservation list");
                }
            }
            catch (Exception e) { DiagLog.WriteError("BedSharingWoundedOrder", e); }
        }
    }

    [HarmonyPatch(typeof(Interactable), "AppendUseBehaviours",
        new Type[] { typeof(InteractiveCharacter), typeof(IBehavioursContext), typeof(float), typeof(bool),
                     typeof(InteractableSlot), typeof(bool), typeof(SlotUsageFlags) })]
    internal static class Patch_BedSharingFirstPlaceForWounded
    {
        private static int _logLines;

        private static bool Usable(InteractableSlot s, InteractiveCharacter c, SlotUsageFlags flags)
        {
            return s != null && (s.ReservingCharacter == null || s.ReservingCharacter == c) && s.Character == null &&
                   s.isActiveAndEnabled && (!s.IsBelowWater || (c.HasRebreather && (flags & SlotUsageFlags.AllowUnderWaterWithRebreather) != 0) ||
                                            (flags & SlotUsageFlags.AllowUnderWaterAlways) != 0);
        }

        static bool Prefix(Interactable __instance, InteractableSlot[] ___crewWorkSlots, InteractiveCharacter character,
                           IBehavioursContext context, float profit, bool isPlayerOrder, ref InteractableSlot slotToIgnore,
                           bool isSwapping, SlotUsageFlags slotUsageFlags, ref bool __result)
        {
            try
            {
                if (!(__instance is Bed) || character == null || ___crewWorkSlots == null || !BedSharing.IsShared(__instance)) return true;
                var slots = __instance.Slots;
                if (slots == null || slots.Length == 0) return true;
                var first = slots[0];
                if (first == null || Array.IndexOf(___crewWorkSlots, first) < 0 || first == slotToIgnore) return true;

                if (character.Injury)
                {
                    // Wounded: take place 1 when it is free (same checks the game makes for any place).
                    if (!Usable(first, character, slotUsageFlags)) return true;
                    if (__instance.ReservingCharacters.Count >= __instance.MaxCharacters && !__instance.ReservingCharacters.Contains(character)) return true;
                    __result = first.AppendUseBehaviours(character, context, profit, isPlayerOrder, isSwapping, slotUsageFlags);
                    Note(__instance, character, "injured -> place 1");
                    return false;
                }

                // Healthy: leave place 1 free while one of places 2..x is free.
                if (slotToIgnore != null) return true;
                for (int i = 0; i < ___crewWorkSlots.Length; i++)
                {
                    var s = ___crewWorkSlots[i];
                    if (s != first && Usable(s, character, slotUsageFlags))
                    {
                        slotToIgnore = first;
                        Note(__instance, character, "healthy -> place 2..x");
                        return true;
                    }
                }
            }
            catch (Exception e) { DiagLog.WriteError("BedSharingFirstPlace", e); }
            return true;
        }

        private static void Note(Interactable bed, InteractiveCharacter c, string what)
        {
            if (_logLines >= 30) return;
            _logLines++;
            DiagLog.Write("[BedSharing] '" + bed.name + "': " + DiagLog.NameOf(c) + " " + what);
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/CrewAi/CrewAIBedYield.cs
// ====================================================================================================
// Crew-KI (Teil des UBOAT Toolkit): CrewAIBedYield. Schalter: F10 > QoL > "Crew-KI".

namespace UboatToolkit.Modules.CrewAi
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::UnityEngine;
    using global::HarmonyLib;
    using global::UBOAT.Game.Core.AI;
    using global::UBOAT.Game.Core.AI.Behaviours;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Navigation;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Items;

    internal static class BedYieldToggles
    {
        public static bool Enabled => UboatToolkit.Settings.B("CrewBedYield");

        public const float TiredStress = 0.5f;

        public const float RestedStress = 0.1f;

        public const float HoldSeconds = 120f;

        public const float ReserveSeconds = 60f;
        public const int MaxReserveLogLines = 40;

        public const int MaxPerTick = 2;

        public const float VacateTimeoutSeconds = 15f;

        public const int MaxReachTests = 4;

        public const int MaxLogLines = 100;
    }

    internal static class BedYield
    {
        private sealed class Hold
        {
            public PlayableCharacter Who;
            public string WhoName;
            public Bed Bed;
            public string BedName;
            public string ForName;
            public PlayableCharacter For;       // v1.0.0: the tired man the bunk is reserved for
            public bool ReserveOver;            // reservation ended (taken, expired, not needed)
            public bool GiverReleased;          // the giver is no longer kept out of bed
            public CompoundBehaviour RestBeh;   // non-null when he was resting
            public float Since, Until;          // unscaled
            public bool Vacated, StuckLogged, NextLogged;
        }

        private static readonly Dictionary<int, Hold> _holds = new Dictionary<int, Hold>();
        private static readonly HashSet<CompoundBehaviour> _heldRest = new HashSet<CompoundBehaviour>();
        private static readonly object _lock = new object();
        private static volatile int _holdCount;
        private static int _logLines;

        private static readonly FieldInfo StandaloneField =
            AccessTools.Field(typeof(SleepBehaviour), "isUsingStandaloneRestSlot");

        public static int TiredWaiting { get; private set; }
        public static int RestedInBunks { get; private set; }

        private static void Log(string s)
        {
            if (_logLines >= BedYieldToggles.MaxLogLines) return;
            _logLines++;
            DiagLog.Write(s);
        }

        public static bool IsHeldAi(CharacterAI ai)
        {
            if (_holdCount == 0 || ai == null) return false;
            if (!BedYieldToggles.Enabled) return false;   // switched off mid-session: let everyone sleep
            float now = Time.unscaledTime;
            try { lock (_lock) { Hold h; return _holds.TryGetValue(ai.GetInstanceID(), out h) && !h.GiverReleased && now <= h.Until; } }
            catch { return false; }
        }

        public static bool IsHeld(PlayableCharacter c)
        {
            if (_holdCount == 0 || c == null) return false;
            try { return IsHeldAi(c.Ai); } catch { return false; }
        }

        private static void ReleaseAll()
        {
            if (_holdCount == 0) return;
            lock (_lock) { _holds.Clear(); _heldRest.Clear(); _holdCount = 0; }
            Log("[BedYield] switched off - all holds and reservations released");
        }

        private static int _reserveLines;

        private static bool ReservationLive(Hold h, float now)
        {
            if (h.ReserveOver || h.Bed == null || now - h.Since > BedYieldToggles.ReserveSeconds) return false;
            try { return h.For != null; } catch { return false; }
        }

        public static Bed ReservedBedFor(PlayableCharacter c)
        {
            if (_holdCount == 0 || c == null) return null;
            float now = Time.unscaledTime;
            Bed any = null;
            lock (_lock)
            {
                foreach (var h in _holds.Values)
                {
                    if (!ReservationLive(h, now) || h.For != c) continue;
                    if (h.Vacated) return h.Bed;          // free now: this one first
                    if (any == null) any = h.Bed;
                }
            }
            return any;
        }

        public static bool ReservedForOther(Bed b, PlayableCharacter c)
        {
            if (_holdCount == 0 || b == null) return false;
            float now = Time.unscaledTime;
            lock (_lock)
            {
                foreach (var h in _holds.Values)
                    if (ReservationLive(h, now) && h.Bed == b && h.For != c) return true;
            }
            return false;
        }

        public static void EnforceReservation(PlayerShip ship, NavMeshNavigator nav, bool includeUpperBeds, ref Bed result)
        {
            if (!BedYieldToggles.Enabled || _holdCount == 0) return;
            PlayableCharacter c = null;
            try { c = nav != null ? nav.Character as PlayableCharacter : null; } catch { }
            if (c == null) return;

            var mine = ReservedBedFor(c);
            if (mine != null)
            {
                if (result == mine) return;
                if (BedSharing.FreeSpots(mine, c.GetInstanceID()) > 0 && (includeUpperBeds || !mine.Upper) &&
                    NearestBed.WhyNotFor(mine, c) == null && Reachable(c, mine))
                {
                    result = mine;
                    OptCounters.YieldReservedSteered++;
                    if (_reserveLines < BedYieldToggles.MaxReserveLogLines)
                    {
                        _reserveLines++;
                        DiagLog.Write("[BedYield] " + SafeName(c) + " sent to '" + SafeName(mine) + "', the bunk freed for him");
                    }
                }
                return;   // not free yet: leave his result alone
            }

            if (result != null && ReservedForOther(result, c))
            {
                Bed was = result;
                result = EmptyAlternative(ship, c, includeUpperBeds);
                if (result == null && BedSharingToggles.Enabled) result = BedSharing.FindShared(ship, nav, includeUpperBeds);
                OptCounters.YieldReservedRefused++;
                if (_reserveLines < BedYieldToggles.MaxReserveLogLines)
                {
                    _reserveLines++;
                    DiagLog.Write("[BedYield] " + SafeName(c) + " (fatigue " + Fatigue(c).ToString("F2") + ") kept off '" +
                                  SafeName(was) + "', reserved for a tired man -> " +
                                  (result != null ? "'" + SafeName(result) + "'" : "no bunk"));
                }
            }
        }

        private static Bed EmptyAlternative(PlayerShip ship, PlayableCharacter c, bool includeUpperBeds)
        {
            try
            {
                var beds = ship != null ? ship.Beds : null;
                if (beds == null) return null;
                Compartment from = null;
                try { from = c.CurrentCompartment; } catch { }
                Bed best = null;
                int bestHops = int.MaxValue;
                for (int i = 0; i < beds.Length; i++)
                {
                    var b = beds[i];
                    if (b == null || !b.isActiveAndEnabled) continue;
                    if (!includeUpperBeds && b.Upper) continue;
                    if (b.ReservingCharacters != null && b.ReservingCharacters.Count != 0) continue;
                    if (BedSharing.FreeSpots(b, c.GetInstanceID()) <= 0) continue;
                    if (ReservedForOther(b, c) || NearestBed.WhyNotFor(b, c) != null) continue;
                    int h = from != null ? CompartmentGraph.Hops(from, b.Compartment) : 0;
                    if (h < 0) h = 1000;
                    if (h >= bestHops) continue;
                    if (!Reachable(c, b)) continue;
                    best = b; bestHops = h;
                    if (h == 0) break;
                }
                return best;
            }
            catch { return null; }
        }

        public static bool IsHeldRest(CompoundBehaviour b)
        {
            if (_holdCount == 0 || b == null) return false;
            lock (_lock) { return _heldRest.Contains(b); }
        }

        private static float Fatigue(PlayableCharacter c)
        {
            try { var n = c.GetNeed(NeedType.Fatigue); return n != null ? n.stress : -1f; }
            catch { return -1f; }
        }

        private static bool IsSailor(PlayableCharacter c)
        {
            try { return c.Data != null && c.Data.CharacterType == PlayableCharacterData.Type.Sailor; }
            catch { return false; }
        }

        private static bool Standalone(SleepBehaviour sb)
        {
            try { return StandaloneField != null && (bool)StandaloneField.GetValue(sb); }
            catch { return false; }
        }

        public static bool IsLyingAsleep(PlayableCharacter c)
        {
            try
            {
                var ai = c != null ? c.Ai : null;
                var sb = ai != null ? ai.GetActiveBehaviour<SleepBehaviour>() : null;
                return sb != null && sb.State == AIBehaviourState.Active &&
                       sb.ActiveBehaviour is InteractionBehaviour && !Standalone(sb);
            }
            catch { return false; }
        }

        public static void Tick(PlayerShip ship, PlayableCharacter[] chars, bool alarmed, float now)
        {
            if (!BedYieldToggles.Enabled) { TiredWaiting = 0; ReleaseAll(); return; }
            if (ship == null || chars == null) { TiredWaiting = 0; return; }
            long t0 = Perf.Begin();
            try
            {
                var beds = ship.Beds;
                if (beds == null) { TiredWaiting = 0; return; }

                var bedOf = new Dictionary<int, Bed>();
                for (int i = 0; i < beds.Length; i++)
                {
                    var b = beds[i];
                    if (b == null) continue;
                    var list = b.ReservingCharacters;
                    if (list == null) continue;
                    for (int k = 0; k < list.Count; k++)
                    {
                        var rc = list[k] as PlayableCharacter;
                        if (rc != null) bedOf[rc.GetInstanceID()] = b;
                    }
                }

                ReviewHolds(bedOf, now);

                var waiting = new List<PlayableCharacter>();
                var waitingStress = new List<float>();
                var yielders = new List<PlayableCharacter>();
                var yClass = new List<int>();
                var yStress = new List<float>();

                for (int i = 0; i < chars.Length; i++)
                {
                    var c = chars[i];
                    if (c == null || !IsSailor(c)) continue;
                    float st = Fatigue(c);
                    if (st < 0f) continue;
                    bool inBunk = bedOf.ContainsKey(c.GetInstanceID());

                    if (inBunk)
                    {
                        if (IsHeld(c)) continue;
                        try { if (c.IsImpersonated || c.IsSelected) continue; } catch { continue; }
                        if (st < BedYieldToggles.TiredStress && RestController.RestBehaviourOf(c) != null &&
                            RestController.IsResting(c))
                        { yielders.Add(c); yClass.Add(0); yStress.Add(st); }
                        else if (st <= BedYieldToggles.RestedStress && IsLyingAsleep(c))
                        { yielders.Add(c); yClass.Add(1); yStress.Add(st); }
                        continue;
                    }

                    if (st < BedYieldToggles.TiredStress || !IsWaitingForBunk(c)) continue;
                    int at = 0;
                    while (at < waitingStress.Count && waitingStress[at] >= st) at++;
                    waiting.Insert(at, c); waitingStress.Insert(at, st);
                }

                TiredWaiting = waiting.Count;
                RestedInBunks = yielders.Count;
                if (alarmed || waiting.Count == 0 || yielders.Count == 0) return;

                int free = BedSharingToggles.Enabled ? BedSharing.FreeSpots(ship) : CountEmptyBunks(beds);
                int inProgress;
                lock (_lock)
                {
                    inProgress = 0;
                    foreach (var h in _holds.Values) if (!h.Vacated) inProgress++;
                }
                int needed = waiting.Count - free - inProgress;
                if (needed <= 0) return;

                var used = new HashSet<int>();
                int asked = 0;
                for (int w = 0; w < waiting.Count && asked < needed && asked < BedYieldToggles.MaxPerTick; w++)
                {
                    var tired = waiting[w];
                    if (ReservedBedFor(tired) != null) continue;   // v1.0.0: one bunk per tired man
                    Compartment from = null;
                    try { from = tired.CurrentCompartment; } catch { }

                    var order = new List<int>();
                    var keyHops = new List<int>();
                    for (int y = 0; y < yielders.Count; y++)
                    {
                        if (used.Contains(y)) continue;
                        Bed b;
                        if (!bedOf.TryGetValue(yielders[y].GetInstanceID(), out b)) continue;
                        int h = from != null ? CompartmentGraph.Hops(from, b.Compartment) : 0;
                        if (h < 0) h = 1000;
                        int at = 0;
                        while (at < order.Count && Before(yClass[order[at]], keyHops[at], yStress[order[at]], yClass[y], h, yStress[y])) at++;
                        order.Insert(at, y); keyHops.Insert(at, h);
                    }

                    for (int o = 0; o < order.Count && o < BedYieldToggles.MaxReachTests; o++)
                    {
                        int y = order[o];
                        var giver = yielders[y];
                        Bed b = bedOf[giver.GetInstanceID()];
                        if (!Reachable(tired, b)) continue;

                        Ask(giver, yClass[y] == 0, yStress[y], b, tired, waitingStress[w], keyHops[o], now);
                        used.Add(y);
                        asked++;
                        break;
                    }
                }
            }
            catch (Exception e) { DiagLog.WriteError("BedYield.Tick", e); }
            finally { Perf.End("BedYield.Tick", t0); }
        }

        private static bool Before(int c1, int h1, float s1, int c2, int h2, float s2)
        {
            if (c1 != c2) return c1 < c2;
            if (h1 != h2) return h1 < h2;
            return s1 <= s2;
        }

        private static bool IsWaitingForBunk(PlayableCharacter c)
        {
            try
            {
                if (c.IsImpersonated || c.IsSelected || c.Injury) return false;
                if (IsHeld(c)) return false;
                if (c.Action != null) return false;
                var q = c.ActionQueue;
                if (q != null && q.Actions != null && q.Actions.Count > 0) return false;
                var comp = c.CurrentCompartment;
                if (comp == null || comp.Evacuation) return false;
                var ai = c.Ai;
                if (ai == null || !ai.enabled) return false;

                var sb = ai.GetActiveBehaviour<SleepBehaviour>();
                if (sb != null && sb.State == AIBehaviourState.Active)
                    return Standalone(sb);   // on a Rest slot: waiting. Walking to a bunk: not.
                return ai.LowestActiveProfit <= RestToggles.LowestProfitThreshold;
            }
            catch { return false; }
        }

        private static bool Reachable(PlayableCharacter c, Bed b)
        {
            try
            {
                var nav = c.Navigator;
                var slots = b.Slots;
                if (nav == null || slots == null || slots.Length == 0 || slots[0] == null) return true;
                return nav.IsDestinationReachable(slots[0].transform.position, false);
            }
            catch { return false; }
        }

        private static int CountEmptyBunks(Bed[] beds)
        {
            int n = 0;
            for (int i = 0; i < beds.Length; i++)
            {
                try
                {
                    var b = beds[i];
                    if (b == null || !b.isActiveAndEnabled) continue;
                    if (b.ReservingCharacters != null && b.ReservingCharacters.Count != 0) continue;
                    if (b.Compartment != null && b.Compartment.Evacuation) continue;
                    n++;
                }
                catch { }
            }
            return n;
        }

        private static void Ask(PlayableCharacter giver, bool resting, float giverStress, Bed bed,
                                PlayableCharacter tired, float tiredStress, int hops, float now)
        {
            var h = new Hold();
            h.Who = giver; h.WhoName = SafeName(giver);
            h.Bed = bed; h.BedName = SafeName(bed);
            h.ForName = SafeName(tired);
            h.For = tired;
            h.Since = now; h.Until = now + BedYieldToggles.HoldSeconds;
            if (resting) h.RestBeh = RestController.RestBehaviourOf(giver);

            lock (_lock)
            {
                _holds[giver.Ai.GetInstanceID()] = h;
                if (h.RestBeh != null) _heldRest.Add(h.RestBeh);
                _holdCount = _holds.Count;
            }
            NearestBed.ReleaseFor(giver);
            BedSharing.ForgetPending(giver.GetInstanceID());
            OptCounters.YieldAsked++;

            string where = "?";
            try { if (tired.CurrentCompartment != null) where = tired.CurrentCompartment.name; } catch { }
            Log("[BedYield] " + h.WhoName + " (" + (resting ? "resting" : "asleep") + ", fatigue " +
                giverStress.ToString("F2") + ") gives up '" + h.BedName + "' for " + h.ForName +
                " (fatigue " + tiredStress.ToString("F2") + ", in '" + where + "', " +
                (hops >= 1000 ? "?" : hops.ToString()) + " hop(s)) - kept out of bed for " +
                BedYieldToggles.HoldSeconds.ToString("F0") + "s");
        }

        private static void ReviewHolds(Dictionary<int, Bed> bedOf, float now)
        {
            var notes = new List<string>();
            lock (_lock)
            {
                List<int> done = null;
                foreach (var kv in _holds)
                {
                    var h = kv.Value;
                    bool gone;
                    try { gone = h.Who == null; } catch { gone = true; }

                    if (!gone && !h.Vacated)
                    {
                        Bed still;
                        bool inIt = bedOf.TryGetValue(h.Who.GetInstanceID(), out still) && still == h.Bed;
                        if (!inIt)
                        {
                            h.Vacated = true;
                            OptCounters.YieldVacated++;
                            notes.Add("[BedYield] " + h.WhoName + " got up from '" + h.BedName + "' after " +
                                      (now - h.Since).ToString("F1") + "s");
                        }
                        else if (!h.StuckLogged && now - h.Since > BedYieldToggles.VacateTimeoutSeconds)
                        {
                            h.StuckLogged = true;
                            OptCounters.YieldStuck++;
                            notes.Add("[BedYield] STUCK: " + h.WhoName + " still in '" + h.BedName + "' " +
                                      (now - h.Since).ToString("F0") + "s after being asked - " + Doing(h.Who));
                        }
                    }

                    if (!h.ReserveOver)
                    {
                        bool forGone;
                        try { forGone = h.For == null; } catch { forGone = true; }
                        bool his = false;
                        try
                        {
                            var rl = h.Bed != null ? h.Bed.ReservingCharacters : null;
                            for (int k = 0; !forGone && rl != null && k < rl.Count; k++) if (rl[k] == h.For) his = true;
                        }
                        catch { }
                        string why = null;
                        Bed elsewhere = null;
                        if (his) why = null;
                        else if (forGone) why = "he is gone";
                        else if (bedOf.TryGetValue(h.For.GetInstanceID(), out elsewhere) && elsewhere != h.Bed)
                            why = "he took '" + SafeName(elsewhere) + "' instead";
                        else if (now - h.Since > BedYieldToggles.ReserveSeconds) why = "expired after " + BedYieldToggles.ReserveSeconds.ToString("F0") + "s";
                        else if (Fatigue(h.For) < BedYieldToggles.TiredStress) why = "he is no longer tired";
                        if (his || why != null)
                        {
                            h.ReserveOver = true;
                            if (why != null)
                            {
                                OptCounters.YieldReserveLapsed++;
                                notes.Add("[BedYield] reservation of '" + h.BedName + "' for " + h.ForName + " ended unused: " + why);
                            }
                        }
                    }

                    if (h.Vacated && !h.NextLogged && h.Bed != null)
                    {
                        try
                        {
                            var list = h.Bed.ReservingCharacters;
                            for (int k = 0; list != null && k < list.Count; k++)
                            {
                                var rc = list[k] as PlayableCharacter;
                                if (rc == null || rc == h.Who) continue;
                                float st = Fatigue(rc);
                                bool tired = st >= BedYieldToggles.TiredStress;
                                if (tired) OptCounters.YieldTakenByTired++;
                                h.NextLogged = true;
                                notes.Add("[BedYield] '" + h.BedName + "' taken by " + SafeName(rc) + " (fatigue " +
                                          st.ToString("F2") + (tired ? ", tired" : ", NOT tired") + ") " +
                                          (now - h.Since).ToString("F0") + "s after " + h.WhoName + " was asked");
                                break;
                            }
                        }
                        catch { }
                    }

                    bool tiredAgain = !gone && Fatigue(h.Who) >= BedYieldToggles.TiredStress;
                    if ((gone || now > h.Until || tiredAgain) && !h.GiverReleased)
                    {
                        h.GiverReleased = true;
                        if (h.RestBeh != null) _heldRest.Remove(h.RestBeh);
                    }
                    if (h.GiverReleased && (h.ReserveOver || now - h.Since > BedYieldToggles.ReserveSeconds))
                    {
                        if (done == null) done = new List<int>();
                        done.Add(kv.Key);
                    }
                }
                if (done != null)
                {
                    for (int i = 0; i < done.Count; i++)
                    {
                        Hold h;
                        if (_holds.TryGetValue(done[i], out h) && h.RestBeh != null) _heldRest.Remove(h.RestBeh);
                        _holds.Remove(done[i]);
                    }
                    _holdCount = _holds.Count;
                }
            }
            for (int i = 0; i < notes.Count; i++) Log(notes[i]);
        }

        private static string Doing(PlayableCharacter c)
        {
            try
            {
                var ai = c.Ai;
                var sb = ai != null ? ai.GetActiveBehaviour<SleepBehaviour>() : null;
                if (sb != null) return "SleepBehaviour " + sb.State + " profit " + sb.Profit.ToString("F2");
                return "lowest active profit " + (ai != null ? ai.LowestActiveProfit.ToString("F2") : "?");
            }
            catch { return "?"; }
        }

        private static string SafeName(PlayableCharacter c)
        {
            try { return c != null ? (c.Name ?? "?") : "gone"; } catch { return "?"; }
        }

        private static string SafeName(Bed b)
        {
            try { return b != null ? b.name : "?"; } catch { return "?"; }
        }
    }

    [HarmonyPatch(typeof(SleepBehaviour), "OnBehaviourBackgroundUpdate")]
    internal static class Patch_BedYield_Sleep
    {
        static void Postfix(SleepBehaviour __instance)
        {
            try
            {
                if (__instance != null && BedYield.IsHeldAi(__instance.AI)) __instance.Profit = -1f;
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(CompoundBehaviour), "OnBehaviourBackgroundUpdate")]
    internal static class Patch_BedYield_Rest
    {
        static void Postfix(CompoundBehaviour __instance)
        {
            try
            {
                if (BedYield.IsHeldRest(__instance)) __instance.Profit = -1f;
            }
            catch { }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/CrewAi/CrewAIBowRun.cs
// ====================================================================================================
// Crew-KI (Teil des UBOAT Toolkit): CrewAIBowRun. Schalter: F10 > QoL > "Crew-KI".

namespace UboatToolkit.Modules.CrewAi
{
    using global::System;
    using global::System.Collections.Generic;
    using global::UnityEngine;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Items;
    using global::UBOAT.Game.Scene.Tasks;

    internal static class BowRunToggles
    {
        public static bool Enabled => UboatToolkit.Settings.B("CrewBowRunCap");

        public static int MaxRunners => Mathf.Clamp(Mathf.RoundToInt(UboatToolkit.Settings.F("CrewBowRunMen")), 0, 30);

        public const float ReleaseMin = 0.4f;
        public const float ReleaseMax = 1.2f;
    }

    internal static class BowRun
    {
        public static bool IsRunning(PlayableCharacter c)
        {
            try { return c != null && c.Action is RunAtBowAction; } catch { return false; }
        }

        internal static string SafeName(PlayableCharacter c)
        {
            try { return c != null ? (c.Name ?? "?") : "?"; } catch { return "?"; }
        }
    }

    [HarmonyPatch(typeof(IdleCrewRunsAtBow), "PlayerShipOnSubmergeStateChanged")]
    internal static class Patch_BowRun_Trigger
    {
        static void Prefix(RunAtBowAction ___action, out int __state)
        {
            __state = 0;
            try { if (___action != null && ___action.Characters != null) __state = ___action.Characters.Count; }
            catch { }
        }

        static void Postfix(RunAtBowAction ___action, int __state)
        {
            if (!BowRunToggles.Enabled) return;
            try
            {
                var list = ___action != null ? ___action.Characters : null;
                if (list == null) return;
                int before = __state, sent = list.Count;
                if (sent <= before) return;   // surfaced (RemoveAllCharacters) or nobody new

                OptCounters.BowRunDives++;
                OptCounters.BowRunSent += sent - before;

                int trimmed = 0;
                int cap = Math.Max(0, BowRunToggles.MaxRunners);
                if (sent > cap)
                {
                    Vector3 bow = Vector3.zero;
                    bool haveBow = false;
                    try
                    {
                        var ship = RestController.Ship;
                        var comp = ship != null ? ship.GetEquipment<Compartment>("Bow Torpedo Room") : null;
                        if (comp != null) { bow = comp.transform.position; haveBow = true; }
                    }
                    catch { }

                    var order = new List<KeyValuePair<PlayableCharacter, float>>(sent);
                    for (int i = 0; i < list.Count; i++)
                    {
                        var c = list[i];
                        float d = 0f;
                        try { if (haveBow && c != null) d = (c.transform.position - bow).sqrMagnitude; } catch { }
                        order.Add(new KeyValuePair<PlayableCharacter, float>(c, d));
                    }
                    if (haveBow) order.Sort((x, y) => y.Value.CompareTo(x.Value));
                    else order.Reverse();   // vanilla adds from the end of the crew list

                    int excess = sent - cap;
                    for (int i = 0; i < order.Count && trimmed < excess; i++)
                    {
                        try { ___action.RemoveCharacter(order[i].Key); trimmed++; }
                        catch (Exception e) { DiagLog.WriteError("BowRun.Trim", e); }
                    }
                    OptCounters.BowRunTrimmed += trimmed;
                }

                DiagLog.Write("[BowRun] dive drill: vanilla sent " + (sent - before) + " men to the bow" +
                              (before > 0 ? " (" + before + " already there)" : "") +
                              ", kept " + (sent - trimmed) + ", sent " + trimmed + " back to what they were doing");
            }
            catch (Exception e) { DiagLog.WriteError("BowRun.Trigger", e); }
        }
    }

    [HarmonyPatch(typeof(IdleCrewRunsAtBow), "RemoveOneCharacter")]
    internal static class Patch_BowRun_Release
    {
        static void Prefix(RunAtBowAction ___action, out int __state)
        {
            __state = 0;
            try { if (___action != null && ___action.Characters != null) __state = ___action.Characters.Count; }
            catch { }
        }

        static void Postfix(ref float __result, int __state)
        {
            if (!BowRunToggles.Enabled) return;
            try
            {
                if (__state <= 0) return;
                OptCounters.BowRunReleased++;
                if (__state == 1) DiagLog.Write("[BowRun] last man released from the bow");
                if (!float.IsNaN(__result))
                    __result = UnityEngine.Random.Range(BowRunToggles.ReleaseMin, BowRunToggles.ReleaseMax);
            }
            catch (Exception e) { DiagLog.WriteError("BowRun.Release", e); }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/CrewAi/CrewAICooldown.cs
// ====================================================================================================
// Crew-KI (Teil des UBOAT Toolkit): CrewAICooldown. Schalter: F10 > QoL > "Crew-KI".

namespace UboatToolkit.Modules.CrewAi
{
    using global::System;
    using global::System.Collections.Generic;
    using global::UnityEngine;
    using global::HarmonyLib;
    using global::UBOAT.Game.Core.AI;

    internal static class CooldownToggles
    {
        public static bool Enabled => UboatToolkit.Settings.B("CrewStuckCooldown");

        public const int DistinctTargetsBeforeCooldown = 3;

        public const float FailureWindowSeconds = 20f;

        public const float CooldownSeconds = 60f;

        public static readonly string[] ExemptBehaviours =
        {
            "Escape", "Danger", "Damage", "Repair", "Leak", "Fire", "Alarm",
            "Combat", "Fight", "Battle", "Evacuat", "Wounded", "Medic",
            "Treatment", "Bleeding", "Order", "Command",
            "Idle", "Compound", "Blank"
        };

        public static readonly bool LogCooldowns = true;
    }

    internal static class BehaviourCooldown
    {
        private class Record
        {
            public readonly HashSet<string> Targets = new HashSet<string>();
            public float LastFailure;       // unscaled
            public float CooldownUntil;     // unscaled; 0 = not on cooldown
        }

        private static readonly Dictionary<string, Record> _records = new Dictionary<string, Record>();

        private static readonly Dictionary<string, string> _lastBehaviour = new Dictionary<string, string>();

        private static readonly object _lock = new object();
        private static float _lastPrune;

        private static string Key(string who, string behaviour) { return who + "|" + behaviour; }

        public static bool IsExempt(string behaviour)
        {
            if (string.IsNullOrEmpty(behaviour)) return true;
            var ex = CooldownToggles.ExemptBehaviours;
            for (int i = 0; i < ex.Length; i++)
            {
                if (behaviour.IndexOf(ex[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        public static void NoteBehaviourStarted(string who, string behaviour)
        {
            if (!CooldownToggles.Enabled) return;
            if (string.IsNullOrEmpty(who) || string.IsNullOrEmpty(behaviour)) return;
            try { lock (_lock) { _lastBehaviour[who] = behaviour; } }
            catch { }
        }

        public static string LastBehaviourOf(string who)
        {
            if (string.IsNullOrEmpty(who)) return null;
            try
            {
                string b;
                lock (_lock) { return _lastBehaviour.TryGetValue(who, out b) ? b : null; }
            }
            catch { return null; }
        }

        public static void NoteUnreachable(string who, string targetDesc)
        {
            if (!CooldownToggles.Enabled) return;
            if (string.IsNullOrEmpty(who)) return;
            try
            {
                string behaviour;
                lock (_lock)
                {
                    if (!_lastBehaviour.TryGetValue(who, out behaviour)) return;
                }
                if (IsExempt(behaviour)) return;

                float now = Time.unscaledTime;
                bool tripped = false;
                int distinct = 0;

                lock (_lock)
                {
                    Record r;
                    string k = Key(who, behaviour);
                    if (!_records.TryGetValue(k, out r))
                    {
                        r = new Record();
                        _records[k] = r;
                    }

                    if (now - r.LastFailure > CooldownToggles.FailureWindowSeconds)
                        r.Targets.Clear();

                    r.LastFailure = now;
                    r.Targets.Add(targetDesc ?? "?");
                    distinct = r.Targets.Count;

                    if (r.CooldownUntil <= now && distinct >= CooldownToggles.DistinctTargetsBeforeCooldown)
                    {
                        r.CooldownUntil = now + CooldownToggles.CooldownSeconds;
                        r.Targets.Clear();
                        tripped = true;
                    }
                }

                OptCounters.CooldownFailuresSeen++;
                if (tripped)
                {
                    OptCounters.CooldownsStarted++;
                    if (CooldownToggles.LogCooldowns)
                    {
                        DiagLog.Write("[Cooldown] " + who + " cannot reach " + distinct +
                                      " distinct targets for '" + behaviour + "' - suppressing it for " +
                                      CooldownToggles.CooldownSeconds + "s (latest target=" + targetDesc + ")");
                    }
                }
            }
            catch (Exception e) { DiagLog.WriteError("Cooldown.NoteUnreachable", e); }
        }

        public static bool IsOnCooldown(string who, string behaviour)
        {
            if (!CooldownToggles.Enabled) return false;
            if (string.IsNullOrEmpty(who) || IsExempt(behaviour)) return false;
            try
            {
                float now = Time.unscaledTime;
                lock (_lock)
                {
                    Record r;
                    if (!_records.TryGetValue(Key(who, behaviour), out r)) return false;
                    if (r.CooldownUntil <= now) return false;
                    return true;
                }
            }
            catch (Exception e)
            {
                DiagLog.WriteError("Cooldown.IsOnCooldown", e);
                return false; // fail open - never suppress because of our own error
            }
        }

        public static void MaybePrune()
        {
            try
            {
                float now = Time.unscaledTime;
                if (now - _lastPrune < 60f) return;
                _lastPrune = now;
                lock (_lock)
                {
                    var dead = new List<string>();
                    foreach (var kv in _records)
                    {
                        var r = kv.Value;
                        if (r.CooldownUntil <= now &&
                            now - r.LastFailure > CooldownToggles.FailureWindowSeconds * 2f)
                            dead.Add(kv.Key);
                    }
                    for (int i = 0; i < dead.Count; i++) _records.Remove(dead[i]);
                }
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(CharacterAI), "StartBehaviour", new Type[] { typeof(AIBehaviour) })]
    internal static class Patch_Cooldown_Enforce
    {
        static void Postfix(CharacterAI __instance, AIBehaviour behaviour)
        {
            if (!CooldownToggles.Enabled) return;
            long t0 = Perf.Begin();
            try
            {
                if (behaviour == null) return;
                string type = behaviour.GetType().Name;
                if (BehaviourCooldown.IsExempt(type)) return;

                string who = null;
                try { who = __instance != null ? __instance.name : null; } catch { }
                if (who == null) return;

                if (BehaviourCooldown.IsOnCooldown(who, type))
                {
                    behaviour.State = AIBehaviourState.Cancelled;
                    OptCounters.CooldownBehavioursCancelled++;
                }
                BehaviourCooldown.MaybePrune();
            }
            catch (Exception e) { DiagLog.WriteError("Cooldown.Enforce", e); }
            finally { Perf.End("Cooldown.Enforce", t0); }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/CrewAi/CrewAICrowdCensus.cs
// ====================================================================================================
// Crew-KI (Teil des UBOAT Toolkit): CrewAICrowdCensus. Schalter: F10 > QoL > "Crew-KI".

namespace UboatToolkit.Modules.CrewAi
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Text;
    using global::UnityEngine;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Items;

    internal static class CrowdToggles
    {
        public static readonly bool Enabled = true;
        public const float IntervalSeconds = 60f;     // real
        public const int CrowdedThreshold = 8;        // FreeSpaceBehaviour's own trigger
        public const float LongIdleSeconds = 60f;
    }

    internal static class CrowdCensus
    {
        private static float _last;

        private sealed class Tally
        {
            public int Total, Idle, LongIdle, Resting, Asleep, BowRun, Busy;
        }

        public static void MaybeReport()
        {
            if (!CrowdToggles.Enabled) return;
            float now = Time.unscaledTime;
            if (now - _last < CrowdToggles.IntervalSeconds) return;
            _last = now;

            long t0 = Perf.Begin();
            try
            {
                var crew = RestController.Crew;
                if (crew == null) return;
                PlayableCharacter[] chars = null;
                try { chars = crew.Characters; } catch { }
                if (chars == null || chars.Length == 0) return;

                var byComp = new Dictionary<string, Tally>();
                var ctrlIdlers = new List<KeyValuePair<string, float>>();
                int totalIdle = 0, totalResting = 0, totalAsleep = 0;

                for (int i = 0; i < chars.Length; i++)
                {
                    var c = chars[i];
                    if (c == null) continue;
                    string comp = "(outside)";
                    try { if (c.CurrentCompartment != null) comp = c.CurrentCompartment.name; } catch { }

                    Tally t;
                    if (!byComp.TryGetValue(comp, out t)) { t = new Tally(); byComp[comp] = t; }
                    t.Total++;

                    CharacterAction a = null;
                    try { a = c.Action; } catch { }

                    if (RestController.IsResting(c)) { t.Resting++; totalResting++; continue; }
                    if (a is SleepAction || BedYield.IsLyingAsleep(c)) { t.Asleep++; totalAsleep++; continue; }
                    if (a is RunAtBowAction) { t.BowRun++; continue; }   // v0.32: the crash-dive drill

                    bool idle = false;
                    try
                    {
                        var q = c.ActionQueue;
                        idle = a == null && (q == null || q.Actions == null || q.Actions.Count == 0) &&
                               c.Ai != null && c.Ai.LowestActiveProfit <= RestToggles.LowestProfitThreshold;
                    }
                    catch { }

                    if (!idle) { t.Busy++; continue; }
                    t.Idle++; totalIdle++;
                    float idleFor = RestController.IdleSeconds(c);
                    if (idleFor >= CrowdToggles.LongIdleSeconds) t.LongIdle++;
                    if (comp.IndexOf("Control", StringComparison.OrdinalIgnoreCase) >= 0)
                        ctrlIdlers.Add(new KeyValuePair<string, float>(SafeName(c), idleFor));
                }

                foreach (var kv in byComp)
                {
                    var t = kv.Value;
                    if (t.Total <= CrowdToggles.CrowdedThreshold && t.Idle == 0) continue;
                    DiagLog.Write("[Crowd] " + kv.Key + " " + t.Total +
                                  (t.Total > CrowdToggles.CrowdedThreshold ? " CROWDED" : "") +
                                  ": idle " + t.Idle + " (" + t.LongIdle + " >" + CrowdToggles.LongIdleSeconds.ToString("F0") + "s)" +
                                  ", resting " + t.Resting + ", asleep " + t.Asleep +
                                  (t.BowRun > 0 ? ", bowRun " + t.BowRun : "") + ", busy " + t.Busy);
                }

                var sb = new StringBuilder("[Crowd:Totals] crew " + chars.Length + " | idle " + totalIdle +
                                           " | resting " + totalResting + " | asleep " + totalAsleep +
                                           " | free bunks " + FreeBunks() +
                                           (BedSharingToggles.Enabled ? " | free sleeping spots " + BedSharing.FreeSpots(RestController.Ship) : "") +
                                           " | tired sailors waiting for a bunk " + BedYield.TiredWaiting +
                                           " | rested men in bunks " + BedYield.RestedInBunks);
                if (ctrlIdlers.Count > 0)
                {
                    ctrlIdlers.Sort((x, y) => y.Value.CompareTo(x.Value));
                    sb.Append(" | Control Room idlers: ");
                    for (int i = 0; i < ctrlIdlers.Count && i < 8; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append(ctrlIdlers[i].Key).Append(' ');
                        sb.Append(ctrlIdlers[i].Value >= 0 ? ctrlIdlers[i].Value.ToString("F0") + "s" : "?");
                    }
                }
                DiagLog.Write(sb.ToString());

                ReportEmptyBunks(chars);

                string loiter = LoiterFilter.DrainTallies();
                if (loiter != null) DiagLog.Write("[Crowd:Loiter] refusals since last census: " + loiter);
            }
            catch (Exception e) { DiagLog.WriteError("Crowd.Report", e); }
            finally { Perf.End("Crowd.Report", t0); }
        }

        private static int _bunkLines;

        private static void ReportEmptyBunks(PlayableCharacter[] chars)
        {
            if (_bunkLines >= 60) return;
            var ship = RestController.Ship;
            var beds = ship != null ? ship.Beds : null;
            if (beds == null) return;

            PlayableCharacter sample = null;
            float sampleIdle = -2f;
            int idleSailors = 0, idleOfficers = 0;
            for (int i = 0; i < chars.Length; i++)
            {
                var c = chars[i];
                if (c == null) continue;
                bool idle = false;
                try
                {
                    var q = c.ActionQueue;
                    idle = c.Action == null && (q == null || q.Actions == null || q.Actions.Count == 0) &&
                           c.Ai != null && c.Ai.LowestActiveProfit <= RestToggles.LowestProfitThreshold &&
                           c.CurrentCompartment != null;
                }
                catch { }
                if (!idle) continue;
                bool sailor = false;
                try { sailor = c.Data != null && c.Data.CharacterType == PlayableCharacterData.Type.Sailor; } catch { }
                if (!sailor) { idleOfficers++; continue; }
                idleSailors++;
                float s = RestController.IdleSeconds(c);
                if (s > sampleIdle) { sampleIdle = s; sample = c; }
            }
            if (idleSailors == 0 || sample == null) return;

            var free = new List<Bed>();
            for (int i = 0; i < beds.Length; i++)
            {
                var b = beds[i];
                if (b == null || !b.isActiveAndEnabled) continue;
                try
                {
                    if (b.ReservingCharacters != null && b.ReservingCharacters.Count != 0) continue;
                    if (b.InteractingCharacters != null && b.InteractingCharacters.Count != 0) continue;
                }
                catch { continue; }
                free.Add(b);
            }
            if (free.Count <= RestToggles.ReserveFreeBunks) return;

            string whyMan = RestController.WhyNotEligible(sample);
            var sb = new StringBuilder("[Crowd:Bunks] " + free.Count + " free sailor bunk(s) while " + idleSailors +
                                       " sailor(s) stand idle" + (idleOfficers > 0 ? " (+" + idleOfficers + " idle officers, left to vanilla)" : "") +
                                       ". Longest idle: " + SafeName(sample) +
                                       (sampleIdle >= 0 ? " " + sampleIdle.ToString("F0") + "s" : "") +
                                       " - " + (whyMan ?? "ELIGIBLE, should be sent this tick") + ". Bunks: ");
            NavMeshNavigatorProxy nav = new NavMeshNavigatorProxy(sample);
            for (int i = 0; i < free.Count && i < 12; i++)
            {
                var b = free[i];
                if (i > 0) sb.Append("; ");
                string comp = "?";
                try { comp = b.Compartment != null ? b.Compartment.name : "?"; } catch { }
                sb.Append('\'').Append(b.name).Append("' (").Append(comp).Append("): ").Append(BunkReason(b, sample, nav));
            }
            if (free.Count > 12) sb.Append("; +" + (free.Count - 12) + " more");
            _bunkLines++;
            DiagLog.Write(sb.ToString());
        }

        private static string BunkReason(Bed b, PlayableCharacter c, NavMeshNavigatorProxy nav)
        {
            try
            {
                if (!b.HasAnyCrewSlotsAboveWater) return "flooded";
                var comp = b.Compartment;
                if (comp != null && comp.Evacuation) return "compartment evacuating";
                var slots = b.Slots;
                if (slots == null || slots.Length == 0 || slots[0] == null || !slots[0].isActiveAndEnabled) return "sleeping slot disabled";
                string ours = NearestBed.WhyNotFor(b, c);
                if (ours != null) return ours;
                if (!nav.Reachable(slots[0].transform.position)) return "UNREACHABLE from " + SafeName(c) + " (locked hatch?)";
                return "AVAILABLE";
            }
            catch { return "?"; }
        }

        private struct NavMeshNavigatorProxy
        {
            private readonly UBOAT.Game.Scene.Characters.Navigation.NavMeshNavigator _nav;
            public NavMeshNavigatorProxy(PlayableCharacter c)
            {
                UBOAT.Game.Scene.Characters.Navigation.NavMeshNavigator n = null;
                try { n = c != null ? c.Navigator : null; } catch { }
                _nav = n;
            }
            public bool Reachable(Vector3 p)
            {
                if (_nav == null) return true;
                try { return _nav.IsDestinationReachable(p, false); } catch { return true; }
            }
        }

        private static int FreeBunks()
        {
            int free = 0;
            try
            {
                var ship = RestController.Ship;
                var beds = ship != null ? ship.Beds : null;
                if (beds == null) return -1;
                for (int i = 0; i < beds.Length; i++)
                {
                    var b = beds[i];
                    if (b == null || !b.isActiveAndEnabled) continue;
                    if (b.ReservingCharacters != null && b.ReservingCharacters.Count != 0) continue;
                    if (b.InteractingCharacters != null && b.InteractingCharacters.Count != 0) continue;
                    free++;
                }
            }
            catch { }
            return free;
        }

        private static string SafeName(PlayableCharacter c)
        {
            try { return c != null ? (c.Name ?? "?") : "?"; } catch { return "?"; }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/CrewAi/CrewAIDiagnostics.cs
// ====================================================================================================
// Crew-KI (Teil des UBOAT Toolkit): CrewAIDiagnostics. Schalter: F10 > QoL > "Crew-KI".

namespace UboatToolkit.Modules.CrewAi
{
    using global::System;
    using global::System.IO;
    using global::System.Reflection;
    using global::System.Text;
    using global::System.Collections.Generic;
    using global::UnityEngine;
    using global::UnityEngine.AI;
    using global::HarmonyLib;
    using global::UBOAT.Game;
    using global::UBOAT.Game.Core.AI;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using Activator = global::UBOAT.Game.Scene.Characters.Activators.Activator; // System.Activator also in scope
    using global::UBOAT.Game.Scene.Characters.Navigation;

    internal static class DiagToggles
    {
        public static bool Verbose;
        public static bool VerboseSetting => UboatToolkit.Settings.B("CrewAILog");
        public static bool LogActions { get { return Verbose; } }
        public static bool LogBehaviours { get { return Verbose; } }
        public static bool LogGameLog { get { return Verbose; } }
        public static bool LogMovement { get { return Verbose; } }

        public static readonly bool LogAnimation = false;

        public static readonly bool LogWorldSimEntities = false;
    }

    internal static class CrewAIInstall
    {
        internal static void Install(Harmony harmony)
        {
            try
            {
                CrewAISettings.Load();   // first: Init and the patch classes read settings
                DiagLog.Init();
                int failed;
                int patched = CrewAIPatching.PatchOwnClasses(harmony, out failed);
                DiagLog.Write("=== Crew-KI (UBOAT Toolkit v" + DiagLog.ManifestVersion() + ") geladen ===" +
                              (DiagToggles.Verbose ? " (verbose logging)" : ""));
                DiagLog.Write("[Patches] " + patched + " patch class(es) applied" + (failed > 0 ? ", " + failed + " FAILED (see output_log.txt)" : ""));
                Debug.Log("[UboatToolkit Crew-KI] geladen (v" + DiagLog.ManifestVersion() + "): " + patched +
                          " patch class(es)" + (failed > 0 ? ", " + failed + " failed" : "") + "; log " + DiagLog.LogPath);
                DiagLog.Write("[Settings] " + CrewAISettings.Status);
                DiagLog.Write("[Settings] " + CrewAISettings.Summary());
                DiagLog.Write("[Config] goToThrottle=" + OptToggles.ThrottleGoToRetries +
                              " (every " + OptToggles.MinRetryFrames + " frames)" +
                              " | slotBlacklist=" + OptToggles.BlacklistUnreachableSlots +
                              " (needs " + OptToggles.MinDistinctFailures + " distinct failures, " +
                              OptToggles.BlacklistSeconds + "s real)" +
                              " | periscopePolicy=" + PeriscopeToggles.Policy +
                              " | worldSimLogging=" + DiagToggles.LogWorldSimEntities +
                              " | behaviourCooldown=" + CooldownToggles.Enabled +
                              " (" + CooldownToggles.DistinctTargetsBeforeCooldown + " targets/" +
                              CooldownToggles.FailureWindowSeconds + "s -> " +
                              CooldownToggles.CooldownSeconds + "s) " +
                              "| loiterFilter=" + LoiterToggles.Enabled +
                              " (max " + LoiterToggles.MaxIdleTravelMeters + "m, restricted=" +
                              string.Join("/", LoiterToggles.RestrictedCompartments) + ")" +
                              " | attackPeriscopeSkipperOnly=" + PeriscopeToggles.AttackPeriscopeSkipperOnly +
                              " | keepHatchOrder=" + HatchToggles.KeepCommandersHatchOrder +
                              " | proximity=" + ProximityToggles.Enabled +
                              " (Abzug je Raum " + ProximityToggles.PerCompartmentStep +
                              "/compartment, floor " + ProximityToggles.MinScale + ")" +
                              " | transitLog=" + TransitToggles.Enabled +
                              " | noEatDuringAlarm=" + AlarmToggles.NoEatingDuringAlarm +
                              " (unless hunger>=" + AlarmToggles.CriticalHunger + ")" +
                              " | nearestBed=" + BedChoiceToggles.PreferNearestBed +
                              " (claim " + BedChoiceToggles.ClaimSeconds + "s, sticky=" +
                              BedChoiceToggles.StickyChoice + ", tieBreak=" +
                              BedChoiceToggles.RandomiseWithinTier + ")" +
                              " | perfProbe=" + PerfToggles.Enabled +
                              " (slow>" + PerfToggles.SlowCallMs + "ms)" +
                              " | orderTracking=" + OrderToggles.Enabled +
                              " (stall>" + OrderToggles.StallSeconds + "s)" +
                              " | restInBunks=" + RestToggles.Enabled +
                              " (idle " + RestToggles.IdleSecondsBeforeRest + "s, sailors only, reserve " +
                              RestToggles.ReserveFreeBunks + " bunks, restProfit " + RestToggles.RestProfit + ", via Use(bed))" +
                              " | crowdCensus=" + CrowdToggles.Enabled +
                              " | keepRunningJobs=" + RoleDropToggles.KeepRunningJobs +
                              " | bedSharing=" + BedSharingToggles.Enabled + " (" + BedSharingToggles.MenPerBunk +
                              " per sailor bunk, only when no empty bunk is left)" +
                              " | crewSlotWaterFix=" + BedSharingToggles.FixCrewSlotWaterCheck +
                              " | queueRoom=" + QueueRoomToggles.MakeRoomForPlayerOrders +
                              " (caps " + QueueRoomToggles.PerCharacterLimit + "/" +
                              QueueRoomToggles.GlobalLimit + ")");
            }
            catch (Exception e)
            {
                Debug.LogError("[UboatToolkit Crew-KI] Fehler beim Einbau");
                Debug.LogException(e);
            }
        }
    }

    internal static class DiagLog
    {
        private static StreamWriter _writer;
        private static bool _open;
        private static readonly object _lock = new object();
        public static string LogPath { get; private set; }

        private static readonly Dictionary<string, float> _lastErrorAt = new Dictionary<string, float>();
        private const float ErrorThrottleSeconds = 5f;

        private const int KeepLogs = 10;

        public static void Init()
        {
            try
            {
                string dir = Path.Combine(UboatToolkit.Settings.Folder, "CrewKI");
                Directory.CreateDirectory(dir);
                try { DiagToggles.Verbose = DiagToggles.VerboseSetting || File.Exists(Path.Combine(dir, "verbose.txt")); } catch { }
                if (!DiagToggles.Verbose) PruneOldLogs(dir);
                LogPath = Path.Combine(dir, "crew_ai_log_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                _writer = new StreamWriter(LogPath, false, Encoding.UTF8) { AutoFlush = true };
                _writer.Dispose(); _writer = null; _open = true;   // file created; lines go through the background writer
            }
            catch (Exception e)
            {
                Debug.LogError("[UboatToolkit Crew-KI] Log-Datei konnte nicht geöffnet werden");
                Debug.LogException(e);
            }
        }

        private static void PruneOldLogs(string dir)
        {
            try
            {
                var logs = Directory.GetFiles(dir, "crew_ai_log_*.txt");
                Array.Sort(logs, StringComparer.Ordinal);
                for (int i = 0; i < logs.Length - (KeepLogs - 1); i++)
                {
                    try { File.Delete(logs[i]); } catch { }
                }
            }
            catch { }
        }

        public static string ManifestVersion()
        {
            try
            {
                var manager = DWS.Common.InjectionFramework.ScriptableObjectSingleton.LoadSingleton<UBOAT.Game.Core.Mods.ModManager>();
                var self = manager != null ? manager.GetMod(Assembly.GetExecutingAssembly()) : null;
                if (self == null || string.IsNullOrEmpty(self.Path)) return "?";
                string m = Path.Combine(self.Path, "Manifest.json");
                if (!File.Exists(m)) return "?";
                var match = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(m), "\"version\"\\s*:\\s*\"([^\"]+)\"");
                if (match.Success) return match.Groups[1].Value;
            }
            catch { }
            return "?";
        }

        public static void Write(string line)
        {
            lock (_lock)
            {
                try
                {
                    if (!_open) return;
                    UboatToolkit.BgFile.Append(LogPath, "[gt=" + Time.time.ToString("F2") +
                                      " rt=" + Time.unscaledTime.ToString("F2") + "] " + line + "\r\n");
                }
                catch { /* never let logging crash the game */ }
            }
        }

        public static void WriteError(string tag, Exception ex)
        {
            try
            {
                float now = Time.unscaledTime;
                lock (_lock)
                {
                    float last;
                    if (_lastErrorAt.TryGetValue(tag, out last) && now - last < ErrorThrottleSeconds)
                        return;
                    _lastErrorAt[tag] = now;
                }
                Write("[ERROR:" + tag + "] " + ex.GetType().Name + ": " + ex.Message);
            }
            catch { /* absolute last resort */ }
        }

        public static string NameOf(PlayableCharacter c) { return c != null ? c.Name : "unknown"; }

        public static string NameOf(InteractiveCharacter c)
        {
            var p = c as PlayableCharacter;
            if (p != null) return p.Name;
            return c != null ? c.name : "unknown";
        }

        public static bool IsWorldSimName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < name.Length - 1; i++)
            {
                if (name[i] == '#' && name[i + 1] >= '0' && name[i + 1] <= '9') return true;
            }
            return false;
        }

        public static bool ShouldLog(string name)
        {
            if (DiagToggles.LogWorldSimEntities) return true;
            return !IsWorldSimName(name);
        }
    }

    [HarmonyPatch(typeof(PlayableCharacter), "StartAction",
        new Type[] { typeof(Activator), typeof(CharacterAction) })]
    internal static class Patch_StartAction
    {
        static bool Prepare() { return DiagToggles.Verbose; }   // logging only
        static void Postfix(PlayableCharacter __instance, Activator activator, CharacterAction action)
        {
            if (!DiagToggles.LogActions) return;
            try
            {
                string who = DiagLog.NameOf(__instance);
                if (!DiagLog.ShouldLog(who)) return;
                string actionType = action != null ? action.GetType().Name : "null";
                string activatorName = activator != null ? activator.name : "null";
                DiagLog.Write("[Action] StartAction | character=" + who + " action=" + actionType +
                              " activator=" + activatorName);
            }
            catch (Exception e) { DiagLog.WriteError("StartAction", e); }
        }
    }

    [HarmonyPatch(typeof(PlayableCharacter), "StopCurrentAction", new Type[] { typeof(bool) })]
    internal static class Patch_StopCurrentAction
    {
        static bool Prepare() { return DiagToggles.Verbose; }   // logging only
        static void Prefix(PlayableCharacter __instance, bool ignoreCanBeStoppedFlag)
        {
            if (!DiagToggles.LogActions) return;
            try
            {
                string who = DiagLog.NameOf(__instance);
                if (!DiagLog.ShouldLog(who)) return;
                string actionType = "none";
                try
                {
                    if (__instance != null && __instance.Action != null)
                        actionType = __instance.Action.GetType().Name;
                }
                catch { }
                DiagLog.Write("[Action] StopCurrentAction | character=" + who +
                              " currentAction=" + actionType +
                              " ignoreCanBeStoppedFlag=" + ignoreCanBeStoppedFlag);
            }
            catch (Exception e) { DiagLog.WriteError("StopCurrentAction", e); }
        }
    }

    internal static class ChurnTracker
    {
        private class Started { public int Frame; }
        private static readonly Dictionary<string, Started> _open = new Dictionary<string, Started>();
        private static readonly object _lock = new object();

        private static string Key(string owner, string behaviour) { return owner + "|" + behaviour; }

        public static void NoteStart(string owner, string behaviour)
        {
            if (!OptToggles.MeasureBehaviourChurn) return;
            try
            {
                OptCounters.BehaviourStarts++;
                lock (_lock) { _open[Key(owner, behaviour)] = new Started { Frame = Time.frameCount }; }
            }
            catch { }
        }

        public static bool NoteStop(string owner, string behaviour)
        {
            if (!OptToggles.MeasureBehaviourChurn) return false;
            try
            {
                Started s;
                string k = Key(owner, behaviour);
                lock (_lock)
                {
                    if (!_open.TryGetValue(k, out s)) return false;
                    _open.Remove(k);
                }
                if (s.Frame == Time.frameCount)
                {
                    OptCounters.BehaviourSameFrameStops++;
                    return true;
                }
            }
            catch { }
            return false;
        }
    }

    [HarmonyPatch(typeof(CharacterAI), "StartBehaviour", new Type[] { typeof(AIBehaviour) })]
    internal static class Patch_CharacterAI_StartBehaviour
    {
        static void Postfix(CharacterAI __instance, AIBehaviour behaviour)
        {
            try
            {
                string owner = __instance != null ? __instance.name : "unknown";
                string b = behaviour != null ? behaviour.GetType().Name : "null";
                ChurnTracker.NoteStart(owner, b);
                BehaviourCooldown.NoteBehaviourStarted(owner, b);
                if (!DiagToggles.LogBehaviours || !DiagLog.ShouldLog(owner)) return;
                DiagLog.Write("[Behaviour] StartBehaviour | owner=" + owner + " behaviour=" + b);
            }
            catch (Exception e) { DiagLog.WriteError("StartBehaviour", e); }
        }
    }

    [HarmonyPatch(typeof(CharacterAI), "StopBehaviour", new Type[] { typeof(AIBehaviour) })]
    internal static class Patch_CharacterAI_StopBehaviour
    {
        static void Prefix(CharacterAI __instance, AIBehaviour behaviour)
        {
            try
            {
                string owner = __instance != null ? __instance.name : "unknown";
                string b = behaviour != null ? behaviour.GetType().Name : "null";
                bool sameFrame = ChurnTracker.NoteStop(owner, b);
                if (!DiagToggles.LogBehaviours || !DiagLog.ShouldLog(owner)) return;
                DiagLog.Write("[Behaviour] StopBehaviour | owner=" + owner + " behaviour=" + b +
                              (sameFrame ? " SAMEFRAME" : ""));
            }
            catch (Exception e) { DiagLog.WriteError("StopBehaviour", e); }
        }
    }

    [HarmonyPatch(typeof(AIBehaviour), "OnBehaviourStart")]
    internal static class Patch_AIBehaviour_OnStart
    {
        static bool Prepare() { return DiagToggles.Verbose; }   // logging only
        static void Postfix(AIBehaviour __instance)
        {
            if (!DiagToggles.LogBehaviours) return;
            try
            {
                if (__instance == null) return;
                string owner = __instance.AI != null ? __instance.AI.name : "unknown";
                if (!DiagLog.ShouldLog(owner)) return;
                DiagLog.Write("[Behaviour] OnBehaviourStart | type=" + __instance.GetType().Name + " owner=" + owner);
            }
            catch (Exception e) { DiagLog.WriteError("OnBehaviourStart", e); }
        }
    }

    [HarmonyPatch(typeof(AIBehaviour), "OnBehaviourStop")]
    internal static class Patch_AIBehaviour_OnStop
    {
        static bool Prepare() { return DiagToggles.Verbose; }   // logging only
        static void Postfix(AIBehaviour __instance)
        {
            if (!DiagToggles.LogBehaviours) return;
            try
            {
                if (__instance == null) return;
                string owner = __instance.AI != null ? __instance.AI.name : "unknown";
                if (!DiagLog.ShouldLog(owner)) return;
                DiagLog.Write("[Behaviour] OnBehaviourStop | type=" + __instance.GetType().Name + " owner=" + owner);
            }
            catch (Exception e) { DiagLog.WriteError("OnBehaviourStop", e); }
        }
    }

    [HarmonyPatch(typeof(CharacterAI), "Awake")]
    internal static class Patch_CharacterAI_Awake
    {
        static bool Prepare() { return DiagToggles.Verbose; }   // logging only
        static readonly FieldInfo LogField = AccessTools.Field(typeof(CharacterAI), "log");
        static readonly FieldInfo ActionLogField = AccessTools.Field(typeof(CharacterAI), "actionLog");
        static bool _warnedOnce;

        static void Postfix(CharacterAI __instance)
        {
            if (!DiagToggles.LogGameLog) return;
            try
            {
                if (ActionLogField != null && ActionLogField.GetValue(__instance) == null)
                {
                    try
                    {
                        object instance = System.Activator.CreateInstance(ActionLogField.FieldType);
                        ActionLogField.SetValue(__instance, instance);
                    }
                    catch (Exception ctorEx)
                    {
                        if (!_warnedOnce)
                        {
                            _warnedOnce = true;
                            DiagLog.Write("[GameLog] couldn't construct " + ActionLogField.FieldType.Name +
                                          " (" + ctorEx.GetType().Name + ": " + ctorEx.Message +
                                          ") - leaving CharacterAI.log off to avoid crashing behaviour evaluation");
                        }
                    }
                }

                bool allocated = ActionLogField == null || ActionLogField.GetValue(__instance) != null;
                if (allocated && LogField != null) LogField.SetValue(__instance, true);
            }
            catch (Exception e) { DiagLog.WriteError("CharacterAI.Awake", e); }
        }
    }

    [HarmonyPatch(typeof(CharacterAI), "LogAction", new Type[] { typeof(string) })]
    internal static class Patch_CharacterAI_LogAction
    {
        static bool Prepare() { return DiagToggles.Verbose; }   // logging only
        static void Postfix(CharacterAI __instance, string text)
        {
            if (!DiagToggles.LogGameLog) return;
            try
            {
                string owner = __instance != null ? __instance.name : "unknown";
                if (!DiagLog.ShouldLog(owner)) return;
                DiagLog.Write("[GameLog] " + owner + " | " + text);
            }
            catch (Exception e) { DiagLog.WriteError("LogAction", e); }
        }
    }

    [HarmonyPatch(typeof(NavMeshNavigator), "GoTo", new Type[] { typeof(Vector3), typeof(float), typeof(bool) })]
    internal static class Patch_Navigator_GoTo
    {
        static bool Prepare() { return DiagToggles.Verbose; }   // logging only
        static void Postfix(NavMeshNavigator __instance, Vector3 navSpacePosition, float stopDistance, bool forceRun)
        {
            if (!DiagToggles.LogMovement) return;
            try
            {
                bool throttled = GoToLogState.LastCallThrottled;
                string who = __instance != null ? DiagLog.NameOf(__instance.Character) : "unknown";
                if (!DiagLog.ShouldLog(who)) return;
                string area = (__instance != null && __instance.Area != null) ? __instance.Area.name : "?";
                DiagLog.Write("[Movement] GoTo | character=" + who + " target=" + navSpacePosition +
                              " stopDist=" + stopDistance + " forceRun=" + forceRun + " area=" + area +
                              " throttled=" + throttled);
            }
            catch (Exception e) { DiagLog.WriteError("Navigator.GoTo", e); }
        }
    }

    [HarmonyPatch(typeof(NavMeshNavigator), "Stop")]
    internal static class Patch_Navigator_Stop
    {
        static bool Prepare() { return DiagToggles.Verbose; }   // logging only
        static void Prefix(NavMeshNavigator __instance)
        {
            if (!DiagToggles.LogMovement) return;
            try
            {
                string who = __instance != null ? DiagLog.NameOf(__instance.Character) : "unknown";
                if (!DiagLog.ShouldLog(who)) return;
                DiagLog.Write("[Movement] Stop | character=" + who);
            }
            catch (Exception e) { DiagLog.WriteError("Navigator.Stop", e); }
        }
    }

    [HarmonyPatch(typeof(NavMeshNavigator), "StartWaitAt", new Type[] { typeof(Vector3) })]
    internal static class Patch_Navigator_StartWaitAtPos
    {
        static bool Prepare() { return DiagToggles.Verbose; }   // logging only
        static void Postfix(NavMeshNavigator __instance, Vector3 targetPosition)
        {
            if (!DiagToggles.LogMovement) return;
            try
            {
                string who = __instance != null ? DiagLog.NameOf(__instance.Character) : "unknown";
                if (!DiagLog.ShouldLog(who)) return;
                DiagLog.Write("[Movement] StartWaitAt(pos) | character=" + who + " pos=" + targetPosition);
            }
            catch (Exception e) { DiagLog.WriteError("Navigator.StartWaitAt", e); }
        }
    }

    [HarmonyPatch(typeof(NavMeshNavigator), "StopWait")]
    internal static class Patch_Navigator_StopWait
    {
        static bool Prepare() { return DiagToggles.Verbose; }   // logging only
        static void Prefix(NavMeshNavigator __instance)
        {
            if (!DiagToggles.LogMovement) return;
            try
            {
                string who = __instance != null ? DiagLog.NameOf(__instance.Character) : "unknown";
                if (!DiagLog.ShouldLog(who)) return;
                DiagLog.Write("[Movement] StopWait | character=" + who);
            }
            catch (Exception e) { DiagLog.WriteError("Navigator.StopWait", e); }
        }
    }

    [HarmonyPatch(typeof(Animator), "SetTrigger", new Type[] { typeof(string) })]
    internal static class Patch_SetTrigger
    {
        static bool Prepare() { return DiagToggles.LogAnimation; }   // v1.0.0: patches EVERY Animator; not installed while off
        static void Prefix(Animator __instance, string name)
        {
            if (!DiagToggles.LogAnimation) return;
            try
            {
                string who = __instance != null ? __instance.name : "unknown";
                if (!DiagLog.ShouldLog(who)) return;
                DiagLog.Write("[Animation] SetTrigger | object=" + who + " trigger=" + name);
            }
            catch (Exception e) { DiagLog.WriteError("Animator.SetTrigger", e); }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/CrewAi/CrewAIDiscovery.cs
// ====================================================================================================
// Crew-KI (Teil des UBOAT Toolkit): CrewAIDiscovery. Schalter: F10 > QoL > "Crew-KI".

namespace UboatToolkit.Modules.CrewAi
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Text;
    using global::UnityEngine;
    using global::HarmonyLib;
    using global::UBOAT.Game;
    using global::UBOAT.Game.Core.AI;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Actions;

    internal static class Discovery
    {
        private static readonly HashSet<string> _seenBehaviours = new HashSet<string>();
        private static readonly HashSet<string> _seenActions = new HashSet<string>();

        private static readonly Dictionary<string, int> _behaviourCounts = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> _actionCounts = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> _charEvents = new Dictionary<string, int>();

        private static long _crewBehaviourEvents, _worldBehaviourEvents;
        private static readonly HashSet<string> _crewNames = new HashSet<string>();
        private static readonly HashSet<string> _worldNames = new HashSet<string>();

        private static readonly object _lock = new object();

        private static float _lastSummary;
        private const float SummaryInterval = 180f;

        private static float _lastCharSnapshot;
        private static int _snapshotCursor;
        private const int SnapshotBatch = 4; // characters per pass - keeps it O(1), not O(N)

        public static void NoteBehaviour(AIBehaviour b, CharacterAI ai)
        {
            if (b == null) return;
            try
            {
                string type = b.GetType().Name;
                string owner = ai != null ? ai.name : null;
                bool isWorld = DiagLog.IsWorldSimName(owner);
                bool isNew;
                lock (_lock)
                {
                    isNew = _seenBehaviours.Add(type);
                    if (isWorld)
                    {
                        _worldBehaviourEvents++;
                        if (owner != null) _worldNames.Add(owner);
                    }
                    else
                    {
                        _crewBehaviourEvents++;
                        if (owner != null) _crewNames.Add(owner);
                        int n; _behaviourCounts.TryGetValue(type, out n); _behaviourCounts[type] = n + 1;
                    }
                }

                if (isNew && OptToggles.DiscoverBehaviourTypes)
                {
                    string flags = "?";
                    string ns = "?";
                    try { flags = b.Flags.ToString(); } catch { }
                    try { ns = b.GetType().Namespace ?? "?"; } catch { }
                    DiagLog.Write($"[Discover:Behaviour] first sighting '{type}' ns={ns} flags={flags}");
                }

                if (OptToggles.DiscoverBehaviourProfit && ai != null)
                {
                    if (UnityEngine.Random.value < 0.02f)
                    {
                        string profit = "?", count = "?", attention = "?";
                        try { profit = ai.LowestActiveProfit.ToString("F3"); } catch { }
                        try { count = ai.BehavioursCount.ToString(); } catch { }
                        try { attention = ai.Attention.ToString("F2"); } catch { }
                        DiagLog.Write($"[Discover:Profit] {SafeName(ai)} started '{type}' | " +
                                      $"behavioursCount={count} lowestActiveProfit={profit} attention={attention}");
                    }
                }
            }
            catch (Exception e) { DiagLog.WriteError("Discover.NoteBehaviour", e); }
        }

        public static void NoteAction(CharacterAction a, PlayableCharacter c)
        {
            if (a == null) return;
            try
            {
                string type = a.GetType().Name;
                bool isNew;
                lock (_lock)
                {
                    isNew = _seenActions.Add(type);
                    int n; _actionCounts.TryGetValue(type, out n); _actionCounts[type] = n + 1;
                    if (c != null)
                    {
                        string who = SafeCharName(c);
                        int m; _charEvents.TryGetValue(who, out m); _charEvents[who] = m + 1;
                    }
                }

                if (isNew && OptToggles.DiscoverActionTypes)
                {
                    DiagLog.Write($"[Discover:Action] first sighting '{type}' | {DescribeAction(a)}");
                }
            }
            catch (Exception e) { DiagLog.WriteError("Discover.NoteAction", e); }
        }

        private static string DescribeAction(CharacterAction a)
        {
            var sb = new StringBuilder();
            try { sb.Append("hardReq=[").Append(Join(a.HardRequirements)).Append("] "); } catch { sb.Append("hardReq=? "); }
            try { sb.Append("softReq=[").Append(Join(a.SoftRequirements)).Append("] "); } catch { sb.Append("softReq=? "); }
            try { sb.Append("queuePriority=").Append(a.QueuePriority.ToString("F2")).Append(' '); } catch { }
            try { sb.Append("interruptable=").Append(a.IsInterruptable).Append(' '); } catch { }
            try { sb.Append("lowPriority=").Append(a.IsLowPriorityAction).Append(' '); } catch { }
            try { sb.Append("playerOrder=").Append(a.IsDirectPlayerOrder).Append(' '); } catch { }
            try { sb.Append("forced=").Append(a.IsForcedAction).Append(' '); } catch { }
            try { sb.Append("canBeStopped=").Append(a.CanBeStopped).Append(' '); } catch { }
            try { sb.Append("charLimit=").Append(a.CharactersLimit).Append(' '); } catch { }
            try { sb.Append("silent=").Append(a.Silent).Append(' '); } catch { }
            try { sb.Append("delegable=").Append(a.AllowDelegationToOtherCharacters).Append(' '); } catch { }
            try { var r = a.Role; sb.Append("role=").Append(r != null ? r.ToString() : "null"); } catch { }
            return sb.ToString().TrimEnd();
        }

        private static string Join(string[] arr)
        {
            if (arr == null || arr.Length == 0) return "";
            return string.Join(", ", arr);
        }

        public static void MaybeSnapshotCharacters()
        {
            if (!OptToggles.DiscoverCharacterState) return;
            try
            {
                float now = Time.realtimeSinceStartup;
                if (now - _lastCharSnapshot < OptToggles.CharacterStateInterval) return;
                _lastCharSnapshot = now;

                PlayableCharacter[] all = UnityEngine.Object.FindObjectsOfType<PlayableCharacter>();
                if (all == null || all.Length == 0) return;

                DiagLog.Write($"[Discover:Character] roster size={all.Length} (sampling {SnapshotBatch})");
                for (int i = 0; i < SnapshotBatch && i < all.Length; i++)
                {
                    var c = all[(_snapshotCursor + i) % all.Length];
                    DiagLog.Write("[Discover:Character] " + DescribeCharacter(c));
                }
                _snapshotCursor = (_snapshotCursor + SnapshotBatch) % all.Length;
            }
            catch (Exception e) { DiagLog.WriteError("Discover.Snapshot", e); }
        }

        private static string DescribeCharacter(PlayableCharacter c)
        {
            if (c == null) return "null";
            var sb = new StringBuilder();
            try { sb.Append(SafeCharName(c)).Append(" | "); } catch { }
            try { var r = c.Role; sb.Append("role=").Append(r != null ? r.ToString() : "null").Append(' '); } catch { }
            try { sb.Append("officer=").Append(c.IsOfficer).Append(' '); } catch { }
            try { sb.Append("skipper=").Append(c.IsSkipper).Append(' '); } catch { }
            try { sb.Append("onWatch=").Append(c.IsOnWatch).Append(' '); } catch { }
            try { sb.Append("energy=").Append(c.Energy.ToString("F2")).Append(' '); } catch { }
            try { sb.Append("needs=").Append(c.NeedsLevel.ToString("F2")).Append(' '); } catch { }
            try { sb.Append("tired=").Append(c.IsTired).Append(' '); } catch { }
            try { sb.Append("health=").Append(c.Health.ToString("F2")).Append(' '); } catch { }
            try { sb.Append("trauma=").Append(c.Trauma.ToString("F2")).Append(' '); } catch { }
            try { var cm = c.CurrentCompartment; sb.Append("compartment=").Append(cm != null ? cm.name : "null").Append(' '); } catch { }
            try { var a = c.Action; sb.Append("action=").Append(a != null ? a.GetType().Name : "none").Append(' '); } catch { }
            try { sb.Append("floor=").Append(FloorTypeOf(c)); } catch { }
            return sb.ToString().TrimEnd();
        }

        private static readonly System.Reflection.PropertyInfo FloorTypeProp =
            HarmonyLib.AccessTools.Property(typeof(PlayableCharacter), "FloorType");

        private static string FloorTypeOf(PlayableCharacter c)
        {
            try
            {
                if (FloorTypeProp == null) return "?";
                object v = FloorTypeProp.GetValue(c, null);
                return v != null ? v.ToString() : "?";
            }
            catch { return "?"; }
        }

        public static void MaybeSummary()
        {
            try
            {
                float now = Time.realtimeSinceStartup;
                if (now - _lastSummary < SummaryInterval) return;
                _lastSummary = now;

                lock (_lock)
                {
                    DiagLog.Write("[Discover:Summary] crew=" + _crewNames.Count + " entities (" +
                                  _crewBehaviourEvents + " behaviour events) | worldSim=" +
                                  _worldNames.Count + " entities (" + _worldBehaviourEvents + " events)");
                    DiagLog.Write("[Discover:Summary] crew behaviours: " + TopOf(_behaviourCounts, 10));
                    DiagLog.Write("[Discover:Summary] actions:    " + TopOf(_actionCounts, 10));
                    DiagLog.Write("[Discover:Summary] busiest characters: " + TopOf(_charEvents, 6));
                }
            }
            catch (Exception e) { DiagLog.WriteError("Discover.Summary", e); }
        }

        private static string TopOf(Dictionary<string, int> d, int n)
        {
            try
            {
                var list = new List<KeyValuePair<string, int>>(d);
                list.Sort((x, y) => y.Value.CompareTo(x.Value));
                var sb = new StringBuilder();
                for (int i = 0; i < n && i < list.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(list[i].Key).Append('=').Append(list[i].Value);
                }
                if (list.Count == 0) sb.Append("(none)");
                return sb.ToString();
            }
            catch { return "(error)"; }
        }

        public static string SafeName(UnityEngine.Object o)
        {
            try { return o != null ? o.name : "null"; } catch { return "?"; }
        }

        public static string SafeCharName(PlayableCharacter c)
        {
            try { return c != null ? c.Name : "null"; } catch { return "?"; }
        }
    }

    [HarmonyPatch(typeof(CharacterAI), "StartBehaviour", new Type[] { typeof(AIBehaviour) })]
    internal static class Patch_Discover_StartBehaviour
    {
        static bool Prepare() { return DiagToggles.Verbose; }   // v1.0.0: discovery is a dev aid
        static void Postfix(CharacterAI __instance, AIBehaviour behaviour)
        {
            try
            {
                Discovery.NoteBehaviour(behaviour, __instance);
                Discovery.MaybeSnapshotCharacters();
                Discovery.MaybeSummary();
            }
            catch (Exception e) { DiagLog.WriteError("Discover.StartBehaviourHook", e); }
        }
    }

    [HarmonyPatch(typeof(PlayableCharacter), "StartAction",
        new Type[] { typeof(UBOAT.Game.Scene.Characters.Activators.Activator), typeof(CharacterAction) })]
    internal static class Patch_Discover_StartAction
    {
        static bool Prepare() { return DiagToggles.Verbose; }   // v1.0.0: discovery is a dev aid
        static void Postfix(PlayableCharacter __instance, CharacterAction action)
        {
            try { Discovery.NoteAction(action, __instance); }
            catch (Exception e) { DiagLog.WriteError("Discover.StartActionHook", e); }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/CrewAi/CrewAIHatchDiscipline.cs
// ====================================================================================================
// Crew-KI (Teil des UBOAT Toolkit): CrewAIHatchDiscipline. Schalter: F10 > QoL > "Crew-KI".

namespace UboatToolkit.Modules.CrewAi
{
    using global::System;
    using global::System.Collections;
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UBOAT.Game.Scene.Items;

    internal static class HatchToggles
    {
        public static bool KeepCommandersHatchOrder => UboatToolkit.Settings.B("CrewHatchOrders");

        public static readonly bool LogPreservation = true;

        public static readonly bool ExcludeExteriorHatches = false;
    }

    [HarmonyPatch(typeof(HatchOpenCloseAction), "OnDeviceUsed")]
    internal static class Patch_Hatch_OnDeviceUsed
    {
        private static readonly HashSet<int> _logged = new HashSet<int>();
        private static readonly object _lock = new object();

        private static Hatch Resolve(HatchOpenCloseAction action)
        {
            try
            {
                var act = action != null ? action.Activator : null;
                return act != null ? act.GetComponent<Hatch>() : null;
            }
            catch { return null; }
        }

        static void Prefix(HatchOpenCloseAction __instance, out bool[] __state)
        {
            __state = null;
            if (!HatchToggles.KeepCommandersHatchOrder) return;
            try
            {
                var h = Resolve(__instance);
                if (h == null) return;
                __state = new[] { h.Locked, h.IsPreferredToBeLocked };
            }
            catch (Exception e) { DiagLog.WriteError("Hatch.Prefix", e); }
        }

        static void Postfix(HatchOpenCloseAction __instance, bool[] __state)
        {
            if (!HatchToggles.KeepCommandersHatchOrder || __state == null) return;
            try
            {
                var h = Resolve(__instance);
                if (h == null) return;

                bool wasLocked = __state[0];
                bool orderWasKeepShut = __state[1];

                if (!wasLocked || h.Locked || !orderWasKeepShut) return;

                h.IsPreferredToBeLocked = true;
                OptCounters.HatchOrdersPreserved++;

                if (HatchToggles.LogPreservation)
                {
                    int id = h.GetInstanceID();
                    bool first;
                    lock (_lock) { first = _logged.Add(id); }
                    if (first)
                    {
                        DiagLog.Write("[Hatch] preserved commander's keep-shut order on '" +
                                      SafeName(h) + "' after crew opened it - " +
                                      "HatchCloseController will shut it once clear");
                    }
                }
            }
            catch (Exception e) { DiagLog.WriteError("Hatch.Postfix", e); }
        }

        private static string SafeName(UnityEngine.Object o)
        {
            try { return o != null ? o.name : "null"; } catch { return "?"; }
        }
    }

    [HarmonyPatch]
    internal static class Patch_Hatch_CloseAllOrder
    {
        static MethodBase TargetMethod()
        {
            var t = UboatToolkit.Util.FindType("UBOAT.Game.UI.CharacterSwitchUI.CharacterSwitchCloseHatchesUI");
            return t == null ? null : AccessTools.Method(t, "OnClick");
        }

        static bool Prepare() { return TargetMethod() != null; }

        static void Postfix(object __instance)
        {
            if (!HatchToggles.KeepCommandersHatchOrder) return;
            try
            {
                var t = __instance.GetType();
                var closedField = AccessTools.Field(t, "areHatchesClosed");
                var hatchesField = AccessTools.Field(t, "hatches");
                if (closedField == null || hatchesField == null) return;

                if (!(bool)closedField.GetValue(__instance)) return;

                var list = hatchesField.GetValue(__instance) as IEnumerable;
                if (list == null) return;

                int applied = 0;
                foreach (var o in list)
                {
                    var h = o as Hatch;
                    if (h == null) continue;
                    if (HatchToggles.ExcludeExteriorHatches)
                    {
                        bool ext = false;
                        try { ext = h.IsExteriorHatch; } catch { }
                        if (ext) continue;
                    }
                    if (h.IsPreferredToBeLocked) continue;   // setter early-outs anyway
                    h.IsPreferredToBeLocked = true;
                    applied++;
                }

                if (applied > 0)
                {
                    OptCounters.HatchOrdersApplied += applied;
                    DiagLog.Write("[Hatch] close-all order: set keep-shut on " + applied +
                                  " hatch(es) that vanilla left without one " +
                                  "(it only marks already-locked hatches)");
                }
            }
            catch (Exception e) { DiagLog.WriteError("Hatch.CloseAllOrder", e); }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/CrewAi/CrewAILoitering.cs
// ====================================================================================================
// Crew-KI (Teil des UBOAT Toolkit): CrewAILoitering. Schalter: F10 > QoL > "Crew-KI".

namespace UboatToolkit.Modules.CrewAi
{
    using global::System;
    using global::System.Collections.Generic;
    using global::UnityEngine;
    using global::HarmonyLib;
    using global::UBOAT.Game.Core.AI.Behaviours;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Items;

    internal static class LoiterToggles
    {
        public static bool Enabled => UboatToolkit.Settings.B("CrewControlRoom");

        public static readonly bool EnforceIdleTravelDistance = false;
        public const float MaxIdleTravelMeters = 12f;

        public static readonly string[] RestrictedCompartments = { "Control" };

        public static readonly bool AllowShuffleIfAlreadyInside = true;

        public static readonly bool LogRejections = true;
    }

    internal static class LoiterFilter
    {
        private static readonly Vector3 NoDestination =
            new Vector3(float.NaN, float.NaN, float.NaN);

        private static Compartment[] _compartments;
        private static float _cacheStamp;
        private const float CacheSeconds = 60f;   // unscaled

        public static bool IsNoDestination(Vector3 v) { return float.IsNaN(v.x); }
        public static Vector3 Reject() { return NoDestination; }

        private static Compartment[] Compartments()
        {
            try
            {
                float now = Time.unscaledTime;
                if (_compartments == null || now - _cacheStamp > CacheSeconds)
                {
                    _compartments = UnityEngine.Object.FindObjectsOfType<Compartment>();
                    _cacheStamp = now;
                }
            }
            catch { }
            return _compartments;
        }

        private static Compartment CompartmentAt(Vector3 point)
        {
            var all = Compartments();
            if (all == null) return null;
            for (int i = 0; i < all.Length; i++)
            {
                try
                {
                    if (all[i] != null && all[i].Contains(point)) return all[i];
                }
                catch { }
            }
            return null;
        }

        private static bool IsRestricted(Compartment c)
        {
            if (c == null) return false;
            string n;
            try { n = c.name; } catch { return false; }
            if (string.IsNullOrEmpty(n)) return false;
            var list = LoiterToggles.RestrictedCompartments;
            for (int i = 0; i < list.Length; i++)
            {
                if (n.IndexOf(list[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        public static bool ShouldReject(PlayableCharacter character, Vector3 dest,
                                        bool enforceDistance, out string why)
        {
            why = null;
            if (!LoiterToggles.Enabled) return false;
            if (character == null) return false;
            if (IsNoDestination(dest)) return false;   // already "none"

            long t0 = Perf.Begin();
            try
            {
                Vector3 from = character.transform.position;

                if (enforceDistance && LoiterToggles.EnforceIdleTravelDistance)
                {
                    float d = Vector3.Distance(from, dest);
                    if (d > LoiterToggles.MaxIdleTravelMeters)
                    {
                        why = "distance " + d.ToString("F1") + "m > " +
                              LoiterToggles.MaxIdleTravelMeters + "m";
                        return true;
                    }
                }

                var destComp = CompartmentAt(dest);
                if (IsRestricted(destComp))
                {
                    Compartment cur = null;
                    try { cur = character.CurrentCompartment; } catch { }

                    bool alreadyInside = LoiterToggles.AllowShuffleIfAlreadyInside &&
                                         cur != null && destComp != null && cur == destComp;
                    if (!alreadyInside)
                    {
                        why = "restricted compartment '" +
                              (destComp != null ? destComp.name : "?") + "'";
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                DiagLog.WriteError("Loiter.ShouldReject", e);
                return false; // fail open
            }
            finally { Perf.End("Loiter.ShouldReject", t0); }
            return false;
        }

        private const float LogIntervalPerManSeconds = 60f;
        private const int MaxRejectionLines = 300;
        private static readonly Dictionary<int, float> _lastLoggedFor = new Dictionary<int, float>();
        private static readonly Dictionary<string, int> _tallies = new Dictionary<string, int>();
        private static readonly object _tallyLock = new object();
        private static int _rejectionLines;

        public static void NoteRejection(PlayableCharacter c, string source, string why)
        {
            if (!LoiterToggles.LogRejections) return;
            try
            {
                OptCounters.LoiterDestinationsRejected++;

                string where = "?";
                int head = -1;
                try
                {
                    var cur = c != null ? c.CurrentCompartment : null;
                    if (cur != null) { where = cur.name; head = cur.Characters != null ? cur.Characters.Count : -1; }
                    else where = "(outside)";
                }
                catch { }

                string key = where + " -> " + (why ?? "?");
                lock (_tallyLock)
                {
                    int n0;
                    _tallies.TryGetValue(key, out n0);
                    _tallies[key] = n0 + 1;
                }

                if (_rejectionLines >= MaxRejectionLines || c == null) return;
                int id = c.GetInstanceID();
                float now = Time.unscaledTime;
                float last;
                lock (_tallyLock)
                {
                    if (_lastLoggedFor.TryGetValue(id, out last) && now - last < LogIntervalPerManSeconds) return;
                    _lastLoggedFor[id] = now;
                }
                _rejectionLines++;
                string n = "unknown";
                try { n = c.Name; } catch { }
                DiagLog.Write("[Loiter] refused " + source + " for " + n + " standing in '" + where + "' (" +
                              head + " present) - " + why);
            }
            catch { }
        }

        public static string DrainTallies()
        {
            lock (_tallyLock)
            {
                if (_tallies.Count == 0) return null;
                var list = new List<KeyValuePair<string, int>>(_tallies);
                _tallies.Clear();
                list.Sort((a, b) => b.Value.CompareTo(a.Value));
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < list.Count && i < 10; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(list[i].Key).Append(" x").Append(list[i].Value);
                }
                return sb.ToString();
            }
        }
    }

    [HarmonyPatch(typeof(FreeSpaceBehaviour), "FindFreeIdleSlot")]
    internal static class Patch_Loiter_FindFreeIdleSlot
    {
        static readonly System.Reflection.FieldInfo CharacterField =
            AccessTools.Field(typeof(FreeSpaceBehaviour), "character");

        static void Postfix(FreeSpaceBehaviour __instance, ref Vector3 __result)
        {
            if (!LoiterToggles.Enabled) return;
            try
            {
                if (CharacterField == null) return;
                var c = CharacterField.GetValue(__instance) as PlayableCharacter;
                string why;
                if (LoiterFilter.ShouldReject(c, __result, true, out why))
                {
                    LoiterFilter.NoteRejection(c, "idle slot", why);
                    __result = LoiterFilter.Reject();
                }
            }
            catch (Exception e) { DiagLog.WriteError("Loiter.FindFreeIdleSlot", e); }
        }
    }

    [HarmonyPatch(typeof(FreeSpaceBehaviour), "FindFreeSpaceNearby")]
    internal static class Patch_Loiter_FindFreeSpaceNearby
    {
        static readonly System.Reflection.FieldInfo CharacterField =
            AccessTools.Field(typeof(FreeSpaceBehaviour), "character");

        static void Postfix(FreeSpaceBehaviour __instance, ref Vector3 __result)
        {
            if (!LoiterToggles.Enabled) return;
            try
            {
                if (CharacterField == null) return;
                var c = CharacterField.GetValue(__instance) as PlayableCharacter;
                string why;
                if (LoiterFilter.ShouldReject(c, __result, false, out why))
                {
                    LoiterFilter.NoteRejection(c, "nearby space", why);
                    __result = LoiterFilter.Reject();
                }
            }
            catch (Exception e) { DiagLog.WriteError("Loiter.FindFreeSpaceNearby", e); }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/CrewAi/CrewAIMigration.cs
// ====================================================================================================
// Crew-KI (Teil des UBOAT Toolkit): einmalige Übernahme alter Werte.
// Wer die Crew-KI vorher als einzelnen Mod oder im großen Patch benutzt hat, behält seine Werte:
// beim ersten Start mit dem Toolkit werden sie aus der alten Datei in die UboatToolkit.ini übernommen.
// Danach nie wieder (Merker-Datei Crew-KI-uebernommen.txt im Toolkit-Ordner).

namespace UboatToolkit.Modules.CrewAi
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Globalization;
    using global::System.IO;
    using global::System.Text;
    using global::UnityEngine;

    internal static class CrewAIMigration
    {
        internal const string Prefix = "CrewAIOverhaul.";
        internal const string OldPrefix = "Crew_AI_Overhaul.";
        internal const string OwnFileName = "CrewAIOverhaul.cfg";
        internal const string PatchFileName = "DustysCommunityPatch.cfg";
        internal const string MarkerName = "Crew-KI-uebernommen.txt";

        // Newest source wins: the patch's own key, then its "Other Mods" key, then the standalone file.
        internal static Dictionary<string, string> Plan(IDictionary<string, string> patchCfg,
                                                       IDictionary<string, string> ownCfg,
                                                       IEnumerable<SettingRow> rows)
        {
            var plan = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (SettingRow r in rows)
            {
                string v;
                if ((patchCfg != null && patchCfg.TryGetValue(Prefix + r.Key, out v)) ||
                    (patchCfg != null && patchCfg.TryGetValue(OldPrefix + r.Key, out v)) ||
                    (ownCfg != null && ownCfg.TryGetValue(r.Key, out v)))
                    plan[r.ToolkitKey] = Normalise(v);
            }
            return plan;
        }

        // "Key: value" and "Key=value" lines; '#' and ';' start a comment line.
        internal static Dictionary<string, string> ParseFile(IEnumerable<string> lines)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in lines)
            {
                string line = raw == null ? "" : raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';' || line[0] == '[') continue;
                int colon = line.IndexOf(':');
                int eq = line.IndexOf('=');
                int sep = colon <= 0 ? eq : (eq <= 0 ? colon : Math.Min(colon, eq));
                if (sep <= 0) continue;
                d[line.Substring(0, sep).Trim()] = line.Substring(sep + 1).Trim();
            }
            return d;
        }

        private static string Normalise(string v)
        {
            string s = (v ?? "").Trim().ToLowerInvariant();
            if (s == "true" || s == "on" || s == "yes" || s == "1") return "true";
            if (s == "false" || s == "off" || s == "no" || s == "0") return "false";
            return (v ?? "").Trim();
        }

        private static Dictionary<string, string> ReadFirst(string name)
        {
            string root = Application.persistentDataPath;
            string[] places = { Path.Combine(root, name), Path.Combine(Path.Combine(root, "ModData"), name),
                                Path.Combine(Path.Combine(root, "Mods"), name) };
            foreach (string p in places)
                if (File.Exists(p)) return ParseFile(File.ReadAllLines(p, Encoding.UTF8));
            return null;
        }

        internal static string RunOnce(IList<SettingRow> rows)
        {
            string marker = Path.Combine(UboatToolkit.Settings.Folder, MarkerName);
            if (File.Exists(marker)) return "";
            try
            {
                Dictionary<string, string> patch = ReadFirst(PatchFileName);
                Dictionary<string, string> own = ReadFirst(OwnFileName);
                Dictionary<string, string> plan = Plan(patch, own, rows);
                int applied = 0;
                foreach (KeyValuePair<string, string> kv in plan)
                {
                    var o = UboatToolkit.Settings.Get(kv.Key);
                    if (o == null) continue;
                    if (o.IsBool) { if (kv.Value == "true") o.Value = 1; else if (kv.Value == "false") o.Value = 0; else continue; }
                    else
                    {
                        float f;
                        if (!float.TryParse(kv.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out f)) continue;
                        o.Value = Mathf.Clamp(f, o.Min, o.Max);
                    }
                    applied++;
                }
                if (applied > 0) UboatToolkit.Settings.Save();
                Directory.CreateDirectory(UboatToolkit.Settings.Folder);
                File.WriteAllText(marker, "Alte Crew-KI-Werte übernommen: " + applied + " (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ")\n");
                return applied + " alte Wert(e) übernommen (" + (patch == null ? "keine " + PatchFileName : patch.Count + " Zeilen in " + PatchFileName) + ", " +
                       (own == null ? "keine " + OwnFileName : own.Count + " Zeilen in " + OwnFileName) + "); ";
            }
            catch (Exception e)
            {
                return "Übernahme fehlgeschlagen (" + e.GetType().Name + ": " + e.Message + "), es gelten die INI-Werte; ";
            }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/CrewAi/CrewAIOptimizations.cs
// ====================================================================================================
// Crew-KI (Teil des UBOAT Toolkit): CrewAIOptimizations. Schalter: F10 > QoL > "Crew-KI".

namespace UboatToolkit.Modules.CrewAi
{
    using global::System;
    using global::System.Collections.Generic;
    using global::UnityEngine;
    using global::UnityEngine.AI;
    using global::HarmonyLib;
    using global::UBOAT.Game;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Interactions;
    using global::UBOAT.Game.Scene.Characters.Navigation;

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
        public static long GoToTotal, GoToThrottled, GoToNeeded;
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
                    "[Counters] GoTo " + GoToThrottled + "/" + GoToTotal + " throttled (" + Pct(GoToThrottled, GoToTotal) + "), " + GoToNeeded + " repeats let through because no usable path | " +
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
                    // 0.4.17 (measured 2026-11-18: 341 x "Path to target is invalid" in 10 minutes, stations stay empty).
                    // The game's GoToBehaviour repeats GoTo only when the agent has NO usable path (none, stopped or
                    // stale) and counts every repeat; after 4 repeats without a path it gives the target up as
                    // unreachable. Swallowing those repeats here made it give up without ever trying again.
                    // Now a repeat is only swallowed when it is really redundant: a path is being calculated or a
                    // good path is already there.
                    bool redundant = false;
                    try
                    {
                        var ag = nav.Agent;
                        redundant = ag != null && ag.isOnNavMesh && (ag.pathPending || (ag.hasPath && !ag.isStopped && !ag.isPathStale));
                    }
                    catch { redundant = false; }
                    if (redundant) return true;
                    OptCounters.GoToNeeded++;
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
                // 0.4.24: what the navmesh itself said (to tell "target cut off" from "man off the mesh")
                string navInfo = "";
                try
                {
                    var nf = AccessTools.Field(__instance.GetType(), "navigator");
                    var tf = AccessTools.Field(__instance.GetType(), "target");
                    var nv = nf != null ? nf.GetValue(__instance) as NavMeshNavigator : null;
                    var ag = nv != null ? nv.Agent : null;
                    if (ag != null && tf != null)
                    {
                        var tgt = (Vector3)tf.GetValue(__instance);
                        navInfo = " | nav: mode=" + nv.AgentMode + " onMesh=" + ag.isOnNavMesh + " hasPath=" + ag.hasPath + " status=" + ag.pathStatus
                                + " pathEndToTarget=" + (ag.hasPath ? (ag.pathEndPosition - tgt).magnitude.ToString("0.00") : "?")
                                + " manToTarget=" + (ag.nextPosition - tgt).magnitude.ToString("0.0")
                                + " slotUsers=" + (slot != null && slot.Interactable != null ? slot.Interactable.InteractingCharacters.Count.ToString() : "?");
                    }
                }
                catch (Exception ne) { navInfo = " | nav: Fehler " + ne.GetType().Name; }
                DiagLog.Write("[CancelProbe] " + who + " | attempts=" + attempts + " wasPathValid=" + wasValid +
                              " pathSet=" + pathSet + " unreachableAction=" + unreachableStr +
                              " | target=" + targetDesc + " | slot=" + slotName + " | " + why + navInfo);
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

// ====================================================================================================
// Abschnitt: Modules/CrewAi/CrewAIOrderQueueRoom.cs
// ====================================================================================================
// Crew-KI (Teil des UBOAT Toolkit): CrewAIOrderQueueRoom. Schalter: F10 > QoL > "Crew-KI".

namespace UboatToolkit.Modules.CrewAi
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::UnityEngine;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Actions;

    internal static class QueueRoomToggles
    {
        public static readonly bool ReportRefusals = true;

        public static bool MakeRoomForPlayerOrders => UboatToolkit.Settings.B("CrewOrderRoom");

        public const int PerCharacterLimit = 6;
        public const int GlobalLimit = 9;

        public const int MaxRefusalLines = 60;
        public const int MaxEvictionLines = 40;
    }

    internal static class QueueRoom
    {
        private static int _refusalLines, _evictionLines;

        private static readonly PropertyInfo StageProp =
            AccessTools.Property(typeof(CharacterAction), "Stage");

        private static int Stage(CharacterAction a)
        {
            try
            {
                if (StageProp == null || a == null) return -1;
                object v = StageProp.GetValue(a, null);
                return v == null ? -1 : Convert.ToInt32(v);
            }
            catch { return -1; }
        }

        private static bool IsPlayerOrder(CharacterAction a)
        {
            try { return a != null && (a.IsDirectPlayerOrder || a.IsForcedAction); }
            catch { return false; }
        }

        private static string Describe(CharacterAction a)
        {
            try { return a != null ? a.GetType().Name : "?"; } catch { return "?"; }
        }

        private static string ActivatorOf(CharacterAction a)
        {
            try { return a != null && a.Activator != null ? a.Activator.name : "none"; }
            catch { return "?"; }
        }

        private static string OwnerOf(ActionQueue q)
        {
            try { return q != null && q.Owner != null ? q.Owner.Name : "(global)"; }
            catch { return "?"; }
        }

        private static int LimitFor(ActionQueue q)
        {
            try
            {
                return (q != null && q.Owner != null)
                    ? QueueRoomToggles.PerCharacterLimit
                    : QueueRoomToggles.GlobalLimit;
            }
            catch { return QueueRoomToggles.GlobalLimit; }
        }

        public static void BeforeAddPreStarted(ActionQueue queue, CharacterAction action)
        {
            if (action == null || queue == null) return;
            try
            {
                List<CharacterAction> actions = null;
                try { actions = queue.Actions; } catch { }
                if (actions == null) return;

                int limit = LimitFor(queue);
                if (actions.Count <= limit) return;      // vanilla will accept it

                bool player = IsPlayerOrder(action);

                if (QueueRoomToggles.ReportRefusals && _refusalLines < QueueRoomToggles.MaxRefusalLines)
                {
                    _refusalLines++;
                    DiagLog.Write("[Order] REFUSED '" + Describe(action) + "' on '" +
                                  ActivatorOf(action) + "' | queue=" + OwnerOf(queue) +
                                  " depth=" + actions.Count + " cap=" + limit +
                                  " playerOrder=" + player +
                                  " - vanilla ActionQueue.AddPreStarted discards this SILENTLY");
                }
                OptCounters.OrdersRefusedQueueFull++;

                bool direct = false;
                try { direct = action.IsDirectPlayerOrder; } catch { }
                if (direct) OptCounters.PlayerOrdersRefusedQueueFull++;

                if (!QueueRoomToggles.MakeRoomForPlayerOrders || !direct) return;

                CharacterAction victim = null;
                float worst = float.MaxValue;
                for (int i = 0; i < actions.Count; i++)
                {
                    var a = actions[i];
                    if (a == null) continue;
                    if (IsPlayerOrder(a)) continue;          // never sacrifice intent
                    if (Stage(a) >= 2) continue;             // never abort running work
                    bool stoppable;
                    try { stoppable = a.CanBeStopped; } catch { continue; }
                    if (!stoppable) continue;                // vanilla's own Clear() test

                    float p;
                    try { p = a.QueuePriority; } catch { continue; }
                    if (p < worst) { worst = p; victim = a; }
                }

                if (victim == null) return;                  // nothing safe to drop

                queue.Remove(victim, true);
                OptCounters.OrdersMadeRoom++;

                if (_evictionLines < QueueRoomToggles.MaxEvictionLines)
                {
                    _evictionLines++;
                    DiagLog.Write("[Order] made room for player order '" + Describe(action) +
                                  "' on '" + ActivatorOf(action) + "' by evicting '" +
                                  Describe(victim) + "' on '" + ActivatorOf(victim) +
                                  "' (priority " + worst.ToString("F2") +
                                  ", stage " + Stage(victim) + ") from " + OwnerOf(queue));
                }
            }
            catch (Exception e) { DiagLog.WriteError("QueueRoom.BeforeAddPreStarted", e); }
        }
    }

    [HarmonyPatch(typeof(ActionQueue), "AddPreStarted", new Type[] { typeof(CharacterAction) })]
    internal static class Patch_QueueRoom_AddPreStarted
    {
        static void Prefix(ActionQueue __instance, CharacterAction action)
        {
            try { QueueRoom.BeforeAddPreStarted(__instance, action); }
            catch (Exception e) { DiagLog.WriteError("QueueRoom.Prefix", e); }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/CrewAi/CrewAIOrderTracking.cs
// ====================================================================================================
// Crew-KI (Teil des UBOAT Toolkit): CrewAIOrderTracking. Schalter: F10 > QoL > "Crew-KI".

namespace UboatToolkit.Modules.CrewAi
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::UnityEngine;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Actions;

    internal static class OrderToggles
    {
        public static readonly bool Enabled = true;

        public const float StallSeconds = 90f;

        public const int MaxQueuedLines = 60;
        public const int MaxDroppedLines = 60;
        public const int MaxStartedLines = 20;
        public const int MaxStallLines = 30;

        public static readonly bool TrackAiActionsToo = false;
    }

    internal static class OrderTracker
    {
        private sealed class Tracked
        {
            public string What;         // action type
            public string Where;        // activator
            public string Owner;
            public float QueuedAt;      // unscaled
            public bool Started;
            public bool Stalled;
            public bool Player;
        }

        private static readonly Dictionary<CharacterAction, Tracked> _tracked =
            new Dictionary<CharacterAction, Tracked>();
        private static readonly object _lock = new object();

        private static int _queuedLines, _droppedLines, _startedLines, _stallLines;
        private static float _lastSweep;

        private static readonly PropertyInfo StageProp =
            AccessTools.Property(typeof(CharacterAction), "Stage");

        private static int Stage(CharacterAction a)
        {
            try
            {
                if (StageProp == null || a == null) return -1;
                object v = StageProp.GetValue(a, null);
                return v == null ? -1 : Convert.ToInt32(v);
            }
            catch { return -1; }
        }

        private static string StageName(int s)
        {
            switch (s)
            {
                case 0: return "Uninitialized";
                case 1: return "Initialized";
                case 2: return "Running";
                case 3: return "Completed";
                default: return "?";
            }
        }

        private static bool IsPlayerOrder(CharacterAction a)
        {
            try { return a != null && (a.IsDirectPlayerOrder || a.IsForcedAction); }
            catch { return false; }
        }

        private static string Describe(CharacterAction a)
        {
            try { return a != null ? a.GetType().Name : "?"; } catch { return "?"; }
        }

        private static string ActivatorOf(CharacterAction a)
        {
            try { return a != null && a.Activator != null ? a.Activator.name : "none"; }
            catch { return "?"; }
        }

        public static void NoteQueued(ActionQueue queue, CharacterAction action)
        {
            if (!OrderToggles.Enabled || action == null) return;
            try
            {
                bool player = IsPlayerOrder(action);

                float now = Time.unscaledTime;
                string owner = "?";
                int depth = -1;
                try { owner = queue != null && queue.Owner != null ? queue.Owner.Name : "(global)"; } catch { }
                try { var acts = queue != null ? queue.Actions : null; depth = acts != null ? acts.Count : -1; } catch { }

                var t = new Tracked();
                t.What = Describe(action);
                t.Where = ActivatorOf(action);
                t.Owner = owner;
                t.QueuedAt = now;
                t.Player = player;

                bool isNew;
                lock (_lock)
                {
                    isNew = !_tracked.ContainsKey(action);
                    _tracked[action] = t;
                }
                if (!isNew) return;

                if (player) OptCounters.PlayerOrdersSeen++;
                if (player && _queuedLines < OrderToggles.MaxQueuedLines)
                {
                    _queuedLines++;
                    DiagLog.Write("[Order] queued '" + t.What + "' on '" + t.Where +
                                  "' | owner=" + owner + " queueDepth=" + depth +
                                  " stage=" + StageName(Stage(action)) +
                                  " sourceJob=" + SafeJob(action));
                }
            }
            catch (Exception e) { DiagLog.WriteError("Order.NoteQueued", e); }
        }

        private static string SafeJob(CharacterAction a)
        {
            try
            {
                var j = a.SourceJob;
                return j == null ? "null(pinned 100+)" : j.GetType().Name;
            }
            catch { return "?"; }
        }

        public static void NoteRemoved(ActionQueue queue, CharacterAction action, string via)
        {
            if (!OrderToggles.Enabled || action == null) return;
            try
            {
                Tracked t;
                lock (_lock)
                {
                    if (!_tracked.TryGetValue(action, out t)) return;
                    _tracked.Remove(action);
                }

                int stage = Stage(action);
                float waited = Time.unscaledTime - t.QueuedAt;

                if (stage >= 2) return;

                bool player = IsPlayerOrder(action);
                if (player) OptCounters.PlayerOrdersDropped++;
                else OptCounters.AiActionsDropped++;

                if ((player || _droppedLines < 12) && _droppedLines < OrderToggles.MaxDroppedLines)
                {
                    _droppedLines++;
                    DiagLog.Write("[Order] DROPPED '" + t.What + "' on '" + t.Where +
                                  "' | owner=" + t.Owner +
                                  " playerOrder=" + player +
                                  " reachedStage=" + StageName(stage) +
                                  " waited=" + waited.ToString("F1") + "s real" +
                                  " via=" + via +
                                  " - removed from the queue without ever running");
                }
            }
            catch (Exception e) { DiagLog.WriteError("Order.NoteRemoved", e); }
        }

        public static void Sweep()
        {
            if (!OrderToggles.Enabled) return;
            try
            {
                float now = Time.unscaledTime;
                if (now - _lastSweep < 20f) return;
                _lastSweep = now;

                List<KeyValuePair<CharacterAction, Tracked>> snapshot;
                lock (_lock)
                {
                    snapshot = new List<KeyValuePair<CharacterAction, Tracked>>(_tracked);
                }

                List<CharacterAction> expired = null;
                for (int i = 0; i < snapshot.Count; i++)
                {
                    var t = snapshot[i].Value;

                    if (now - t.QueuedAt > 900f)
                    {
                        if (expired == null) expired = new List<CharacterAction>();
                        expired.Add(snapshot[i].Key);
                        continue;
                    }

                    if (!t.Started && now - t.QueuedAt > OrderToggles.StallSeconds && !t.Stalled)
                    {
                        t.Stalled = true;
                        if (!t.Player) continue;
                        OptCounters.PlayerOrdersStalled++;
                        if (_stallLines < OrderToggles.MaxStallLines)
                        {
                            _stallLines++;
                            DiagLog.Write("[Order:Stalled] '" + t.What + "' on '" + t.Where +
                                          "' | owner=" + t.Owner +
                                          " still below Running after " +
                                          (now - t.QueuedAt).ToString("F0") + "s real");
                        }
                    }
                }

                if (expired != null)
                {
                    lock (_lock)
                    {
                        for (int i = 0; i < expired.Count; i++) _tracked.Remove(expired[i]);
                    }
                }
            }
            catch (Exception e) { DiagLog.WriteError("Order.Sweep", e); }
        }

        public static void NoteStageChanged(CharacterAction action)
        {
            if (!OrderToggles.Enabled || action == null) return;
            try
            {
                int stage = Stage(action);
                if (stage < 2) return;

                Tracked t;
                lock (_lock)
                {
                    if (!_tracked.TryGetValue(action, out t)) return;
                }
                if (t.Started) return;
                t.Started = true;

                if (_startedLines < OrderToggles.MaxStartedLines)
                {
                    _startedLines++;
                    DiagLog.Write("[Order] started '" + t.What + "' on '" + t.Where +
                                  "' | owner=" + t.Owner +
                                  " after " + (Time.unscaledTime - t.QueuedAt).ToString("F1") + "s real");
                }
            }
            catch (Exception e) { DiagLog.WriteError("Order.NoteStageChanged", e); }
        }
    }

    [HarmonyPatch(typeof(ActionQueue), "Add", new Type[] { typeof(CharacterAction) })]
    internal static class Patch_Order_Add
    {
        static void Postfix(ActionQueue __instance, CharacterAction action)
        {
            try { OrderTracker.NoteQueued(__instance, action); }
            catch (Exception e) { DiagLog.WriteError("Order.Add", e); }
        }
    }

    [HarmonyPatch(typeof(ActionQueue), "ResetAndAdd", new Type[] { typeof(CharacterAction) })]
    internal static class Patch_Order_ResetAndAdd
    {
        static void Postfix(ActionQueue __instance, CharacterAction action)
        {
            try { OrderTracker.NoteQueued(__instance, action); }
            catch (Exception e) { DiagLog.WriteError("Order.ResetAndAdd", e); }
        }
    }

    [HarmonyPatch(typeof(ActionQueue), "Remove",
        new Type[] { typeof(CharacterAction), typeof(bool) })]
    internal static class Patch_Order_Remove
    {
        static void Prefix(ActionQueue __instance, CharacterAction action)
        {
            try { OrderTracker.NoteRemoved(__instance, action, "Remove"); }
            catch (Exception e) { DiagLog.WriteError("Order.Remove", e); }
        }
    }

    [HarmonyPatch(typeof(ActionQueue), "RemoveAt", new Type[] { typeof(int) })]
    internal static class Patch_Order_RemoveAt
    {
        static void Prefix(ActionQueue __instance, int index)
        {
            try
            {
                var acts = __instance != null ? __instance.Actions : null;
                if (acts == null || index < 0 || index >= acts.Count) return;
                OrderTracker.NoteRemoved(__instance, acts[index], "RemoveAt");
            }
            catch (Exception e) { DiagLog.WriteError("Order.RemoveAt", e); }
        }
    }

    [HarmonyPatch(typeof(ActionQueue), "AddPreStarted", new Type[] { typeof(CharacterAction) })]
    internal static class Patch_Order_AddPreStarted
    {
        static void Postfix(ActionQueue __instance, CharacterAction action)
        {
            try { OrderTracker.NoteQueued(__instance, action); }
            catch (Exception e) { DiagLog.WriteError("Order.AddPreStarted", e); }
        }
    }

    [HarmonyPatch(typeof(ActionQueue), "ResetAndAddPreStarted", new Type[] { typeof(CharacterAction) })]
    internal static class Patch_Order_ResetAndAddPreStarted
    {
        static void Postfix(ActionQueue __instance, CharacterAction action)
        {
            try { OrderTracker.NoteQueued(__instance, action); }
            catch (Exception e) { DiagLog.WriteError("Order.ResetAndAddPreStarted", e); }
        }
    }

    [HarmonyPatch(typeof(ActionQueue), "Action_StageChanged",
        new Type[] { typeof(CharacterAction) })]
    internal static class Patch_Order_StageChanged
    {
        static void Postfix(CharacterAction action)
        {
            try { OrderTracker.NoteStageChanged(action); }
            catch (Exception e) { DiagLog.WriteError("Order.StageChanged", e); }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/CrewAi/CrewAIPatching.cs
// ====================================================================================================
// Crew-KI (Teil des UBOAT Toolkit): CrewAIPatching. Schalter: F10 > QoL > "Crew-KI".

namespace UboatToolkit.Modules.CrewAi
{
    using global::System;
    using global::System.Collections.Generic;
    using global::HarmonyLib;
    using global::UnityEngine;

    internal static class CrewAIPatching
    {
        internal static int PatchOwnClasses(Harmony harmony, out int failed)
        {
            failed = 0;
            int classes = 0;
            string ns = typeof(CrewAIPatching).Namespace;
            Type[] types;
            try { types = typeof(CrewAIPatching).Assembly.GetTypes(); }
            catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types; }
            var mine = new List<Type>();
            foreach (Type t in types)
            {
                if (t == null || t.Namespace != ns) continue;
                if (t.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
                mine.Add(t);
            }
            mine.Sort(delegate (Type a, Type b) { return string.CompareOrdinal(a.FullName, b.FullName); });
            foreach (Type t in mine)
            {
                try
                {
                    harmony.CreateClassProcessor(t).Patch();
                    classes++;
                }
                catch (Exception e)
                {
                    failed++;
                    Debug.LogWarning("[UboatToolkit Crew-KI] konnte " + t.Name + " nicht patchen, dieser Teil bleibt wie im Spiel: " + e.Message);
                    try { DiagLog.Write("ERROR patching " + t.Name + ": " + e); } catch { }
                }
            }
            return classes;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/CrewAi/CrewAIPerf.cs
// ====================================================================================================
// Crew-KI (Teil des UBOAT Toolkit): CrewAIPerf. Schalter: F10 > QoL > "Crew-KI".

namespace UboatToolkit.Modules.CrewAi
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Text;
    using global::UnityEngine;

    internal static class PerfToggles
    {
        public static readonly bool Enabled = true;

        public const double SlowCallMs = 15.0;

        public const int MaxSlowLines = 60;

        public const float ReportIntervalSeconds = 60f;
    }

    internal static class Perf
    {
        private sealed class Site
        {
            public long Calls;
            public double TotalMs;
            public double MaxMs;
            public float MaxAtUnscaled;
            public long SlowCalls;
        }

        private static readonly Dictionary<string, Site> _sites = new Dictionary<string, Site>();
        private static readonly object _lock = new object();
        private const double MsPerTick = 0.001;
        private static long Now() { return (long)(Time.realtimeSinceStartupAsDouble * 1000000.0); }

        private static int _slowLines;
        private static float _lastReport;
        private static bool _reportedFirst;

        public static long Begin()
        {
            if (!PerfToggles.Enabled) return 0L;
            return Now();
        }

        public static void End(string site, long startTicks)
        {
            if (!PerfToggles.Enabled || startTicks == 0L) return;
            try
            {
                double ms = (Now() - startTicks) * MsPerTick;
                float now = Time.unscaledTime;

                bool slow;
                lock (_lock)
                {
                    Site s;
                    if (!_sites.TryGetValue(site, out s)) { s = new Site(); _sites[site] = s; }
                    s.Calls++;
                    s.TotalMs += ms;
                    if (ms > s.MaxMs) { s.MaxMs = ms; s.MaxAtUnscaled = now; }
                    slow = ms >= PerfToggles.SlowCallMs;
                    if (slow) s.SlowCalls++;
                }

                if (slow && _slowLines < PerfToggles.MaxSlowLines)
                {
                    _slowLines++;
                    DiagLog.Write("[Perf] SLOW " + site + " took " + ms.ToString("F1") +
                                  " ms in one call (threshold " + PerfToggles.SlowCallMs + " ms)");
                }
            }
            catch { }
        }

        public static void MaybeReport()
        {
            if (!PerfToggles.Enabled) return;
            try
            {
                float now = Time.unscaledTime;
                if (_reportedFirst && now - _lastReport < PerfToggles.ReportIntervalSeconds) return;
                _lastReport = now;
                _reportedFirst = true;

                var sb = new StringBuilder("[Perf] ");
                bool any = false;
                lock (_lock)
                {
                    foreach (var kv in _sites)
                    {
                        var s = kv.Value;
                        if (s.Calls == 0) continue;
                        any = true;
                        sb.Append(kv.Key).Append(' ')
                          .Append("n=").Append(s.Calls).Append(' ')
                          .Append("total=").Append(s.TotalMs.ToString("F1")).Append("ms ")
                          .Append("mean=").Append((s.TotalMs / s.Calls).ToString("F3")).Append("ms ")
                          .Append("max=").Append(s.MaxMs.ToString("F1")).Append("ms@rt")
                          .Append(s.MaxAtUnscaled.ToString("F1"));
                        if (s.SlowCalls > 0) sb.Append(" slow=").Append(s.SlowCalls);
                        sb.Append(" | ");
                    }
                }
                if (any) DiagLog.Write(sb.ToString());
            }
            catch { }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/CrewAi/CrewAIProximity.cs
// ====================================================================================================
// Crew-KI (Teil des UBOAT Toolkit): CrewAIProximity. Schalter: F10 > QoL > "Crew-KI".

namespace UboatToolkit.Modules.CrewAi
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::UnityEngine;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UBOAT.Game.Scene.Items;
    using Activator = global::UBOAT.Game.Scene.Characters.Activators.Activator;

    internal static class ProximityToggles
    {
        public static bool Enabled => UboatToolkit.Settings.B("CrewNearbyJobs");

        public const float PerCompartmentStep = 0.02f;

        public const float MinScale = 0.90f;

        public const int MaxHops = 12;

        public static readonly bool LogSamples = true;
    }

    internal static class CompartmentGraph
    {
        private static readonly Dictionary<long, int> _hops = new Dictionary<long, int>();
        private static readonly Dictionary<int, Compartment> _activatorComp =
            new Dictionary<int, Compartment>();
        private static readonly object _lock = new object();

        private static long Key(int a, int b) { return ((long)a << 32) ^ (uint)b; }

        public static Compartment CompartmentOf(Activator activator)
        {
            if (activator == null) return null;
            try
            {
                int id = activator.GetInstanceID();
                Compartment cached;
                lock (_lock)
                {
                    if (_activatorComp.TryGetValue(id, out cached)) return cached;
                }

                Compartment found = null;
                var inter = activator.Interactable;
                if (inter != null)
                {
                    var slots = inter.Slots;
                    if (slots != null)
                    {
                        for (int i = 0; i < slots.Length && found == null; i++)
                        {
                            if (slots[i] != null) found = slots[i].Compartment;
                        }
                    }
                }

                lock (_lock) { _activatorComp[id] = found; }
                return found;
            }
            catch { return null; }
        }

        private static Dictionary<int, List<Compartment>> _adj;
        private static float _lastBuild;
        private static bool _rebuildRequested;

        private static Dictionary<int, List<Compartment>> Adjacency()
        {
            if (_adj != null && !_rebuildRequested) return _adj;

            long t0 = Perf.Begin();
            try
            {
                float now = Time.unscaledTime;
                if (_adj != null && now - _lastBuild < 120f) return _adj;
                _rebuildRequested = false;

                var map = new Dictionary<int, List<Compartment>>();
                Action<Compartment, Compartment> link = (a, b) =>
                {
                    if (a == null || b == null || a == b) return;
                    List<Compartment> l;
                    int ka = a.GetInstanceID();
                    if (!map.TryGetValue(ka, out l)) { l = new List<Compartment>(); map[ka] = l; }
                    if (!l.Contains(b)) l.Add(b);
                };

                var comps = UnityEngine.Object.FindObjectsOfType<Compartment>();
                if (comps != null)
                {
                    for (int i = 0; i < comps.Length; i++)
                    {
                        var c = comps[i];
                        if (c == null) continue;
                        Compartment[] nb = null;
                        try { nb = c.NeighbouringCompartments; } catch { }
                        if (nb == null) continue;
                        for (int n = 0; n < nb.Length; n++) { link(c, nb[n]); link(nb[n], c); }
                    }
                }

                var hatches = UnityEngine.Object.FindObjectsOfType<Hatch>();
                if (hatches != null)
                {
                    for (int i = 0; i < hatches.Length; i++)
                    {
                        var h = hatches[i];
                        if (h == null) continue;
                        Compartment a = null, b = null;
                        try { a = h.LeftCompartment; b = h.RightCompartment; } catch { }
                        link(a, b); link(b, a);
                    }
                }

                _adj = map; _lastBuild = now;
                lock (_lock) { _hops.Clear(); _towerOf.Clear(); }   // topology changed shape
                DiagLog.Write("[Proximity] compartment graph built: " + map.Count +
                              " compartment(s) with neighbours");
                return _adj;
            }
            catch { return _adj; }
            finally { Perf.End("Compartments.Rebuild", t0); }
        }

        private static readonly Dictionary<int, Compartment> _towerOf = new Dictionary<int, Compartment>();

        public static Compartment ConningTower(Compartment sameBoatAs)
        {
            if (sameBoatAs == null) return null;
            try
            {
                int id = sameBoatAs.GetInstanceID();
                Compartment cachedTower;
                lock (_lock) { if (_towerOf.TryGetValue(id, out cachedTower)) return cachedTower; }

                var map = Adjacency();
                if (map == null) return null;
                Compartment found = null;
                var seen = new HashSet<int> { id };
                var frontier = new List<Compartment> { sameBoatAs };
                for (int depth = 0; depth <= ProximityToggles.MaxHops && frontier.Count > 0 && found == null; depth++)
                {
                    var next = new List<Compartment>();
                    for (int i = 0; i < frontier.Count && found == null; i++)
                    {
                        var c = frontier[i];
                        string n = null;
                        try { n = c.name; } catch { }
                        if (n != null && n.IndexOf("Conning Tower", StringComparison.OrdinalIgnoreCase) >= 0)
                        { found = c; break; }
                        List<Compartment> nb;
                        if (!map.TryGetValue(c.GetInstanceID(), out nb)) continue;
                        for (int k = 0; k < nb.Count; k++)
                            if (nb[k] != null && seen.Add(nb[k].GetInstanceID())) next.Add(nb[k]);
                    }
                    frontier = next;
                }
                lock (_lock) { _towerOf[id] = found; }   // null cached too: no tower, no re-walk
                return found;
            }
            catch { return null; }
        }

        private static List<Compartment> NeighboursOf(Compartment c)
        {
            try
            {
                var map = Adjacency();
                if (map == null || c == null) return null;
                List<Compartment> l;
                if (map.TryGetValue(c.GetInstanceID(), out l)) return l;

                _rebuildRequested = true;
                return null;
            }
            catch { return null; }
        }

        public static int Hops(Compartment from, Compartment to)
        {
            if (from == null || to == null) return -1;
            if (from == to) return 0;
            try
            {
                int a = from.GetInstanceID(), b = to.GetInstanceID();
                long k = Key(a, b);
                int cached;
                lock (_lock)
                {
                    if (_hops.TryGetValue(k, out cached)) return cached;
                }

                int result = -1;
                var seen = new HashSet<int> { a };
                var frontier = new List<Compartment> { from };
                var next = new List<Compartment>();

                for (int depth = 1; depth <= ProximityToggles.MaxHops && frontier.Count > 0; depth++)
                {
                    next.Clear();
                    for (int i = 0; i < frontier.Count; i++)
                    {
                        var nb = NeighboursOf(frontier[i]);
                        if (nb == null) continue;
                        for (int n = 0; n < nb.Count; n++)
                        {
                            var c = nb[n];
                            if (c == null) continue;
                            int cid = c.GetInstanceID();
                            if (!seen.Add(cid)) continue;
                            if (cid == b) { result = depth; break; }
                            next.Add(c);
                        }
                        if (result >= 0) break;
                    }
                    if (result >= 0) break;
                    var swap = frontier; frontier = next; next = swap;
                }

                lock (_lock) { _hops[k] = result; }
                return result;
            }
            catch { return -1; }
        }
    }

    [HarmonyPatch]
    internal static class Patch_Proximity_GetActionProfit
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var sig = new[] { typeof(Activator), typeof(PlayableCharacter), typeof(float) };
            var found = new List<MethodBase>();
            try
            {
                foreach (var t in typeof(CharacterAction).Assembly.GetTypes())
                {
                    if (t == null || !typeof(CharacterAction).IsAssignableFrom(t)) continue;
                    MethodInfo m = null;
                    try
                    {
                        m = t.GetMethod("GetActionProfit",
                            BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.Instance | BindingFlags.DeclaredOnly,
                            null, sig, null);
                    }
                    catch { }
                    if (m != null && !m.IsAbstract) found.Add(m);
                }
            }
            catch (Exception e) { DiagLog.WriteError("Proximity.TargetMethods", e); }
            return found;
        }

        private static readonly HashSet<string> _sampled = new HashSet<string>();
        private static readonly object _lock = new object();

        static void Postfix(CharacterAction __instance, Activator activator,
                            PlayableCharacter character, ref float __result)
        {
            if (!ProximityToggles.Enabled) return;
            if (__result <= 0f) return;
            long t0 = Perf.Begin();
            try
            {
                if (character == null || activator == null) return;

                if (__instance != null)
                {
                    bool exempt = false;
                    try { exempt = __instance.IsDirectPlayerOrder; }
                    catch { }
                    if (exempt)
                    {
                        OptCounters.ProximityPlayerOrderExempt++;
                        return;
                    }
                }

                if (__instance is SleepAction)
                {
                    OptCounters.ProximitySleepExempt++;
                    return;
                }

                var to = CompartmentGraph.CompartmentOf(activator);
                if (to == null) return;

                Compartment from = null;
                try { from = character.CurrentCompartment; } catch { }
                if (from == null)
                {
                    from = CompartmentGraph.ConningTower(to);
                    if (from == null) return;
                    OptCounters.ProximityDeckAsTower++;
                }

                int hops = CompartmentGraph.Hops(from, to);
                if (hops <= 0) return;      // same compartment, or unknown

                // 0.4.14 (DoMe: assigned men with priorities do nothing, posts stay empty). The old discount was
                // x0.7 per room down to x0.3: a helm job (priority 8) three rooms away fell to 2.7 and lost against
                // any nearby job of priority 3, so the game's own priority order was turned upside down and far posts
                // stayed empty. Now distance only breaks ties: 2 % per room, never more than 10 % in total, which
                // cannot move a job below the next lower priority (9 > 8 > 7 ... stay in order).
                float scale = Mathf.Max(ProximityToggles.MinScale, 1f - ProximityToggles.PerCompartmentStep * hops);

                __result *= scale;
                OptCounters.ProximityScaled++;

                if (ProximityToggles.LogSamples)
                {
                    string act = __instance != null ? __instance.GetType().Name : "?";
                    string key = act + "|" + hops;
                    bool first;
                    lock (_lock) { first = _sampled.Add(key); }
                    if (first)
                    {
                        DiagLog.Write("[Proximity] " + act + " scaled x" + scale.ToString("F2") +
                                      " for " + hops + " compartment(s) - " +
                                      SafeName(from) + " -> " + SafeName(to));
                    }
                }
            }
            catch (Exception e)
            {
                DiagLog.WriteError("Proximity.GetActionProfit", e);
            }
            finally { Perf.End("Proximity.GetProfit", t0); }
        }

        private static string SafeName(Compartment c)
        {
            try { return c != null ? c.name : "null"; } catch { return "?"; }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/CrewAi/CrewAIRestInBunks.cs
// ====================================================================================================
// Crew-KI (Teil des UBOAT Toolkit): CrewAIRestInBunks. Schalter: F10 > QoL > "Crew-KI".

namespace UboatToolkit.Modules.CrewAi
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::UnityEngine;
    using global::HarmonyLib;
    using global::UBOAT.Game.Core.AI;
    using global::UBOAT.Game.Core.AI.Behaviours;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Interactions;
    using global::UBOAT.Game.Scene.Items;

    internal static class RestToggles
    {
        public static bool Enabled => UboatToolkit.Settings.B("CrewRestInBunks");

        public const float IdleSecondsBeforeRest = 20f;

        public const float TickSeconds = 2f;       // real seconds
        public const int MaxPerTick = 2;           // no all-crew bursts

        public const float LowestProfitThreshold = 0.55f;

        public const float RestProfit = 0.6f;

        public const int ReserveFreeBunks = 1;

        public const float StartGraceSeconds = 10f;

        public const int MaxLogLines = 120;
    }

    internal static class RestController
    {
        private sealed class RestRecord
        {
            public PlayableCharacter Who;
            public CompoundBehaviour Beh;
            public string Bed;
            public string From;
            public float Since;         // unscaled
            public bool Took;           // has been Active at least once
            public bool InBunk;         // Active at the last review
        }

        private static readonly Dictionary<int, float> _idleSince = new Dictionary<int, float>();
        private static readonly Dictionary<int, RestRecord> _resting = new Dictionary<int, RestRecord>();
        private static readonly object _lock = new object();

        private static float _lastTick, _lastLookup;
        private static int _logLines;
        private static bool _reserveLogged;

        private static PlayerCrew _crew;
        private static PlayerShip _ship;

        private static readonly PropertyInfo ItemProp = AccessTools.Property(typeof(PlayableCharacter), "Item");
        private static readonly PropertyInfo BackpackProp = AccessTools.Property(typeof(PlayableCharacter), "Backpack");
        private static readonly Type BlockingItemType =
            UboatToolkit.Util.FindType("UBOAT.Game.Scene.Items.IInteractionsBlockingItem");
        private static PropertyInfo _backpackItemProp;

        public static PlayerCrew Crew { get { Lookup(Time.unscaledTime); return _crew; } }
        public static PlayerShip Ship { get { Lookup(Time.unscaledTime); return _ship; } }

        private static bool IsFinal(AIBehaviourState s)
        {
            return s == AIBehaviourState.Cancelled || s == AIBehaviourState.Completed;
        }

        public static bool IsResting(PlayableCharacter c)
        {
            if (c == null) return false;
            try
            {
                lock (_lock)
                {
                    RestRecord r;
                    return _resting.TryGetValue(c.GetInstanceID(), out r) && r.Beh != null &&
                           r.Beh.State == AIBehaviourState.Active;
                }
            }
            catch { return false; }
        }

        public static CompoundBehaviour RestBehaviourOf(PlayableCharacter c)
        {
            if (c == null) return null;
            try
            {
                lock (_lock)
                {
                    RestRecord r;
                    if (!_resting.TryGetValue(c.GetInstanceID(), out r) || r.Beh == null) return null;
                    return IsFinal(r.Beh.State) ? null : r.Beh;
                }
            }
            catch { return null; }
        }

        private static bool HasRest(int id)
        {
            RestRecord r;
            if (!_resting.TryGetValue(id, out r) || r.Beh == null) return false;
            try { return !IsFinal(r.Beh.State); } catch { return false; }
        }

        public static float IdleSeconds(PlayableCharacter c)
        {
            if (c == null) return -1f;
            try
            {
                float since;
                lock (_lock) { if (!_idleSince.TryGetValue(c.GetInstanceID(), out since)) return -1f; }
                return Time.unscaledTime - since;
            }
            catch { return -1f; }
        }

        private static void Lookup(float now)
        {
            if (_crew != null && _ship != null) return;
            if (now - _lastLookup < 5f) return;
            _lastLookup = now;
            try
            {
                if (_crew == null) _crew = UnityEngine.Object.FindObjectOfType<PlayerCrew>();
                if (_ship == null) _ship = UnityEngine.Object.FindObjectOfType<PlayerShip>();
            }
            catch { }
        }

        public static void MaybeTick()
        {
            float now = Time.unscaledTime;
            if (now - _lastTick < RestToggles.TickSeconds) return;
            _lastTick = now;

            long t0 = Perf.Begin();
            try
            {
                Lookup(now);
                if (_crew == null || _ship == null) return;
                BedSharing.EnsureExpanded(_ship);          // v0.25, once per boat
                BedSharing.SyncClones();                   // v0.33: stowed bunks
                PlayableCharacter[] chars = null;
                try { chars = _crew.Characters; } catch { }
                if (chars == null) return;

                ReviewResting(now);

                bool alarmed = false;
                try { alarmed = _ship.Alarmed; } catch { }

                BedYield.Tick(_ship, chars, alarmed, now);
                if (!RestToggles.Enabled) { lock (_lock) { _idleSince.Clear(); } return; }
                if (!alarmed && BedYield.TiredWaiting > 0)
                {
                    lock (_lock) { _idleSince.Clear(); }
                    OptCounters.RestHeldForTired++;
                    return;
                }
                if (alarmed)
                {
                    lock (_lock) { _idleSince.Clear(); }
                    OptCounters.RestSkippedAlarm++;
                    return;
                }

                var candidates = new List<PlayableCharacter>();
                var crowd = new List<int>();
                for (int i = 0; i < chars.Length; i++)
                {
                    var c = chars[i];
                    if (c == null) continue;
                    int id = c.GetInstanceID();
                    bool eligible = Eligible(c);
                    if (eligible) { lock (_lock) { eligible = !HasRest(id); } }
                    if (!eligible)
                    {
                        lock (_lock) { _idleSince.Remove(id); }
                        continue;
                    }
                    float since;
                    lock (_lock)
                    {
                        if (!_idleSince.TryGetValue(id, out since)) { _idleSince[id] = now; continue; }
                    }
                    if (now - since < RestToggles.IdleSecondsBeforeRest) continue;

                    int head = 0;
                    try { head = c.CurrentCompartment.Characters.Count; } catch { }
                    int at = 0;   // most crowded compartment first
                    while (at < crowd.Count && crowd[at] >= head) at++;
                    candidates.Insert(at, c);
                    crowd.Insert(at, head);
                }
                if (candidates.Count == 0) return;

                int free = CountFreeBunks();
                int sent = 0;
                for (int i = 0; i < candidates.Count && sent < RestToggles.MaxPerTick; i++)
                {
                    if (free <= RestToggles.ReserveFreeBunks)
                    {
                        OptCounters.RestSkippedReserve++;
                        if (!_reserveLogged)
                        {
                            _reserveLogged = true;
                            DiagLog.Write("[Rest] holding back: only " + free + " free bunk(s) left, " +
                                          RestToggles.ReserveFreeBunks + " kept for a tired man (" +
                                          candidates.Count + " idle men waiting)");
                        }
                        break;
                    }
                    if (TrySend(candidates[i], crowd[i], now)) { sent++; free--; }
                }
            }
            catch (Exception e) { DiagLog.WriteError("Rest.Tick", e); }
            finally { Perf.End("Rest.Tick", t0); }
        }

        private static bool Eligible(PlayableCharacter c)
        {
            return WhyNotEligible(c, false) == null;
        }

        public static string WhyNotEligible(PlayableCharacter c)
        {
            return WhyNotEligible(c, true);
        }

        private static string WhyNotEligible(PlayableCharacter c, bool includeIdleClock)
        {
            if (c == null) return "gone";
            try
            {
                var data = c.Data;
                if (data == null) return "no character data";
                if (data.CharacterType != PlayableCharacterData.Type.Sailor)
                    return data.CharacterType.ToString().ToLowerInvariant() + " - left to vanilla";
                if (c.IsImpersonated) return "you are looking through him";
                if (c.Injury) return "injured";
                if (c.IsSelected) return "selected";
                if (c.Action != null) return "has action " + c.Action.GetType().Name;
                var q = c.ActionQueue;
                if (q != null && q.Actions != null && q.Actions.Count > 0) return q.Actions.Count + " action(s) queued";
                var ai = c.Ai;
                if (ai == null || !ai.enabled) return "AI disabled";
                if (ai.LowestActiveProfit > RestToggles.LowestProfitThreshold)
                    return "busy-level AI profit " + ai.LowestActiveProfit.ToString("F2");
                if (ai.GetActiveBehaviour<SleepBehaviour>() != null) return "already heading to bed";
                if (BedYield.IsHeld(c)) return "gave up his bunk to a tired man";
                var comp = c.CurrentCompartment;
                if (comp == null) return "outside the hull";
                if (comp.Evacuation) return "compartment evacuating";
                if (HoldsBlockingItem(c)) return "carrying a blocking item";
                if (includeIdleClock)
                {
                    lock (_lock) { if (HasRest(c.GetInstanceID())) return "rest already in progress"; }
                    float idle = IdleSeconds(c);
                    if (idle < RestToggles.IdleSecondsBeforeRest)
                        return "idle only " + (idle < 0 ? 0 : idle).ToString("F0") + "s of " + RestToggles.IdleSecondsBeforeRest + "s";
                }
                return null;
            }
            catch { return "?"; }   // fail closed: if we cannot tell, do not move him
        }

        private static bool HoldsBlockingItem(PlayableCharacter c)
        {
            if (BlockingItemType == null) return false;
            try
            {
                object it = ItemProp != null ? ItemProp.GetValue(c, null) : null;
                if (it != null && BlockingItemType.IsInstanceOfType(it)) return true;
                object bp = BackpackProp != null ? BackpackProp.GetValue(c, null) : null;
                if (bp != null)
                {
                    if (_backpackItemProp == null) _backpackItemProp = AccessTools.Property(bp.GetType(), "Item");
                    object it2 = _backpackItemProp != null ? _backpackItemProp.GetValue(bp, null) : null;
                    if (it2 != null && BlockingItemType.IsInstanceOfType(it2)) return true;
                }
                return false;
            }
            catch { return true; }
        }

        private static int CountFreeBunks()
        {
            if (BedSharingToggles.Enabled) return BedSharing.FreeSpots(_ship);
            int free = 0;
            try
            {
                var beds = _ship.Beds;
                if (beds == null) return 0;
                for (int i = 0; i < beds.Length; i++)
                {
                    var b = beds[i];
                    if (b == null || !b.isActiveAndEnabled) continue;
                    if (b.ReservingCharacters != null && b.ReservingCharacters.Count != 0) continue;
                    if (b.InteractingCharacters != null && b.InteractingCharacters.Count != 0) continue;
                    var comp = b.Compartment;
                    if (comp != null && comp.Evacuation) continue;
                    free++;
                }
            }
            catch { }
            return free;
        }

        private static bool TrySend(PlayableCharacter c, int head, float now)
        {
            try
            {
                Bed bed = _ship.FindFreeBed(c.Navigator, true, false);
                if (bed == null) { OptCounters.RestNoBunk++; return false; }
                // 0.3.64 (DoMe: a sailor crossed the whole boat again and again): an idle man only lies down in a bunk
                // close by (15 m). No near bunk = he stays where he is instead of walking through every room.
                if ((bed.transform.position - c.transform.position).sqrMagnitude > 15f * 15f) { OptCounters.RestNoBunk++; return false; }

                CompoundBehaviour beh = c.Use(bed, RestToggles.RestProfit, null, SlotUsageFlags.None, false);
                if (beh == null)
                {
                    OptCounters.RestNotAllowed++;
                    if (_logLines < RestToggles.MaxLogLines)
                    {
                        _logLines++;
                        DiagLog.Write("[Rest] Use() refused '" + bed.name + "' for " + SafeName(c));
                    }
                    return false;
                }

                var rec = new RestRecord();
                rec.Who = c; rec.Beh = beh; rec.Since = now; rec.Bed = bed.name;
                try { rec.From = c.CurrentCompartment != null ? c.CurrentCompartment.name : "?"; } catch { rec.From = "?"; }
                int id = c.GetInstanceID();
                float idleFor = 0f;
                lock (_lock)
                {
                    float since;
                    if (_idleSince.TryGetValue(id, out since)) idleFor = now - since;
                    _resting[id] = rec;
                    _idleSince.Remove(id);
                }
                OptCounters.RestSent++;

                if (_logLines < RestToggles.MaxLogLines)
                {
                    _logLines++;
                    DiagLog.Write("[Rest] " + SafeName(c) + " idle " + idleFor.ToString("F0") + "s in '" +
                                  rec.From + "' (" + head + " present) -> resting in '" + rec.Bed + "'");
                }
                return true;
            }
            catch (Exception e)
            {
                DiagLog.WriteError("Rest.TrySend", e);
                return false;
            }
        }

        private static void ReviewResting(float now)
        {
            var notes = new List<string>();
            lock (_lock)
            {
                List<int> done = null;
                foreach (var kv in _resting)
                {
                    var r = kv.Value;
                    bool gone;
                    try { gone = r.Who == null; } catch { gone = true; }
                    AIBehaviourState st = AIBehaviourState.Cancelled;
                    if (!gone && r.Beh != null) { try { st = r.Beh.State; } catch { } }

                    bool active = !gone && st == AIBehaviourState.Active;
                    if (active)
                    {
                        if (!r.InBunk && r.Took)
                        {
                            OptCounters.RestResumed++;
                            notes.Add("[Rest] " + SafeName(r.Who) + " back to '" + r.Bed + "'");
                        }
                        r.Took = true; r.InBunk = true;
                        continue;
                    }

                    if (r.InBunk)
                    {
                        r.InBunk = false;
                        string next = Doing(r.Who);
                        if (next.StartsWith("action ")) OptCounters.RestEndedForWork++;
                        else OptCounters.RestEndedOther++;
                        notes.Add("[Rest] " + SafeName(r.Who) + " got up from '" + r.Bed + "' after " +
                                  (now - r.Since).ToString("F0") + "s -> " + next);
                    }

                    bool final = gone || IsFinal(st) || (r.Took && st == AIBehaviourState.Detached);
                    bool expired = !r.Took && now - r.Since > RestToggles.StartGraceSeconds;
                    if (final || expired)
                    {
                        if (!r.Took)
                        {
                            OptCounters.RestNeverStarted++;
                            notes.Add("[Rest] " + SafeName(r.Who) + " never settled in '" + r.Bed + "' (" +
                                      st + " after " + (now - r.Since).ToString("F0") + "s) -> " + Doing(r.Who));
                        }
                        if (done == null) done = new List<int>();
                        done.Add(kv.Key);
                    }
                }
                if (done != null) for (int i = 0; i < done.Count; i++) _resting.Remove(done[i]);
            }
            for (int i = 0; i < notes.Count && _logLines < RestToggles.MaxLogLines; i++)
            {
                _logLines++;
                DiagLog.Write(notes[i]);
            }
        }

        private static string Doing(PlayableCharacter c)
        {
            try
            {
                if (c == null) return "(gone)";
                if (c.Action != null) return "action " + c.Action.GetType().Name;
                var ai = c.Ai;
                if (ai != null && ai.GetActiveBehaviour<SleepBehaviour>() != null) return "own SleepBehaviour";
                return "behaviour change (profit " + (ai != null ? ai.LowestActiveProfit.ToString("F2") : "?") + ")";
            }
            catch { return "?"; }
        }

        private static string SafeName(PlayableCharacter c)
        {
            try { return c != null ? (c.Name ?? "?") : "gone"; } catch { return "?"; }
        }
    }

    [HarmonyPatch(typeof(CharacterAI), "StartBehaviour", new Type[] { typeof(AIBehaviour) })]
    internal static class Patch_Rest_Tick
    {
        static void Postfix()
        {
            try { RestController.MaybeTick(); } catch { }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/CrewAi/CrewAIRoleDropLog.cs
// ====================================================================================================
// Crew-KI (Teil des UBOAT Toolkit): CrewAIRoleDropLog. Schalter: F10 > QoL > "Crew-KI".

namespace UboatToolkit.Modules.CrewAi
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::System.Text;
    using global::UnityEngine;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Actions;

    internal static class RoleDropToggles
    {
        public static readonly bool Enabled = true;

        public static bool KeepRunningJobs => UboatToolkit.Settings.B("CrewKeepJobs");

        public const float TieTolerance = 0.05f;

        public const int MaxLogLines = 60;
        public const int SampleEvery = 50;
        public const int MaxCandidatesShown = 6;
    }

    [HarmonyPatch(typeof(CharacterRole), "RemoveOutdatedActions",
        new Type[] { typeof(ActionQueue), typeof(ActionProfitabilityData) })]
    internal static class Patch_RoleDrop_RemoveOutdatedActions
    {
        static readonly FieldInfo ActionsField = AccessTools.Field(typeof(CharacterRole), "actions");
        static readonly FieldInfo ActionsCountField = AccessTools.Field(typeof(CharacterRole), "actionsCount");
        static readonly PropertyInfo StageProp = AccessTools.Property(typeof(CharacterAction), "Stage");

        static int _dropLines, _keepLines;

        static bool ShouldLog(ref int lines, long counter)
        {
            if (lines < RoleDropToggles.MaxLogLines) { lines++; return true; }
            return counter % RoleDropToggles.SampleEvery == 0;
        }

        static string Act(CharacterAction a, UBOAT.Game.Scene.Characters.Activators.Activator act)
        {
            string t = "?", s = "?";
            try { t = a != null ? a.GetType().Name : "none"; } catch { }
            try { s = act != null ? act.name : "none"; } catch { }
            return t + " at '" + s + "'";
        }

        static void Prefix(CharacterRole __instance, ActionQueue actionQueue, ActionProfitabilityData bestAction,
                           out int __state)
        {
            __state = -1;
            if (!RoleDropToggles.Enabled || __instance == null || actionQueue == null) return;
            try
            {
                var acts = actionQueue.Actions;
                if (acts == null || acts.Count == 0) return;
                if (ActionsField == null || ActionsCountField == null) return;

                var list = ActionsField.GetValue(__instance) as ActionProfitabilityData[];
                int count = (int)ActionsCountField.GetValue(__instance);
                if (list == null) { list = new ActionProfitabilityData[0]; count = 0; }
                if (count > list.Length) count = list.Length;
                int originalCount = count;

                for (int i = acts.Count - 1; i >= 0; i--)
                {
                    var a = acts[i];
                    if (a == null) continue;
                    object role = null;
                    try { role = a.Role; } catch { }
                    if (!ReferenceEquals(role, __instance)) continue;          // only this role's actions are touched

                    bool kept = false;
                    try
                    {
                        if (bestAction.Action != null && a.IsInterchangeable(bestAction.Action, bestAction.Activator)) kept = true;
                        for (int j = 0; j < count && !kept; j++)
                            if (list[j].Action != null && a.IsInterchangeable(list[j].Action, list[j].Activator)) kept = true;
                    }
                    catch { kept = true; }
                    if (kept) continue;

                    int stage = -1;
                    try { if (StageProp != null) stage = Convert.ToInt32(StageProp.GetValue(a, null)); } catch { }
                    if (stage != 2) continue;                                   // only RUNNING jobs disturb a man

                    PlayableCharacter who = null;
                    try { var cs = a.Characters; if (cs != null && cs.Count > 0) who = cs[0]; } catch { }
                    if (who == null) { try { who = actionQueue.Owner; } catch { } }

                    float score = float.NaN;
                    try
                    {
                        float basePrio = a.SourceJob != null ? a.SourceJob.BasePriority : 1f;
                        if (who != null) score = a.GetActionProfit(a.Activator, who, basePrio);
                    }
                    catch { }

                    bool sleepJob = a is SleepAction;
                    bool sleepShift = false;
                    try { sleepShift = who != null && who.Schedule != null && who.Schedule.CurrentBehaviour == Schedule.Behaviour.Sleep; } catch { }

                    bool keep = RoleDropToggles.KeepRunningJobs && who != null && !sleepJob && !sleepShift &&
                                !float.IsNaN(score) && score > 0.001f &&
                                (bestAction.Action == null || score >= bestAction.Profit - RoleDropToggles.TieTolerance);
                    // A man at a station (hydrophone, engines, guns, helm ...) never leaves it just to carry things from storage
                    // (DoMe 2026-11-13: crew "flattert"; log showed CarryItemsAction at 'Main Storage' outscoring stations). Port: carrying wins.
                    if (!keep && RoleDropToggles.KeepRunningJobs && who != null && !sleepJob && !sleepShift && !float.IsNaN(score) && score > 0.001f
                        && bestAction.Action != null && bestAction.Action.GetType().Name == "CarryItemsAction" && a.GetType().Name != "CarryItemsAction"
                        && a.Activator != null && a.Activator.AssociatedEquipment != null)
                    {
                        var ps = Util.PlayerShip;
                        if (ps != null && !ps.Docked) keep = true;
                    }

                    if (keep)
                    {
                        if (count >= list.Length)
                        {
                            var grown = new ActionProfitabilityData[Math.Max(4, list.Length * 2)];
                            Array.Copy(list, grown, list.Length);
                            list = grown;
                            ActionsField.SetValue(__instance, list);
                        }
                        list[count] = new ActionProfitabilityData(a, a.Activator, score, a.SourceJob);
                        count++;
                        ActionsCountField.SetValue(__instance, count);
                        __state = originalCount;                              // postfix restores

                        OptCounters.RoleKeeps++;
                        if (ShouldLog(ref _keepLines, OptCounters.RoleKeeps))
                        {
                            DiagLog.Write("[RoleKeep] " + SafeRole(__instance) + ": kept " + (who.Name ?? "?") +
                                          " on running " + Act(a, a.Activator) + " (scores " + score.ToString("F2") +
                                          ") instead of " + Act(bestAction.Action, bestAction.Activator) + " " +
                                          bestAction.Profit.ToString("F2"));
                        }
                        continue;
                    }

                    OptCounters.RoleDropsRunning++;
                    if (!ShouldLog(ref _dropLines, OptCounters.RoleDropsRunning)) continue;

                    string why = who == null ? "no holder" : sleepJob ? "sleep job" : sleepShift ? "his schedule is Sleep" :
                                 float.IsNaN(score) ? "unscorable" : score <= 0.001f ? "no longer valid for him" :
                                 "outscored";
                    var sb = new StringBuilder("[RoleDrop] ");
                    sb.Append(SafeRole(__instance)).Append(": ").Append(who != null ? (who.Name ?? "?") : "(group)");
                    sb.Append(" loses running ").Append(Act(a, a.Activator));
                    sb.Append(" (scores ").Append(float.IsNaN(score) ? "?" : score.ToString("F2")).Append(" for him now)");
                    sb.Append(" - best is ").Append(Act(bestAction.Action, bestAction.Activator));
                    sb.Append(" ").Append(bestAction.Profit.ToString("F2"));
                    sb.Append(" [").Append(why).Append("] | candidates: ");
                    if (originalCount == 0) sb.Append("none");
                    for (int j = 0; j < originalCount && j < RoleDropToggles.MaxCandidatesShown; j++)
                    {
                        if (j > 0) sb.Append(", ");
                        sb.Append(Act(list[j].Action, list[j].Activator)).Append(' ').Append(list[j].Profit.ToString("F2"));
                    }
                    DiagLog.Write(sb.ToString());
                }
            }
            catch (Exception e) { DiagLog.WriteError("RoleDrop.Prefix", e); }
        }

        static void Postfix(CharacterRole __instance, int __state)
        {
            if (__state < 0 || ActionsCountField == null) return;
            try { ActionsCountField.SetValue(__instance, __state); }
            catch (Exception e) { DiagLog.WriteError("RoleDrop.Postfix", e); }
        }

        static string SafeRole(CharacterRole r)
        {
            try { return r.Name ?? "?"; } catch { return "?"; }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/CrewAi/CrewAISettings.cs
// ====================================================================================================
// Crew-KI (Teil des UBOAT Toolkit): Schalter-Tabelle.
// Alle Schalter stehen in F10 > QoL (Crew-KI) und in der UboatToolkit.ini.
// Jeder Schalter wird in dem Moment gelesen, in dem er gebraucht wird (wirkt sofort).
// Ausnahmen: der Hauptschalter "CrewAIOverhaul" und "CrewAILog" wirken erst beim nächsten Spielstart.

namespace UboatToolkit.Modules.CrewAi
{
    using global::System;
    using global::System.Text;

    internal sealed class SettingRow
    {
        public string Key, ToolkitKey;
        public bool IsInt;
        public int Min, Max;
    }

    internal static class CrewAISettings
    {
        private static SettingRow B(string key, string tk) { return new SettingRow { Key = key, ToolkitKey = tk }; }
        private static SettingRow I(string key, string tk, int min, int max) { return new SettingRow { Key = key, ToolkitKey = tk, IsInt = true, Min = min, Max = max }; }

        // Key = the original key (used for the one-time import), ToolkitKey = our INI key.
        public static readonly SettingRow[] Rows =
        {
            B("RestInBunks", "CrewRestInBunks"),
            B("NearestBunk", "CrewNearestBed"),
            B("BedSharing", "CrewBedSharing"),
            B("BedYield", "CrewBedYield"),
            B("BowDrillLimit", "CrewBowRunCap"),
            I("BowDrillMen", "CrewBowRunMen", 0, 30),
            B("SkipperAttackPeriscope", "CrewSkipperAttackScope"),
            B("NoEatingInAlarm", "CrewNoEatAlarm"),
            B("ControlRoomDiscipline", "CrewControlRoom"),
            B("NearbyJobs", "CrewNearbyJobs"),
            B("WatchClaims", "CrewWatchClaims"),
            B("KeepRunningJobs", "CrewKeepJobs"),
            B("OrderRoom", "CrewOrderRoom"),
            B("HatchOrders", "CrewHatchOrders"),
            B("StuckCooldown", "CrewStuckCooldown"),
            B("VerboseLog", "CrewAILog"),
        };

        public static string Status { get; private set; }

        public static void Load()
        {
            var notes = new StringBuilder();
            try
            {
                notes.Append(CrewAIMigration.RunOnce(Rows));
                notes.Append("Werte aus UboatToolkit.ini");
            }
            catch (Exception e) { notes.Append("Einstellungen lesen fehlgeschlagen: " + e.GetType().Name + ": " + e.Message); }
            Status = notes.ToString();
        }

        public static string Summary()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Rows.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                var o = UboatToolkit.Settings.Get(Rows[i].ToolkitKey);
                sb.Append(Rows[i].ToolkitKey).Append('=');
                if (o == null) sb.Append('?');
                else if (Rows[i].IsInt) sb.Append(Convert.ToInt32(o.Value));
                else sb.Append(o.On);
            }
            return sb.ToString();
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/CrewAi/CrewAITransit.cs
// ====================================================================================================
// Crew-KI (Teil des UBOAT Toolkit): CrewAITransit. Schalter: F10 > QoL > "Crew-KI".

namespace UboatToolkit.Modules.CrewAi
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Items;

    internal static class TransitToggles
    {
        public static readonly bool Enabled = true;

        public const int MaxTransitLines = 400;

        public const float SummaryInterval = 300f;

        public const string ControlRoomName = "Control";
    }

    internal static class TransitTracker
    {
        private static readonly Dictionary<int, Compartment> _lastCompartment =
            new Dictionary<int, Compartment>();
        private static readonly Dictionary<string, int> _perCharacter = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> _perBehaviour = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> _viaControl = new Dictionary<string, int>();
        private static readonly object _lock = new object();

        private static int _lines;
        private static float _lastSummary;
        private static Compartment _controlRoom;
        private static bool _controlSearched;

        private static Compartment ControlRoom()
        {
            if (_controlSearched) return _controlRoom;
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<Compartment>();
                if (all != null)
                {
                    for (int i = 0; i < all.Length; i++)
                    {
                        if (all[i] == null) continue;
                        string n = null;
                        try { n = all[i].name; } catch { }
                        if (!string.IsNullOrEmpty(n) &&
                            n.IndexOf(TransitToggles.ControlRoomName,
                                      StringComparison.OrdinalIgnoreCase) >= 0)
                        { _controlRoom = all[i]; break; }
                    }
                }
                _controlSearched = true;
            }
            catch { _controlSearched = true; }
            return _controlRoom;
        }

        private static readonly Dictionary<int, Compartment> _enteredControlFrom =
            new Dictionary<int, Compartment>();

        private static int ScoreControlRoom(PlayableCharacter c, Compartment from, Compartment to)
        {
            try
            {
                var cr = ControlRoom();
                if (cr == null || c == null) return 0;
                int id = c.GetInstanceID();

                if (to == cr)
                {
                    lock (_lock) { _enteredControlFrom[id] = from; }
                    return 0;                       // scored on the way out
                }
                if (from == cr)
                {
                    Compartment origin = null;
                    lock (_lock)
                    {
                        _enteredControlFrom.TryGetValue(id, out origin);
                        _enteredControlFrom.Remove(id);
                    }
                    if (origin == null) return 0;   // we never saw him arrive
                    return to != origin ? 2 : 1;
                }
                return 0;
            }
            catch { return 0; }
        }

        public static void Note(PlayableCharacter c, Compartment to)
        {
            if (!TransitToggles.Enabled || c == null || to == null) return;
            try
            {
                int id = c.GetInstanceID();
                Compartment from;
                lock (_lock)
                {
                    _lastCompartment.TryGetValue(id, out from);
                    _lastCompartment[id] = to;
                }
                if (from == null || from == to) return;   // first sighting, or no move

                string who = "unknown";
                try { who = c.Name ?? "unknown"; } catch { }
                string behaviour = BehaviourCooldown.LastBehaviourOf(who) ?? "?";
                int crScore = ScoreControlRoom(c, from, to);
                bool passThrough = crScore == 2;
                bool inAndBack = crScore == 1;
                int hops = CompartmentGraph.Hops(from, to);

                OptCounters.CompartmentTransits++;
                if (passThrough) OptCounters.ControlRoomPassThroughs++;
                if (inAndBack) OptCounters.ControlRoomInAndBack++;

                lock (_lock)
                {
                    Bump(_perCharacter, who);
                    Bump(_perBehaviour, behaviour);
                    if (passThrough) Bump(_viaControl, behaviour);
                }

                if (_lines < TransitToggles.MaxTransitLines)
                {
                    _lines++;
                    DiagLog.Write("[Transit] " + who + " | " + SafeName(from) + " -> " + SafeName(to) +
                                  " (" + hops + " hop" + (hops == 1 ? "" : "s") + ")" +
                                  (passThrough ? " CONTROL-PASSTHROUGH" : inAndBack ? " CONTROL-RETURN" : "") +
                                  " | behaviour=" + behaviour +
                                  " | " + Needs(c));
                }

                MaybeSummary();
            }
            catch (Exception e) { DiagLog.WriteError("Transit.Note", e); }
        }

        private static readonly FieldInfo NeedsField =
            AccessTools.Field(typeof(PlayableCharacter), "needs");

        private static string Needs(PlayableCharacter c)
        {
            try
            {
                if (NeedsField == null) return "needs=?";
                var arr = NeedsField.GetValue(c) as Array;
                if (arr == null || arr.Length == 0) return "needs=none";
                var sb = new System.Text.StringBuilder("needs=[");
                for (int i = 0; i < arr.Length; i++)
                {
                    var n = arr.GetValue(i);
                    if (n == null) continue;
                    if (i > 0) sb.Append(' ');
                    float stress = 0f;
                    try
                    {
                        var p = AccessTools.Property(n.GetType(), "BiasedStress");
                        if (p != null) stress = (float)p.GetValue(n, null);
                    }
                    catch { }
                    sb.Append(i).Append(':').Append(stress.ToString("F2"));
                }
                sb.Append(']');
                return sb.ToString();
            }
            catch { return "needs=?"; }
        }

        private static void Bump(Dictionary<string, int> d, string k)
        {
            if (string.IsNullOrEmpty(k)) return;
            int n; d.TryGetValue(k, out n); d[k] = n + 1;
        }

        private static void MaybeSummary()
        {
            try
            {
                float now = Time.unscaledTime;
                if (now - _lastSummary < TransitToggles.SummaryInterval) return;
                _lastSummary = now;
                lock (_lock)
                {
                    DiagLog.Write("[Transit:Summary] total=" + OptCounters.CompartmentTransits +
                                  " controlPassThroughs=" + OptCounters.ControlRoomPassThroughs +
                                  " controlInAndBack=" + OptCounters.ControlRoomInAndBack +
                                  " | by behaviour: " + Top(_perBehaviour, 8));
                    DiagLog.Write("[Transit:Summary] control PASS-THROUGHS by behaviour: " + Top(_viaControl, 8));
                    DiagLog.Write("[Transit:Summary] busiest movers: " + Top(_perCharacter, 8));
                }
            }
            catch { }
        }

        private static string Top(Dictionary<string, int> d, int n)
        {
            try
            {
                var list = new List<KeyValuePair<string, int>>(d);
                list.Sort((x, y) => y.Value.CompareTo(x.Value));
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < n && i < list.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(list[i].Key).Append('=').Append(list[i].Value);
                }
                if (list.Count == 0) sb.Append("(none)");
                return sb.ToString();
            }
            catch { return "(error)"; }
        }

        private static string SafeName(Compartment c)
        {
            try { return c != null ? c.name : "null"; } catch { return "?"; }
        }
    }

    [HarmonyPatch(typeof(Compartment), "AddCharacter", new Type[] { typeof(PlayableCharacter) })]
    internal static class Patch_Transit_AddCharacter
    {
        static void Postfix(Compartment __instance, PlayableCharacter character)
        {
            if (!TransitToggles.Enabled) return;
            try { TransitTracker.Note(character, __instance); }
            catch (Exception e) { DiagLog.WriteError("Transit.AddCharacter", e); }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/CrewAi/CrewAIWatchClaims.cs
// ====================================================================================================
// Crew-KI (Teil des UBOAT Toolkit): CrewAIWatchClaims. Schalter: F10 > QoL > "Crew-KI".

namespace UboatToolkit.Modules.CrewAi
{
    using global::System;
    using global::System.Collections.Generic;
    using global::UnityEngine;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using Activator = global::UBOAT.Game.Scene.Characters.Activators.Activator;

    internal static class WatchClaimToggles
    {
        public static bool Enabled => UboatToolkit.Settings.B("CrewWatchClaims");

        public const float MaxClaimSeconds = 120f;

        public const int MaxLogLines = 40;
    }

    internal static class WatchClaims
    {
        private sealed class Claim
        {
            public PlayableCharacter Who;
            public CharacterAction Action;
            public float Since;      // unscaled
        }

        private static readonly Dictionary<int, Claim> _byActivator = new Dictionary<int, Claim>();
        private static readonly object _lock = new object();
        private static int _logLines;

        public static void NoteQueued(ActionQueue queue, CharacterAction action)
        {
            if (!WatchClaimToggles.Enabled || queue == null || action == null) return;
            try
            {
                if (!(action is ObserveAction)) return;
                PlayableCharacter who = null;
                try { who = queue.Owner; } catch { }
                if (who == null) return;                 // global queue: no single holder
                Activator act = null;
                try { act = action.Activator; } catch { }
                if (act == null) return;

                if (AttackPeriscopeGate.IsAttackPeriscope(act) && AttackPeriscopeGate.Denied(action, who)) return;

                var c = new Claim();
                c.Who = who; c.Action = action; c.Since = Time.unscaledTime;
                lock (_lock) { _byActivator[act.GetInstanceID()] = c; }
                OptCounters.WatchClaimsTaken++;
            }
            catch (Exception e) { DiagLog.WriteError("WatchClaims.NoteQueued", e); }
        }

        public static void NoteRemoved(CharacterAction action)
        {
            if (!WatchClaimToggles.Enabled || action == null) return;
            try
            {
                if (!(action is ObserveAction)) return;
                Activator act = null;
                try { act = action.Activator; } catch { }
                if (act == null) return;
                int id = act.GetInstanceID();
                lock (_lock)
                {
                    Claim c;
                    if (_byActivator.TryGetValue(id, out c) && c.Action == action) _byActivator.Remove(id);
                }
            }
            catch (Exception e) { DiagLog.WriteError("WatchClaims.NoteRemoved", e); }
        }

        private static bool Live(Claim c, float now)
        {
            try
            {
                if (c == null || c.Who == null || c.Action == null) return false;   // Unity null covers destroyed men
                if (c.Who.Action == c.Action) return true;                          // running it now
                if (now - c.Since > WatchClaimToggles.MaxClaimSeconds) return false;
                var q = c.Who.ActionQueue;
                var acts = q != null ? q.Actions : null;
                return acts != null && acts.Contains(c.Action);                     // still queued for him
            }
            catch { return false; }
        }

        public static bool HeldByOther(Activator activator, PlayableCharacter character, out PlayableCharacter holder)
        {
            holder = null;
            if (!WatchClaimToggles.Enabled || activator == null || character == null) return false;
            try
            {
                int id = activator.GetInstanceID();
                float now = Time.unscaledTime;
                Claim c;
                lock (_lock)
                {
                    if (!_byActivator.TryGetValue(id, out c)) return false;
                    if (!Live(c, now)) { _byActivator.Remove(id); return false; }
                }
                if (c.Who == character) return false;
                holder = c.Who;
                return true;
            }
            catch { return false; }
        }

        public static void NoteRefused(Activator activator, PlayableCharacter character, PlayableCharacter holder)
        {
            OptCounters.WatchRefusedClaimed++;
            if (_logLines >= WatchClaimToggles.MaxLogLines) return;
            _logLines++;
            try
            {
                DiagLog.Write("[Watch] '" + (activator != null ? activator.name : "?") + "' is held by " +
                              (holder != null ? holder.Name : "?") + " - not offering it to " +
                              (character != null ? character.Name : "?") +
                              " (vanilla would let him walk up and evict the holder)");
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(ObserveAction), "GetActionProfit",
        new Type[] { typeof(Activator), typeof(PlayableCharacter), typeof(float) })]
    internal static class Patch_WatchClaims_Profit
    {
        static void Postfix(ObserveAction __instance, Activator activator,
                            PlayableCharacter character, ref float __result)
        {
            if (!WatchClaimToggles.Enabled || __result <= 0f) return;
            try
            {
                if (__instance != null && __instance.IsDirectPlayerOrder) return;
                PlayableCharacter holder;
                if (WatchClaims.HeldByOther(activator, character, out holder))
                {
                    __result = -1f;
                    WatchClaims.NoteRefused(activator, character, holder);
                }
            }
            catch (Exception e)
            {
                DiagLog.WriteError("WatchClaims.Profit", e);
            }
        }
    }

    [HarmonyPatch(typeof(ActionQueue), "AddPreStarted", new Type[] { typeof(CharacterAction) })]
    internal static class Patch_WatchClaims_AddPreStarted
    {
        static void Postfix(ActionQueue __instance, CharacterAction action)
        {
            try { WatchClaims.NoteQueued(__instance, action); }
            catch (Exception e) { DiagLog.WriteError("WatchClaims.AddPreStarted", e); }
        }
    }

    [HarmonyPatch(typeof(ActionQueue), "ResetAndAddPreStarted", new Type[] { typeof(CharacterAction) })]
    internal static class Patch_WatchClaims_ResetAndAddPreStarted
    {
        static void Postfix(ActionQueue __instance, CharacterAction action)
        {
            try { WatchClaims.NoteQueued(__instance, action); }
            catch (Exception e) { DiagLog.WriteError("WatchClaims.ResetAndAddPreStarted", e); }
        }
    }

    [HarmonyPatch(typeof(ActionQueue), "Remove", new Type[] { typeof(CharacterAction), typeof(bool) })]
    internal static class Patch_WatchClaims_Remove
    {
        static void Prefix(CharacterAction action)
        {
            try { WatchClaims.NoteRemoved(action); }
            catch (Exception e) { DiagLog.WriteError("WatchClaims.Remove", e); }
        }
    }

    [HarmonyPatch(typeof(ActionQueue), "RemoveAt", new Type[] { typeof(int) })]
    internal static class Patch_WatchClaims_RemoveAt
    {
        static void Prefix(ActionQueue __instance, int index)
        {
            try
            {
                var acts = __instance != null ? __instance.Actions : null;
                if (acts == null || index < 0 || index >= acts.Count) return;
                WatchClaims.NoteRemoved(acts[index]);
            }
            catch (Exception e) { DiagLog.WriteError("WatchClaims.RemoveAt", e); }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/CrewAi/CrewAIWatchFlip.cs
// ====================================================================================================
// Crew-KI (Teil des UBOAT Toolkit): CrewAIWatchFlip. Schalter: F10 > QoL > "Crew-KI".

namespace UboatToolkit.Modules.CrewAi
{
    using global::System;
    using global::System.Collections.Generic;
    using global::UnityEngine;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Actions;

    internal static class WatchFlipToggles
    {
        public static readonly bool Enabled = true;
        public const float FlipSeconds = 10f;     // real
        public const int MaxLogLines = 80;
    }

    internal static class WatchFlips
    {
        private sealed class Holding
        {
            public string Who;
            public float Since;
        }

        private static readonly Dictionary<CharacterAction, Holding> _holder = new Dictionary<CharacterAction, Holding>();
        private static readonly Dictionary<CharacterAction, KeyValuePair<string, float>> _lastLoser =
            new Dictionary<CharacterAction, KeyValuePair<string, float>>();
        private static readonly object _lock = new object();
        private static int _lines;

        private static string Spot(CharacterAction a)
        {
            try { return a.Activator != null ? a.Activator.name : "?"; } catch { return "?"; }
        }

        private static string Name(PlayableCharacter c)
        {
            try { return c != null ? (c.Name ?? "?") : "?"; } catch { return "?"; }
        }

        public static void Added(CharacterAction a, PlayableCharacter c)
        {
            if (!WatchFlipToggles.Enabled || !(a is ObserveAction) || c == null) return;
            try
            {
                float now = Time.unscaledTime;
                string who = Name(c);
                string line = null;
                lock (_lock)
                {
                    var h = new Holding(); h.Who = who; h.Since = now;
                    _holder[a] = h;
                    KeyValuePair<string, float> lost;
                    if (_lastLoser.TryGetValue(a, out lost) && now - lost.Value < 2f && lost.Key != who)
                        line = "[WatchFlip] ObserveAction at '" + Spot(a) + "' handed to " + who +
                               " (" + lost.Key + " lost it " + (now - lost.Value).ToString("F1") + "s earlier)";
                    _lastLoser.Remove(a);
                }
                if (line != null && _lines < WatchFlipToggles.MaxLogLines) { _lines++; DiagLog.Write(line); }
            }
            catch { }
        }

        public static void Removed(CharacterAction a, PlayableCharacter c)
        {
            if (!WatchFlipToggles.Enabled || !(a is ObserveAction) || c == null) return;
            try
            {
                float now = Time.unscaledTime;
                string who = Name(c);
                float held = -1f;
                lock (_lock)
                {
                    Holding h;
                    if (_holder.TryGetValue(a, out h) && h.Who == who) { held = now - h.Since; _holder.Remove(a); }
                    _lastLoser[a] = new KeyValuePair<string, float>(who, now);
                    if (_lastLoser.Count > 64) _lastLoser.Clear();     // bounded
                }
                if (held < 0f || held >= WatchFlipToggles.FlipSeconds) return;

                OptCounters.WatchFlips++;
                if (_lines >= WatchFlipToggles.MaxLogLines) return;
                _lines++;
                string prio = "?", stage = "?", queue = "?";
                try { prio = a.QueuePriority.ToString("F2"); } catch { }
                try { stage = a.Stage.ToString(); } catch { }
                try { var q = a.ActionQueue; queue = q == null ? "none" : (q.Owner != null ? "personal" : "group/global"); } catch { }
                string sched = "?", job = "?";
                try { sched = c.Schedule != null ? c.Schedule.CurrentBehaviour.ToString() : "none"; } catch { }
                try { job = a.SourceJob != null ? a.SourceJob.GetType().Name : "none"; } catch { }
                DiagLog.Write("[WatchFlip] " + who + " lost ObserveAction at '" + Spot(a) + "' after " +
                              held.ToString("F1") + "s (queuePriority " + prio + ", stage " + stage + ", queue " + queue +
                              ", job " + job + ", his schedule " + sched + ")");
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(CharacterAction), "AddCharacter", new Type[] { typeof(PlayableCharacter) })]
    internal static class Patch_WatchFlip_Add
    {
        static void Postfix(CharacterAction __instance, PlayableCharacter character)
        {
            try { WatchFlips.Added(__instance, character); } catch { }
        }
    }

    [HarmonyPatch(typeof(CharacterAction), "RemoveCharacter", new Type[] { typeof(PlayableCharacter) })]
    internal static class Patch_WatchFlip_Remove
    {
        static void Prefix(CharacterAction __instance, PlayableCharacter character)
        {
            try { WatchFlips.Removed(__instance, character); } catch { }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/CrewAi/SkipperPeriscope.cs
// ====================================================================================================
// Crew-KI (Teil des UBOAT Toolkit): SkipperPeriscope. Schalter: F10 > QoL > "Crew-KI".

namespace UboatToolkit.Modules.CrewAi
{
    using global::System;
    using global::HarmonyLib;
    using global::UBOAT.Game;
    using global::UBOAT.Game.Scene;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using Activator = global::UBOAT.Game.Scene.Characters.Activators.Activator; // System.Activator also in scope

    internal enum PeriscopePolicy
    {
        Off,            // no C# patch; data sheet only
        BlockOfficers,  // default - sailors keep the watch station, officers refused
        SkipperOnly     // v0.5 behaviour
    }

    internal static class PeriscopeToggles
    {
        public static PeriscopePolicy Policy => UboatToolkit.Settings.B("CrewScopeNoOfficers") ? PeriscopePolicy.BlockOfficers : PeriscopePolicy.Off;

        public static bool AttackPeriscopeSkipperOnly => UboatToolkit.Settings.B("CrewSkipperAttackScope");

        public const string PeriscopeActivatorTag = "Periscope";

        public const string AttackPeriscopeObjectName = "Attack Periscope";

        public static readonly bool AllowDirectPlayerOrder = true;

        public static readonly bool LogWhoUsesIt = true;
    }

    internal static class PeriscopeLog
    {
        private static readonly System.Collections.Generic.HashSet<string> _seen =
            new System.Collections.Generic.HashSet<string>();
        private static readonly object _lock = new object();

        public static void NoteOnce(PlayableCharacter c, string what)
        {
            if (!PeriscopeToggles.LogWhoUsesIt || c == null) return;
            try
            {
                string name = c.Name ?? "unknown";
                string key = what + "|" + name;
                lock (_lock) { if (!_seen.Add(key)) return; }

                string role = "?";
                try { role = c.Role != null ? c.Role.ToString() : "null"; } catch { }
                DiagLog.Write("[Periscope] " + what + " | character=" + name +
                              " role=" + role + " officer=" + SafeBool(c, true) +
                              " skipper=" + SafeBool(c, false));
            }
            catch (Exception e) { DiagLog.WriteError("Periscope.NoteOnce", e); }
        }

        private static string SafeBool(PlayableCharacter c, bool officer)
        {
            try { return (officer ? c.IsOfficer : c.IsSkipper).ToString(); }
            catch { return "?"; }
        }

        public static bool IsDenied(PlayableCharacter c)
        {
            if (c == null) return false;
            switch (PeriscopeToggles.Policy)
            {
                case PeriscopePolicy.Off:
                    return false;
                case PeriscopePolicy.SkipperOnly:
                    return !c.IsSkipper;
                case PeriscopePolicy.BlockOfficers:
                    return c.IsOfficer && !c.IsSkipper;
                default:
                    return false;
            }
        }
    }

    [HarmonyPatch(typeof(BasicObservationPeriscopeUseAction), "GetActionProfit",
        new Type[] { typeof(Activator), typeof(PlayableCharacter) })]
    internal static class Patch_BasicObservationPeriscope_GetActionProfit
    {
        static void Postfix(PlayableCharacter character, ref ActionProfit __result)
        {
            try
            {
                if (character == null) return;
                PeriscopeLog.NoteOnce(character, "scored");
                if (PeriscopeToggles.Policy == PeriscopePolicy.Off) return;

                if (PeriscopeLog.IsDenied(character))
                {
                    __result = new ActionProfit(__result.ProfitForPlayer, 0f);
                }
            }
            catch (Exception e)
            {
                DiagLog.WriteError("Periscope.GetActionProfit", e);
            }
        }
    }

    [HarmonyPatch(typeof(BasicObservationPeriscopeUseAction), "StartCharacter",
        new Type[] { typeof(PlayableCharacter) })]
    internal static class Patch_BasicObservationPeriscope_StartCharacter
    {
        static bool Prefix(PlayableCharacter character)
        {
            try
            {
                if (character == null) return true;
                PeriscopeLog.NoteOnce(character, "started");
                if (PeriscopeToggles.Policy == PeriscopePolicy.Off) return true;

                if (PeriscopeLog.IsDenied(character))
                {
                    DiagLog.Write("[Periscope] BLOCKED start | character=" + (character.Name ?? "unknown") +
                                  " policy=" + PeriscopeToggles.Policy);
                    return false; // skip the original StartCharacter entirely
                }
            }
            catch (Exception e)
            {
                DiagLog.WriteError("Periscope.StartCharacter", e);
            }
            return true;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/CrewAiThrottle.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::HarmonyLib;
    using global::UBOAT.Game.Core.AI;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UnityEngine;

    // 0.3.70 (DoMe: relieve the main core). Measured: the per-frame update of all crew members costs about 3.9 s per
    // minute with 130+ men (PlayableCharacter.Update + InteractiveCharacter.Update). Every man runs his AI every frame.
    // Men who sleep in a bunk and are not on screen now run it only every x-th frame ("CrewAiSleepEvery").
    // Only the per-frame call is thinned out; direct calls of the game (alarm, stories, cutscenes) always run.
    // Once per minute the log shows the real AI cost, so the effect is measured, not guessed.
    public class CrewAiThrottle : ModuleBase
    {
        internal static double Seconds;
        internal static int Calls, Skipped;
        internal static int Every = 1;
        internal static bool Settle = true;
        internal static float LoadingUntil;
        internal static int Held, PathInvalid;
        private long lastThrottled, lastNeeded;
        private float nextLog, nextLoadCheck;

        public override void Frame()
        {
            Settle = Settings.B("CrewAiWaitLoad");
            if (!Settle || Time.unscaledTime < nextLoadCheck) return;
            nextLoadCheck = Time.unscaledTime + 0.25f;
            if (SaveLoading()) LoadingUntil = Time.unscaledTime + 3f;
        }

        private static bool SaveLoading()
        {
            try { var sm = DWS.Common.InjectionFramework.ScriptableObjectSingleton.LoadSingleton<UBOAT.Game.Core.Serialization.SavesManager>(); return sm != null && sm.IsLoading; } catch { return false; }
        }

        public override void Tick()
        {
            Every = Mathf.Clamp(Mathf.RoundToInt(Settings.F("CrewAiSleepEvery")), 1, 10);
            if (Time.unscaledTime < nextLog) return;
            if (nextLog > 0f && Calls + Skipped > 0)
                Util.Log("Crew-KI: " + (Seconds * 1000.0).ToString("0") + " ms pro Minute in " + Calls + " Aufrufen, " + Skipped + " übersprungen (schlafend, nicht sichtbar)");
            {
                long t = CrewAi.OptCounters.GoToThrottled, n = CrewAi.OptCounters.GoToNeeded;
                if (nextLog > 0f) Util.Log("Crew-Wege: " + PathInvalid + " mal \"Weg ungültig\" in der Minute; Weg-Wiederholungen: " + (t - lastThrottled) + " überflüssige geschluckt, " + (n - lastNeeded) + " nötige durchgelassen; " + CrewWarp_Patch.Warped + " Mann an den Platz gesetzt"); CrewWarp_Patch.Warped = 0;
                lastThrottled = t; lastNeeded = n; PathInvalid = 0;
            }
            if (Held > 0) { Util.Log("Crew-KI: " + Held + " Aufrufe angehalten, weil das Boot noch lädt"); Held = 0; }
            nextLog = Time.unscaledTime + 60f;
            Seconds = 0; Calls = 0; Skipped = 0;
        }
    }

    [HarmonyPatch(typeof(CharacterAI), nameof(CharacterAI.UpdateAI))]
    internal static class CrewAiThrottle_Patch
    {
        private static readonly Dictionary<CharacterAI, PlayableCharacter> owners = new Dictionary<CharacterAI, PlayableCharacter>();

        private static bool Prefix(CharacterAI __instance, bool ignoreIsUpdatingFlag, out double __state)
        {
            __state = 0;
            if (!ignoreIsUpdatingFlag) return true;   // direct call of the game: never touched
            // 0.3.91 (DoMe: console flickers while loading, log full of crew-AI null errors on the Warthog): while a save
            // is still loading and in the first seconds of a new boat the crew AI of the player's boat waits, so it does
            // not look for beds, food or free space before the interior exists.
            // 0.4.29 (player-only rule): only figures of the player's crew list are held or thinned, nobody else.
            if (!owners.TryGetValue(__instance, out var pc))
            {
                if (owners.Count > 2000) owners.Clear();
                pc = __instance.GetComponent<PlayableCharacter>();
                owners[__instance] = pc;
            }
            if (pc == null) return true;
            if (CrewAiThrottle.Settle && (Time.unscaledTime < CrewAiThrottle.LoadingUntil || (Util.PlayerShip != null && !Util.ShipReady))) { CrewAiThrottle.Held++; return false; }
            int every = CrewAiThrottle.Every;
            if (every > 1)
            {
                if (pc != null && !pc.IsVisible && pc.Action is SleepAction && (Time.frameCount + (__instance.GetInstanceID() & 0x3ff)) % every != 0)
                {
                    CrewAiThrottle.Skipped++;
                    return false;
                }
            }
            __state = Time.realtimeSinceStartupAsDouble;
            return true;
        }

        private static void Postfix(double __state)
        {
            if (__state == 0) return;
            CrewAiThrottle.Seconds += Time.realtimeSinceStartupAsDouble - __state;
            CrewAiThrottle.Calls++;
        }
    }
}

namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::HarmonyLib;
    using global::UBOAT.Game.Core.AI;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UnityEngine;

    // 0.4.17: counts how often the game gives a walking target up as unreachable (proof line "Crew-Wege").
    [HarmonyPatch(typeof(CharacterAI), "LogAction", new System.Type[] { typeof(string) })]
    internal static class CrewPathInvalid_Patch
    {
        private static void Prefix(string text)
        {
            if (text != null && text.StartsWith("Path to target is invalid", System.StringComparison.Ordinal)) CrewAiThrottle.PathInvalid++;
        }
    }
}

namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::HarmonyLib;
    using global::UBOAT.Game.Core.AI;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UnityEngine;

    // 0.4.18 (DoMe: "können wir ihn auch teleportieren?"). When the game gives a walking target up as unreachable it
    // cancels the walk and the man's job with it. The game has a second mode for exactly this case (used in stories):
    // set the man onto the target. For player crew with a job or an order and a real place as target (a slot at a
    // station, a bunk ...) that mode is used now, so the post gets manned instead of staying empty.
    [HarmonyPatch(typeof(UBOAT.Game.Core.AI.Behaviours.GoToBehaviour), "Cancel")]
    internal static class CrewWarp_Patch
    {
        private static readonly AccessTools.FieldRef<UBOAT.Game.Core.AI.Behaviours.GoToBehaviour, UBOAT.Game.Core.AI.Behaviours.GoToBehaviour.TargetUnreachable> mode =
            AccessTools.FieldRefAccess<UBOAT.Game.Core.AI.Behaviours.GoToBehaviour, UBOAT.Game.Core.AI.Behaviours.GoToBehaviour.TargetUnreachable>("targetUnreachableAction");
        private static readonly AccessTools.FieldRef<UBOAT.Game.Core.AI.Behaviours.GoToBehaviour, Transform> place =
            AccessTools.FieldRefAccess<UBOAT.Game.Core.AI.Behaviours.GoToBehaviour, Transform>("loggingTarget");
        private static readonly System.Reflection.FieldInfo navField = AccessTools.Field(typeof(UBOAT.Game.Core.AI.Behaviours.GoToBehaviour), "navigator");
        internal static int Warped;
        private static int lines;

        [HarmonyPriority(Priority.Last)]
        private static void Prefix(UBOAT.Game.Core.AI.Behaviours.GoToBehaviour __instance)
        {
            try
            {
                if (!Settings.B("CrewWarpNoPath") || __instance == null) return;
                if (mode(__instance) != UBOAT.Game.Core.AI.Behaviours.GoToBehaviour.TargetUnreachable.CancelBehaviour) return;
                var t = place(__instance);
                if (t == null) return;                                   // no real place (free walking): leave it to the game
                var nav = navField != null ? navField.GetValue(__instance) as UBOAT.Game.Scene.Characters.Navigation.NavMeshNavigator : null;
                var pc = nav != null ? nav.Character as PlayableCharacter : null;
                if (pc == null || pc.Action == null) return;             // only our crew, only with a job or an order
                mode(__instance) = UBOAT.Game.Core.AI.Behaviours.GoToBehaviour.TargetUnreachable.Warp;
                Warped++;
                if (lines < 80) { lines++; Util.Log("Crew-Wege: " + pc.Name + " findet keinen Weg zu \"" + t.name + "\"" + (t.parent != null ? " (" + t.parent.name + ")" : "") + " und wird hingesetzt (Aufgabe " + pc.Action.GetType().Name + ")"); }
            }
            catch (System.Exception e) { if (lines < 80) { lines++; Util.Log("Crew-Wege: Hinsetzen Fehler " + e.GetType().Name); } }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/CrewCheats.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Navigation;
    using global::UBOAT.Game.Scene.Items;
    using global::UnityEngine;

    // Cheats: crew walks faster (#32), crew cannot die / lose health, supplies never run out (#10).
    public class CrewCheats : ModuleBase
    {
        private PlayerCrew crew;

        public override void Tick()
        {
            if (crew == null) crew = Object.FindObjectOfType<PlayerCrew>();
            if (crew == null) return;
            float f = Mathf.Max(1f, Settings.F("CheatCrewWalkSpeed"));
            foreach (var c in crew.Characters)
            {
                if (c == null) continue;
                var nav = c.GetComponent<NavMeshNavigator>();
                if (nav != null) Util.ScaleParameter(nav.SpeedMultiplier, f);
            }
        }
    }

    [HarmonyPatch(typeof(PlayableCharacter), "SetHealth")]
    internal static class CrewCheats_HealthPatch
    {
        private static void Prefix(PlayableCharacter __instance, ref float value)
        {
            bool noWounds = Settings.B("CheatNoWounds");
            if (!noWounds && !Settings.B("CheatImmortalCrew")) return;
            if (__instance.Data == null) return;
            var t = __instance.Data.CharacterType;
            if (t != PlayableCharacterData.Type.Sailor && t != PlayableCharacterData.Type.Officer && t != PlayableCharacterData.Type.Skipper) return;
            if (noWounds) { if (value < __instance.Health) value = __instance.Health; }
            else if (value <= 0f) value = 0.01f;   // immortal only: health may drop, but never to death
        }
    }

    [HarmonyPatch(typeof(PlayableCharacter), "BleedoutHealth", MethodType.Setter)]
    internal static class CrewCheats_BleedoutPatch
    {
        private static void Prefix(PlayableCharacter __instance, ref float value)
        {
            if (!Settings.B("CheatImmortalCrew") && !Settings.B("CheatNoWounds")) return;
            if (value < __instance.BleedoutHealth) value = __instance.BleedoutHealth;
        }
    }

    // Supplies on the boat are never used up (food, spare parts, medkits ...). Cooking still works.
    [HarmonyPatch(typeof(Cargo), "Consume")]
    internal static class CrewCheats_SupplyPatch
    {
        private static bool Prefix(Cargo __instance, float count)
        {
            if (!Settings.B("CheatNoSupplyUse") || count <= 0f) return true;
            return !Util.IsPlayers(__instance);
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/CrewFill.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Linq;
    using global::HarmonyLib;
    using global::UBOAT.Game.Sandbox;
    using global::UBOAT.Game.Scene.Characters;
    using global::UnityEngine;

    // QoL #37: full crew from the start. Before the first patrol, new sailors are recruited
    // the same way the game does it at a new start (generated "German Sailor" added to the career)
    // until the crew roster has the wanted size. Only runs in the real game scene with a boat.
    public class CrewFill : ModuleBase
    {
        private PlayerCareer career;
        private CharacterGenerator generator;
        private int failures;

        private int pendingSailors;

        public override void Tick()
        {
            if (Settings.B("CheatAddSailors"))
            {
                Settings.Set("CheatAddSailors", 0);
                pendingSailors += Mathf.Max(1, Mathf.RoundToInt(Settings.F("CheatAddSailorsAmount")));
            }
            if (pendingSailors > 0) Util.Safe("AddSailors", AddSailors);
            if (!Settings.B("FullCrewAtStart") || failures >= 3) return;
            if (!Util.ShipReady) return;                            // not in main menu / commander creation
            if (career == null) career = Util.Career;
            if (career == null) return;
            if (!Traverse.Create(career).Field("beforeFirstPatrol").GetValue<bool>()) return;
            if (generator == null) generator = Resources.FindObjectsOfTypeAll<CharacterGenerator>().FirstOrDefault();
            if (generator == null) return;

            int target = Mathf.RoundToInt(Settings.F("FullCrewSize"));
            int have = career.Characters.Count(c => c != null && (c.CharacterType == PlayableCharacterData.Type.Sailor || c.CharacterType == PlayableCharacterData.Type.Officer || c.CharacterType == PlayableCharacterData.Type.Skipper));
            int added = 0;
            try
            {
                while (have + added < target && added < 2)   // two per second, so new men get placed in the boat properly, so the game is not stalled
                {
                    var data = generator.GenerateCharacterData("German Sailor", PlayableCharacterData.Type.Sailor);
                    if (data == null) { failures++; break; }
                    career.AddCharacter(data);
                    added++;
                }
            }
            catch (System.Exception e) { failures++; Util.Log("Volle Crew: Fehler beim Anwerben (" + failures + "/3): " + e.Message); }
            if (added > 0) Util.Log("Volle Crew: " + added + " Matrosen angeworben, jetzt " + (have + added) + " von " + target);
        }
    
        // Cheat button: recruit N sailors at any time, two per second (same way as above).
        private void AddSailors()
        {
            if (!Util.ShipReady) return;
            if (career == null) career = Util.Career;
            if (generator == null) generator = Resources.FindObjectsOfTypeAll<CharacterGenerator>().FirstOrDefault();
            if (career == null || generator == null) return;
            for (int i = 0; i < 2 && pendingSailors > 0; i++)
            {
                var data = generator.GenerateCharacterData("German Sailor", PlayableCharacterData.Type.Sailor);
                if (data == null) { pendingSailors = 0; ToolkitRunner.ShowNotice("Matrosen anwerben fehlgeschlagen", 4f); return; }
                career.AddCharacter(data);
                pendingSailors--;
            }
            if (pendingSailors == 0) ToolkitRunner.ShowNotice("Neue Matrosen sind an Bord", 4f);
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/CrewGear.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::HarmonyLib;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Scene.Items;

    // Crew gear and wounded handling.
    // H: injured crew is not carried away automatically.  J: rebreathers last longer.
    public class CrewGear : ModuleBase
    {
        public override void Tick()
        {
            float f = Settings.F("RebreatherDurability");
            foreach (var r in Util.Find<Rebreather>())
                Util.ScaleParameter(Traverse.Create(r).Field("durability").GetValue<Parameter>(), f);
        }
    }

    // Only the "carry wounded" part is blocked: the game skips carrying when no bed is free.
    // Pulling divers back on board keeps working.
    [HarmonyPatch(typeof(UBOAT.Game.Scene.Tasks.AutoCarryWoundedToSafety), "DoUpdate")]
    internal static class CrewGear_NoAutoCarryPatch
    {
        internal static bool Inside;
        private static void Prefix() { Inside = Settings.B("NoAutoCarryWounded"); }
        private static void Finalizer() { Inside = false; }
    }

    [HarmonyPatch(typeof(UBOAT.Game.Scene.Entities.PlayerShip), "HasFreeBed")]
    internal static class CrewGear_NoFreeBedPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!CrewGear_NoAutoCarryPatch.Inside) return true;
            __result = false;
            return false;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/CrewLod.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::HarmonyLib;
    using global::UBOAT.Game.Core.AI;
    using global::UBOAT.Game.Scene.Characters;
    using global::UnityEngine;

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

// ====================================================================================================
// Abschnitt: Modules/CrewSkills.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::System.Reflection.Emit;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Characters.Activators;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Items;
    using global::UnityEngine;

    // Better crew at the guns and at the hydrophone (QoL, not cheats: the game's own mechanics, just tuned).
    //
    // Guns (only your own boat, only when the crew aims, never when you aim yourself):
    //  * Ranging in: the game raises the hit chance a little with every shot ("einschießen").
    //    GunnerRangingSpeed multiplies that step.
    //  * New target: the game throws most of the ranging away (keeps 55 %). GunnerKeepRanging sets how much stays.
    //  * Target not seen with the eyes (night, only heard / radar): the game adds extra spread.
    //    GunnerNightPenalty sets how much of that extra spread is used (100 % = game).
    // Hydrophone (only while a crewman sits at the listening room):
    //  * HorcherRange: hears ships further away, so more and earlier reports.
    //  * HorcherBearing: contacts on the map get a more exact bearing.
    public class CrewSkills : ModuleBase
    {
        internal const string ModName = "Toolkit Horcher";
        internal static bool HydrophoneManned;
        private static readonly List<Hydrophone> boosted = new List<Hydrophone>();
        private float lastRange = -1f;

        public override void Tick()
        {
            var ship = Util.PlayerShip;
            float pct = Mathf.Max(0f, Settings.F("HorcherRange"));
            bool manned = false;
            Hydrophone hydro = null;
            if (ship != null)
                foreach (var act in Util.Find<Activator>())
                {
                    if (act == null || !(act.AssociatedEquipment is ListeningRoom lr) || !Util.IsPlayers(lr)) continue;
                    hydro = lr.Hydrophone;
                    manned = act.Users.Count > 0;
                    break;
                }
            HydrophoneManned = manned;

            bool want = manned && hydro != null && pct > 0f;
            // 0.4.19: a bonus stored in the savegame stays on the hydrophone after loading although nobody listens
            if (!want && hydro != null && boosted.Count == 0) { Util.DropScale(hydro.DetectionRange, ModName); Util.DropScale(hydro.GroupDetectionRange, ModName); }
            // Drop the bonus when nobody listens, the setting changed or the hydrophone is another one.
            if (boosted.Count > 0 && (!want || pct != lastRange || !boosted.Contains(hydro)))
            {
                foreach (var h in boosted)
                    if (h != null) { Util.DropScale(h.DetectionRange, ModName); Util.DropScale(h.GroupDetectionRange, ModName); }
                boosted.Clear();
            }
            if (want && boosted.Count == 0)
            {
                float f = 1f + pct / 100f;
                Util.OneScale(hydro.DetectionRange, ModName).Value = f;
                Util.OneScale(hydro.GroupDetectionRange, ModName).Value = f;
                boosted.Add(hydro);
                lastRange = pct;
                if (Settings.B("CrewSkillLog")) Util.Log("Hydrophon: Reichweite x" + f.ToString("0.00") + " (Hydrophon besetzt)");
            }
        }
    }

    // More exact bearings while the hydrophone is manned.
    [HarmonyPatch(typeof(SingularContactInfo), "BearingInaccuracy", MethodType.Setter)]
    internal static class CrewSkills_BearingPatch
    {
        private static void Prefix(ref float value)
        {
            if (!CrewSkills.HydrophoneManned) return;
            float pct = Mathf.Clamp(Settings.F("HorcherBearing"), 0f, 90f);
            if (pct > 0f) value *= 1f - pct / 100f;
        }
    }

    // Gun ranging: bigger step per shot.
    [HarmonyPatch(typeof(Gun), "Fire")]
    internal static class CrewSkills_RangingPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(Gun __instance, bool ___manualAim, float ___currentAccuracy, out float __state)
        {
            __state = -1f;
            if (!___manualAim && Util.IsPlayers(__instance)) __state = ___currentAccuracy;
        }

        private static void Postfix(Gun __instance, ref float ___currentAccuracy, float __state)
        {
            if (__state < 0f) return;
            float mul = Settings.F("GunnerRangingSpeed");
            float step = ___currentAccuracy - __state;
            if (mul > 1f && step > 0f) ___currentAccuracy = Mathf.Min(1f, __state + step * mul);
        }

        // The extra spread when the target is not seen with the eyes (constant 0.022 in Gun.Fire).
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            var get = AccessTools.Method(typeof(CrewSkills_RangingPatch), nameof(NightPenalty));
            int n = 0;
            foreach (var ci in code)
            {
                if (ci.opcode == OpCodes.Ldc_R4 && ci.operand is float f && Mathf.Abs(f - 0.022f) < 1e-6f)
                {
                    var call = new CodeInstruction(OpCodes.Call, get);
                    call.labels.AddRange(ci.labels);
                    call.blocks.AddRange(ci.blocks);
                    yield return call;
                    n++;
                    continue;
                }
                yield return ci;
            }
            if (n != 1) Util.Log("Schützen: Nacht-Streuung im Spiel nicht gefunden (" + n + "), bleibt wie im Spiel");
        }

        internal static float NightPenalty()
        {
            // Weapons_FirePatch.Current is set only while one of OUR guns fires.
            if (Weapons_FirePatch.Current == null) return 0.022f;
            if (Settings.B("CheatPerfectAim")) return 0f;
            return 0.022f * Mathf.Max(0f, Settings.F("GunnerNightPenalty")) / 100f;
        }
    }

    // New target: keep more of the ranging.
    [HarmonyPatch(typeof(Gun), "TargetOverride", MethodType.Setter)]
    internal static class CrewSkills_TargetPatch
    {
        private static void Prefix(Gun __instance, float ___currentAccuracy, Entity ___targetOverride, Entity value, out float __state)
        {
            __state = (___targetOverride != null && ___targetOverride != value && Util.IsPlayers(__instance)) ? ___currentAccuracy : -1f;
        }

        private static void Postfix(ref float ___currentAccuracy, float __state)
        {
            if (__state < 0f) return;
            float keep = Mathf.Clamp(Settings.F("GunnerKeepRanging"), 0f, 100f) / 100f;
            ___currentAccuracy = Mathf.Max(___currentAccuracy, __state * keep);
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/CrewSupplies.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Collections.Generic;
    using global::HarmonyLib;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Items;
    using global::UnityEngine;

    // Air, food and packing when the crew is bigger than normal:
    // - Air: a full crew always reaches the same dive time (default 11 h), whatever the crew limit is.
    //   Fewer men aboard = longer. Optional: prisoners shorten it (up to x % with a full prisoner hold).
    //   Absorbers / snorkel still add on top.
    // - Food: a bigger crew limit does not empty the stores faster (eats like the normal crew, or like x men).
    // - Tight packing skill: every man with it adds +x to the stack size of all stackable items
    //   (instead of the game's one extra slot).
    public class CrewSupplies : ModuleBase
    {
        private const string FoodMod = "Toolkit Food Norm";
        private Kitchen kitchen;
        private int lastPack = int.MinValue;

        public override void Tick()
        {
            var ship = Util.PlayerShip;
            if (ship == null) return;
            Util.Safe("Luft", () => AirNorm.Apply(ship));
            Util.Safe("Essen", Food);
            Util.Safe("Packen", Pack);
        }

        internal static int MaxCrew()
        {
            var c = Util.Career;
            return c == null ? 0 : Mathf.RoundToInt(c.SailorsOnBoardLimit.Value + c.OfficersOnBoardLimit.Value);
        }

        // Normal crew = limits without our own "more officers" bonus.
        internal static int NormalCrew()
        {
            var c = Util.Career;
            if (c == null) return 0;
            var m = c.OfficersOnBoardLimit.GetDeltaModifier("Toolkit Officers", false);
            int v = Mathf.RoundToInt(c.SailorsOnBoardLimit.Value + c.OfficersOnBoardLimit.Value - (m != null ? m.Value : 0f));
            return v > 0 ? v : MaxCrew();
        }

        private void Food()
        {
            if (kitchen == null) kitchen = UnityEngine.Object.FindObjectOfType<Kitchen>();
            if (kitchen == null) return;
            var rate = kitchen.FoodConsumptionRate;
            var mod = Util.OneScale(rate, FoodMod);
            float scale = 1f;
            if (Settings.B("FoodByCrew"))
            {
                int max = MaxCrew();
                int refCrew = Mathf.RoundToInt(Settings.F("FoodRefCrew"));
                if (refCrew <= 0) refCrew = NormalCrew();
                if (max > 0 && refCrew > 0) scale = Mathf.Min(1f, (float)refCrew / max);
            }
            if (Math.Abs(mod.Value - scale) > 1e-6f) mod.Value = scale;
        }

        private static void Inventory_Stack(int delta) { StackData.PackBonus = delta; StackData.Apply(); }

        private void Pack()
        {
            int per = Mathf.RoundToInt(Settings.F("TightPackPerMan"));
            int holders = 0;
            if (per > 0)
                foreach (var crew in Util.Find<PlayerCrew>(30f))
                    if (crew != null && crew.Characters != null)
                        foreach (var pc in crew.Characters)
                            if (pc != null && pc.Data != null && pc.Data.HasStatus(typeof(UBOAT.Game.Scene.Characters.Skills.TightPacking))) holders++;
            int delta = per * holders;
            if (delta == lastPack) return;
            lastPack = delta;
            Inventory_Stack(delta);
            if (holders > 0) Util.Log("Dichtes Packen: " + holders + " Mann, Stapel +" + delta);
        }
    }

    internal static class AirNorm
    {
        private static bool wasOn;
        private static readonly System.Reflection.MethodInfo vanilla = AccessTools.Method(typeof(PlayerShip), "ValidateOxygenBreathModifier");
        internal static void Apply(PlayerShip ship)
        {
            if (ship == null) return;
            if (!Settings.B("AirByCrew"))
            {
                if (wasOn) { wasOn = false; vanilla?.Invoke(ship, null); }   // back to the game's own value
                return;
            }
            wasOn = true;
            double t = Mathf.Max(0.25f, Settings.F("AirHours")) * 3600.0;
            var crew = Util.Find<PlayerCrew>(30f);
            PlayerCrew pc = null; foreach (var c in crew) { pc = c; break; }
            if (pc == null) return;
            if (Settings.B("AirPrisoners"))
            {
                int pris = pc.SurvivorCount;
                var career = Util.Career;
                float cap = career != null ? career.SurvivorsOnBoardLimit.Value : 0f;
                if (pris > 0)
                {
                    float frac = cap > 0f ? Mathf.Min(1f, pris / cap) : 1f;
                    t = Math.Max(60.0, t * (1.0 - Settings.F("AirPrisonerPenalty") / 100.0 * frac));
                }
            }
            int n = pc.Count, max = CrewSupplies.MaxCrew();
            if (n <= 0 || max <= 0) return;
            if (n > max) max = n;
            var mod = Traverse.Create(ship).Field("oxygenBreathModifier").GetValue<Modifier>();
            if (mod == null) return;
            float drain = (float)(-(1.0 / t) * ((double)n / max));
            if (Math.Abs(mod.Value - drain) > 1e-10f) mod.Value = drain;
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "ValidateOxygenBreathModifier")]
    internal static class AirNorm_Patch
    {
        private static void Postfix(PlayerShip __instance) { try { AirNorm.Apply(__instance); } catch { } }
    }

    // The skill now means deeper stacks, so the game's extra slot is not added.
    [HarmonyPatch(typeof(UBOAT.Game.Scene.Characters.Skills.TightPacking), "ValidateAdditionalSlotPresence")]
    internal static class TightPack_NoSlot
    {
        private static bool Prefix() => Settings.F("TightPackPerMan") <= 0f;
    }
}

// ====================================================================================================
// Abschnitt: Modules/DeckCrew.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Statuses;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Items;

    // Cheat "deck crew stays": the boat dives even with men on deck, they stay at the guns and never drown.
    // Guns stay usable while diving; they still fire only as long as the gun itself is above the water.

    // The game refuses to close the tower hatch while someone is on deck (and drowns the injured).
    [HarmonyPatch(typeof(UBOAT.Game.Scene.Tasks.CloseExteriorHatch), "ValidateHatchOpeness")]
    internal static class DeckCrew_HatchPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!Settings.B("CheatDeckCrewStays")) return true;
            var ship = Util.PlayerShip;
            var hatch = ship != null ? ship.GetEquipment<Hatch>("Exterior") : null;
            if (hatch == null) return true;
            hatch.Locked = true;
            __result = true;
            return false;
        }
    }

    // Nobody drowns: "drowning off board" death is blocked.
    [HarmonyPatch(typeof(PlayableCharacterData), nameof(PlayableCharacterData.AddStatus), new[] { typeof(CharacterStatus) })]
    internal static class DeckCrew_NoDrownPatch
    {
        // With "immortal crew" every death is blocked (also deaths the game sets directly, not via health).
        // Immortal also means unhurt: no bleeding wounds.
        private static bool Prefix(CharacterStatus status)
        {
            if (Settings.B("CheatImmortalCrew") && status is Dead) return false;
            if (Settings.B("CheatNoWounds") && status is Bleeding) return false;
            return !(Settings.B("CheatNoDrown") && status is Dead d && d.Cause == DeathCause.DrowningOffBoard);
        }
    }

    // Guns are not switched off when the boat dives.
    [HarmonyPatch(typeof(Gun), "ParentShip_SubmergeStateChanged")]
    internal static class DeckCrew_GunPatch
    {
        private static void Postfix(Gun __instance)
        {
            // 0.4.22 (option audit, player-only rule): only OUR guns. Before, every gun in the scene kept working under
            // water, enemy submarines included.
            if (Settings.B("CheatGunsUnderWater") && Util.IsPlayers(__instance) && !Util.WarthogModOwns(Util.PlayerShip)) __instance.SetDisableFlag(DeviceDisableFlag.Depth, false);
        }
    }

    // Immortal crew does not fall over from hits.
    [HarmonyPatch(typeof(PlayableCharacter), nameof(PlayableCharacter.EnableRagdoll))]
    internal static class DeckCrew_NoRagdollPatch
    {
        private static bool Prefix() => !Settings.B("CheatNoRagdoll");
    }

    // One-shot heal: every crew member gets full health, wounds and illnesses are removed, the fallen stand up.
    public class HealAll : ModuleBase
    {
        public override void Frame()
        {
            if (Keys.Down("KeyHealAll")) { var o = Settings.Get("CheatHealNow"); if (o != null) o.Value = 1; }
        }

        public override void Tick()
        {
            var o = Settings.Get("CheatHealNow");
            if (o == null || !o.On) return;
            o.Value = 0;
            Util.Safe("HealAll", () =>
            {
                var crew = UnityEngine.Object.FindObjectOfType<PlayerCrew>();
                if (crew == null) return;
                int n = 0;
                foreach (var c in crew.Characters)
                {
                    if (c == null || c.Data == null || c.Data.HasStatus<Dead>()) continue;
                    c.Data.RemoveStatuses<Bleeding>();
                    c.Data.RemoveStatuses<Illness>();
                    c.Data.RemoveStatuses<EarlyIllness>();
                    c.Data.RemoveStatuses<Choking>();
                    c.Health = 1f;
                    c.BleedoutHealth = 1f;
                    if (c.Ragdoll) c.DisableRagdoll();
                    n++;
                }
                ToolkitRunner.ShowNotice(n + " Mann geheilt", 4f);
            });
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Medical.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UBOAT.Game.Scene.Characters.Activators;
    using global::UBOAT.Game.Scene.Characters.Skills;
    using global::UBOAT.Game.Scene.Characters.Statuses;
    using global::UBOAT.Game.Scene.Items;
    using global::UBOAT.Game.Scene.Utilities;
    using global::UnityEngine;

    // QoL #29: wounded / sick crew get first aid automatically.
    // The game already does this, but only via the radioman group. If nobody treats a
    // wounded man for a few seconds, the nearest free crew member grabs a MedKit from
    // the ship's storage into his backpack and treats him (game's own FirstAidAction).
    public class Medical : ModuleBase
    {
        private PlayerCrew crew;
        private readonly Dictionary<PlayableCharacter, float> untreatedSince = new Dictionary<PlayableCharacter, float>();
        private float nextCheck;

        public override void Tick()
        {
            if (!Settings.B("AutoFirstAid") || Time.time < nextCheck) return;
            nextCheck = Time.time + 2f;
            if (crew == null) crew = Object.FindObjectOfType<PlayerCrew>();
            if (crew == null) return;

            var chars = crew.Characters;
            for (int i = 0; i < chars.Length; i++)
            {
                var w = chars[i];
                if (w == null || !NeedsAid(w)) { if (w != null) untreatedSince.Remove(w); continue; }
                var act = w.CharacterActivator;
                if (act == null || IsBeingTreated(w, act)) { untreatedSince.Remove(w); continue; }

                if (!untreatedSince.TryGetValue(w, out float since)) { untreatedSince[w] = Time.time; continue; }
                if (Time.time - since < Settings.F("AutoFirstAidDelay")) continue;

                if (SendHelper(w, act)) untreatedSince.Remove(w);
            }
        }

        private static bool NeedsAid(PlayableCharacter c)
        {
            if (c.Data.HasStatus<Dead>()) return false;
            return c.Data.GetStatus<Bleeding>() != null || c.Data.GetStatus<Illness>() != null;
        }

        private static bool IsBeingTreated(PlayableCharacter w, Activator act)
        {
            foreach (var u in act.Users)
                if (u != w) return true;
            return false;
        }

        private static bool IsFree(PlayableCharacter h)
        {
            if (h.Injury || h.Ai == null || !h.Ai.isActiveAndEnabled || h.IsImpersonated || h.IsSelected) return false;
            if (h.Data.HasStatus<Dead>()) return false;
            var a = h.Action;
            return a == null || (a.CanBeStopped && a.Role != null);
        }

        // Lower = preferred. Skills: Medical Training (faster, uses MedKit in hand),
        // radioman, men of a "Medical Coordinator" officer, Runner (walks faster), MedKit already in hand.
        private static float SkillFactor(PlayableCharacter h)
        {
            float f = 1f;
            if (!Settings.B("AutoFirstAidSkills")) return HasMedKit(h) ? 0.25f : 1f;
            if (h.Data.HasStatus<MedicalTraining>()) f *= 0.05f;
            else if (h.Data.HasStatus<Radioman>()) f *= 0.2f;
            if (h.Employer != null && h.Employer.Data.HasStatus("Medical Coordinator")) f *= 0.4f;
            if (h.Data.HasStatus("Runner")) f *= 0.8f;
            if (HasMedKit(h)) f *= 0.5f;
            return f;
        }

        private static bool HasMedKit(PlayableCharacter h) => h.Backpack.Item is MedKit;

        private bool SendHelper(PlayableCharacter w, Activator act)
        {
            PlayableCharacter best = null;
            float bestDist = float.MaxValue;
            bool fetch = Settings.B("AutoFetchMedKit");
            var chars = crew.Characters;
            for (int i = 0; i < chars.Length; i++)
            {
                var h = chars[i];
                if (h == null || h == w || !IsFree(h)) continue;
                bool radioman = h.Data.HasStatus<Radioman>();
                // Radiomen treat from storage; everyone else needs a MedKit in hand (or free hands to fetch one).
                if (!radioman && !HasMedKit(h) && !(fetch && h.Backpack.Item == null)) continue;
                float d = (h.transform.position - w.transform.position).sqrMagnitude;
                d *= SkillFactor(h);
                if (d < bestDist) { bestDist = d; best = h; }
            }
            if (best == null) return false;

            // Fetch a MedKit. A radioman with Medical Training works faster with one in hand,
            // without one he still treats from storage.
            if (!HasMedKit(best) && best.Backpack.Item == null && fetch) GiveMedKit(best);
            if (!HasMedKit(best) && !best.Data.HasStatus<Radioman>())
            {
                LogOnce("Kein Verbandskasten (MedKit) mehr im Lager.");
                return false;
            }

            CharacterActionInfo info = act.GetActionInfo<FirstAidAction>(best, true);
            if (info.ActionTemplate == null || info.State != ActionState.Enabled) return false;

            var action = info.ActionTemplate.Duplicate(act);
            action.QueuePriority = 99f;
            best.StopCurrentAction(true);
            best.StartAction(act, action);
            Util.Log("Erste Hilfe: " + best.Name + " versorgt " + w.Name);
            return true;
        }

        // Take one MedKit out of any storage on the boat into the helper's backpack.
        private static bool GiveMedKit(PlayableCharacter h)
        {
            if (Util.PlayerShip == null) return false;
            foreach (var s in Util.Find<Storage>())
            {
                if (s.IsShop) continue;
                for (int i = Util.Slots(s) - 1; i >= 0; i--)
                {
                    if (s.GetItemAt(i) is MedKit)
                    {
                        StorageUtility.TransferInstantly(s, i, h.Backpack, 0, 1f);
                        return HasMedKit(h);
                    }
                }
            }
            return false;
        }

        private static float lastLog = -999f;
        private static void LogOnce(string t)
        {
            if (Time.time - lastLog < 60f) return;
            lastLog = Time.time;
            Util.Log(t);
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/OfficerRest.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Collections.Generic;
    using global::HarmonyLib;
    using global::UBOAT.Game.Core.AI;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Core.AI.Behaviours;
    using global::UBOAT.Game.Core.Serialization;
    using global::UBOAT.Game.Core.Time;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Items;
    using global::UnityEngine;

    // Officers and bunks:
    // - Overflow: officers only sleep in the few wardroom bunks. If more officers are aboard than those bunks,
    //   a tired officer without a free wardroom bunk gets the game's own sailor sleep and takes a crew bunk.
    //   He gets up when rested, at his work hour, on alarm, on a player order or in port.
    //   Before every save this is removed again (the save stays clean) and put back afterwards.
    // - Hot bunking: if the bunks are too few for the men who use them, sleep in a bunk recovers faster
    //   (needed bunk-hours / available bunk-hours, capped).
    // - Endurance: officers tire slower/faster at stations (factor).
    // - Cards: officer roles without the card game get it (priority x), so off-duty officers play cards.
    public class OfficerRest : ModuleBase
    {
        private class Man { public SleepBehaviour Sleep; public double IdleSince; public bool IdleKnown, ResumeAfterSave; }
        private static readonly Dictionary<PlayableCharacter, Man> men = new Dictionary<PlayableCharacter, Man>();
        private const string HotMod = "Toolkit HotBunk", EndMod = "Toolkit Endurance";
        private const float ForcedRestRelease = 0.4f;
        private static float fOff = 1f, fCrew = 1f, nextPool;
        private static GameTime gameTime;
        private static SavesManager saves;
        private static readonly HashSet<ICharacterRole> cardsChecked = new HashSet<ICharacterRole>();
        private static ICharacterRoleJob cardsTemplate;
        private static object cardsDb;

        private static double Now()
        {
            if (gameTime == null) gameTime = UnityEngine.Object.FindObjectOfType<GameTime>();
            return gameTime != null ? gameTime.Seconds : Time.time;
        }

        public override void Tick()
        {
            var ship = Util.PlayerShip;
            PlayerCrew crew = null; foreach (var c in Util.Find<PlayerCrew>(30f)) { crew = c; break; }
            if (ship == null || crew == null || crew.Characters == null) return;
            HookSaves();
            if (saves != null && (saves.IsSaving || saves.IsLoading)) return;
            Util.Safe("Karten", Cards);
            if (Time.realtimeSinceStartup >= nextPool) { nextPool = Time.realtimeSinceStartup + 4.7f; Pools(ship, crew.Characters); }
            bool blocked = false, docked = false;
            try { blocked = ship.IsCrewAlerted || ship.Alarmed; docked = ship.Docked; } catch { }
            double now = Now();
            foreach (var pc in crew.Characters)
                if (pc != null) Util.Safe("Offiziersschlaf", () => TickMan(ship, pc, now, blocked, docked));
            var gone = new List<PlayableCharacter>();
            foreach (var k in men.Keys) if (k == null) gone.Add(k);
            foreach (var k in gone) men.Remove(k);
        }

        private static void TickMan(PlayerShip ship, PlayableCharacter pc, double now, bool blocked, bool docked)
        {
            var data = pc.Data;
            if (data == null) return;
            if (data.CharacterType == PlayableCharacterData.Type.Sailor && Settings.B("HotBunkSailors"))
            {
                Bed sb = null; try { sb = pc.InteractableSlot != null ? pc.InteractableSlot.Interactable as Bed : null; } catch { }
                if (pc.EnergyGain != null) SetScale(pc.EnergyGain, HotMod, sb != null ? (sb.AvailableForSailors ? fCrew : fOff) : 1f);
                return;
            }
            if (data.CharacterType != PlayableCharacterData.Type.Officer)
            {
                if (men.TryGetValue(pc, out var old)) { Detach(pc, old); men.Remove(pc); Neutral(pc); }
                return;
            }
            var ai = pc.Ai;
            if (ai == null) return;
            if (!men.TryGetValue(pc, out var st)) { st = new Man(); men[pc] = st; }
            EnsureCards(pc.Role);

            // energy factors
            var slot = pc.InteractableSlot;
            Bed bed = null; try { bed = slot != null ? slot.Interactable as Bed : null; } catch { }
            var gain = pc.EnergyGain;
            if (gain != null)
            {
                SetScale(gain, HotMod, bed != null ? (bed.AvailableForSailors ? fCrew : fOff) : 1f);
                float endu = Settings.F("OfficerEndurance");
                SetScale(gain, EndMod, bed == null && Math.Abs(endu - 1f) > 0.0005f && gain.Value < 0f ? endu : 1f);
            }

            if (!Settings.B("OfficerOverflowSleep")) { if (st.Sleep != null) Detach(pc, st); return; }

            var sched = Schedule.Behaviour.Any;
            try { if (pc.Schedule != null) sched = pc.Schedule.CurrentBehaviour; } catch { }
            float energy = pc.Energy, restBelow = Settings.F("OfficerRestBelow"), wakeAt = Mathf.Max(restBelow + 0.1f, Settings.F("OfficerWakeAt"));
            bool forced = IsForcedRest(pc) || pc.IsTired;
            bool duty = sched == Schedule.Behaviour.Work;
            bool order = HasOrder(pc);
            bool hard = blocked || docked || order || pc.IsImpersonated || pc.Injury || pc.NeedsToBeCarried || data.StoryCharacterGroup != null || !ai.enabled;
            bool mustWork = hard || (duty && !(forced && energy <= ForcedRestRelease));
            var q = pc.ActionQueue;
            bool idle = pc.Action == null && (q == null || q.PreStartingAction == null);
            if (!idle || !st.IdleKnown || now < st.IdleSince) { st.IdleSince = now; st.IdleKnown = true; }
            bool lowRest = energy < restBelow && now - st.IdleSince >= Mathf.Max(25f, Settings.F("OfficerIdleBefore"));
            if (st.ResumeAfterSave && (mustWork || energy >= wakeAt)) st.ResumeAfterSave = false;
            bool wants = st.ResumeAfterSave || sched == Schedule.Behaviour.Sleep || forced || lowRest;
            bool canStart = wants && energy < (st.ResumeAfterSave ? wakeAt : wakeAt - 0.05f);

            if (st.Sleep != null)
            {
                if (!Attached(ai, st.Sleep)) { st.Sleep = null; ResetFatigue(pc); }
                else if (mustWork || energy >= wakeAt)
                {
                    Detach(pc, st);
                    // the game only ends forced rest at full energy; an officer woken here would stand around
                    if (energy > ForcedRestRelease && IsForcedRest(pc)) try { pc.RoleOverride = null; } catch { }
                    st.IdleSince = now;
                }
            }
            else if (canStart && !mustWork && idle && !pc.IsSelected)
            {
                Bed free = null; try { free = ship.FindFreeOfficerBed(pc.Navigator, true, false); } catch { }
                if (free == null && ai.GetBehaviour<SleepBehaviour>() == null && ai.BehavioursCount < 28)
                {
                    var fat = pc.GetNeed(NeedType.Fatigue);
                    if (fat == null) return;
                    fat.stress = 1f; fat.profitBonus = 0.25f;   // waiting 1.60 / lying 2.10: above idle, below a player order (2.3)
                    var b = new SleepBehaviour(ai, 1f);
                    ai.AddBehaviour(b);
                    st.Sleep = b; st.ResumeAfterSave = false;
                    Util.Log("Offizier " + data.Name + " schläft in einer Mannschaftskoje (Offizierskojen voll)");
                }
            }
        }

        private static void Pools(PlayerShip ship, PlayableCharacter[] chars)
        {
            int bOff = 0, bCrew = 0, nOff = 0, nCrew = 0;
            try { bOff = ship.OfficerBeds?.Length ?? 0; bCrew = ship.Beds?.Length ?? 0; } catch { }
            // shared bunks (several men per bunk) count as several places
            if (CrewAi.BedSharingToggles.Enabled) bCrew *= CrewAi.BedSharingToggles.MenPerBunk;
            foreach (var pc in chars)
            {
                if (pc == null || pc.Data == null) continue;
                if (pc.Data.CharacterType == PlayableCharacterData.Type.Sailor) nCrew++;
                else if (pc.Data.CharacterType == PlayableCharacterData.Type.Officer) nOff++;
            }
            float max = Mathf.Clamp(Settings.F("HotBunkMax"), 1f, 100f), boost = Mathf.Clamp(Settings.F("HotBunkBoost"), 1f, 100f);
            if (Settings.B("HotBunkAuto")) { fOff = Factor(nOff, bOff, max); fCrew = Factor(nCrew, bCrew, max); }
            else fOff = fCrew = Mathf.Clamp(Settings.F("HotBunkManual"), 0.1f, max);
            fOff *= boost; fCrew *= boost;
        }

        private static float Factor(int n, int b, float max)
        {
            if (b <= 0 || n <= 0) return 1f;
            float need = n * Mathf.Clamp(Settings.F("HotBunkHours"), 1f, 24f);
            float have = 24f * b * Mathf.Clamp(Settings.F("HotBunkUse"), 0.1f, 1f);
            return have <= 0f ? max : Mathf.Clamp(need / have, 1f, max);
        }

        private static void SetScale(Parameter p, string name, float v)
        {
            var m = p.GetScaleModifier(name, false);
            if (m == null) { if (Math.Abs(v - 1f) < 0.0005f) return; m = Util.OneScale(p, name); if (m == null) return; }
            if (Math.Abs(m.Value - v) > 0.0005f) m.Value = v;
        }

        private static void Neutral(PlayableCharacter pc)
        {
            try { var p = pc?.EnergyGain; if (p == null) return; SetScale(p, HotMod, 1f); SetScale(p, EndMod, 1f); } catch { }
        }

        private static bool Attached(CharacterAI ai, AIBehaviour b)
        {
            for (int i = 0; i < ai.BehavioursCount; i++) if (ReferenceEquals(ai.GetBehaviourAt(i), b)) return true;
            return false;
        }

        private static void Detach(PlayableCharacter pc, Man st)
        {
            var b = st.Sleep; st.Sleep = null;
            try { var ai = pc.Ai; if (b != null && ai != null && Attached(ai, b)) ai.RemoveBehaviourImmediate(b); } catch { }
            ResetFatigue(pc);
        }

        private static void ResetFatigue(PlayableCharacter pc)
        {
            try { var f = pc.GetNeed(NeedType.Fatigue); if (f != null) { f.profitBonus = 0f; f.stress = 0f; } } catch { }
        }

        private static bool IsForcedRest(PlayableCharacter pc)
        {
            try { return pc.RoleOverride != null && pc.RoleOverride.Name == "ForcedRest"; } catch { return false; }
        }

        private static bool HasOrder(PlayableCharacter pc)
        {
            try
            {
                if (pc.Action != null && pc.Action.Role == null) return true;
                var q = pc.ActionQueue;
                if (q == null) return false;
                if (q.PreStartingAction != null && q.PreStartingAction.Role == null) return true;
                if (q.Actions != null) foreach (var a in q.Actions) if (a != null && a.Role == null) return true;
            }
            catch { }
            return false;
        }

        // Saves never contain our sleep behaviour; men asleep lie down again afterwards.
        private static void HookSaves()
        {
            if (saves != null) return;
            try { saves = DWS.Common.InjectionFramework.ScriptableObjectSingleton.LoadSingleton<SavesManager>(); } catch { }
            if (saves == null) saves = UnityEngine.Object.FindObjectOfType<SavesManager>();
            if (saves != null) saves.SavingStarted += OnSaving;
        }

        private static void OnSaving()
        {
            foreach (var kv in men)
            {
                if (kv.Key == null) continue;
                if (kv.Value.Sleep != null) kv.Value.ResumeAfterSave = true;
                Detach(kv.Key, kv.Value);
                Neutral(kv.Key);
            }
        }

        // Card game for officer roles (template: the Chief Boatswain's card job).
        private static void Cards()
        {
            float prio = Settings.F("OfficerCards");
            if (prio <= 0f) return;
            ICharacterRolesDatabase db = null;
            foreach (var t in new[] { typeof(UBOAT.Game.Scene.Characters.Skills.Authority), UboatToolkit.Util.FindType("UBOAT.Game.Sandbox.Missions.StartScript"), UboatToolkit.Util.FindType("UBOAT.Game.Scene.Tasks.ExpandedSchedulesController") })
            {
                if (t == null) continue;
                foreach (var f in t.GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public))
                    if (typeof(ICharacterRolesDatabase).IsAssignableFrom(f.FieldType)) { db = f.GetValue(null) as ICharacterRolesDatabase; if (db != null) break; }
                if (db != null) break;
            }
            if (db == null || ReferenceEquals(db, cardsDb) && cardsTemplate != null) return;
            var all = new List<ICharacterRole>();
            db.GetAllRoles(all);
            ICharacterRoleJob fallback = null; cardsTemplate = null;
            foreach (var r in all)
                if (r is CharacterRole cr)
                    foreach (var j in cr.Jobs)
                    {
                        if (j == null || !j.HasAction<PlayCardsAction>()) continue;
                        if (string.Equals(cr.Name, "Chief Boatswain", StringComparison.OrdinalIgnoreCase)) { cardsTemplate = j; break; }
                        if (fallback == null && j.BasePriority > 0f) fallback = j;
                    }
            if (cardsTemplate == null) cardsTemplate = fallback;
            if (cardsTemplate == null) return;
            cardsDb = db; cardsChecked.Clear();
            int added = 0;
            foreach (var r in all) if (AddCards(r, prio)) added++;
            if (added > 0) Util.Log("Kartenspiel für " + added + " Offiziers-Rollen");
        }

        private static void EnsureCards(ICharacterRole role)
        {
            float prio = Settings.F("OfficerCards");
            if (role == null || cardsTemplate == null || prio <= 0f || cardsChecked.Contains(role)) return;
            cardsChecked.Add(role);
            AddCards(role, prio);
        }

        private static readonly HashSet<string> skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "ForcedRest", "Gunner", "EmptyRole", "GenericSailorRole", "GenericOfficerRole", "WarCorrespondent" };

        private static bool AddCards(ICharacterRole role, float prio)
        {
            if (!(role is CharacterRole cr)) return false;
            cardsChecked.Add(cr);
            string n = cr.Name ?? "";
            if (skip.Contains(n) || n.EndsWith("Follower", StringComparison.OrdinalIgnoreCase)) return false;
            if (cr.Requirements != null)
                foreach (var q in cr.Requirements)
                    if (q != null && (q.Equals("Sailor", StringComparison.OrdinalIgnoreCase) || q.Equals("INTERNAL", StringComparison.OrdinalIgnoreCase))) return false;
            if (cr.HasAction<PlayCardsAction>()) return false;
            var job = cardsTemplate.Clone();
            if (job == null) return false;
            job.BasePriority = prio;
            cr.AddJob(job);
            return true;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/RainGear.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::HarmonyLib;
    using global::DWS.Common.Characters;
    using global::UnityEngine;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Navigation;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Environment;
    using global::UBOAT.Game.Scene.Tasks;

    // Rain coats for the deck watch. Replaces the game's rain coat task with our own pass:
    // coat on deck (always or only in rain), sou'wester only in rain or wind, the man you look
    // through is dressed too, gear the game or another mod put on is left alone.
    [HarmonyPatch(typeof(EquipRainCoats), "DoUpdate")]
    internal static class RainGear_Patch
    {
        private static readonly FieldInfo WeatherF = AccessTools.Field(typeof(EquipRainCoats), "weather");
        private static readonly FieldInfo ShipF = AccessTools.Field(typeof(EquipRainCoats), "playerShip");
        private static readonly FieldInfo CrewF = AccessTools.Field(typeof(EquipRainCoats), "playerCrew");
        private static bool givenUp;
        private static int failures;

        private static bool Prefix(ref float __result)
        {
            if (givenUp || !Settings.B("RainGearWind")) return true;
            float iv = Settings.F("RainGearInterval");
            __result = float.IsNaN(iv) || iv <= 0f ? 2f : Mathf.Max(0.25f, iv);
            try
            {
                RainGear.Tick(WeatherF?.GetValue(null) as Weather, ShipF?.GetValue(null) as PlayerShip, CrewF?.GetValue(null) as PlayerCrew);
                failures = 0;
            }
            catch (Exception e)
            {
                failures++;
                Util.Log("Regenkleidung: Fehler (" + failures + "/3) " + e.Message);
                if (failures >= 3) { givenUp = true; Util.Log("Regenkleidung: gebe an das Spiel zurück."); }
            }
            return false;
        }
    }

    internal static class RainGear
    {
        private const string SlotCoat = "Rain Coat", SlotCap = "Rain Coat Cap";
        private const string CoatBlack = "Character/Other/Rain Coat Black", CoatGray = "Character/Other/Rain Coat Gray", Cap = "Character/Other/Rain Coat Cap";
        private static readonly string[] CoatAssets = { CoatBlack, CoatGray };
        private static readonly string[] CapAssets = { Cap };
        private static readonly HashSet<int> foreignReported = new HashSet<int>();
        private static float nextPass, settleUntil;
        private static int lastShip;

        public static void Tick(Weather weather, PlayerShip ship, PlayerCrew crew)
        {
            float now = Time.unscaledTime;
            if (now < nextPass) return;
            nextPass = now + Mathf.Max(0.25f, Settings.F("RainGearInterval"));
            if (!weather || !ship || crew == null) return;
            int id = ship.GetInstanceID();
            if (id != lastShip) { lastShip = id; settleUntil = now + 30f; }   // let a fresh load settle first
            if (now < settleUntil) return;

            int mode = Mathf.RoundToInt(Settings.F("RainGearMode"));   // 0 rain only, 1 always on deck, 2 off
            bool sheltered = ship.IsSheltered;
            float rain = (float)weather.Rain;
            float bft = 0f;
            try { bft = weather.SeaCondition; } catch { }
            bool permits = Settings.B("RainGearSheltered") || !sheltered;
            bool coatWeather = mode == 1 || rain >= Settings.F("RainGearRain");
            bool coats = mode != 2 && permits && coatWeather;
            bool capWeather = rain >= Settings.F("RainGearCapRain") || bft >= Settings.F("RainGearCapWind");
            bool log = Settings.B("RainGearLog");

            for (int i = crew.Count - 1; i >= 0; i--)
            {
                var c = crew[i];
                if (!(bool)c || !c.isActiveAndEnabled) continue;
                var data = c.Data;
                if (data == null || data.CharacterType == PlayableCharacterData.Type.Captive) continue;
                if (c.IsImpersonated && !Settings.B("RainGearImpersonated")) continue;
                var nav = c.Navigator;
                if (nav == null) continue;
                var layer = nav.Layer;
                bool outside = layer == NavMeshLayer.UboatExterior || layer == NavMeshLayer.World;
                bool wantCoat = coats && outside;
                if (IsForeign(c, SlotCoat, CoatAssets))
                {
                    if (log && foreignReported.Add(c.GetInstanceID())) Util.Log("Regenkleidung: " + data.Name + " trägt fremden Mantel - bleibt unberührt.");
                    if (foreignReported.Count > 512) foreignReported.Clear();
                    continue;
                }
                bool wantCap = wantCoat && capWeather && (!c.IsSkipper || Settings.B("RainGearSkipperCap"));
                bool a = Apply(c, SlotCoat, wantCoat, CoatFor(c), CoatAssets);
                bool b = Apply(c, SlotCap, wantCap, Cap, CapAssets);
                if (log && (a || b))
                    Util.Log(string.Format("Regenkleidung: {0}{1} Ebene={2} Regen={3:0.###} Wind={4:0.#} geschützt={5} -> Mantel={6} Südwester={7}",
                        data.Name, c.IsImpersonated ? " [du]" : "", layer, rain, bft, sheltered, wantCoat, wantCap));
            }
        }

        private static bool Apply(Character c, string slot, bool wanted, string asset, string[] owned)
        {
            var cur = c.GetWearableAt(slot);
            bool occupied = (bool)cur;
            if (wanted)
            {
                if (occupied) return false;
                c.LoadWearableIfNotAlreadyLoadedAsync(slot, asset, true);
                return true;
            }
            foreach (var o in owned) c.RemoveLoadingWearables(o);
            if (occupied && IsOwned(cur, owned)) { c.RemoveWearable(cur, Character.WearableRemovalAction.Destroy); return true; }
            return false;
        }

        private static bool IsForeign(Character c, string slot, string[] owned)
        {
            var cur = c.GetWearableAt(slot);
            return (bool)cur && !IsOwned(cur, owned);
        }

        private static bool IsOwned(Wearable w, string[] owned)
        {
            var prefab = w.GetComponent<UBOAT.Game.Serialization.GameObjectPrefab>();
            if (!prefab) return false;
            foreach (var o in owned) if (string.Equals(o, prefab.PrefabPath, StringComparison.Ordinal)) return true;
            return false;
        }

        // The same man always gets the same coat colour.
        private static string CoatFor(PlayableCharacter c)
        {
            var key = c.Data != null && !string.IsNullOrEmpty(c.Data.Name) ? c.Data.Name : c.name;
            if (string.IsNullOrEmpty(key)) return CoatBlack;
            int h = 17;
            foreach (char ch in key) h = unchecked(h * 31 + ch);
            return (h & 1) == 0 ? CoatBlack : CoatGray;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/SkillIconFix.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::System.Reflection.Emit;
    using global::DWS.Common.Resources;
    using global::HarmonyLib;
    using global::UnityEngine;

    // Fix 'Requested asset at path "Icons/Skills/<Name> (Owned)" doesn't exist.': some skills (e.g. "Advanced
    // Equipment", or skills added by other mods such as "RadiomanAuthority1") have no picture for every state.
    // The game then logs an error with a long stack for each button, every time the skill tree opens.
    // Now: try the picture quietly, else the same skill without the state suffix, else the game's "Unknown" icon.
    [HarmonyPatch(typeof(UBOAT.Game.UI.SkillButton), "Start")]
    internal static class SkillIconFix
    {
        private static readonly MethodInfo Ours = AccessTools.Method(typeof(SkillIconFix), nameof(Get));

        public static Resource<Sprite> Get(ResourceManager rm, string path, object owner, bool logErrors)
        {
            if (!Settings.B("FixSkillIcons")) return rm.RetrieveAsset<Sprite>(path, owner, logErrors);
            var s = rm.RetrieveAsset<Sprite>(path, owner, false);
            if (s.Valid) return s;
            int i = path.IndexOf(" (");
            if (i > 0) { s = rm.RetrieveAsset<Sprite>(path.Substring(0, i), owner, false); if (s.Valid) return s; }
            return rm.RetrieveAsset<Sprite>("Icons/Skills/Unknown", owner, false);
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            foreach (var c in code)
            {
                if ((c.opcode == OpCodes.Callvirt || c.opcode == OpCodes.Call) && c.operand is MethodInfo m && m.Name == "RetrieveAsset"
                    && m.IsGenericMethod && m.GetGenericArguments()[0] == typeof(Sprite) && m.GetParameters().Length == 3)
                { c.opcode = OpCodes.Call; c.operand = Ours; }
                yield return c;
            }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Stations.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UBOAT.Game.Scene.Characters.Activators;
    using global::UBOAT.Game.Scene.Characters.Skills;
    using global::UBOAT.Game.Scene.Items;
    using global::UnityEngine;

    // QoL #34/#35: keep the depth steers (Tiefenruder) and the navigation table manned.
    // If a station stays empty for a few seconds, the nearest free sailor is sent there
    // with the game's own work action (sailors handle both very well).
    public class Stations : ModuleBase
    {
        private PlayerCrew crew;
        private readonly Dictionary<Activator, float> emptySince = new Dictionary<Activator, float>();
        // Against "X takes over" every few seconds: if the last man left again quickly, wait longer
        // before sending the next one (30 s, 60 s, ... up to 10 min). Resets once a man stays 2 minutes.
        private readonly Dictionary<Activator, float> assignedAt = new Dictionary<Activator, float>();
        private readonly Dictionary<Activator, float> backoff = new Dictionary<Activator, float>();
        private readonly HashSet<Activator> warned = new HashSet<Activator>();
        private readonly Dictionary<Activator, float> waitUntil = new Dictionary<Activator, float>();
        private readonly List<CharacterActionInfo> infos = new List<CharacterActionInfo>();

        // Safety net: under water the tower hatch is locked whatever the game decided, and every open leak under
        // water is named in the log once per minute (proof where water comes from).
        private float nextHatchNote, nextLeakLine;
        private double lastWater;
        private void HatchSafety(UBOAT.Game.Scene.Entities.PlayerShip ship)
        {
            if (ship == null || ship.Interior == null) return;
            if (Settings.B("HatchSafety") && ship.DeckDepth > 1.5f && !Util.WarthogModOwns(ship))
            {
                var h = ship.GetEquipment<UBOAT.Game.Scene.Items.Hatch>("Exterior");
                if (h != null && !h.Locked)
                {
                    h.Locked = true;
                    if (Time.unscaledTime >= nextHatchNote)
                    {
                        nextHatchNote = Time.unscaledTime + 30f;
                        ToolkitRunner.ShowNotice("Turmluke war unter Wasser offen und wurde geschlossen", 5f);
                    }
                }
            }
            if (ship.DeckDepth > 1.5f && Time.unscaledTime >= nextLeakLine)
            {
                nextLeakLine = Time.unscaledTime + (lastWater > 0.05 ? 10f : 60f);   // 0.4.11: every 10 s while water is in the boat
                var sb = new System.Text.StringBuilder();
                int n = 0; double water = 0;
                foreach (var c in ship.Interior.Compartments)
                {
                    if (c == null) continue;
                    water += c.Water;
                    foreach (var hole in c.Holes)
                        if (hole != null && hole.Openess > 0f)
                        { n++; if (n <= 12) sb.Append(" ").Append(c.name).Append("/").Append(hole.name).Append(" offen ").Append(hole.Openess.ToString("0.##")).Append(" Größe ").Append(hole.HoleSize.ToString("0.#####")).Append(hole.IsUnderWater ? " unter Wasser" : " trocken").Append(";"); }
                }
                lastWater = water;
                var eh = ship.GetEquipment<UBOAT.Game.Scene.Items.Hatch>("Exterior");
                Util.Log("Lecks getaucht (Tiefe " + ship.DeckDepth.ToString("0") + " m, Soll " + ship.TargetDepth.ToString("0") + " m): " + n + " offene Stellen, Wasser in den Räumen " + (water * 1000.0).ToString("0") + " l, Turmluke " + (eh == null ? "?" : eh.Locked ? "zu" : "OFFEN") + ", Vertikal-Strahlruder " + Warthog.WarthogVThrusters.LastCmd.ToString("0.##") + "." + sb);
            }
        }

        public override void Tick()
        {
            if (crew == null) crew = Object.FindObjectOfType<PlayerCrew>();
            if (crew == null) return;
            var ship = Util.PlayerShip;
            // 0.4.12: with the game's option "expanded schedules" the rudder is dead while nobody stands at the helm
            // (HelmsmanStation.PerformanceOnChanged) -> it stays where it was (DoMe: stuck at 35 right, not clickable).
            if (Settings.B("KeepHelm"))
                foreach (var st in Util.Find<HelmsmanStation>())
                    Keep(st.GetComponent<Activator>(), "Steuerstand");
            if (Settings.B("KeepDepthStation"))
                foreach (var st in Util.Find<DivingPlanesStation>())
                    Keep(st.GetComponent<Activator>(), "Tiefenruder");
            if (Settings.B("KeepNavTable"))
                foreach (var st in Util.Find<NavigationTable>())
                    Keep(st.GetComponent<Activator>(), "Kartentisch");

            // Hydrophone: under water or alarm. Radio room: whenever the radio can receive.
            bool hydro = Settings.B("AutoManHydrophone") && (ship.SubmergedOrGoingToSubmerge || ship.Alarmed);
            bool radio = Settings.B("AutoManRadio") && !ship.Docked;   // in port nobody needs to listen
            if (hydro || radio)
                foreach (var act in Util.Find<Activator>())
                {
                    if (act == null) continue;
                    var eq = act.AssociatedEquipment;
                    if (hydro && eq is ListeningRoom) Keep(act, "Hydrophon");
                    else if (radio && eq is PlayerShipRadio) Keep(act, "Funkraum");
                }

            // 0.4.10 (DoMe 2026-11-17: boat floods suddenly): on an alarm dive the deck is still dry for some seconds; in that
            // time men were sent to the guns although the boat was already diving. With men on deck the game does not lock
            // the tower hatch, so the boat dived with an open hatch and filled within seconds. Never while diving.
            bool guns = ship.DeckDepth < 1f && !ship.SubmergedOrGoingToSubmerge && (((Settings.B("AutoManGuns") || Settings.B("AutoManFlak")) && ship.Alarmed) || Time.unscaledTime < UnderFire.ManGunsUntil);
            bool gunsAll = Time.unscaledTime < UnderFire.ManGunsUntil;
            Util.Safe("Luken-Sicherung", () => HatchSafety(ship));
            bool engines = Settings.B("AutoManEngines") && !ship.Docked;
            if (guns || engines)
                foreach (var act in Util.Find<Activator>())
                {
                    if (act == null) continue;
                    var eq = act.AssociatedEquipment;
                    if (guns && eq is Gun gg && (gunsAll || Settings.B(gg.IsAntiAircraftWeapon ? "AutoManFlak" : "AutoManGuns"))) Keep(act, gg.IsAntiAircraftWeapon ? "Flak" : "Geschütz");
                    else if (engines && eq is PlayerShipEngine eng && eng.GearIndex != 0) Keep(act, "Maschinenraum");
                }

            // Echo sounder as a sailor job: under water a free sailor goes there every x seconds and pings (game's own PingDeep action).
            float every = Settings.F("EchoSailorSeconds");
            // The interval is game time; under time compression that meant a new sailor pulled from his work every few
            // real seconds (log: "Echolot: X übernimmt" every 6 s, rotating names). Now also at least 60 real seconds apart.
            if (Settings.B("EchoSailor") && every > 0f && ship.SubmergedOrGoingToSubmerge && Time.time - lastPing > every
                && Time.unscaledTime - lastPingReal > Mathf.Min(60f, every)   // 0.4.22: values under 60 s work again at normal speed
                && !(Settings.B("EchoSailorQuiet") && ship.Alarmed))
                foreach (var act in Util.Find<Activator>())
                    if (act != null && act.AssociatedEquipment is SoundRangingDevice && act.Users.Count == 0)
                    {
                        if (Send(act, "Echolot", t => t is PingDeep)) { lastPing = Time.time; lastPingReal = Time.unscaledTime; }
                        break;
                    }

            // More sailors per officer.
            // Auto: every sailor on board fits into an officer's group (sailors / officers, rounded up),
            // and an officer never has more followers than he may lead (the "10/6" display).
            float max = Settings.F("MaxFollowers");
            bool auto = Settings.B("FollowersAuto");
            if (max > 0f || auto || hadMax)
            {
                int officers = 0, sailors = 0;
                foreach (var c in crew.Characters) { if (c == null) continue; if (c.IsOfficer) officers++; else sailors++; }
                float share = officers > 0 ? Mathf.Ceil(sailors / (float)officers) : 0f;
                foreach (var c in crew.Characters)
                {
                    if (c == null || !c.IsOfficer) continue;
                    var a = c.Data.GetStatus<Authority>();
                    if (a == null) continue;
                    float want = max;
                    if (auto) want = Mathf.Max(want, Mathf.Max(share, a.Followers.Count));
                    if (auto && want > 0f && want <= a.FollowersLimit.GetValueExcludingModifier(Util.ModifierName) && max <= 0f) want = 0f;   // game value already enough
                    Util.SetParameter(a.FollowersLimit, want);
                }
                hadMax = max > 0f || auto;
            }
        }
        private bool hadMax;
        private float lastPing = -9999f, lastPingReal = -9999f;

        // Sends the nearest free sailor to do one action at this activator (for one-off jobs).
        private bool Send(Activator act, string label, System.Func<CharacterAction, bool> accept)
        {
            PlayableCharacter best = null; CharacterAction tmpl = null; float bestD = float.MaxValue;
            foreach (var h in crew.Characters)
            {
                if (h == null || h.IsOfficer || !IsFree(h)) continue;
                act.GetActionInfos(infos, h, ActionContext.Normal, true);
                CharacterAction t = null;
                foreach (var i in infos)
                    if (i.ActionTemplate != null && i.State == ActionState.Enabled && accept(i.ActionTemplate)) { t = i.ActionTemplate; break; }
                if (t == null) continue;
                float d = (h.transform.position - act.transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = h; tmpl = t; }
            }
            if (best == null) return false;
            var a = tmpl.Duplicate(act);
            a.QueuePriority = 90f;
            best.StopCurrentAction(true);
            best.StartAction(act, a);
            Util.Log(label + ": " + best.Name + " übernimmt");
            return true;
        }

        private void Keep(Activator act, string label)
        {
            if (act == null || !act.isActiveAndEnabled) return;
            if (act.Users.Count > 0 || OnTheWay(act))
            {
                emptySince.Remove(act);
                if (assignedAt.TryGetValue(act, out float at) && Time.time - at > 120f) { backoff.Remove(act); assignedAt.Remove(act); }
                return;
            }
            CrewLod.UrgentUntil = Time.unscaledTime + 3f;   // 0.4.13: a kept station is empty -> all men think every frame
            if (!emptySince.TryGetValue(act, out float since))
            {
                emptySince[act] = Time.time;
                if (assignedAt.TryGetValue(act, out float at) && Time.time - at < 120f)
                {
                    float b = backoff.TryGetValue(act, out float old) ? Mathf.Min(600f, old * 2f) : 30f;
                    backoff[act] = b;
                    waitUntil[act] = Time.time + b;
                    if (b >= 600f && !warned.Contains(act)) { warned.Add(act); Util.Log(label + ": Leute verlassen den Platz immer wieder, warte jetzt 10 min"); }
                }
                assignedAt.Remove(act);
                return;
            }
            if (waitUntil.TryGetValue(act, out float until) && Time.time < until) return;
            if (Time.time - since < Settings.F("StationDelay")) return;

            PlayableCharacter best = null; CharacterAction tmpl = null; float bestD = float.MaxValue;
            foreach (var h in crew.Characters)
            {
                if (h == null || h.IsOfficer || !IsFree(h)) continue;
                var t = FindWork(act, h);
                if (t == null) continue;
                float d = (h.transform.position - act.transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = h; tmpl = t; }
            }
            if (best == null) return;
            var a = tmpl.Duplicate(act);
            a.QueuePriority = 90f;
            best.StopCurrentAction(true);
            best.StartAction(act, a);
            emptySince.Remove(act);
            assignedAt[act] = Time.time;
            Util.Log(label + ": " + best.Name + " übernimmt");
        }

        // The game's sailor work action for this station (BasicWorkAction / UseNavigationTableAction), if allowed now.
        private CharacterAction FindWork(Activator act, PlayableCharacter h)
        {
            act.GetActionInfos(infos, h, ActionContext.Normal, true);
            foreach (var i in infos)
                if (i.ActionTemplate != null && i.State == ActionState.Enabled &&
                    (i.ActionTemplate is BasicWorkAction || i.ActionTemplate is WorkAtHelmsmanStation || i.ActionTemplate is UseNavigationTableAction ||
                     i.ActionTemplate is UseGun || i.ActionTemplate is ManageEnginesAction ||
                     i.ActionTemplate is HydrophoneUseAction || i.ActionTemplate is SailorRadioUsageAction))
                    return i.ActionTemplate;
            return null;
        }

        // Someone is already walking there for this station. Without this check a second man was sent
        // while the first was still on his way; with two men at the radio the game ends the job for both
        // (SailorRadioUsageAction: more than one user -> complete) and both walked off again.
        private bool OnTheWay(Activator act)
        {
            foreach (var h in crew.Characters)
            {
                if (h == null) continue;
                var a = h.Action;
                if (a != null && a.Activator == act) return true;
                var q = h.ActionQueue;
                if (q != null && q.Actions != null)
                    foreach (var qa in q.Actions) if (qa != null && qa.Activator == act) return true;
            }
            return false;
        }

        private static bool IsFree(PlayableCharacter h)
        {
            if (h.Injury || h.Ai == null || !h.Ai.isActiveAndEnabled || h.IsImpersonated || h.IsSelected) return false;
            var a = h.Action;
            return a == null || (a.CanBeStopped && a.Role != null);
        }
    }
}

namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UBOAT.Game.Scene.Characters.Activators;
    using global::UBOAT.Game.Scene.Characters.Skills;
    using global::UBOAT.Game.Scene.Items;
    using global::UnityEngine;

    // Officers may take every available sailor (the game only allows 60 % without alarm).
    [HarmonyLib.HarmonyPatch(typeof(PlayerCrew), "ValidateWorkingCharactersCount")]
    internal static class Stations_FollowersPatch
    {
        private static void Postfix(PlayerCrew __instance)
        {
            if (!Settings.B("FollowersNoLimit") || __instance.Characters == null) return;
            int n = 0;
            foreach (var c in __instance.Characters)
                if (c != null && c.Data != null && c.Data.CharacterType == PlayableCharacterData.Type.Sailor && !c.Injury && c.Health > 0f) n++;
            var t = HarmonyLib.Traverse.Create(__instance);
            t.Field("maxOfficerFollowersCount").SetValue(n);
            t.Field("maxOfficerFollowersCountWithoutAlarm").SetValue(n);
        }
    }
}

namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UBOAT.Game.Scene.Characters.Activators;
    using global::UBOAT.Game.Scene.Characters.Skills;
    using global::UBOAT.Game.Scene.Items;
    using global::UnityEngine;

    // Diagnosis: when "add sailor" finds nobody, write WHY into the toolkit log (one line per click).
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Scene.Characters.Skills.Authority), "AddNearbyCharacter")]
    internal static class Stations_FollowerDiagPatch
    {
        private static void Postfix(UBOAT.Game.Scene.Characters.Skills.Authority __instance, PlayerCrew crew, bool silent, bool __result)
        {
            if (__result || silent || crew == null) return;
            Util.Safe("FollowerDiag", () =>
            {
                var me = __instance.Character;
                int sailors = 0, employed = 0, groupAwake = 0, cmdNotEnabled = 0, ok = 0;
                var gt = HarmonyLib.Traverse.Create(__instance).Field("gameTime").GetValue();
                int hour = 0;
                try { hour = ((System.DateTime)HarmonyLib.Traverse.Create(gt).Property("CurrentDateTime").GetValue()).Hour; } catch { }
                var tca = new UBOAT.Game.Scene.Characters.Actions.TakeCommandAction();
                string firstState = "";
                for (int i = 0; i < crew.Count; i++)
                {
                    var c = crew[i];
                    if (c == null || c.Data == null || c.Data.CharacterType != PlayableCharacterData.Type.Sailor) continue;
                    sailors++;
                    if (c.Employer) { employed++; continue; }
                    if (c.SailorGroup != null && c.SailorGroup.Schedule[hour] != Schedule.Behaviour.Sleep) { groupAwake++; continue; }
                    var act = c.GetComponent<CharacterActivator>();
                    var st = act != null && me != null ? tca.GetState(act, me, false, true, ActionContext.Normal).ActionState : ActionState.Hidden;
                    if (st != ActionState.Enabled) { cmdNotEnabled++; if (firstState == "") firstState = st.ToString(); continue; }
                    ok++;
                }
                Util.Log("Matrose zuweisen an " + (me != null ? me.Name : "?") + ": keiner gefunden. Matrosen " + sailors + ", schon bei Offizier " + employed
                    + ", Gruppe hat gerade Dienst " + groupAwake + ", Befehl nicht möglich " + cmdNotEnabled + (firstState != "" ? " (" + firstState + ")" : "") + ", frei " + ok + ", Stunde " + hour);
            });
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/UniqueNames.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::System.Linq;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Characters;
    using global::UnityEngine;

    // More unique crew names: the game rolls first + last name freely, so the same last name
    // (or even the same full name) shows up again and again. A new man now gets a last name that
    // nobody on the crew has yet. The game's name lists are mixed with a bigger list of our own.
    // The button "Doppelte Namen jetzt ändern" renames men who share a name with someone else.
    public class UniqueNames : ModuleBase
    {
        internal static readonly string[] First = {
            "Adolf","Albert","Albrecht","Alfons","Alfred","Alois","Anton","Arnold","Arthur","August","Bernhard","Berthold","Bruno","Carl","Christian","Clemens",
            "Conrad","Dietrich","Eberhard","Eduard","Egon","Emil","Engelbert","Erich","Ernst","Erwin","Eugen","Ewald","Felix","Ferdinand","Franz","Friedrich",
            "Fritz","Georg","Gerhard","Gottfried","Gottlieb","Gustav","Hans","Harald","Hartmut","Heinrich","Heinz","Helmut","Herbert","Hermann","Horst","Hubert",
            "Hugo","Ignaz","Jakob","Joachim","Johann","Josef","Julius","Jürgen","Karl","Kaspar","Klaus","Konrad","Kurt","Leo","Leonhard","Leopold","Lothar",
            "Ludwig","Lukas","Manfred","Martin","Matthias","Max","Michael","Nikolaus","Oskar","Oswald","Otto","Paul","Peter","Philipp","Rainer","Reinhard",
            "Richard","Robert","Rolf","Rudolf","Rupert","Siegfried","Stefan","Theodor","Thomas","Ulrich","Valentin","Viktor","Volker","Walter","Werner","Wilhelm",
            "Willi","Wolfgang","Xaver","Anselm","Balthasar","Benedikt","Eckhard","Edmund","Gebhard","Gregor","Gunther","Hagen","Heribert","Ingo","Kilian",
            "Lambert","Meinrad","Norbert","Ottmar","Quirin","Raimund","Roland","Sebastian","Severin","Sigmund","Tobias","Ulf","Veit","Waldemar","Wendelin"
        };
        internal static readonly string[] Last = {
            "Ackermann","Adler","Albrecht","Arnold","Bachmann","Bauer","Baumann","Beck","Becker","Behrens","Berger","Bergmann","Beyer","Bischoff","Blum","Böhm",
            "Brandt","Braun","Brückner","Busch","Dietrich","Dittrich","Döring","Eckert","Engel","Ernst","Falk","Fiedler","Fischer","Fleischer","Frank","Franke",
            "Friedrich","Fuchs","Gärtner","Graf","Groß","Günther","Haas","Hahn","Hartmann","Hauser","Heinrich","Heller","Herrmann","Hesse","Hoffmann","Hofmann",
            "Horn","Huber","Jäger","Jung","Kaiser","Keller","Kirchner","Klein","Koch","Köhler","König","Krämer","Kraus","Krause","Krüger","Kuhn","Kühn","Kunz",
            "Lang","Lange","Lehmann","Lorenz","Ludwig","Maier","Marx","Mayer","Meier","Meyer","Möller","Morgenstern","Müller","Nagel","Neubauer","Neumann",
            "Nowak","Oswald","Otto","Pfeiffer","Pohl","Popp","Puschmann","Rau","Reuter","Richter","Ritter","Roth","Sauer","Schäfer","Scharf","Schilling",
            "Schmid","Schmidt","Schmitt","Schneider","Scholz","Schreiber","Schröder","Schubert","Schulte","Schulz","Schumacher","Schuster","Schwarz","Seidel",
            "Seifert","Sommer","Stahl","Stein","Steiner","Thiel","Unger","Vogel","Vogt","Voigt","Wagner","Walter","Weber","Wegener","Weidner","Weiß","Wendt",
            "Werner","Wilke","Winkler","Winter","Wirth","Witt","Wolf","Wolff","Zimmermann","Ziegler","Zander","Brenner","Drechsler","Eberle","Fendt","Gerlach",
            "Haberland","Hölzl","Jansen","Kessler","Lindner","Mahler","Niemann","Ostermann","Pichler","Quast","Riedel","Sattler","Tischler","Uhlig","Vollmer",
            "Waldner","Zeller","Achterberg","Behnke","Claasen","Dorn","Eilers","Feddersen","Gosch","Harms","Iwersen","Jessen","Kröger","Lüders","Mommsen",
            "Nissen","Ohlsen","Petersen","Rohwer","Sievers","Thomsen","Ulrichs","Vosgerau","Wichmann","Asmussen","Brodersen","Carstens","Detlefsen","Ehlers"
        };

        internal static bool Busy;
        private static readonly System.Random rng = new System.Random();

        public static IEnumerable<string> UsedNames()
        {
            var c = Util.Career;
            if (c?.Characters == null) return Enumerable.Empty<string>();
            return c.Characters.Where(d => d != null && !string.IsNullOrEmpty(d.Name)).Select(d => d.Name);
        }

        public static string LastName(string full) { var i = full == null ? -1 : full.LastIndexOf(' '); return i < 0 ? full : full.Substring(i + 1); }

        // Picks a name whose last name is not used yet; if every last name is taken, a full name nobody has.
        public static string Pick(System.Func<string> gameRoll, string current, string skip = null)
        {
            var used = UsedNames().Where(n => !ReferenceEquals(n, skip)).ToList();
            var lastUsed = new HashSet<string>(used.Select(LastName));
            var fullUsed = new HashSet<string>(used);
            if (current != null && !lastUsed.Contains(LastName(current))) return current;
            string fallback = null;
            for (int i = 0; i < 200; i++)
            {
                string n = (i % 2 == 0 && gameRoll != null) ? gameRoll() : First[rng.Next(First.Length)] + " " + Last[rng.Next(Last.Length)];
                if (string.IsNullOrEmpty(n)) continue;
                if (!lastUsed.Contains(LastName(n))) return n;
                if (fallback == null && !fullUsed.Contains(n)) fallback = n;
            }
            return fallback ?? current;
        }

        public override void Tick()
        {
            var o = Settings.Get("RenameDuplicateNames");
            if (o == null || !o.On) return;
            o.Value = 0;
            Util.Safe("RenameDuplicates", () =>
            {
                var c = Util.Career;
                if (c?.Characters == null) return;
                var seen = new HashSet<string>();
                int n = 0;
                foreach (var d in c.Characters)
                {
                    if (d == null || string.IsNullOrEmpty(d.Name)) continue;
                    if (seen.Add(LastName(d.Name))) continue;
                    var name = Pick(null, null, d.Name);
                    if (name == null) continue;
                    d.Name = name;
                    seen.Add(LastName(name));
                    n++;
                }
                ToolkitRunner.ShowNotice(n > 0 ? n + " Männer umbenannt, jetzt hat jeder einen eigenen Nachnamen" : "Keine doppelten Namen gefunden", 5f);
            });
        }
    }

    [HarmonyPatch]
    internal static class UniqueNames_Patch
    {
        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(CharacterGenerator), "GenerateCharacterNameByCountry", new[] { typeof(string) });
            yield return AccessTools.Method(typeof(CharacterGenerator), "GenerateCharacterNameByType", new[] { typeof(string) });
        }

        private static void Postfix(CharacterGenerator __instance, System.Reflection.MethodBase __originalMethod, string __0, ref string __result)
        {
            if (UniqueNames.Busy || __result == null || !Settings.B("UniqueCrewNames")) return;
            try
            {
                UniqueNames.Busy = true;
                var inst = __instance; var arg = __0; var m = __originalMethod;
                __result = UniqueNames.Pick(() => m.Invoke(inst, new object[] { arg }) as string, __result);
            }
            catch (System.Exception e) { Util.Log("Namen: " + e.Message); }
            finally { UniqueNames.Busy = false; }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Watch.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Linq;
    using global::HarmonyLib;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Scene.Entities;
    using global::UnityEngine;

    // QoL #1: time compression stops when a new ship or aircraft contact appears.
    // QoL #6: autosave every X minutes (uses the game's own autosave slots).
    // Cheat #11: backup save right before a cheat is switched on.
    // QoL #8: warnings before diesel, air or battery run low.
    public class Watch : ModuleBase
    {
        private DynamicContactsController contacts;
        private float nextAutosave = -1f, lastBackup = -9999f; private string noted;
        private int lastCheatCount = -1;
        private readonly System.Collections.Generic.Dictionary<string, bool> warned = new System.Collections.Generic.Dictionary<string, bool>();

        public override void Tick()
        {
            HookContacts();
            Autosave();
            Backup();
            Warnings();
        }

        private void HookContacts()
        {
            if (contacts != null) return;
            contacts = Traverse.Create(typeof(UBOAT.Game.UI.GameInputModule)).Field("dynamicContactsController").GetValue() as DynamicContactsController;
            if (contacts != null) contacts.ContactAdded += OnContact;
        }

        private static void OnContact(ContactEvent e)
        {
            try
            {
                var c = e.Contact;
                if (c == null) return;
                Events.Diary((c.IsAirborne ? "Flugzeug" : "Kontakt") + ": " + c.Name);
                if (Traffic.Ignore(c)) return;
                string n = c.Name ?? "";
                if (n.IndexOf("Wrack", System.StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Wreck", System.StringComparison.OrdinalIgnoreCase) >= 0) return;   // wrecks never stop time (DoMe)
                bool air = c.IsAirborne;
                if (air ? !Settings.B("StopTimeOnAircraft") : !Settings.B("StopTimeOnShip")) return;
                Game.StopTimeCompression((air ? "Flugzeug" : "Kontakt") + " gesichtet: " + c.Name);
            }
            catch (System.Exception ex) { Util.Log("Kontakt-Stopp: " + ex.Message); }
        }

        private void Autosave()
        {
            float min = Settings.F("AutoSaveMinutes");
            if (min <= 0f) { nextAutosave = -1f; return; }
            if (nextAutosave < 0f) { nextAutosave = Time.unscaledTime + min * 60f; return; }
            // 0.4.42 (DoMe: "autosave kommt mehrfach vor, als wenn die time sich überschneiden"): the game has its own
            // auto-save clock (every x minutes, on docking, on leaving to the menu) and this part had a second one that
            // knew nothing of the others, so two saves could follow each other within a minute. Now EVERY save of the
            // game (by hand, quick save, the game's auto-save, ours) restarts this clock: the next one comes x minutes
            // after the last save, whoever made it.
            if (AutoSaveClock.LastSave > 0f && AutoSaveClock.LastSave + min * 60f > nextAutosave)
            {
                nextAutosave = AutoSaveClock.LastSave + min * 60f;
                if (AutoSaveClock.LastName != noted) { noted = AutoSaveClock.LastName; Util.Log("Auto-Speichern: Uhr neu gestellt, weil gerade gespeichert wurde (" + noted + "). Nächstes in " + min.ToString("0") + " min"); }
            }
            if (Time.unscaledTime < nextAutosave || !Game.CanSave() || PortWindowOpen()) return;
            nextAutosave = Time.unscaledTime + min * 60f;
            var saver = Game.Tasks?.GetRunningTask<AutoSaver>();
            if (saver == null) return;
            saver.DoAutoSave();
            noted = AutoSaveClock.LastName;
            Util.Log("Auto-Speichern (Toolkit-Uhr, " + min.ToString("0") + " min seit dem letzten Speichern)");
        }

        private void Backup()
        {
            int n = Settings.All.Count(o => o.Tab == Settings.Cheat && o.Key != "CheatShowActive" && (o.IsBool ? o.On : o.Value != o.Default));
            if (lastCheatCount >= 0 && n > lastCheatCount && Settings.B("BackupBeforeCheats")
                && Time.unscaledTime - lastBackup > 600f && Game.CanSave() && !PortWindowOpen())
            {
                lastBackup = Time.unscaledTime;
                Game.Saves.Save("UboatToolkit_Backup");
                ToolkitRunner.ShowNotice("Backup gespeichert (UboatToolkit_Backup), Cheat ist an.");
            }
            lastCheatCount = n;
        }

        private void Warnings()
        {
            float lim = Settings.F("LowWarnPercent") / 100f;
            if (lim <= 0f) return;
            var ship = Util.PlayerShip;
            Warn(ship, "Fuel", "Diesel", lim);
            Warn(ship, "Energy", "Batterie", lim);
            if (ship.SubmergedOrGoingToSubmerge) Warn(ship, "Oxygen", "Luft", lim); else warned["Oxygen"] = false;
        }

        private void Warn(PlayerShip ship, string res, string label, float lim)
        {
            var r = ship.GetResource(res);
            if (r == null) return;
            float v = (float)r.Amount;
            warned.TryGetValue(res, out bool w);
            if (v < lim && !w)
            {
                warned[res] = true;
                Game.StopTimeCompression("Warnung: " + label + " nur noch " + (v * 100f).ToString("0") + " %");
                ToolkitRunner.ShowNotice("Warnung: " + label + " nur noch " + (v * 100f).ToString("0") + " %", 10f);
            }
            else if (v > lim + 0.05f) warned[res] = false;
        }

        // Saving while the port window (shop, crew, storage) is open makes the game rebuild its UI while the
        // window closes -> "GameObject is already being activated or deactivated". Wait until it is closed.
        private static bool PortWindowOpen()
        {
            try { return UnityEngine.Object.FindObjectOfType<UBOAT.Game.UI.PortActivitiesUI>() != null; }
            catch { return false; }
        }
    }
}


// ====================================================================================================
// Abschnitt: Auto-Speichern, eine Uhr für alle (0.4.42)
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Core.Serialization;

    // Remembers when the game last wrote a savegame, whoever asked for it. Read by the auto-save part. Changes nothing.
    [HarmonyPatch(typeof(SavesManager), "Save")]
    internal static class AutoSaveClock
    {
        internal static float LastSave = -1f; internal static string LastName = "";
        private static void Postfix(string name)
        {
            LastSave = Time.unscaledTime; LastName = name ?? "";
            Util.Log("Speichern: Spielstand \"" + LastName + "\" wird geschrieben");
        }
    }
}
