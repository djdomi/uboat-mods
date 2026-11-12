using System;
using System.Collections.Generic;
using HarmonyLib;
using UBOAT.Game.Core.AI;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Core.AI.Behaviours;
using UBOAT.Game.Core.Serialization;
using UBOAT.Game.Core.Time;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Characters.Actions;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;
using UnityEngine;

namespace UboatToolkit.Modules
{
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
            if (Time.realtimeSinceStartup >= nextPool) { nextPool = Time.realtimeSinceStartup + 5f; Pools(ship, crew.Characters); }
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
            foreach (var pc in chars)
            {
                if (pc == null || pc.Data == null) continue;
                if (pc.Data.CharacterType == PlayableCharacterData.Type.Sailor) nCrew++;
                else if (pc.Data.CharacterType == PlayableCharacterData.Type.Officer) nOff++;
            }
            float max = Mathf.Clamp(Settings.F("HotBunkMax"), 1f, 10f);
            if (Settings.B("HotBunkAuto")) { fOff = Factor(nOff, bOff, max); fCrew = Factor(nCrew, bCrew, max); }
            else fOff = fCrew = Mathf.Clamp(Settings.F("HotBunkManual"), 0.1f, max);
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
            if (m == null) { if (Math.Abs(v - 1f) < 0.0005f) return; m = p.GetScaleModifier(name, true); if (m == null) return; }
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
            foreach (var t in new[] { typeof(UBOAT.Game.Scene.Characters.Skills.Authority), AccessTools.TypeByName("UBOAT.Game.Sandbox.Missions.StartScript"), AccessTools.TypeByName("UBOAT.Game.Scene.Tasks.ExpandedSchedulesController") })
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
