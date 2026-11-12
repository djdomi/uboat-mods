using HarmonyLib;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System;
using TMPro;
using UBOAT.Game.Core.Serialization;
using UBOAT.Game.Sandbox;
using UBOAT.Game.UI;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine;

namespace UboatToolkit.Modules.SaveCats
{
    // Load menu grouped by career: a tab per career above the save list (plus "Alle").
    // Career identity comes from data the game already stores; saves are never written to.
    // Our own small index file (toolkit-career-index.tsv) remembers which save belongs to which career.
    internal static class SaveCatCfg
    {
        public static bool Active => Settings.B("SaveGroupCareer");
    }

    internal static class SaveCatClock
    {
        public static long Now() => UboatToolkit.Modules.LoadClock.Now();
        public static double MsSince(long t) => UboatToolkit.Modules.LoadClock.Seconds(UboatToolkit.Modules.LoadClock.Now() - t) * 1000.0;
    }

    internal static class CscLog
    {
        public static void Info(string m) { if (Settings.B("SaveCatLog")) Util.Log("Karrieren: " + m); }
        public static void Warn(string m) { Util.Log("Karrieren: " + m); }
        public static void Error(string m, Exception ex) { Util.Log("Karrieren: " + m + (ex != null ? " - " + ex : "")); }
    }

    internal static class CscSession
    {
        private static string _careerGuid;
        internal static string CareerGuid
        {
            get { return _careerGuid; }
        }
        internal static void BeginNewCareer()
        {
            _careerGuid = Guid.NewGuid().ToString("N");
            CscLog.Info("new career started, tracking as " + Short(_careerGuid));
        }
        internal static void Adopt(string careerGuid)
        {
            if (string.IsNullOrEmpty(careerGuid))
            {
                _careerGuid = null;
                CscLog.Warn("loaded a save with no identifiable career - the next save will start a new group");
                return;
            }
            _careerGuid = careerGuid;
            CscLog.Info("continuing career " + Short(careerGuid));
        }
        internal static string CareerForSave()
        {
            if (string.IsNullOrEmpty(_careerGuid))
            {
                _careerGuid = Guid.NewGuid().ToString("N");
                CscLog.Info("saving with no career held, tracking as " + Short(_careerGuid));
            }
            return _careerGuid;
        }
        private static string Short(string guid)
        {
            return string.IsNullOrEmpty(guid) || guid.Length < 8 ? guid : guid.Substring(0, 8);
        }
    }


    internal static class CscFingerprint
    {
        internal static string FromSkipperJson(string skipperJson)
        {
            if (string.IsNullOrEmpty(skipperJson))
                return null;
            var birthDate = ReadRawValue(skipperJson, "birthDate", 0);
            var portraitTiming = ReadRawValue(skipperJson, "portraitTiming", 0);
            if (string.IsNullOrEmpty(birthDate))
                return null;
            var hair = ReadHairColor(skipperJson);
            return birthDate + "|" + portraitTiming + "|" + hair;
        }
        private static string ReadHairColor(string json)
        {
            var at = json.IndexOf("\"hairColor\"", StringComparison.Ordinal);
            if (at < 0)
                return string.Empty;
            var r = ReadRawValue(json, "r", at);
            var g = ReadRawValue(json, "g", at);
            var b = ReadRawValue(json, "b", at);
            return r + "," + g + "," + b;
        }
        private static string ReadRawValue(string json, string key, int startAt)
        {
            var needle = "\"" + key + "\"";
            var at = json.IndexOf(needle, startAt, StringComparison.Ordinal);
            if (at < 0)
                return string.Empty;
            var colon = json.IndexOf(':', at + needle.Length);
            if (colon < 0)
                return string.Empty;
            var i = colon + 1;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t'))
                i++;
            var end = i;
            while (end < json.Length && json[end] != ',' && json[end] != '}' && json[end] != '\r' && json[end] != '\n')
                end++;
            return json.Substring(i, end - i).Trim().Trim(new[] { '"' });
        }
    }


