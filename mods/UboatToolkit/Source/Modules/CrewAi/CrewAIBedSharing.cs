// Crew-KI (Teil des UBOAT Toolkit): CrewAIBedSharing. Schalter: F10 > QoL > "Crew-KI".
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using HarmonyLib;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Characters.Navigation;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Interactions;
using UBOAT.Game.Scene.Items;

namespace UboatToolkit.Modules.CrewAi
{
    internal static class BedSharingToggles
    {
        public static bool Enabled => UboatToolkit.Settings.B("CrewBedSharing");

        public const int MenPerBunk = 2;

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
