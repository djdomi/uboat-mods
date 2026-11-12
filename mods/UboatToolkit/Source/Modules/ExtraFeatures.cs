using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;
using DWS.Common.Tasks;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Characters.Actions;
using UBOAT.Game.Scene.Characters.Skills;
using UBOAT.Game.Scene.Items;
using UBOAT.Game.UI.Loading;

namespace UboatToolkit.Modules
{
    // Ideas from popular workshop mods, own implementation, all in one:
    // energy saving, tube display (torpedo types), officer roles + skill choice (tab "Crew"),
    // faster torpedo loading, faster game loading.
    public class ExtraFeatures : ModuleBase
    {
        private bool energySaved;
        public static string TubeLine;

        // Searching all loaded objects for these took 150-200 ms each second (lag every few seconds).
        // They are found once and kept; the skill cheats only need to run every 10 seconds.
        private static CharacterClasses classesCache;
        private static PlayerCrew crewCache;
        private static float nextSkills;
        private static int lastFlags = -1;

        // A failed search (main menu, loading) used to be repeated on every call: 100-400 ms each time.
        private static float classesRetry, crewRetry;
        private static IEnumerable<CharacterClass> Classes()
        {
            if (classesCache == null && Time.unscaledTime >= classesRetry) { classesRetry = Time.unscaledTime + 30f; classesCache = Resources.FindObjectsOfTypeAll<CharacterClasses>().FirstOrDefault(); }
            return classesCache?.GetClassesDirect();
        }

        private static PlayerCrew Crew()
        {
            if (crewCache == null && Time.unscaledTime >= crewRetry) { crewRetry = Time.unscaledTime + 30f; crewCache = Object.FindObjectOfType<PlayerCrew>(); }
            return crewCache;
        }

        // The skill cheats walk every skill of every crew member (100-400 ms with a big crew). The result only changes
        // when the crew changes, so they run again only then (and every 5 minutes as a safety net).
        private static int lastCrewCount = -1;
        private static float fullRunAt;
        private static bool CrewChanged()
        {
            int n = Util.SafeGet(() => Crew()?.Characters?.Count() ?? -1, -1);
            if (n == lastCrewCount && Time.unscaledTime < fullRunAt) return false;
            lastCrewCount = n; fullRunAt = Time.unscaledTime + 300f;
            return true;
        }

        public override void Tick()
        {
            var ship = Util.PlayerShip;
            EnergySaver(ship);
            Tubes();
            // Crew specializations (diving / silent running / discipline, 10 each): always enough free points.
            if (Settings.B("CheatSpecPoints"))
                Util.Safe("SpecPoints", () =>
                {
                    var c = Crew();
                    if (c == null) return;
                    int need = Mathf.Max(0, 30 - c.SpecializationDivingPoints - c.SpecializationSilentRunningPoints - c.SpecializationDisciplinePoints);
                    if (c.FreeSpecializationPoints < need) c.FreeSpecializationPoints = need;
                });
            bool any = Settings.B("CheatSkillsAnyLevel");
            if (!any && skillLevels.Count > 0) SkillLevels(false);
            if (Time.unscaledTime < nextSkills) return;
            nextSkills = Time.unscaledTime + 10f;
            bool all = Settings.B("CheatAllSkills"), ling = Settings.B("CheatLinguist");
            bool turnedOn = (all ? 1 : 0) + (any ? 2 : 0) + (ling ? 4 : 0) != lastFlags;
            lastFlags = (all ? 1 : 0) + (any ? 2 : 0) + (ling ? 4 : 0);
            if (!CrewChanged() && !turnedOn) return;
            if (all) AllSkills();
            if (any) SkillLevels(true);
            if (ling) Linguists();
        }

        // Everyone who speaks one foreign language ("... Speaker") also gets "Linguist" (all languages).
        private static void Linguists()
        {
            var classes = Classes();
            var ling = classes?.SelectMany(c => c.Skills).FirstOrDefault(x => x != null && x.Name == "Linguist");
            var crew = Crew();
            if (ling == null || crew?.Characters == null) return;
            foreach (var ch in crew.Characters)
            {
                var d = ch?.Data;
                if (d == null) continue;
                var sk = d.Statuses.OfType<CharacterSkill>().ToList();
                if (sk.Any(x => x.Name.EndsWith(" Speaker")) && !sk.Any(x => x.Name == "Linguist")) d.LearnSkill(ling, false);
            }
        }

        // All skills can be learned at any level: the level requirement is set to 0 (and restored when switched off).
        private static readonly Dictionary<CharacterSkill, int> skillLevels = new Dictionary<CharacterSkill, int>();
        private static void SkillLevels(bool on)
        {
            if (!on)
            {
                if (skillLevels.Count == 0) return;
                foreach (var kv in skillLevels) if (kv.Key != null) kv.Key.RequiredLevel = kv.Value;
                skillLevels.Clear();
                return;
            }
            var list = new List<CharacterSkill>();
            var classes = Classes();
            if (classes != null) list.AddRange(classes.SelectMany(c => c.Skills));
            var crew = Crew();
            if (crew?.Characters != null)
                foreach (var ch in crew.Characters)
                    if (ch?.Data?.AvailableSkills != null) list.AddRange(ch.Data.AvailableSkills);
            foreach (var sk in list)
            {
                if (sk == null || sk.RequiredLevel <= 0) continue;
                if (!skillLevels.ContainsKey(sk)) skillLevels[sk] = sk.RequiredLevel;
                sk.RequiredLevel = 0;
            }
        }