    internal sealed class CscSaveRecord
    {
        internal string Path;
        internal long AssignedWriteTicks;
        internal string CareerGuid;
        internal string Fingerprint;
        internal string SkipperName;
        internal long GameDateTicks;
    }
    internal sealed class CscCareer
    {
        internal string Guid;
        internal string Label;
        internal long LastWriteTicks;
        internal long FirstWriteTicks;
        internal int SaveCount;
    }
    internal static class CscIndex
    {
        private const int DecodeBudgetMs = 4000;
        internal const long PendingWriteTicks = -1L;
        private const string Header = "#csc-index v2";
        private static readonly Dictionary<string, CscSaveRecord> ByPath =
            new Dictionary<string, CscSaveRecord>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> GuidByFingerprint =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private static bool _cacheLoaded;
        private static bool _dirty;
        private static string CachePath
        {
            get { return Application.persistentDataPath + "/toolkit-career-index.tsv"; }
        }
        private static readonly ConstructorInfo GameStateDataCtor =
            AccessTools.Constructor(typeof(SavesManager.GameStateData), new[] { typeof(string) });
        internal static CscSaveRecord[] Refresh(SavesManager.GameStateData[] saves)
        {
            LoadCache();
            var result = new CscSaveRecord[saves.Length];
            var timerStart = SaveCatClock.Now();
            var decoded = 0;
            var deferred = 0;
            for (var i = 0; i < saves.Length; i++)
            {
                var save = saves[i];
                if (save == null)
                    continue;
                var path = save.Path;
                long writeTicks;
                try
                {
                    writeTicks = save.WriteDate.Ticks;
                }
                catch
                {
                    writeTicks = 0L;
                }
                CscSaveRecord row;
                if (ByPath.TryGetValue(path, out row) && !string.IsNullOrEmpty(row.CareerGuid))
                {
                    if (row.AssignedWriteTicks == PendingWriteTicks)
                    {
                        row.AssignedWriteTicks = writeTicks;
                        _dirty = true;
                        if ((long)SaveCatClock.MsSince(timerStart) <= DecodeBudgetMs)
                            FillDisplayMetadata(save, row);
                        result[i] = row;
                        continue;
                    }
                    if (row.AssignedWriteTicks == writeTicks)
                    {
                        if (string.IsNullOrEmpty(row.SkipperName) && (long)SaveCatClock.MsSince(timerStart) <= DecodeBudgetMs)
                            FillDisplayMetadata(save, row);
                        result[i] = row;
                        continue;
                    }
                }
                if ((long)SaveCatClock.MsSince(timerStart) > DecodeBudgetMs)
                {
                    deferred++;
                    result[i] = new CscSaveRecord { Path = path, AssignedWriteTicks = writeTicks };
                    continue;
                }
                result[i] = ResolveByDecoding(save, path, writeTicks);
                decoded++;
            }
            if (decoded > 0 || deferred > 0)
            {
                CscLog.Info("indexed " + decoded + " save(s) in " + (long)SaveCatClock.MsSince(timerStart) + "ms" +
                            (deferred > 0 ? ", " + deferred + " deferred to next open (budget)" : ""));
            }
            if (_dirty)
                SaveCache(saves);
            return result;
        }
        private static CscSaveRecord ResolveByDecoding(SavesManager.GameStateData save, string path, long writeTicks)
        {
            var row = new CscSaveRecord { Path = path, AssignedWriteTicks = writeTicks };
            try
            {
                var extended = save.ReadExtendedData();
                if (extended != null)
                {
                    row.Fingerprint = CscFingerprint.FromSkipperJson(extended.SkipperJSON);
                    row.SkipperName = extended.SkipperName;
                    row.GameDateTicks = extended.GameDate.Ticks;
                }
            }
            catch (Exception ex)
            {
                CscLog.Warn("could not read header of " + System.IO.Path.GetFileName(path) + ": " + ex.Message);
            }
            if (!string.IsNullOrEmpty(row.Fingerprint))
            {
                row.CareerGuid = GuidForFingerprint(row.Fingerprint);
                ByPath[path] = row;
                _dirty = true;
            }
            return row;
        }
        private static void FillDisplayMetadata(SavesManager.GameStateData save, CscSaveRecord row)
        {
            try
            {
                var extended = save.ReadExtendedData();
                if (extended == null)
                    return;
                row.SkipperName = extended.SkipperName;
                row.GameDateTicks = extended.GameDate.Ticks;
                if (string.IsNullOrEmpty(row.Fingerprint))
                    row.Fingerprint = CscFingerprint.FromSkipperJson(extended.SkipperJSON);
                _dirty = true;
            }
            catch (Exception ex)
            {
                CscLog.Warn("could not read header of " + System.IO.Path.GetFileName(row.Path) + ": " + ex.Message);
            }
        }
        private static string GuidForFingerprint(string fingerprint)
        {
            string guid;
            if (GuidByFingerprint.TryGetValue(fingerprint, out guid))
                return guid;
            guid = System.Guid.NewGuid().ToString("N");
            GuidByFingerprint[fingerprint] = guid;
            return guid;
        }
        internal static string ResolveCareerForPath(string path)
        {
            LoadCache();
            CscSaveRecord row;
            if (ByPath.TryGetValue(path, out row) && !string.IsNullOrEmpty(row.CareerGuid))
                return row.CareerGuid;
            if (GameStateDataCtor == null || !File.Exists(path))
                return null;
            try
            {
                var save = (SavesManager.GameStateData)GameStateDataCtor.Invoke(new object[] { path });
                var writeTicks = File.GetLastWriteTime(path).Ticks;
                var resolved = ResolveByDecoding(save, path, writeTicks);
                if (_dirty)
                    SaveCache(null);
                return resolved.CareerGuid;
            }
            catch (Exception ex)
            {
                CscLog.Error("could not resolve career for " + System.IO.Path.GetFileName(path), ex);
                return null;
            }
        }
        internal static void AssignCareer(string path, string careerGuid, string skipperName, long gameDateTicks)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(careerGuid))
                return;
            LoadCache();
            CscSaveRecord row;
            if (!ByPath.TryGetValue(path, out row))
            {
                row = new CscSaveRecord { Path = path };
                ByPath[path] = row;
            }
            row.CareerGuid = careerGuid;
            row.AssignedWriteTicks = PendingWriteTicks;
            if (!string.IsNullOrEmpty(skipperName))
                row.SkipperName = skipperName;
            if (gameDateTicks > 0)
                row.GameDateTicks = gameDateTicks;
            _dirty = true;
            SaveCache(null);
        }
        internal static Dictionary<string, CscCareer> BuildCareers(CscSaveRecord[] records)
        {
            var careers = new Dictionary<string, CscCareer>(StringComparer.Ordinal);
            var earliestGameDate = new Dictionary<string, long>(StringComparer.Ordinal);
            var newestName = new Dictionary<string, string>(StringComparer.Ordinal);
            var newestNameTicks = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var r in records)
            {
                if (r == null || string.IsNullOrEmpty(r.CareerGuid))
                    continue;
                var key = r.CareerGuid;
                CscCareer career;
                if (!careers.TryGetValue(key, out career))
                {
                    career = new CscCareer
                    {
                        Guid = key,
                        LastWriteTicks = long.MinValue,
                        FirstWriteTicks = long.MaxValue
                    };
                    careers[key] = career;
                    earliestGameDate[key] = long.MaxValue;
                    newestNameTicks[key] = long.MinValue;
                }
                career.SaveCount++;
                var ticks = r.AssignedWriteTicks == PendingWriteTicks ? long.MaxValue : r.AssignedWriteTicks;
                if (ticks > career.LastWriteTicks)
                    career.LastWriteTicks = ticks;
                if (ticks != long.MaxValue && ticks < career.FirstWriteTicks)
                    career.FirstWriteTicks = ticks;
                if (r.GameDateTicks > 0 && r.GameDateTicks < earliestGameDate[key])
                    earliestGameDate[key] = r.GameDateTicks;
                if (!string.IsNullOrEmpty(r.SkipperName) && ticks > newestNameTicks[key])
                {
                    newestNameTicks[key] = ticks;
                    newestName[key] = r.SkipperName;
                }
            }
            var ordered = new List<CscCareer>(careers.Values);
            ordered.Sort((a, b) => a.FirstWriteTicks.CompareTo(b.FirstWriteTicks));
            var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var career in ordered)
            {
                string name;
                newestName.TryGetValue(career.Guid, out name);
                var label = Surname(name);
                long earliest;
                if (earliestGameDate.TryGetValue(career.Guid, out earliest) && earliest != long.MaxValue)
                {
                    var year = new DateTime(earliest).Year;
                    label += " '" + (year % 100).ToString("00", CultureInfo.InvariantCulture);
                }
                int seen;
                if (used.TryGetValue(label, out seen))
                {
                    used[label] = seen + 1;
                    label += " #" + (seen + 1).ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    used[label] = 1;
                }
                career.Label = label;
            }
            return careers;
        }
        private static string Surname(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return "Unbekannt";
            var parts = fullName.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0 ? "Unbekannt" : parts[parts.Length - 1];
        }
        private static void LoadCache()
        {
            if (_cacheLoaded)
                return;
            _cacheLoaded = true;
            try
            {
                if (!File.Exists(CachePath))
                    return;
                foreach (var line in File.ReadAllLines(CachePath))
                {
                    if (string.IsNullOrEmpty(line) || line[0] == '#')
                        continue;
                    var f = line.Split(new[] { '\t' });
                    if (f.Length < 6)
                        continue;
                    long assigned, gameDateTicks;
                    if (!long.TryParse(f[1], out assigned))
                        continue;
                    long.TryParse(f[5], out gameDateTicks);
                    var row = new CscSaveRecord
                    {
                        Path = f[0],
                        AssignedWriteTicks = assigned,
                        CareerGuid = f[2].Length == 0 ? null : f[2],
                        Fingerprint = f[3].Length == 0 ? null : f[3],
                        SkipperName = f[4],
                        GameDateTicks = gameDateTicks
                    };
                    ByPath[row.Path] = row;
                    if (!string.IsNullOrEmpty(row.Fingerprint) && !string.IsNullOrEmpty(row.CareerGuid) &&
                        !GuidByFingerprint.ContainsKey(row.Fingerprint))
                    {
                        GuidByFingerprint[row.Fingerprint] = row.CareerGuid;
                    }
                }
                CscLog.Info("loaded " + ByPath.Count + " save assignment(s), " +
                            GuidByFingerprint.Count + " known career fingerprint(s)");
            }
            catch (Exception ex)
            {
                CscLog.Warn("could not read index, rebuilding: " + ex.Message);
                ByPath.Clear();
                GuidByFingerprint.Clear();
            }
        }
        private static void SaveCache(SavesManager.GameStateData[] saves)
        {
            try
            {
                HashSet<string> live = null;
                if (saves != null)
                {
                    live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var s in saves)
                    {
                        if (s != null)
                            live.Add(s.Path);
                    }
                }
                var sb = new StringBuilder();
                sb.Append(Header).Append('\n');
                var kept = 0;
                foreach (var pair in ByPath)
                {
                    var r = pair.Value;
                    if (live != null && !live.Contains(pair.Key) && r.AssignedWriteTicks != PendingWriteTicks)
                        continue;
                    sb.Append(r.Path).Append('\t')
                      .Append(r.AssignedWriteTicks.ToString(CultureInfo.InvariantCulture)).Append('\t')
                      .Append(r.CareerGuid ?? string.Empty).Append('\t')
                      .Append(Clean(r.Fingerprint)).Append('\t')
                      .Append(Clean(r.SkipperName)).Append('\t')
                      .Append(r.GameDateTicks.ToString(CultureInfo.InvariantCulture)).Append('\n');
                    kept++;
                }
                File.WriteAllText(CachePath, sb.ToString());
                _dirty = false;
                CscLog.Info("wrote index (" + kept + " assignment(s))");
            }
            catch (Exception ex)
            {
                CscLog.Warn("could not write index: " + ex.Message);
            }
        }
        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;
            return s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }
    }


    internal static class CscPaths
    {
        internal static string SavesDirectory
        {
            get { return Application.persistentDataPath + "/Saves"; }
        }
        internal static string FromSaveName(string name, bool allowFullPath)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            if (allowFullPath && name.IndexOf('/') >= 0)
                return name;
            return SavesDirectory + "/" + name + ".save";
        }
    }
    [HarmonyPatch(typeof(PlayerCareer), "StartDate", MethodType.Setter)]
    internal static class PlayerCareerStartDatePatch
    {
        private static void Postfix()
        {
            try
            {
                CscSession.BeginNewCareer();
            }
            catch (Exception ex)
            {
                CscLog.Error("failed to start tracking a new career", ex);
            }
        }
    }
    [HarmonyPatch(typeof(SavesManager), "Load")]
    internal static class SavesManagerLoadPatch
    {
        private static void Postfix(string name)
        {
            try
            {
                var path = CscPaths.FromSaveName(name, true);
                if (path == null)
                    return;
                CscSession.Adopt(CscIndex.ResolveCareerForPath(path));
            }
            catch (Exception ex)
            {
                CscLog.Error("failed to adopt career on load", ex);
            }
        }
    }
    [HarmonyPatch(typeof(SavesManager), "Save")]
    internal static class SavesManagerSavePatch
    {
        private static void Postfix(string name)
        {
            try
            {
                var path = CscPaths.FromSaveName(name, false);
                if (path == null)
                    return;
                CscIndex.AssignCareer(path, CscSession.CareerForSave(), null, 0L);
            }
            catch (Exception ex)
            {
                CscLog.Error("failed to stamp save with its career", ex);
            }
        }
    }
    [HarmonyPatch(typeof(SavesManager), "GetSaves")]
    internal static class SavesManagerGetSavesPatch
    {
        private static void Postfix(ref SavesManager.GameStateData[] __result)
        {
            try
            {
                if (!SaveCatCfg.Active)
                {
                    CscRowInfo.Clear();
                    return;
                }
                if (__result == null || __result.Length < 2)
                    return;
                var records = CscIndex.Refresh(__result);
                var careers = CscIndex.BuildCareers(records);
                if (careers.Count < 2)
                {
                    CscRowInfo.Clear();
                    return;
                }
                CscRowInfo.Rebuild(records, careers);
                var items = new List<Entry>(__result.Length);
                for (var i = 0; i < __result.Length; i++)
                {
                    var record = records[i];
                    var guid = record == null ? null : record.CareerGuid;
                    CscCareer career = null;
                    if (!string.IsNullOrEmpty(guid))
                        careers.TryGetValue(guid, out career);
                    items.Add(new Entry
                    {
                        Save = __result[i],
                        CareerGuid = guid ?? string.Empty,
                        CareerRank = career == null ? long.MinValue : career.LastWriteTicks,
                        WriteTicks = record == null ? 0L : record.AssignedWriteTicks,
                        Original = i
                    });
                }
                items.Sort(Compare);
                if (!ReferenceEquals(items[0].Save, __result[0]))
                {
                    CscLog.Warn("grouping would have moved the most recent save out of first " +
                                "position, which would change what auto-load picks - leaving vanilla order");
                    return;
                }
                for (var i = 0; i < items.Count; i++)
                    __result[i] = items[i].Save;
            }
            catch (Exception ex)
            {
                CscLog.Error("failed to group saves, leaving vanilla order", ex);
            }
        }
        private static int Compare(Entry a, Entry b)
        {
            var byCareer = b.CareerRank.CompareTo(a.CareerRank);
            if (byCareer != 0)
                return byCareer;
            var byGuid = string.CompareOrdinal(a.CareerGuid, b.CareerGuid);
            if (byGuid != 0)
                return byGuid;
            var byDate = b.WriteTicks.CompareTo(a.WriteTicks);
            if (byDate != 0)
                return byDate;
            return a.Original.CompareTo(b.Original);
        }
        private sealed class Entry
        {
            internal SavesManager.GameStateData Save;
            internal string CareerGuid;
            internal long CareerRank;
            internal long WriteTicks;
            internal int Original;
        }
    }
    internal static class CscRowInfo
    {
        private static Dictionary<string, string> _labelByPath =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, string> _careerByPath =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static List<CscCareer> _ordered = new List<CscCareer>();
        internal static List<CscCareer> Ordered
        {
            get { return _ordered; }
        }
        internal static void Rebuild(CscSaveRecord[] records, Dictionary<string, CscCareer> careers)
        {
            var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var byPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in records)
            {
                if (r == null || string.IsNullOrEmpty(r.CareerGuid) || string.IsNullOrEmpty(r.Path))
                    continue;
                CscCareer career;
                if (careers.TryGetValue(r.CareerGuid, out career) && career != null)
                {
                    labels[r.Path] = career.Label;
                    byPath[r.Path] = career.Guid;
                }
            }
            var ordered = new List<CscCareer>(careers.Values);
            ordered.Sort((a, b) => b.LastWriteTicks.CompareTo(a.LastWriteTicks));
            _labelByPath = labels;
            _careerByPath = byPath;
            _ordered = ordered;
        }
        internal static void Clear()
        {
            _labelByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _careerByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _ordered = new List<CscCareer>();
        }
        internal static string For(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            string label;
            return _labelByPath.TryGetValue(path, out label) ? label : null;
        }
        internal static string CareerFor(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            string guid;
            return _careerByPath.TryGetValue(path, out guid) ? guid : null;
        }
        internal static string CareerAt(int index)
        {
            return index >= 0 && index < _ordered.Count ? _ordered[index].Guid : null;
        }
    }
    [HarmonyPatch(typeof(GameStateBaseUI), "FetchSaves")]
    internal static class GameStateBaseUiFetchSavesPatch
    {
        private static readonly FieldInfo SavesListField =
            AccessTools.Field(typeof(GameStateBaseUI), "savesList");
        private static readonly FieldInfo ItemsField =
            AccessTools.Field(typeof(GameStateBaseUI), "items");
        private static void Postfix(GameStateBaseUI __instance)
        {
            try
            {
                if (__instance == null || SavesListField == null || ItemsField == null)
                    return;
                var savesList = SavesListField.GetValue(__instance) as RectTransform;
                var items = ItemsField.GetValue(__instance) as GameStateItemUI[];
                if (savesList == null || items == null)
                    return;
                if (!SaveCatCfg.Active || CscRowInfo.Ordered.Count < 2)
                {
                    CscTabStrip.StandDown(savesList, items);
                    return;
                }
                CscTabStrip.Refresh(__instance, savesList, items, CscRowInfo.Ordered);
            }
            catch (Exception ex)
            {
                CscLog.Error("failed to build career tabs", ex);
            }
        }
    }
    [HarmonyPatch(typeof(GameStateItemUI), "Duplicate")]
    internal static class GameStateItemUiDuplicatePatch
    {
        private const string LabelColor = "#C8A45C";
        private static readonly FieldInfo SaveNameField =
            AccessTools.Field(typeof(GameStateItemUI), "saveName");
        private static void Postfix(GameStateItemUI __result, SavesManager.GameStateData saveData)
        {
            try
            {
                if (!SaveCatCfg.Active)
                    return;
                if (__result == null || saveData == null || SaveNameField == null)
                    return;
                var label = CscRowInfo.For(saveData.Path);
                if (string.IsNullOrEmpty(label))
                    return;
                var text = SaveNameField.GetValue(__result) as TMP_Text;
                if (text == null)
                    return;
                text.richText = true;
                text.text = "<color=" + LabelColor + ">" + label + "</color>  " + text.text;
            }
            catch (Exception ex)
            {
                CscLog.Error("failed to label save row", ex);
            }
        }
    }


    internal static class CscTabStrip
    {
        private const string StripName = "ToolkitCareerTabs";
        private const string UnderlineName = "ToolkitTabUnderline";
        private const float StripHeight = 52f;
        private const float Gap = 10f;
        private const float TabPadding = 34f;
        private const float TabSpacing = 6f;
        private const float MinTabWidth = 70f;
        private static readonly Color UnderlineSelected = new Color(0.78f, 0.64f, 0.36f, 1f);
        private static string _filterGuid;
        private static string _defaultedFor;
        internal static void Refresh(GameStateBaseUI ui, RectTransform savesList,
                                     GameStateItemUI[] items, List<CscCareer> careers)
        {
            var strip = FindOrCreateStrip(ui, savesList);
            if (strip == null)
                return;
            if (!strip.gameObject.activeSelf)
                strip.gameObject.SetActive(true);
            var current = CscSession.CareerGuid;
            if (current != _defaultedFor)
            {
                _defaultedFor = current;
                _filterGuid = current; // null in the main menu, which is "All"
            }
            if (_filterGuid != null)
            {
                var stillThere = false;
                foreach (var c in careers)
                {
                    if (c.Guid == _filterGuid)
                    {
                        stillThere = true;
                        break;
                    }
                }
                if (!stillThere)
                    _filterGuid = null;
            }
            BuildTabs(ui, strip, items, careers);
            ApplyFilter(ui, items);
        }
        internal static void StandDown(RectTransform savesList, GameStateItemUI[] items)
        {
            _filterGuid = null;
            try
            {
                var scrollRect = savesList == null ? null : savesList.GetComponentInParent<ScrollRect>();
                var scrollView = scrollRect == null ? null : scrollRect.transform as RectTransform;
                var host = scrollView == null ? null : scrollView.parent as RectTransform;
                var existing = host == null ? null : host.Find(StripName);
                if (existing != null && existing.gameObject.activeSelf)
                    existing.gameObject.SetActive(false);
            }
            catch (Exception ex)
            {
                CscLog.Error("failed to hide the career tabs", ex);
            }
            if (items == null)
                return;
            foreach (var item in items)
                if (item != null && !item.gameObject.activeSelf)
                    item.gameObject.SetActive(true);
        }
        private static RectTransform FindOrCreateStrip(GameStateBaseUI ui, RectTransform savesList)
        {
            var scrollRect = savesList.GetComponentInParent<ScrollRect>();
            if (scrollRect == null)
            {
                CscLog.Warn("no ScrollRect above the save list - cannot place the career tabs");
                return null;
            }
            var scrollView = scrollRect.transform as RectTransform;
            var host = scrollView == null ? null : scrollView.parent as RectTransform;
            if (host == null)
                return null;
            var existing = host.Find(StripName) as RectTransform;
            if (existing != null)
                return existing;
            scrollView.offsetMax = new Vector2(scrollView.offsetMax.x, scrollView.offsetMax.y - (StripHeight + Gap));
            var strip = new GameObject(StripName, typeof(RectTransform)).GetComponent<RectTransform>();
            strip.SetParent(host, false);
            strip.anchorMin = new Vector2(scrollView.anchorMin.x, scrollView.anchorMax.y);
            strip.anchorMax = new Vector2(scrollView.anchorMax.x, scrollView.anchorMax.y);
            strip.pivot = new Vector2(0.5f, 0f);
            strip.offsetMin = new Vector2(scrollView.offsetMin.x, scrollView.offsetMax.y + Gap);
            strip.offsetMax = new Vector2(scrollView.offsetMax.x, scrollView.offsetMax.y + Gap + StripHeight);
            var viewport = new GameObject("Viewport", typeof(RectTransform)).GetComponent<RectTransform>();
            viewport.SetParent(strip, false);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0f, 0f);
            content.anchorMax = new Vector2(0f, 1f);
            content.pivot = new Vector2(0f, 0.5f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            var layout = content.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = TabSpacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            var scroll = strip.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 25f;
            scroll.horizontalScrollbar = null;
            scroll.verticalScrollbar = null;
            CscLog.Info("career tab strip created");
            return strip;
        }
        private static void BuildTabs(GameStateBaseUI ui, RectTransform strip,
                                      GameStateItemUI[] items, List<CscCareer> careers)
        {
            var content = strip.Find("Viewport/Content") as RectTransform;
            if (content == null)
                return;
            for (var i = content.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(content.GetChild(i).gameObject);
            _tabs.Clear();
            var donor = FindDonorButton(ui);
            if (donor == null)
            {
                CscLog.Warn("no button to style the career tabs from");
                return;
            }
            var titles = TabTitles(careers);
            AddTab(content, donor, "Alle", null, ui, items);
            for (var i = 0; i < careers.Count; i++)
                AddTab(content, donor, titles[i], careers[i].Guid, ui, items);
            ClearUiSelection();
        }
        private static List<string> TabTitles(List<CscCareer> careers)
        {
            var titles = new List<string>(careers.Count);
            foreach (var career in careers)
                titles.Add(TabTitle(career.Label));
            var byAge = new List<int>(careers.Count);
            for (var i = 0; i < careers.Count; i++)
                byAge.Add(i);
            byAge.Sort((a, b) => careers[a].FirstWriteTicks.CompareTo(careers[b].FirstWriteTicks));
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var i in byAge)
            {
                var title = titles[i];
                int count;
                if (seen.TryGetValue(title, out count))
                {
                    seen[title] = count + 1;
                    titles[i] = title + " #" + (count + 1);
                }
                else
                {
                    seen[title] = 1;
                }
            }
            return titles;
        }
        private static void ClearUiSelection()
        {
            var events = EventSystem.current;
            if (events != null && events.currentSelectedGameObject != null)
                events.SetSelectedGameObject(null);
        }
        private static string TabTitle(string label)
        {
            if (string.IsNullOrEmpty(label))
                return "Unbekannt";
            var tick = label.IndexOf(" '", StringComparison.Ordinal);
            return tick > 0 ? label.Substring(0, tick) : label;
        }
        private static void AddTab(RectTransform content, Button donor, string title,
                                   string careerGuid, GameStateBaseUI ui, GameStateItemUI[] items)
        {
            var go = new GameObject("ToolkitTab_" + title, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(content, false);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            var image = go.AddComponent<Image>();
            var donorImage = donor.GetComponent<Image>();
            if (donorImage != null)
            {
                image.sprite = donorImage.sprite;
                image.type = donorImage.type;
                image.color = donorImage.color;
                image.material = donorImage.material;
                image.preserveAspect = false;
            }
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = donor.transition;
            button.colors = donor.colors;
            button.spriteState = donor.spriteState;
            var guid = careerGuid;
            button.onClick.AddListener(delegate { OnTabClicked(ui, items, guid); });
            var labelGo = new GameObject("Label", typeof(RectTransform));
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.SetParent(rt, false);
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(0f, 0f);
            lrt.offsetMax = new Vector2(0f, -3f);
            var label = labelGo.AddComponent<TextMeshProUGUI>();
            var donorLabel = donor.GetComponentInChildren<TMP_Text>(true);
            if (donorLabel != null)
            {
                label.font = donorLabel.font;
                label.fontSharedMaterial = donorLabel.fontSharedMaterial;
                label.fontSize = donorLabel.fontSize;
                label.fontStyle = donorLabel.fontStyle;
                label.color = donorLabel.color;
            }
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false;
            label.raycastTarget = false;
            label.text = title;
            var width = Mathf.Max(MinTabWidth, label.preferredWidth + TabPadding);
            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.flexibleWidth = 0f;
            _tabs.Add(new Tab
            {
                Underline = AddUnderline(go.transform, careerGuid == _filterGuid),
                CareerGuid = careerGuid
            });
        }
        private static GameObject AddUnderline(Transform tab, bool selected)
        {
            var underline = new GameObject(UnderlineName, typeof(RectTransform)).GetComponent<RectTransform>();
            underline.SetParent(tab, false);
            underline.anchorMin = new Vector2(0f, 0f);
            underline.anchorMax = new Vector2(1f, 0f);
            underline.pivot = new Vector2(0.5f, 0f);
            underline.offsetMin = new Vector2(6f, 0f);
            underline.offsetMax = new Vector2(-6f, 3f);
            var image = underline.gameObject.AddComponent<Image>();
            image.color = UnderlineSelected;
            image.raycastTarget = false;
            underline.gameObject.SetActive(selected);
            return underline.gameObject;
        }
        private static void OnTabClicked(GameStateBaseUI ui, GameStateItemUI[] items, string careerGuid)
        {
            try
            {
                _filterGuid = careerGuid;
                Deselect(ui);
                ApplyFilter(ui, items);
                RefreshUnderlines();
                ClearUiSelection();
            }
            catch (Exception ex)
            {
                CscLog.Error("failed to switch career tab", ex);
            }
        }
        private static void ApplyFilter(GameStateBaseUI ui, GameStateItemUI[] items)
        {
            if (items == null)
                return;
            foreach (var item in items)
            {
                if (item == null)
                    continue;
                var visible = _filterGuid == null;
                if (!visible)
                {
                    var data = item.SaveData;
                    visible = data != null && CscRowInfo.CareerFor(data.Path) == _filterGuid;
                }
                if (item.gameObject.activeSelf != visible)
                    item.gameObject.SetActive(visible);
            }
        }
        private static void RefreshUnderlines()
        {
            foreach (var tab in _tabs)
            {
                if (tab.Underline != null)
                    tab.Underline.SetActive(tab.CareerGuid == _filterGuid);
            }
        }
        private static readonly List<Tab> _tabs = new List<Tab>();
        private struct Tab
        {
            internal GameObject Underline;
            internal string CareerGuid;
        }
        private static void Deselect(GameStateBaseUI ui)
        {
            var method = AccessTools.Method(ui.GetType(), "SelectSave", new[] { typeof(GameStateItemUI) })
                         ?? AccessTools.Method(typeof(GameStateBaseUI), "SelectSave", new[] { typeof(GameStateItemUI) });
            if (method != null)
                method.Invoke(ui, new object[] { null });
        }
        private static Button FindDonorButton(GameStateBaseUI ui)
        {
            foreach (var name in new[] { "loadButton", "deleteButton", "closeButton" })
            {
                var field = AccessTools.Field(ui.GetType(), name) ?? AccessTools.Field(typeof(GameStateBaseUI), name);
                if (field == null)
                    continue;
                var button = field.GetValue(ui) as Button;
                if (button != null)
                    return button;
            }
            return null;
        }
    }


    [HarmonyPatch(typeof(GameStateBaseUI), "FetchSaves")]
    internal static class CscUiProbe
    {
        internal static readonly bool Enabled = false;
        private static bool _dumped;
        private static readonly FieldInfo SavesListField =
            AccessTools.Field(typeof(GameStateBaseUI), "savesList");
        private static void Postfix(GameStateBaseUI __instance)
        {
            if (!Enabled || _dumped || __instance == null || SavesListField == null)
                return;
            _dumped = true;
            try
            {
                var savesList = SavesListField.GetValue(__instance) as RectTransform;
                if (savesList == null)
                {
                    CscLog.Warn("UI probe: savesList was null");
                    return;
                }
                var sb = new StringBuilder();
                sb.Append("\n===== load menu UI hierarchy =====\n");
                sb.Append("opened by: ").Append(__instance.GetType().Name).Append('\n');
                sb.Append("\n--- ancestors (savesList first) ---\n");
                var depth = 0;
                for (var t = savesList.transform; t != null && depth < 8; t = t.parent, depth++)
                {
                    sb.Append(Indent(depth)).Append(Describe(t)).Append('\n');
                }
                var parent = savesList.parent;
                if (parent != null)
                {
                    sb.Append("\n--- siblings of savesList (in '").Append(parent.name).Append("') ---\n");
                    for (var i = 0; i < parent.childCount; i++)
                    {
                        var child = parent.GetChild(i);
                        sb.Append("  [").Append(i).Append("] ")
                          .Append(ReferenceEquals(child, savesList.transform) ? "<< savesList >> " : "")
                          .Append(Describe(child)).Append('\n');
                    }
                    var grand = parent.parent;
                    if (grand != null)
                    {
                        sb.Append("\n--- siblings of '").Append(parent.name)
                          .Append("' (in '").Append(grand.name).Append("') ---\n");
                        for (var i = 0; i < grand.childCount; i++)
                        {
                            var child = grand.GetChild(i);
                            sb.Append("  [").Append(i).Append("] ")
                              .Append(ReferenceEquals(child, parent) ? "<< parent >> " : "")
                              .Append(Describe(child)).Append('\n');
                        }
                    }
                }
                if (savesList.childCount > 0)
                {
                    sb.Append("\n--- first save row ---\n");
                    DumpTree(sb, savesList.GetChild(0), 0, 3);
                }
                sb.Append("\n--- an existing button, as a styling donor for new controls ---\n");
                foreach (var name in new[] { "loadButton", "deleteButton", "closeButton" })
                {
                    var f = AccessTools.Field(__instance.GetType(), name) ??
                            AccessTools.Field(typeof(GameStateBaseUI), name);
                    if (f == null)
                        continue;
                    var button = f.GetValue(__instance) as Component;
                    if (button == null)
                        continue;
                    sb.Append("  ").Append(name).Append(": ").Append(Path(button.transform)).Append('\n');
                    DumpTree(sb, button.transform, 2, 2);
                }
                sb.Append("===== end =====");
                CscLog.Info(sb.ToString());
            }
            catch (Exception ex)
            {
                CscLog.Error("UI probe failed", ex);
            }
        }
        private static void DumpTree(StringBuilder sb, Transform t, int depth, int maxDepth)
        {
            sb.Append(Indent(depth)).Append(Describe(t)).Append('\n');
            if (depth >= maxDepth)
                return;
            for (var i = 0; i < t.childCount; i++)
                DumpTree(sb, t.GetChild(i), depth + 1, maxDepth);
        }
        private static string Describe(Transform t)
        {
            var sb = new StringBuilder();
            sb.Append('\'').Append(t.name).Append('\'');
            var rect = t as RectTransform;
            if (rect != null)
            {
                var r = rect.rect;
                sb.Append("  size=(").Append(Mathf.RoundToInt(r.width)).Append('x')
                  .Append(Mathf.RoundToInt(r.height)).Append(')');
            }
            sb.Append("  [");
            var components = t.GetComponents<Component>();
            for (var i = 0; i < components.Length; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append(components[i] == null ? "<missing>" : components[i].GetType().Name);
            }
            sb.Append(']');
            if (!t.gameObject.activeSelf)
                sb.Append("  (inactive)");
            return sb.ToString();
        }
        private static string Path(Transform t)
        {
            var sb = new StringBuilder(t.name);
            for (var p = t.parent; p != null; p = p.parent)
                sb.Insert(0, p.name + "/");
            return sb.ToString();
        }
        private static string Indent(int depth)
        {
            return new string(' ', 2 + depth * 2);
        }
    }

}
