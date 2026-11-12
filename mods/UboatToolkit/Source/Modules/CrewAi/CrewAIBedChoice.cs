// Crew-KI (Teil des UBOAT Toolkit): CrewAIBedChoice. Schalter: F10 > QoL > "Crew-KI".
using System;
using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Characters.Navigation;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;

namespace UboatToolkit.Modules.CrewAi
{
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
