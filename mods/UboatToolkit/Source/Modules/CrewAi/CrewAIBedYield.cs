// Crew-KI (Teil des UBOAT Toolkit): CrewAIBedYield. Schalter: F10 > QoL > "Crew-KI".
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using HarmonyLib;
using UBOAT.Game.Core.AI;
using UBOAT.Game.Core.AI.Behaviours;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Characters.Navigation;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;

namespace UboatToolkit.Modules.CrewAi
{
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
