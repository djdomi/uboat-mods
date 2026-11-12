using System.Collections.Generic;
using HarmonyLib;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Characters.Actions;
using UBOAT.Game.Scene.Characters.Activators;
using UBOAT.Game.Scene.Characters.Skills;
using UBOAT.Game.Scene.Characters.Statuses;
using UBOAT.Game.Scene.Items;
using UBOAT.Game.Scene.Utilities;
using UnityEngine;

namespace UboatToolkit.Modules
{
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