        // Battery low under water: switch off big power users (compressor, searchlight) once.
        private void EnergySaver(UBOAT.Game.Scene.Entities.PlayerShip ship)
        {
            float lim = Settings.F("EnergySaverPercent") / 100f;
            var r = ship.GetResource("Energy");
            if (lim <= 0f || r == null) { energySaved = false; return; }
            float v = (float)r.Amount;
            if (v > lim + 0.1f) energySaved = false;
            if (energySaved || v >= lim || !ship.SubmergedOrGoingToSubmerge) return;
            energySaved = true;
            int n = 0;
            foreach (var c in Util.Find<AirCompressor>()) if (!c.HasDisableFlag(DeviceDisableFlag.TurnedOff)) { c.SetDisableFlag(DeviceDisableFlag.TurnedOff, true); n++; }
            foreach (var s in Util.Find<Searchlight>()) if (!s.HasDisableFlag(DeviceDisableFlag.TurnedOff)) { s.SetDisableFlag(DeviceDisableFlag.TurnedOff, true); n++; }
            if (n > 0) ToolkitRunner.ShowNotice("Strom sparen: " + n + " Geräte aus (Batterie " + (v * 100f).ToString("0") + " %)", 6f);
        }

        // "Rohr 1: T2 | Rohr 2: leer ..." in the info area.
        private static void Tubes()
        {
            if (!Settings.B("TubeDisplay")) { TubeLine = null; return; }
            var parts = new List<string>();
            int i = 1;
            foreach (var l in Util.Find<TorpedoLauncher>(2f))
            {
                string t = "leer";
                if (l.Torpedo != null)
                {
                    var m = Regex.Match(l.Torpedo.name, @"\bT\d+[a-z]?\b");
                    t = m.Success ? m.Value : (l.Torpedo is StoredMinePacket ? "Mine" : l.Torpedo.name.Replace("(Clone)", "").Trim());
                    if (!l.IsLoaded) t += " (lädt)";
                }
                parts.Add(i++ + ": " + t);
            }
            TubeLine = parts.Count > 0 ? "Rohre  " + string.Join("  |  ", parts) : null;
        }

        // Cheat: every officer may choose skills of all classes.
        private static void AllSkills()
        {
            var classes = Classes();
            if (classes == null) return;
            var all = classes.SelectMany(c => c.Skills).GroupBy(s => s.Name).Select(g => g.First()).ToList();
            var crew = Crew();
            if (crew?.Characters == null) return;
            foreach (var ch in crew.Characters)
            {
                var d = ch?.Data;
                if (d == null || d.CharacterType == PlayableCharacterData.Type.Sailor) continue;
                var owned = new HashSet<string>(d.Statuses.OfType<CharacterSkill>().Select(s => s.Name));
                var avail = d.AvailableSkills.ToList();
                bool changed = false;
                foreach (var s in all)
                    if (!owned.Contains(s.Name) && !avail.Any(a => a.Name == s.Name)) { avail.Add(s); changed = true; }
                if (changed) Util.SetField(d, "availableSkills", avail.ToArray());
            }
        }

        // ---------- Tab "Crew" in the F10 menu: roles + skill points ----------
        private static readonly List<ICharacterRole> roles = new List<ICharacterRole>();

        public static void DrawCrewTab()
        {
            var crew = Crew();
            if (Util.PlayerShip == null || crew?.Characters == null) { Imgui.Label("Nur im Spiel verfügbar."); return; }
            var db = Traverse.Create(typeof(UBOAT.Game.Scene.Tasks.ExpandedSchedulesController)).Field("characterRolesDatabase").GetValue() as ICharacterRolesDatabase;
            Imgui.Label("Offiziere: Rolle mit Klick wechseln. „Skills bearbeiten“ öffnet den Skill-Editor (alle Skills aller Klassen).");
            foreach (var ch in crew.Characters)
            {
                var d = ch?.Data;
                if (d == null || d.CharacterType == PlayableCharacterData.Type.Sailor) continue;
                Imgui.BeginH();
                Imgui.Label(d.Name + "  (Skillpunkte frei: " + d.GetAvailableSkillPoints() + ")", 330);
                string roleName = ch.Role?.Name ?? "keine";
                if (Imgui.Button("Rolle: " + roleName, 300) && db != null)
                {
                    db.GetAvailableRoles(ch, roles);
                    var allowed = roles.Where(r => r.IsRoleAllowedForCharacter(ch)).ToList();
                    if (allowed.Count > 0)
                    {
                        int i = allowed.IndexOf(ch.Role);
                        ch.Role = allowed[(i + 1) % allowed.Count];
                    }
                }
                bool open = editSkills == d;
                if (Imgui.Button(open ? "Skills zu" : "Skills bearbeiten", 160)) editSkills = open ? null : d;
                Imgui.EndH();
                if (editSkills == d) SkillEditor(d);
            }
        }

