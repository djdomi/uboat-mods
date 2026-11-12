// Crew-KI (Teil des UBOAT Toolkit): CrewAIRestInBunks. Schalter: F10 > QoL > "Crew-KI".
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using HarmonyLib;
using UBOAT.Game.Core.AI;
using UBOAT.Game.Core.AI.Behaviours;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Characters.Actions;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Interactions;
using UBOAT.Game.Scene.Items;

namespace UboatToolkit.Modules.CrewAi
{
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
            AccessTools.TypeByName("UBOAT.Game.Scene.Items.IInteractionsBlockingItem");
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
