using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using DWS.Common.InjectionFramework;
using HarmonyLib;
using UnityEngine;
using UBOAT.Game.Scene.Audio;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Characters.Activators;
using UBOAT.Game.Scene.Selectables;
using UBOAT.Game.UI;
using UBOAT.Game.UI.Banners;
using UBOAT.Game.UI.Notifications;

namespace UboatToolkit.Modules
{
    // Quiet interface: crew names / action icons / rest bars only on hover or selection,
    // a level per notification (Keep / Quiet / Off) in the file toolkit-notifications.txt,
    // the top banner and chime silenced, chosen tooltips blocked, port officer voices muted.
    // Presentation only: nothing of this reaches a savegame.
    internal enum QuietLevel { Keep = 0, Quiet = 1, Off = 2 }

    internal static class QuietCfg
    {
        public static bool On => Settings.B("QuietUI");
        public static string File => Path.Combine(Settings.Folder, "toolkit-notifications.txt");
        public static readonly Dictionary<string, QuietLevel> Levels = new Dictionary<string, QuietLevel>(StringComparer.OrdinalIgnoreCase);
        public static readonly HashSet<string> Exceptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public static readonly HashSet<string> BlockedTooltips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public static int Version;
        private static DateTime stamp;
        private static float nextPoll;

        public static readonly string[] DefaultQuiet = {
            "Undetected", "Hostility", "Alarm", "SilentRun", "NearbyContacts", "Buoyancy", "AirTanks", "AirTanksLow",
            "TravelMode", "ElectricEnginesPreferred", "DecreasedPerformance", "Dirt", "Hunger", "Stress",
            "LowSpareParts", "NoSpareParts", "DepletingResource", "MissingCook", "MissingWorkers", "InspectionReminder",
            "Overwork", "Personality", "Captives", "CrewSpecializationPointGained", "Vacation",
            "AwaitingMessages", "HighPriorityAwaitingMessages", "LowPriorityAwaitingMessages", "Intel", "CheckpointUnlocked",
            "HeadquartersTask", "FreeHeadquarterTaskSlot" };
        public static readonly string[] DefaultOff = { "TimeZoneChanged", "BackgroundLoading" };
        public static readonly string[] DefaultExceptions = { "ShoalAhead", "ShallowArea", "PortDefences", "AntiSubmarineNetCollision", "TriggeredMine", "HullCrush", "AircraftDetection" };
        private static readonly string[][] Groups = {
            new[] { "# Navigation und Gefahren (die sagt dir sonst nichts - am besten Keep lassen)", "ShoalAhead", "ShallowArea", "PortDefences", "AntiSubmarineNetCollision", "TriggeredMine", "HullCrush", "LowNavigationAccuracy", "Lost" },
            new[] { "# Entdeckung und Kampf", "AircraftDetection", "ShipDetection", "SmokeDetection", "PropellerDetection", "DirectDetection", "IndirectDetection", "TransmissionDetectedByEnemy", "Undetected", "Hostility", "Alarm", "SilentRun", "NearbyContacts" },
            new[] { "# Schäden und Geräte (alles, worauf du reagieren musst, bleibt Keep)", "Leak", "BrokenDevice", "Flaw", "FlawActivated", "FlawStatus", "DeviceStatus", "ToxicAir" },
            new[] { "# Boots-Zustand (das Symbol ist die Info, das Band die Störung - Quiet nimmt nur das Band)", "Buoyancy", "AirTanks", "AirTanksLow", "TravelMode", "ElectricEnginesPreferred", "DecreasedPerformance", "Dirt", "Hunger", "Stress", "LowSpareParts", "NoSpareParts", "DepletingResource", "MissingCook", "MissingWorkers", "InspectionReminder" },
            new[] { "# Crew", "Death", "Bleeding", "HeartAttack", "Disease", "SailorOffBoard", "CrewMembersHospitalized", "CrewMembersArrested", "SailorStatus", "Overwork", "Personality", "Captives", "CrewSpecializationPointGained", "Vacation" },
            new[] { "# Kampagne, Funk und Sonstiges", "RadioTransmission", "RadioTransmissionInterrupted", "MissionStatusUpdate", "Resupply", "ResupplyShipSank", "UpgradesInstallation", "Minelaying", "AwaitingMessages", "HighPriorityAwaitingMessages", "LowPriorityAwaitingMessages", "Intel", "CheckpointUnlocked", "HeadquartersTask", "FreeHeadquarterTaskSlot", "TimeZoneChanged", "BackgroundLoading" } };

