// Crew-KI (Teil des UBOAT Toolkit): CrewAICrowdCensus. Schalter: F10 > QoL > "Crew-KI".
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Characters.Actions;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;

namespace UboatToolkit.Modules.CrewAi
{
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