        // Skill editor: every skill of every class, one click adds or removes it (no points, no level needed).
        private static PlayableCharacterData editSkills;
        private static void SkillEditor(PlayableCharacterData d)
        {
            var classes = Classes();
            if (classes == null) { Imgui.Label("  Skill-Liste nicht gefunden."); return; }
            var all = classes.SelectMany(c => c.Skills).Where(x => x != null).GroupBy(x => x.Name).Select(g => g.First()).OrderBy(x => x.Name).ToList();
            var owned = d.Statuses.OfType<CharacterSkill>().ToList();
            Imgui.BeginH();
            Imgui.Label("  Klick = Skill dazu (+) oder weg (✓).", 330);
            if (Imgui.Button("Alle geben", 120)) foreach (var sk in all) if (!owned.Any(o => o.Name == sk.Name)) d.LearnSkill(sk, false);
            if (Imgui.Button("Alle weg", 120)) foreach (var o in owned) d.RemoveStatus(o);
            Imgui.EndH();
            for (int i = 0; i < all.Count; i += 3)
            {
                Imgui.BeginH();
                Imgui.Space(20);
                for (int j = i; j < i + 3 && j < all.Count; j++)
                {
                    var sk = all[j];
                    var have = owned.FirstOrDefault(o => o.Name == sk.Name);
                    if (Imgui.Button((have != null ? "✓ " : "+ ") + sk.Name, 250))
                    {
                        if (have != null) d.RemoveStatus(have);
                        else d.LearnSkill(sk, false);
                    }
                }
                Imgui.EndH();
            }
            Imgui.Space(6);
        }
    }

    // Cheat: extra skill points for every officer.
    [HarmonyPatch(typeof(PlayableCharacterData), nameof(PlayableCharacterData.GetAvailableSkillPoints))]
    internal static class ExtraFeatures_SkillPointsPatch
    {
        // The game clamps at 0, so the extra points are added before that clamp (recomputed here).
        private static void Postfix(PlayableCharacterData __instance, ref int __result)
        {
            if (Settings.B("CheatUnlimitedSkillPoints")) { __result = 99; return; }
            int extra = Mathf.Max(0, (int)Settings.F("CheatExtraSkillPoints"));
            if (extra == 0) return;
            int level = Traverse.Create(__instance).Field("level").GetValue<int>();
            int used = __instance.Statuses.OfType<CharacterSkill>().Count(sk => !sk.IsFree);
            __result = Mathf.Max(0, level - 1 - used + extra);
        }
    }

    // Torpedo loading x faster (1000 = practically instant).
    [HarmonyPatch(typeof(LoadTorpedoesAction), "StartMainCharacter")]
    internal static class ExtraFeatures_TorpedoLoadPatch
    {
        private static void Postfix(LoadTorpedoesAction __instance)
        {
            float f = Settings.F("TorpedoLoadSpeed");
            if (f <= 1f) return;
            var t = Traverse.Create(__instance).Field("performanceMultiplier");
            t.SetValue(t.GetValue<float>() * f);
        }
    }

    // Faster game loading: while the loading screen runs, the game's background work may use the
    // whole frame (normally only ~2 ms per frame) and textures are uploaded in bigger pieces.
    [HarmonyPatch(typeof(LoadingUIController), "UpdateSceneLoading")]
    internal static class ExtraFeatures_LoadingBoostPatch
    {
        private static bool boosted;
        private static int origSlice, origBuffer;
        private static ThreadPriority origPriority;

        private static void Postfix(LoadingUIController __instance)
        {
            bool loading = Traverse.Create(__instance).Field("isLoading").GetValue<bool>();
            var ex = TaskExecutor.Instance;
            if (loading && Settings.B("FastLoading"))
            {
                if (!boosted) { origSlice = QualitySettings.asyncUploadTimeSlice; origBuffer = QualitySettings.asyncUploadBufferSize; origPriority = Application.backgroundLoadingPriority; }
                if (ex != null) ex.UnlimitedFrameBudget = true;
                QualitySettings.asyncUploadTimeSlice = 33;
                QualitySettings.asyncUploadBufferSize = 64;
                Application.backgroundLoadingPriority = ThreadPriority.High;
                boosted = true;
            }
            else if (boosted)
            {
                if (ex != null) ex.UnlimitedFrameBudget = false;
                QualitySettings.asyncUploadTimeSlice = origSlice;
                QualitySettings.asyncUploadBufferSize = origBuffer;
                Application.backgroundLoadingPriority = origPriority;
                boosted = false;
            }
        }
    }
}