        private static void Defaults()
        {
            Levels.Clear(); Exceptions.Clear(); BlockedTooltips.Clear();
            foreach (var n in DefaultQuiet) Levels[n] = QuietLevel.Quiet;
            foreach (var n in DefaultOff) Levels[n] = QuietLevel.Off;
            foreach (var n in DefaultExceptions) Exceptions.Add(n);
        }

        public static QuietLevel LevelFor(string className)
        {
            string key = className.EndsWith("Notification", StringComparison.Ordinal) && className.Length > 12 ? className.Substring(0, className.Length - 12) : className;
            if (!Levels.TryGetValue(key, out var l)) l = QuietLevel.Keep;
            if (l == QuietLevel.Keep && Settings.B("QuietBanner") && !Exceptions.Contains(key)) l = QuietLevel.Quiet;
            return l;
        }

        // Re-reads the file when it changed (about once per second).
        public static void Poll()
        {
            if (Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + 1f;
            try
            {
                if (!System.IO.File.Exists(File)) { Directory.CreateDirectory(Settings.Folder); System.IO.File.WriteAllText(File, Template(), new UTF8Encoding(false)); }
                var t = System.IO.File.GetLastWriteTimeUtc(File);
                if (t == stamp && Version > 0) return;
                stamp = t;
                Load();
            }
            catch (Exception e) { if (Version == 0) { Defaults(); Version++; } Util.Log("Ruhig: Datei nicht lesbar: " + e.Message); }
        }

        private static void Load()
        {
            Defaults();
            bool exceptionsSeen = false;
            foreach (var raw in System.IO.File.ReadAllLines(File))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int c = line.IndexOf(':');
                if (c <= 0) continue;
                string key = line.Substring(0, c).Trim(), val = line.Substring(c + 1).Trim();
                if (key.StartsWith("Notification.", StringComparison.OrdinalIgnoreCase))
                {
                    string name = key.Substring(13).Trim();
                    if (name.Length > 0) Levels[name] = val.Equals("Quiet", StringComparison.OrdinalIgnoreCase) ? QuietLevel.Quiet : val.Equals("Off", StringComparison.OrdinalIgnoreCase) ? QuietLevel.Off : QuietLevel.Keep;
                }
                else if (key.Equals("PopupExceptions", StringComparison.OrdinalIgnoreCase))
                {
                    if (!exceptionsSeen) { Exceptions.Clear(); exceptionsSeen = true; }
                    foreach (var p in val.Split(',')) if (p.Trim().Length > 0) Exceptions.Add(p.Trim());
                }
                else if (key.Equals("BlockedTooltipKeys", StringComparison.OrdinalIgnoreCase))
                    foreach (var p in val.Split(',')) if (p.Trim().Length > 0) BlockedTooltips.Add(p.Trim());
            }
            Version++;
            int q = Levels.Count(kv => kv.Value == QuietLevel.Quiet), o = Levels.Count(kv => kv.Value == QuietLevel.Off);
            Util.Log("Ruhig: Meldungen " + q + " leise / " + o + " aus, " + Exceptions.Count + " Ausnahmen, " + BlockedTooltips.Count + " Tooltips gesperrt");
        }

        private static string Template()
        {
            var b = new StringBuilder();
            void L(string s) { b.Append(s).Append("\r\n"); }
            L("# UBOAT Toolkit - Meldungen und Tooltips (wird im laufenden Spiel nach ca. 1 Sekunde neu gelesen)");
            L("# Nur Anzeige: nichts davon kommt in einen Spielstand.");
            L("#");
            L("# Stufen pro Meldung:  Keep = wie im Spiel,  Quiet = Symbol bleibt, kein Band oben und kein Ton,  Off = gar nichts");
            L("# Schalter „Band oben stumm“ (F10) macht alle Keep-Meldungen leise, außer denen hier:");
            L("PopupExceptions: " + string.Join(", ", DefaultExceptions));
            L("");
            L("# Tooltips sperren: im F10-Menü „Tooltip-Schlüssel mitschreiben“ einschalten, spielen,");
            L("# dann toolkit-tooltip-keys.txt lesen und Schlüssel hier eintragen (mit Komma getrennt).");
            L("BlockedTooltipKeys:");
            L("");
            foreach (var g in Groups)
            {
                foreach (var e in g)
                {
                    if (e[0] == '#') { L(e); continue; }
                    string lv = DefaultOff.Contains(e) ? "Off" : DefaultQuiet.Contains(e) ? "Quiet" : "Keep";
                    L("Notification." + e + ": " + lv);
                }
                L("");
            }
            return b.ToString();
        }
    }

    // Runs the file poll and the tooltip key log flush. Registered in ToolkitRunner's frame loop.
    public class QuietUIHost : ModuleBase
    {
        public override void Frame() { if (QuietCfg.On) { QuietCfg.Poll(); QuietTooltips.Flush(); } }
    }

    [HarmonyPatch(typeof(Notification), "SetActive")]
    internal static class Quiet_NotificationPatch
    {
        private static readonly FieldInfo AudioTicks = AccessTools.Field(typeof(Notification), "nextAudioFxTicks");
        private static readonly Dictionary<Type, QuietLevel> cache = new Dictionary<Type, QuietLevel>();
        private static int cacheVersion = -1;
        private static int cacheBanner = -1;
        private static void Prefix(Notification __instance, ref bool active, ref string ribbonText)
        {
            if (!QuietCfg.On) return;
            try
            {
                QuietCfg.Poll();
                int banner = Settings.B("QuietBanner") ? 1 : 0;
                if (cacheVersion != QuietCfg.Version || cacheBanner != banner) { cache.Clear(); cacheVersion = QuietCfg.Version; cacheBanner = banner; }
                var t = __instance.GetType();
                if (!cache.TryGetValue(t, out var level)) cache[t] = level = QuietCfg.LevelFor(t.Name);
                if (level == QuietLevel.Keep) return;
                bool had = ribbonText != null;
                ribbonText = null;
                AudioTicks?.SetValue(__instance, long.MaxValue);
                if (level == QuietLevel.Off) active = false;
                if (Settings.B("QuietLog") && (had || level == QuietLevel.Off)) Util.Log("Ruhig: Meldung " + t.Name + " -> " + level);
            }
            catch (Exception e) { Util.Log("Ruhig: Meldungs-Filter: " + e.Message); }
        }
    }

    // The second banner route (save confirmation, experience, stress peak, map marker, save error).
    [HarmonyPatch(typeof(NotificationBarUI), "OpenNow")]
    internal static class Quiet_BarOpenPatch
    {
        private static bool Prefix() => !(QuietCfg.On && Settings.B("QuietDirectPopups"));
    }
    [HarmonyPatch(typeof(RecentNotificationsListUI), "OpenNow")]
    internal static class Quiet_RecentOpenPatch
    {
        private static bool Prefix() => !(QuietCfg.On && Settings.B("QuietDirectPopups"));
    }

    internal static class QuietTooltips
    {
        private static readonly HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        private static readonly List<string> pending = new List<string>();
        private static bool warned;

        public static bool Block(string key, string source)
        {
            if (!QuietCfg.On) return false;
            bool log = Settings.B("QuietTooltipLog");
            if (string.IsNullOrEmpty(key)) { if (log) Record("(dynamisch, ohne Schlüssel)", source); return false; }
            if (log) Record(key, source);
            QuietCfg.Poll();
            return QuietCfg.BlockedTooltips.Contains(key);
        }
        private static void Record(string key, string source)
        {
            string line = key + "    [" + source + "]";
            lock (seen) { if (seen.Add(line)) pending.Add(line); }
        }
        public static void Flush()
        {
            string[] batch;
            lock (seen) { if (pending.Count == 0) return; batch = pending.ToArray(); pending.Clear(); }
            try { Directory.CreateDirectory(Settings.Folder); System.IO.File.AppendAllText(Path.Combine(Settings.Folder, "toolkit-tooltip-keys.txt"), string.Join("\r\n", batch) + "\r\n", new UTF8Encoding(false)); }
            catch (Exception e) { if (!warned) { warned = true; Util.Log("Ruhig: Tooltip-Liste nicht schreibbar: " + e.Message); } }
        }
    }
    [HarmonyPatch(typeof(Tooltip), "Open")]
    internal static class Quiet_TooltipPatch
    {
        private static bool Prefix(Tooltip __instance) { try { return !QuietTooltips.Block(__instance.LocalizationKey, "Tooltip"); } catch { return true; } }
    }
    [HarmonyPatch(typeof(InteriorCanvasTooltip), "Open")]
    internal static class Quiet_InteriorTooltipPatch
    {
        private static bool Prefix(InteriorCanvasTooltip __instance) { try { return !QuietTooltips.Block(__instance.LocalizationKey, "InteriorCanvasTooltip"); } catch { return true; } }
    }

    // Voices: light-switch callouts (Voice 158-160) and everyone who is not your own crew. Subtitles stay.
    [HarmonyPatch]
    internal static class Quiet_VoicePatch
    {
        private static readonly string[] LightCallouts = { "Voice 158", "Voice 159", "Voice 160" };
        private static MethodBase TargetMethod() =>
            AccessTools.GetDeclaredMethods(typeof(InteractiveCharacter)).FirstOrDefault(m => m.Name == "SayNow" && m.GetParameters().Length == 6 && m.GetParameters()[0].ParameterType == typeof(string));
        private static void Prefix(InteractiveCharacter __instance, string statementId)
        {
            try
            {
                if (__instance == null) return;
                bool light = Settings.B("MuteLightCallouts"), port = QuietCfg.On && Settings.B("QuietMutePort");
                if (!light && !port) return;
                string why = null;
                if (light && statementId != null && LightCallouts.Any(x => string.Equals(x, statementId, StringComparison.OrdinalIgnoreCase))) why = "Licht-Ansage";
                else if (port)
                {
                    var pc = __instance as PlayableCharacter;
                    if (pc == null) why = "keine Crew";
                    else if (pc.Data != null)
                        switch (pc.Data.CharacterType)
                        {
                            case PlayableCharacterData.Type.Sailor: case PlayableCharacterData.Type.Officer:
                            case PlayableCharacterData.Type.Skipper: case PlayableCharacterData.Type.Guest: break;
                            default: why = "Typ " + pc.Data.CharacterType; break;
                        }
                }
                if (why == null) return;
                DeepAudioSource own = __instance.SpeakAudioSource;
                if (own != null) own.volume = 0f;
                if (Settings.B("QuietLog")) Util.Log("Ruhig: stumm '" + statementId + "' von " + __instance.name + " (" + why + ")");
            }
            catch (Exception e) { Util.Log("Ruhig: Stimme: " + e.Message); }
        }
    }

    // Names, action icons, rest bars, event icons, timers above the crew: only on hover / selection.
    internal static class QuietNames
    {
        private static readonly FieldInfo LabelF = AccessTools.Field(typeof(BaseActivatorBanner), "label");
        private static readonly FieldInfo PlateF = AccessTools.Field(typeof(BaseActivatorBanner), "labelBackground");
        private static readonly FieldInfo HoveredF = AccessTools.Field(typeof(BaseActivatorBanner), "hovered");
        private static readonly FieldInfo ManagerF = AccessTools.Field(typeof(BaseActivatorBanner), "manager");
        private static readonly FieldInfo ActivatorF = AccessTools.Field(typeof(BaseActivatorBanner), "activator");
        private static readonly FieldInfo SelectionsF = AccessTools.Field(typeof(BaseActivatorBanner), "selections");
        private static readonly FieldInfo ProgressF = AccessTools.Field(typeof(CharacterActivatorBanner), "actionProgressBarParent");
        private static readonly FieldInfo EnergyF = AccessTools.Field(typeof(CharacterActivatorBanner), "energyBarParent");
        private static readonly FieldInfo ActionIconF = AccessTools.Field(typeof(CharacterActivatorBanner), "actionIcon");
        private static readonly FieldInfo EventIconF = AccessTools.Field(typeof(CharacterActivatorBanner), "eventIcon");
        private static readonly FieldInfo TimeLeftF = AccessTools.Field(typeof(CharacterActivatorBanner), "timeLeftLabel");
        private static SelectionManager selections;
        private static bool selectionsTried, reported;

        public static bool Ready => LabelF != null && PlateF != null && HoveredF != null && ManagerF != null && ActivatorF != null;
        public static bool ExtrasReady => HoveredF != null && ManagerF != null && ProgressF != null && EnergyF != null;

        private static void Report()
        {
            if (reported) return;
            reported = true;
            if (!Ready || !ExtrasReady) Util.Log("Ruhig: Namensschild-Felder fehlen (Spiel-Update?) - Namen/Symbole bleiben wie im Spiel");
        }

        private static bool WantVisible(BaseActivatorBanner banner, CharacterActivatorBanner character)
        {
            bool showAll = (ManagerF.GetValue(banner) as ActivatorBannersManager)?.ShowHiddenActivators ?? false;
            bool hovered = Settings.B("QuietShowHover") && (bool)HoveredF.GetValue(banner);
            bool selected = Settings.B("QuietShowSelected") && character != null && IsSelected(character);
            return banner.Display && (showAll || hovered || selected);
        }

        public static void Apply(BaseActivatorBanner banner)
        {
            if (banner == null || !QuietCfg.On) return;
            Report();
            if (!Ready) return;
            var ch = banner as CharacterActivatorBanner;
            if (ch != null) ApplyExtras(ch);
            if (ch != null ? !Settings.B("QuietNames") : !Settings.B("QuietDeviceNames")) return;
            var label = LabelF.GetValue(banner) as Behaviour;
            if (label == null) return;
            bool want = WantVisible(banner, ch);
            if (label.enabled != want) label.enabled = want;
            var plate = PlateF.GetValue(banner) as Behaviour;
            if (plate != null)
            {
                bool p = want && (ActivatorF.GetValue(banner) as CharacterActivator) != null;
                if (plate.enabled != p) plate.enabled = p;
            }
        }

        public static void ApplyExtras(CharacterActivatorBanner b)
        {
            if (b == null || !QuietCfg.On || !ExtrasReady) return;
            bool icons = Settings.B("QuietActionIcons"), energy = Settings.B("QuietEnergyBars"), events = Settings.B("QuietEventIcons"), timers = Settings.B("QuietTimers");
            if (!icons && !energy && !events && !timers) return;
            if (WantVisible(b, b)) return;
            if (icons) { Hide(ProgressF.GetValue(b) as GameObject); Hide((ActionIconF?.GetValue(b) as Component)?.gameObject); }
            if (energy) Hide((EnergyF.GetValue(b) as Component)?.gameObject);
            if (events) Hide((EventIconF?.GetValue(b) as Component)?.gameObject);
            if (timers) { var t = TimeLeftF?.GetValue(b) as Behaviour; if (t != null && t.enabled) t.enabled = false; }
        }

        private static void Hide(GameObject go) { if (go != null && go.activeSelf) go.SetActive(false); }

        private static bool IsSelected(CharacterActivatorBanner b)
        {
            var c = b.Character;
            if (c == null) return false;
            if (!selectionsTried)
            {
                selectionsTried = true;
                try { selections = SelectionsF?.GetValue(null) as SelectionManager ?? ScriptableObjectSingleton.LoadSingleton<SelectionManager>(); }
                catch (Exception e) { Util.Log("Ruhig: Auswahl nicht gefunden - ausgewählte Crew zeigt keinen Namen: " + e.Message); }
            }
            try { return selections != null && selections.HasSelection(c); } catch { return false; }
        }
    }

    [HarmonyPatch(typeof(BaseActivatorBanner), "ValidateVisibility")]
    internal static class Quiet_BannerVisibilityPatch
    {
        private static void Postfix(BaseActivatorBanner __instance) => QuietNames.Apply(__instance);
    }
    [HarmonyPatch(typeof(BaseActivatorBanner), "Selectable_HoverStop")]
    internal static class Quiet_BannerHoverStopPatch
    {
        private static void Postfix(BaseActivatorBanner __instance) => QuietNames.Apply(__instance);
    }
    [HarmonyPatch(typeof(CharacterActivatorBanner), "OnSetDisplayMode")]
    internal static class Quiet_BannerDisplayModePatch
    {
        private static void Postfix(CharacterActivatorBanner __instance) => QuietNames.ApplyExtras(__instance);
    }
}
