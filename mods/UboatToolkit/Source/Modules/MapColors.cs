using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UBOAT.Game;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Scene;
using UBOAT.Game.Scene.Camera;
using UBOAT.Game.Scene.Effects;
using UBOAT.Game.Scene.Environment;
using UBOAT.Game.Scene.UI.MainMenu;

namespace UboatToolkit.Modules
{
    // Readable map: grid square labels (AN15 ...) readable on every map skin, two extra skins
    // ("Elfenbein" and "Eigene Farben" with colours set in F10 > Karte), detailed per-skin values in
    // toolkit-map.txt (re-read while the game runs), plus a pixel measurement of the finished frame
    // and a skin report for the log.
    internal static class MapCfg
    {
        internal const string IvoryName = "Elfenbein";
        internal const string CustomName = "Eigene Farben";
        private static readonly Dictionary<string, string> v = new Dictionary<string, string>(StringComparer.Ordinal);
        private static DateTime stamp;
        private static float nextPoll = -1f;
        internal static int Generation;
        internal static string File => Path.Combine(Settings.Folder, "toolkit-map.txt");

        internal const string Defaults =
            "# Karten-Lesbarkeit (UBOAT Toolkit). Zeile: Schluessel: Wert. Farben als #RRGGBB oder #RRGGBBAA.\n" +
            "# Pro Karten-Stil geht auch: GridLabelColor.<Stil>, PlaceNameColor.<Stil>, GridLineColor.<Stil>,\n" +
            "#   GridLabelFont.<Stil>, GridLabelFontMaterial.<Stil>, GridLabelFaceColor.<Stil>, GridLabelFaceDilate.<Stil>,\n" +
            "#   GridLabelOutline.<Stil>, GridLabelOutlineColor.<Stil>, GridLabelOutlineWidth.<Stil>\n" +
            "# Stile im Spiel: Default, Semi-Realistic, Desaturated, Paper-Like (+ Elfenbein, Eigene Farben)\n" +
            "GridLabelFont: FrankRuhlLibre-Black SDF\n" +
            "GridLabelFontMaterial: FrankRuhlLibre-Black SDF Map Geographic Name\n" +
            "GridLabelFaceOverride: true\n" +
            "GridLabelFaceColor: #FFFFFFFF\n" +
            "GridLabelFaceDilate: 0.02\n" +
            "GridLabelFaceDilateByDepth: 1=0.02, 2=0.02, 3=0.05, 4=0.09, 5=0.13\n" +
            "GridLabelOutline: false\n" +
            "GridLabelOutlineColor: #EDE7D8C0\n" +
            "GridLabelOutlineWidth: 0.15\n" +
            "GridLabelOutlineWidthByDepth: 1=0.12, 2=0.12, 3=0.15, 4=0.18, 5=0.20\n" +
            "GridLabelAlphaFloor: 255\n" +
            "GridLabelNormalizeSize: false\n" +
            "GridLabelBaseFontSize: 32\n" +
            "IvoryBaseSkin: Paper-Like\n" +
            "IvoryLandColors: _BaseColor=#DFD7C4, _SceneColor=#DFD7C4\n" +
            "IvoryLandFloats: _GlobalColorIntensity=0.20\n" +
            "IvorySeaColors: _BaseColor=#EDE8DB, _Color=#B9B2A0\n" +
            "IvorySeaFloats: _GlobalColorIntensity=0.16\n" +
            "PixelSampleSeconds: 6\n" +
            "Custom.Sea: #CECEC9\n" +
            "Custom.Land: #BFB499\n" +
            "Custom.Grid: #1A1A1A\n" +
            "Custom.Place: #4D4D4D\n" +
            "Custom.Neutral: #808080\n" +
            "Custom.Allies: #2E394D\n" +
            "Custom.Axis: #4D2A26\n";

        internal static void Poll(bool force = false)
        {
            if (!force && Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + 1f;
            try
            {
                Directory.CreateDirectory(Settings.Folder);
                if (!System.IO.File.Exists(File)) System.IO.File.WriteAllText(File, Defaults);
                var t = System.IO.File.GetLastWriteTimeUtc(File);
                if (!force && t == stamp && v.Count > 0) return;
                stamp = t;
                v.Clear();
                Parse(Defaults);
                Parse(System.IO.File.ReadAllText(File));
                Generation++;
            }
            catch (Exception e) { Util.Log("Karte: toolkit-map.txt nicht lesbar: " + e.Message); if (v.Count == 0) Parse(Defaults); }
        }
        private static void Parse(string text)
        {
            foreach (var raw in text.Split('\n'))
            {
                var l = raw.Trim();
                if (l.Length == 0 || l.StartsWith("#")) continue;
                int i = l.IndexOf(':');
                if (i <= 0) continue;
                v[l.Substring(0, i).Trim()] = l.Substring(i + 1).Trim();
            }
        }
        internal static void Write(string key, string value)
        {
            Poll(true);
            try
            {
                var lines = new List<string>(System.IO.File.ReadAllLines(File));
                bool done = false;
                for (int i = 0; i < lines.Count; i++)
                    if (lines[i].TrimStart().StartsWith(key + ":")) { lines[i] = key + ": " + value; done = true; }
                if (!done) lines.Add(key + ": " + value);
                System.IO.File.WriteAllLines(File, lines.ToArray());
            }
            catch { }
            v[key] = value;
            Generation++;
        }

        internal static string Str(string key) { Poll(); return v.TryGetValue(key, out var s) && s.Length > 0 ? s : null; }
        internal static string PerSkin(string key, string skin) => (skin != null ? Str(key + "." + skin) : null) ?? Str(key);
        internal static bool Bool(string key, bool def) { var s = Str(key); return s != null && bool.TryParse(s, out var b) ? b : def; }
        internal static float Float(string key, float def) { var s = Str(key); return s != null && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : def; }
        internal static Color32? Color(string key) { var s = Str(key); return s != null && TryColor(s, out var c) ? c : (Color32?)null; }
        internal static Color32? SkinColor(string prefix, string skin) => string.IsNullOrEmpty(skin) ? null : Color(prefix + skin);

        internal static Dictionary<string, float> ByDepth(string key)
        {
            var d = new Dictionary<string, float>();
            var s = Str(key);
            if (s == null) return d;
            foreach (var part in s.Split(','))
            {
                var kv = part.Split('=');
                if (kv.Length == 2 && float.TryParse(kv[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) d[kv[0].Trim()] = f;
            }
            return d;
        }
        internal static Dictionary<string, Color32> ColorList(string key)
        {
            var d = new Dictionary<string, Color32>();
            var s = Str(key);
            if (s == null) return d;
            foreach (var part in s.Split(','))
            {
                var kv = part.Split('=');
                if (kv.Length == 2 && TryColor(kv[1], out var c)) d[kv[0].Trim()] = c;
            }
            return d;
        }
        internal static Dictionary<string, float> FloatList(string key)
        {
            var d = new Dictionary<string, float>();
            var s = Str(key);
            if (s == null) return d;
            foreach (var part in s.Split(','))
            {
                var kv = part.Split('=');
                if (kv.Length == 2 && float.TryParse(kv[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) d[kv[0].Trim()] = f;
            }
            return d;
        }

        internal static bool TryColor(string s, out Color32 c)
        {
            c = new Color32(0, 0, 0, 255);
            s = s.Trim().TrimStart('#');
            if (s.Length != 6 && s.Length != 8) return false;
            try
            {
                byte P(int i) => byte.Parse(s.Substring(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                c = new Color32(P(0), P(2), P(4), s.Length == 8 ? P(6) : (byte)255);
                return true;
            }
            catch { return false; }
        }
        internal static string Hex(Color32 c) => "#" + c.r.ToString("X2") + c.g.ToString("X2") + c.b.ToString("X2") + (c.a != 255 ? c.a.ToString("X2") : "");

        // Skins whose grid text borrows the paper map's ink (vanilla light grey text is unreadable there).
        internal static bool UsesPaperInk(string skin) => skin == "Default" || skin == "Semi-Realistic" || skin == "Desaturated";
        internal static bool GridLikeSeaNames(string skin) => UsesPaperInk(skin) || skin == "Paper-Like";
        internal const string InkSource = "Paper-Like";
    }

    internal static class MapSkins
    {
        internal static MapRenderer Map;
        internal static NavalGridRenderer Grid;
        private static readonly Dictionary<string, Color32> vanillaGrid = new Dictionary<string, Color32>();
        private static readonly Dictionary<string, Color32[]> vanillaPlace = new Dictionary<string, Color32[]>();
        private static Material ownLand, ownSea;
        private static string ownFor;
        private static readonly Dictionary<int, Material> ownedLabelMats = new Dictionary<int, Material>();
        private static Material ownedBase; private static string ownedSkin; private static int ownedGen = -1;
        private static Material prefabMat; private static TMP_FontAsset prefabFont;
        private static bool geoCaptured, everNormalized; private static Vector3 prefabScale = Vector3.one; private static Vector2 prefabSize;
        private static readonly Dictionary<MapLabel, int> placeClass = new Dictionary<MapLabel, int>();
        internal static bool StyledOnce;

        internal static bool On => Settings.B("MapReadable");
        internal static bool IvoryOn => On && Settings.B("MapIvory");
        internal static string Current => Map?.CurrentMapSkin?.name;
        internal static bool IsCustom(string s) => s == MapCfg.CustomName;
        internal static bool OwnSkinNow => (IvoryOn && Current == MapCfg.IvoryName) || (On && IsCustom(Current));

        internal static List<MapSkin> Skins(MapRenderer m) => m == null ? null : Traverse.Create(m).Field("mapSkins").GetValue<List<MapSkin>>();
        private static T F<T>(object o, string f) => o == null ? default(T) : Traverse.Create(o).Field(f).GetValue<T>();

        internal static Color Custom(string what)
        {
            MapCfg.TryColor(MapCfg.Str("Custom." + what) ?? "#808080", out var c);
            return c;
        }

        // ---------- label colours ----------
        internal static void ApplyLabelColors(List<MapSkin> skins)
        {
            if (skins == null) return;
            foreach (var s in skins) if (s != null && !string.IsNullOrEmpty(s.name) && !vanillaGrid.ContainsKey(s.name)) vanillaGrid[s.name] = s.gridLabelColor;
            foreach (var s in skins) if (s != null && !string.IsNullOrEmpty(s.name)) s.gridLabelColor = LabelColor(s.name);
        }
        internal static Color32 LabelColor(string skin)
        {
            if (!vanillaGrid.TryGetValue(skin, out var vanilla)) vanilla = new Color32(33, 33, 33, 192);
            if (!On) return vanilla;
            if (IsCustom(skin)) { Color32 g = Custom("Grid"); g.a = 255; return g; }
            var exp = MapCfg.SkinColor("GridLabelColor.", skin);
            if (exp.HasValue)
            {
                var c = exp.Value;   // old "white/near-black" defaults on borrowed-ink skins mean "not set"
                bool stale = MapCfg.UsesPaperInk(skin) && c.a == 255 && ((c.r == 0x1A && c.g == 0x1A && c.b == 0x1A) || (c.r == 0xF5 && c.g == 0xF2 && c.b == 0xEA) || (c.r == 255 && c.g == 255 && c.b == 255) || (c.r == 0xF2 && c.g == 0xF2 && c.b == 0xF2));
                if (!stale) return c;
            }
            if (MapCfg.GridLikeSeaNames(skin))
            {
                var o = MapCfg.Color("BorrowedInkColor");
                if (o.HasValue) return o.Value;
                if (SeaInk(skin, out var ink)) return ink;
            }
            if (vanilla.a == 0) return vanilla;
            byte floor = (byte)Mathf.Clamp(MapCfg.Float("GridLabelAlphaFloor", 255), 0, 255);
            if (vanilla.a < floor) vanilla.a = floor;
            return vanilla;
        }
        private static bool SeaInk(string skin, out Color32 ink)
        {
            ink = default(Color32);
            var exp = On ? MapCfg.SkinColor("PlaceNameColor.", skin) : null;
            if (exp.HasValue) { ink = exp.Value; return true; }
            string src = MapCfg.UsesPaperInk(skin) ? MapCfg.InkSource : skin;
            if (!vanillaPlace.TryGetValue(src, out var b) && !vanillaPlace.TryGetValue(skin, out b)) return false;
            if (b == null || b.Length == 0) return false;
            ink = b[0];
            if (ink.a < 0x80) ink.a = 0xE6;
            return true;
        }
        internal static void ApplyPlaceColors(List<MapSkin> skins)
        {
            if (skins == null) return;
            foreach (var s in skins)
                if (s != null && !string.IsNullOrEmpty(s.name) && !IsCustom(s.name) && !vanillaPlace.ContainsKey(s.name))
                    vanillaPlace[s.name] = new[] { s.customLabelColor1, s.customLabelColor2, s.customLabelColor3 };
            vanillaPlace.TryGetValue(MapCfg.InkSource, out var ink);
            foreach (var s in skins)
            {
                if (s == null || string.IsNullOrEmpty(s.name) || IsCustom(s.name) || !vanillaPlace.ContainsKey(s.name)) continue;
                var van = vanillaPlace[s.name];
                var basis = On && MapCfg.UsesPaperInk(s.name) && ink != null ? ink : van;
                var c = On ? MapCfg.SkinColor("PlaceNameColor.", s.name) : null;
                s.customLabelColor1 = c ?? basis[0];
                s.customLabelColor2 = c ?? basis[1];
                s.customLabelColor3 = c ?? basis[2];
            }
        }
        internal static bool ChangesPlaceNames(string skin) =>
            skin != null && vanillaPlace.ContainsKey(skin) && On && (MapCfg.SkinColor("PlaceNameColor.", skin).HasValue || MapCfg.UsesPaperInk(skin));

        internal static void RecolorGridLabels()
        {
            var sq = F<Dictionary<string, NavalGridRendererSquare>>(Grid, "squares");
            string skin = Current;
            if (sq == null || skin == null) return;
            Color c = LabelColor(skin);
            foreach (var p in sq)
            {
                var l = p.Value?.Label;
                if (l == null) continue;
                l.color = c;
                Style(l, skin, p.Value.Square == null ? -1 : p.Value.Square.Depth);
            }
        }

        // ---------- label font, material, outline, size ----------
        internal static void Style(TextMeshProUGUI label, string skin, int depth)
        {
            if (label == null || Grid == null) return;
            if (On) StyledOnce = true;
            var prefab = F<TextMeshProUGUI>(Grid, "labelPrefab");
            if (prefab == null) return;
            Normalize(label, prefab, depth);
            if (prefabFont == null) prefabFont = prefab.font;
            if (prefabMat == null) prefabMat = prefab.fontSharedMaterial;
            if (!FontPair(skin, out var font, out var mat)) return;
            mat = OwnedMat(skin, mat, depth);
            if (font != null && label.font != font) label.font = font;
            if (mat != null && label.fontSharedMaterial != mat) label.fontSharedMaterial = mat;
        }
        private static bool FontPair(string skin, out TMP_FontAsset font, out Material mat)
        {
            font = prefabFont; mat = prefabMat;
            if (!On) return mat != null;
            string fn = MapCfg.PerSkin("GridLabelFont", skin), mn = MapCfg.PerSkin("GridLabelFontMaterial", skin);
            if (fn == null && mn == null) return mat != null;
            var f = fn == null ? prefabFont : FindFont(fn);
            var m = mn == null ? prefabMat : FindFontMat(mn);
            if (f == null || m == null) { Util.Log("Karte: Schrift '" + fn + "' / '" + mn + "' nicht gefunden, Spiel-Schrift bleibt"); return mat != null; }
            font = f; mat = m;
            return true;
        }
        private static TMP_FontAsset FindFont(string n)
        {
            var fonts = F<TMP_FontAsset[]>(Map, "fonts");
            if (fonts != null) foreach (var f in fonts) if (f != null && f.name == n) return f;
            return null;
        }
        private static Material FindFontMat(string n)
        {
            if (Map == null) return null;
            var m = Map.GetFontMaterial(n);
            if (m != null && m.name == n) return m;
            var all = F<Material[]>(Map, "fontMaterials");
            if (all != null) foreach (var x in all) if (x != null && x.name == n) return x;
            return null;
        }
        private static Material OwnedMat(string skin, Material baseMat, int depth)
        {
            if (baseMat == null) return null;
            bool need = On && (MapCfg.Bool("GridLabelFaceOverride", true) || OutlineOn(skin));
            if (!need && !ZDiffers(baseMat)) return baseMat;
            if (ownedGen != MapCfg.Generation || ownedSkin != skin || ownedBase != baseMat)
            {
                foreach (var p in ownedLabelMats) if (p.Value != null) UnityEngine.Object.Destroy(p.Value);
                ownedLabelMats.Clear();
                ownedGen = MapCfg.Generation; ownedSkin = skin; ownedBase = baseMat;
            }
            int key = depth < 0 ? -1 : depth;
            if (ownedLabelMats.TryGetValue(key, out var cached) && cached != null) return cached;
            var m = Copy(baseMat);
            m.name = baseMat.name + " (Toolkit d" + key + ")";
            if (On && MapCfg.Bool("GridLabelFaceOverride", true))
            {
                var face = MapCfg.SkinColor("GridLabelFaceColor.", skin) ?? MapCfg.Color("GridLabelFaceColor") ?? new Color32(255, 255, 255, 255);
                if (m.HasProperty("_FaceColor")) m.SetColor("_FaceColor", face);
                if (m.HasProperty("_FaceDilate")) m.SetFloat("_FaceDilate", Dilate(skin, depth));
            }
            if (On && OutlineOn(skin))
            {
                var oc = MapCfg.SkinColor("GridLabelOutlineColor.", skin) ?? MapCfg.Color("GridLabelOutlineColor") ?? new Color32(0xED, 0xE7, 0xD8, 0xC0);
                if (m.HasProperty("_OutlineColor")) m.SetColor("_OutlineColor", oc);
                if (m.HasProperty("_OutlineWidth")) m.SetFloat("_OutlineWidth", OutlineWidth(skin, depth));
                m.EnableKeyword("OUTLINE_ON");
            }
            if (prefabMat != null && m.HasProperty("_ZTest") && prefabMat.HasProperty("_ZTest")) m.SetFloat("_ZTest", prefabMat.GetFloat("_ZTest"));   // no z-fighting with the chart
            ownedLabelMats[key] = m;
            return m;
        }
        private static bool ZDiffers(Material m) => prefabMat != null && m != prefabMat && m.HasProperty("_ZTest") && prefabMat.HasProperty("_ZTest") && !Mathf.Approximately(m.GetFloat("_ZTest"), prefabMat.GetFloat("_ZTest"));
        private static bool OutlineOn(string skin) { var s = skin != null ? MapCfg.Str("GridLabelOutline." + skin) : null; return s != null && bool.TryParse(s, out var b) ? b : MapCfg.Bool("GridLabelOutline", false); }
        private static float Dilate(string skin, int depth)
        {
            if (depth >= 0 && MapCfg.ByDepth("GridLabelFaceDilateByDepth").TryGetValue(depth.ToString(), out var d)) return Mathf.Clamp(d, -1, 1);
            return Mathf.Clamp(MapCfg.Float("GridLabelFaceDilate." + skin, MapCfg.Float("GridLabelFaceDilate", 0.02f)), -1, 1);
        }
        private static float OutlineWidth(string skin, int depth)
        {
            if (depth >= 0 && MapCfg.ByDepth("GridLabelOutlineWidthByDepth").TryGetValue(depth.ToString(), out var d)) return Mathf.Clamp01(d);
            return Mathf.Clamp01(MapCfg.Float("GridLabelOutlineWidth." + skin, MapCfg.Float("GridLabelOutlineWidth", 0.15f)));
        }
        private static void Normalize(TextMeshProUGUI label, TextMeshProUGUI prefab, int depth)
        {
            var rect = label.rectTransform; var pr = prefab.rectTransform;
            if (rect == null || pr == null) return;
            if (!geoCaptured) { geoCaptured = true; prefabScale = pr.localScale; prefabSize = pr.sizeDelta; }
            bool on = On && MapCfg.Bool("GridLabelNormalizeSize", false) && depth >= 0;
            if (!on)
            {
                if (!everNormalized) return;
                rect.localScale = prefabScale; rect.sizeDelta = prefabSize;
                return;
            }
            float baseSize = MapCfg.Float("GridLabelBaseFontSize", 32);
            float vanillaSize = prefab.fontSize / Mathf.Pow(4f, Mathf.Max(0, depth - 2));
            if (baseSize <= 0f || vanillaSize <= 0f) return;
            float scale = vanillaSize / baseSize;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            everNormalized = true;
            rect.sizeDelta = prefabSize / scale;
            label.fontSize = baseSize;
            rect.localScale = prefabScale * scale;
        }

        internal static void GridLineColor()
        {
            if (Grid == null || !On || Current == null) return;
            var want = MapCfg.SkinColor("GridLineColor.", Current);
            if (!want.HasValue) return;
            Traverse.Create(Grid).Field("originalGridColor").SetValue((Color)want.Value);
        }

        // ---------- extra skins ----------
        private static MapSkin Find(List<MapSkin> l, string n) { if (n != null) foreach (var s in l) if (s != null && s.name == n) return s; return null; }
        private static MapSkin Clone(MapSkin src)
        {
            var c = new MapSkin();
            foreach (var f in typeof(MapSkin).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) try { f.SetValue(c, f.GetValue(src)); } catch { }
            return c;
        }
        internal static void EnsureExtraSkins(List<MapSkin> skins)
        {
            if (skins == null || !On) return;
            if (IvoryOn && Find(skins, MapCfg.IvoryName) == null)
            {
                var b = Find(skins, MapCfg.Str("IvoryBaseSkin")) ?? Find(skins, "Paper-Like") ?? Find(skins, "Default");
                if (b != null) { var s = Clone(b); s.name = MapCfg.IvoryName; s.gridLabelColor = new Color32(0x2A, 0x26, 0x22, 0xFF); skins.Add(s); }
            }
            var cs = Find(skins, MapCfg.CustomName);
            if (cs == null)
            {
                var b = Find(skins, "Paper-Like") ?? Find(skins, "Default");
                if (b == null) return;
                cs = Clone(b); cs.name = MapCfg.CustomName; skins.Add(cs);
            }
            ApplyCustom(cs);
        }
        internal static void ApplyCustom(MapSkin s)
        {
            if (s == null) return;
            Color32 O(string w) { Color32 c = Custom(w); c.a = 255; return c; }
            s.gridLabelColor = O("Grid");
            s.customLabelColor1 = s.customLabelColor2 = s.customLabelColor3 = O("Place");
            s.neutralLabelColor = O("Neutral");
            s.alliesLabelColor = s.alliesPuppetLabelColor = O("Allies");
            s.axisLabelColor = s.axisPuppetLabelColor = O("Axis");
        }
        internal static List<string> ExtraNames()
        {
            var n = new List<string>();
            if (!On) return n;
            if (IvoryOn) n.Add(MapCfg.IvoryName);
            n.Add(MapCfg.CustomName);
            return n;
        }

        private static Dictionary<string, Color32> OwnColors(string what)
        {
            var d = MapCfg.ColorList(what == "land" ? "IvoryLandColors" : "IvorySeaColors");
            if (IsCustom(Current)) { Color32 p = Custom(what == "land" ? "Land" : "Sea"); p.a = 255; d["_BaseColor"] = p; if (what == "land") d["_SceneColor"] = p; }
            return d;
        }
        private static Dictionary<string, float> OwnFloats(string what) => MapCfg.FloatList(what == "land" ? "IvoryLandFloats" : "IvorySeaFloats");

        internal static void ApplyOwnMaterials()
        {
            if (Map == null) return;
            if (!OwnSkinNow) { ClearOwn(); ownFor = null; return; }
            var land = F<MeshRenderer>(Map, "mapLandRenderer");
            var sea = F<MeshRenderer>(Map, "mapSeaRenderer");
            if (ownLand != null && land != null && land.sharedMaterial == ownLand && ownFor == Current) return;
            ownFor = Current;
            ClearOwn();
            if (land != null && land.sharedMaterial != null)
            {
                ownLand = Tinted(land.sharedMaterial, "land");
                land.sharedMaterial = ownLand;
                Traverse.Create(Map).Field("landMaterial").SetValue(ownLand);
            }
            if (sea != null)
            {
                var arr = sea.sharedMaterials;
                if (arr != null && arr.Length > 0 && arr[0] != null)
                {
                    ownSea = Tinted(arr[0], "sea");
                    arr[0] = ownSea;
                    sea.sharedMaterials = arr;
                    Traverse.Create(Map).Field("seaMaterial").SetValue(ownSea);
                }
            }
        }
        internal static void RetintOwn()
        {
            if (!OwnSkinNow) return;
            if (ownLand != null) Tint(ownLand, "land");
            if (ownSea != null) Tint(ownSea, "sea");
        }
        private static void ClearOwn()
        {
            if (ownLand != null) UnityEngine.Object.Destroy(ownLand);
            if (ownSea != null) UnityEngine.Object.Destroy(ownSea);
            ownLand = ownSea = null;
        }
        private static Material Copy(Material src) { var m = new Material(src); m.renderQueue = src.renderQueue; m.shaderKeywords = src.shaderKeywords; return m; }
        private static Material Tinted(Material src, string what) { var m = Copy(src); m.name = src.name + " (Toolkit)"; Tint(m, what); return m; }
        private static void Tint(Material m, string what)
        {
            foreach (var p in OwnColors(what)) if (m.HasProperty(p.Key)) m.SetColor(p.Key, p.Value); else Util.Log("Karte: Material " + m.name + " hat keine Farbe " + p.Key);
            foreach (var p in OwnFloats(what)) if (m.HasProperty(p.Key)) m.SetFloat(p.Key, p.Value); else Util.Log("Karte: Material " + m.name + " hat keinen Wert " + p.Key);
        }

        // ---------- place and country names (game paints them only once) ----------
        internal static void RecordPlaceClasses(MapRenderer map)
        {
            placeClass.Clear();
            Util.Safe("Map.Places", () =>
            {
                var list = F<List<MapDecoration>>(map, "mapDecorations");
                var data = Traverse.Create(typeof(MapRenderer)).Field("gameData").GetValue<GameData>();
                if (list == null || data == null) return;
                var rows = data.GetDataRows("Graphics/Map Labels");
                if (rows == null || rows.Length > list.Count) return;
                int start = list.Count - rows.Length;
                for (int i = 0; i < rows.Length; i++)
                {
                    var l = list[start + i] as MapLabel;
                    if (l == null || l.Country != null) continue;
                    int c = (int)rows[i].GetParameter(10);
                    placeClass[l] = c == 2 || c == 3 ? c : 1;
                }
            });
        }
        private static string lastSkin;
        internal static void OnSkinChanged()
        {
            string now = Current;
            bool involved = IsCustom(now) || IsCustom(lastSkin) || ChangesPlaceNames(now) || ChangesPlaceNames(lastSkin);
            lastSkin = now;
            if (involved) RepaintNames();
        }
        internal static void RepaintNames()
        {
            Util.Safe("Map.Repaint", () =>
            {
                var skin = Map?.CurrentMapSkin;
                var list = F<List<MapDecoration>>(Map, "mapDecorations");
                if (skin == null || list == null) return;
                var aff = AccessTools.Method(typeof(MapLabel), "AllegianceOnAffiliationChanged");
                foreach (var d in list)
                {
                    var l = d as MapLabel;
                    if (l == null) continue;
                    if (l.Country != null) { aff?.Invoke(l, new object[] { l.Country }); continue; }
                    if (!placeClass.TryGetValue(l, out int c)) continue;
                    var t = l.GetComponent<TextMeshProUGUI>();
                    if (t != null) t.color = c == 3 ? skin.customLabelColor3 : c == 2 ? skin.customLabelColor2 : skin.customLabelColor1;
                }
            });
        }

        internal static void ReapplyAll()
        {
            Util.Safe("Map.Reapply", () =>
            {
                var skins = Skins(Map);
                if (skins != null) { EnsureExtraSkins(skins); ApplyPlaceColors(skins); ApplyLabelColors(skins); }
                ApplyOwnMaterials();
                RetintOwn();
                GridLineColor();
                RecolorGridLabels();
                RepaintNames();
            });
        }

        // ---------- report for the log ----------
        internal static void Report()
        {
            var sb = new StringBuilder("Karten-Bericht:\n");
            var skins = Skins(Map);
            if (skins == null) { Util.Log("Karten-Bericht: Karte noch nicht geladen (einmal Karte öffnen)."); return; }
            sb.Append("aktueller Stil: ").Append(Current).Append('\n');
            foreach (var s in skins)
            {
                if (s == null) continue;
                sb.Append("- ").Append(s.name).Append(": Raster-Text ").Append(MapCfg.Hex(s.gridLabelColor)).Append(", Orte ").Append(MapCfg.Hex(s.customLabelColor1))
                  .Append("/").Append(MapCfg.Hex(s.customLabelColor2)).Append("/").Append(MapCfg.Hex(s.customLabelColor3))
                  .Append(", neutral ").Append(MapCfg.Hex(s.neutralLabelColor)).Append(", Alliierte ").Append(MapCfg.Hex(s.alliesLabelColor)).Append(", Achse ").Append(MapCfg.Hex(s.axisLabelColor))
                  .Append(", Land ").Append(s.landMaterialPath).Append(", See ").Append(s.seaMaterialPath).Append('\n');
            }
            void Mat(string what, Material m)
            {
                if (m == null) return;
                sb.Append(what).Append(" Material ").Append(m.name).Append(" (").Append(m.shader != null ? m.shader.name : "?").Append("):");
                foreach (var p in new[] { "_BaseColor", "_SceneColor", "_Color", "_FaceColor", "_OutlineColor" }) if (m.HasProperty(p)) sb.Append(' ').Append(p).Append('=').Append(MapCfg.Hex((Color32)m.GetColor(p)));
                foreach (var p in new[] { "_GlobalColorIntensity", "_FaceDilate", "_OutlineWidth", "_ZTest" }) if (m.HasProperty(p)) sb.Append(' ').Append(p).Append('=').Append(m.GetFloat(p).ToString("0.###", CultureInfo.InvariantCulture));
                sb.Append('\n');
            }
            Mat("Land", F<MeshRenderer>(Map, "mapLandRenderer")?.sharedMaterial);
            var sea = F<MeshRenderer>(Map, "mapSeaRenderer")?.sharedMaterials;
            if (sea != null && sea.Length > 0) Mat("See", sea[0]);
            var prefab = F<TextMeshProUGUI>(Grid, "labelPrefab");
            if (prefab != null) Mat("Raster-Text", prefab.fontSharedMaterial);
            var fonts = F<TMP_FontAsset[]>(Map, "fonts");
            if (fonts != null) { sb.Append("Schriften:"); foreach (var f in fonts) if (f != null) sb.Append(" '").Append(f.name).Append('\''); sb.Append('\n'); }
            var fm = F<Material[]>(Map, "fontMaterials");
            if (fm != null) { sb.Append("Schrift-Materialien:"); foreach (var m in fm) if (m != null) sb.Append(" '").Append(m.name).Append('\''); sb.Append('\n'); }
            if (Grid != null) sb.Append("Raster-Ebene: ").Append(F<int>(Grid, "displayedDepth")).Append('\n');
            Util.Log(sb.ToString());
            ToolkitRunner.ShowNotice("Karten-Bericht ins Toolkit-Log geschrieben", 4f);
        }
    }

    // Host: polls the file, notices switch changes, repaints after colour edits, pixel measurement.
    internal class MapHost : MonoBehaviour
    {
        private static MapHost me;
        private bool lastOn, lastIvory;
        private int lastGen = -1;
        internal static bool RepaintPending;
        private float nextRepaint, nextSample;
        private bool sampling;
        private readonly HashSet<string> sampled = new HashSet<string>();
        private int sampledGen = -1;
        private static MethodInfo capture; private static bool captureLooked;

        internal static void Ensure()
        {
            if (me != null) return;
            var go = new GameObject("Toolkit Map");
            DontDestroyOnLoad(go);
            me = go.AddComponent<MapHost>();
            me.lastOn = MapSkins.On; me.lastIvory = MapSkins.IvoryOn;
        }

        private void Update()
        {
            MapCfg.Poll();
            if (MapSkins.Map == null) return;
            if (MapSkins.On != lastOn || MapSkins.IvoryOn != lastIvory || MapCfg.Generation != lastGen)
            {
                lastOn = MapSkins.On; lastIvory = MapSkins.IvoryOn; lastGen = MapCfg.Generation;
                MapSkins.ReapplyAll();
            }
            if (RepaintPending && Time.unscaledTime >= nextRepaint)
            {
                RepaintPending = false; nextRepaint = Time.unscaledTime + 0.1f;
                if (MapSkins.IsCustom(MapSkins.Current)) { MapSkins.RecolorGridLabels(); MapSkins.RepaintNames(); }
            }
            Util.Safe("Map.Pixel", Sample);
        }

        // ---------- pixel measurement of a real rendered grid label ----------
        private void Sample()
        {
            if (!Settings.B("MapPixelSample") || sampling || MapSkins.Grid == null) return;
            if (sampledGen != MapCfg.Generation) { sampledGen = MapCfg.Generation; sampled.Clear(); }
            if (Time.unscaledTime < nextSample) return;
            var cam = Traverse.Create(typeof(NavalGridRenderer)).Field("mainCamera").GetValue<MainCamera>();
            if (cam == null || cam.CurrentMode != CameraMode.Map) return;
            int depth = Traverse.Create(MapSkins.Grid).Field("displayedDepth").GetValue<int>();
            string key = MapSkins.Current + "|" + depth;
            if (sampled.Contains(key)) return;
            var c = cam.GetComponent<Camera>();
            var label = FindLabel(c);
            if (label == null) return;
            nextSample = Time.unscaledTime + Mathf.Max(2f, MapCfg.Float("PixelSampleSeconds", 6));
            sampled.Add(key);
            StartCoroutine(Capture(c, label, MapSkins.Current, depth));
        }
        private static TextMeshProUGUI FindLabel(Camera cam)
        {
            var sq = Traverse.Create(MapSkins.Grid).Field("squares").GetValue<Dictionary<string, NavalGridRendererSquare>>();
            if (sq == null) return null;
            foreach (var p in sq)
            {
                var s = p.Value;
                if (s == null || !s.IsVisible) continue;
                var l = s.Label;
                if (l == null || !l.enabled || !l.gameObject.activeInHierarchy || string.IsNullOrEmpty(l.text)) continue;
                if (cam != null)
                {
                    var sp = cam.WorldToScreenPoint(l.transform.position);
                    if (sp.z <= 0f || sp.x < 48f || sp.y < 48f || sp.x > Screen.width - 48f || sp.y > Screen.height - 48f) continue;
                }
                return l;
            }
            return null;
        }
        private IEnumerator Capture(Camera cam, TextMeshProUGUI label, string skin, int depth)
        {
            sampling = true;
            yield return new WaitForEndOfFrame();
            Texture2D shot = null;
            try
            {
                if (!captureLooked)
                {
                    captureLooked = true;
                    var t = Type.GetType("UnityEngine.ScreenCapture, UnityEngine.ScreenCaptureModule") ?? Type.GetType("UnityEngine.ScreenCapture, UnityEngine");
                    capture = t?.GetMethod("CaptureScreenshotAsTexture", Type.EmptyTypes);
                    if (capture == null) Util.Log("Karte: Bildschirm-Aufnahme nicht verfügbar, Pixel-Messung aus");
                }
                shot = capture?.Invoke(null, null) as Texture2D;
                if (shot != null && cam != null && label != null) Measure(shot, cam, label, skin, depth);
            }
            catch (Exception e) { Util.Log("Karte: Pixel-Messung fehlgeschlagen: " + e.Message); }
            finally { if (shot != null) Destroy(shot); sampling = false; }
        }
        private static float Lum(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        private static float Lin(float v) => v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
        private static float RelLum(Color c) => 0.2126f * Lin(c.r) + 0.7152f * Lin(c.g) + 0.0722f * Lin(c.b);
        private static float Ratio(float a, float b) => (Mathf.Max(a, b) + 0.05f) / (Mathf.Min(a, b) + 0.05f);

        private static void Measure(Texture2D shot, Camera cam, TextMeshProUGUI label, string skin, int depth)
        {
            Bounds b = label.textBounds;
            if (b.size.x <= 0.0001f || b.size.y <= 0.0001f) b = label.bounds;
            if (b.size.x <= 0.0001f || b.size.y <= 0.0001f) b = new Bounds(Vector3.zero, new Vector3(label.preferredWidth, label.preferredHeight, 0f));
            if (b.size.x <= 0.0001f || b.size.y <= 0.0001f) return;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            var cc = b.center; var e = b.extents;
            foreach (var corner in new[] { new Vector3(cc.x - e.x, cc.y - e.y, cc.z), new Vector3(cc.x + e.x, cc.y - e.y, cc.z), new Vector3(cc.x + e.x, cc.y + e.y, cc.z), new Vector3(cc.x - e.x, cc.y + e.y, cc.z) })
            {
                var sp = cam.WorldToScreenPoint(label.transform.TransformPoint(corner));
                if (sp.z <= 0f) return;
                minX = Mathf.Min(minX, sp.x); minY = Mathf.Min(minY, sp.y); maxX = Mathf.Max(maxX, sp.x); maxY = Mathf.Max(maxY, sp.y);
            }
            float sx = Screen.width > 0 ? (float)shot.width / Screen.width : 1f, sy = Screen.height > 0 ? (float)shot.height / Screen.height : 1f;
            int x0 = Mathf.FloorToInt(minX * sx), y0 = Mathf.FloorToInt(minY * sy), x1 = Mathf.CeilToInt(maxX * sx), y1 = Mathf.CeilToInt(maxY * sy);
            int w = x1 - x0, h = y1 - y0;
            if (w < 4 || h < 4) return;
            int margin = Mathf.Clamp(Mathf.RoundToInt(h * 0.5f), 3, 24);
            int bx = Mathf.Clamp(x0 - margin, 0, shot.width - 1), by = Mathf.Clamp(y0 - margin, 0, shot.height - 1);
            int bw = Mathf.Clamp(x1 + margin, 0, shot.width) - bx, bh = Mathf.Clamp(y1 + margin, 0, shot.height) - by;
            if (bw < 4 || bh < 4) return;
            var block = shot.GetPixels(bx, by, bw, bh);
            var ring = new List<float>(); var ringC = new List<Color>(); var inner = new List<float>(); var innerC = new List<Color>();
            int ix0 = x0 - bx, iy0 = y0 - by, ix1 = ix0 + w, iy1 = iy0 + h;
            for (int y = 0; y < bh; y++)
                for (int x = 0; x < bw; x++)
                {
                    var c = block[y * bw + x];
                    bool inside = x >= ix0 && x < ix1 && y >= iy0 && y < iy1;
                    if (inside) { inner.Add(Lum(c)); innerC.Add(c); } else { ring.Add(Lum(c)); ringC.Add(c); }
                }
            if (ring.Count < 8 || inner.Count < 8) return;
            var rs = new List<float>(ring); rs.Sort();
            float bg = rs[rs.Count / 2];
            Color bgC = ringC[0]; float bd = float.MaxValue;
            for (int i = 0; i < ringC.Count; i++) { float d = Mathf.Abs(ring[i] - bg); if (d < bd) { bd = d; bgC = ringC[i]; } }
            var sorted = new List<float>(inner); sorted.Sort();
            float p05 = sorted[Mathf.Clamp(Mathf.RoundToInt(0.05f * (sorted.Count - 1)), 0, sorted.Count - 1)];
            float p95 = sorted[Mathf.Clamp(Mathf.RoundToInt(0.95f * (sorted.Count - 1)), 0, sorted.Count - 1)];
            bool dark = (bg - p05) >= (p95 - bg);
            string inkFrom = "Messung";
            if (skin != null) { Color want = MapSkins.LabelColor(skin); if (want.a > 0.01f) { dark = Lum(want) < bg; inkFrom = "Einstellung"; } }
            float textL = dark ? p05 : p95;
            Color textC = innerC[0]; float ext = dark ? float.MaxValue : float.MinValue;
            foreach (var c in innerC) { float l = Lum(c); if (dark ? l < ext : l > ext) { ext = l; textC = c; } }
            int diff = 0, lighter = 0, darker = 0;
            foreach (var l in inner) { float d = l - bg; if (Mathf.Abs(d) > 0.04f) { diff++; if (d > 0) lighter++; else darker++; } }
            float cover = (float)diff / inner.Count;
            float eff = Ratio(textL, bg), peak = Ratio(RelLum(textC), RelLum(bgC));
            string verdict = eff < 1.5f ? "UNSICHTBAR" : eff < 2.2f ? "sehr schwer lesbar" : eff < 3f ? "grenzwertig" : cover < 0.04f ? "lesbar, aber sehr dünn" : "gut lesbar";
            var sb = new StringBuilder();
            sb.Append("Karte Pixel: Stil '").Append(skin).Append("' Ebene ").Append(depth).Append(" Text '").Append(label.text).Append("' ").Append(w).Append('x').Append(h).Append(" px")
              .Append(" | Hintergrund ").Append(MapCfg.Hex((Color32)bgC)).Append(" | Schrift ").Append(MapCfg.Hex((Color32)textC))
              .Append(" | Kontrast ").Append(eff.ToString("0.00", CultureInfo.InvariantCulture)).Append(" (Spitze ").Append(peak.ToString("0.00", CultureInfo.InvariantCulture)).Append(", Farbe aus ").Append(inkFrom).Append(')')
              .Append(" | Deckung ").Append((cover * 100f).ToString("0.0", CultureInfo.InvariantCulture)).Append("% (").Append(darker).Append(" dunkler, ").Append(lighter).Append(" heller)");
            if (darker > 8 && lighter > 8) sb.Append(" | Rand/Schein erkannt (").Append(dark ? "heller" : "dunkler").Append(" als Hintergrund)");
            sb.Append(" | Ergebnis: ").Append(verdict);
            Util.Log(sb.ToString());
        }
    }

    // ---------- patches ----------
    [HarmonyPatch(typeof(MapRenderer), "LoadMapSkins")]
    internal static class Map_SkinsPatch
    {
        private static void Postfix(MapRenderer __instance)
        {
            MapSkins.Map = __instance;
            MapHost.Ensure();
            Util.Safe("Map.Skins", () =>
            {
                var skins = MapSkins.Skins(__instance);
                if (skins == null) return;
                MapSkins.EnsureExtraSkins(skins);
                MapSkins.ApplyPlaceColors(skins);
                MapSkins.ApplyLabelColors(skins);
            });
        }
    }
    [HarmonyPatch(typeof(MapRenderer), "CurrentMapSkin", MethodType.Setter)]
    internal static class Map_CurrentSkinPatch
    {
        private static void Postfix(MapRenderer __instance)
        {
            MapSkins.Map = __instance;
            Util.Safe("Map.Current", () => { MapSkins.ApplyOwnMaterials(); MapSkins.GridLineColor(); MapSkins.OnSkinChanged(); });
        }
    }
    [HarmonyPatch(typeof(MapRenderer), "LoadMapLabels")]
    internal static class Map_LabelsPatch
    {
        private static void Postfix(MapRenderer __instance)
        {
            MapSkins.Map = __instance;
            MapSkins.RecordPlaceClasses(__instance);
            MapSkins.OnSkinChanged();
        }
    }
    [HarmonyPatch(typeof(NavalGridRenderer), "BuildSquares")]
    internal static class Map_BuildSquaresPatch
    {
        private static void Postfix(NavalGridRenderer __instance) { MapSkins.Grid = __instance; Util.Safe("Map.GridLine", MapSkins.GridLineColor); }
    }
    [HarmonyPatch(typeof(NavalGridRenderer), "CreateSquareRenderer")]
    internal static class Map_SquarePatch
    {
        private static void Postfix(NavalGridRenderer __instance, NavalGridRendererSquare __result)
        {
            MapSkins.Grid = __instance;
            if ((!MapSkins.On && !MapSkins.StyledOnce) || __result?.Label == null) return;
            Util.Safe("Map.Square", () => MapSkins.Style(__result.Label, MapSkins.Current, __result.Square == null ? -1 : __result.Square.Depth));
        }
    }
    [HarmonyPatch(typeof(VideoSettingsPanel), "FetchMapSkins")]
    internal static class Map_SettingsListPatch
    {
        private static void Postfix(VideoSettingsPanel __instance, ref string[] ___mapSkins, UnityEngine.UI.TextMeshProDropdown ___mapSkinDropdown)
        {
            var extra = MapSkins.ExtraNames();
            if (extra.Count == 0 || ___mapSkins == null || ___mapSkinDropdown == null) return;
            var opts = ___mapSkinDropdown.options;
            if (opts == null) return;
            var grown = new List<string>(___mapSkins);
            foreach (var n in extra)
            {
                if (grown.Contains(n)) continue;
                opts.Add(new UnityEngine.UI.TextMeshProDropdown.OptionData(n));
                grown.Add(n);
            }
            ___mapSkinDropdown.options = opts;
            ___mapSkins = grown.ToArray();
        }
    }

    // ---------- F10 tab "Karte" ----------
    internal static class MapTab
    {
        private static readonly string[] keys = { "Sea", "Land", "Grid", "Place", "Neutral", "Allies", "Axis" };
        private static readonly string[] names = { "Meer", "Land", "Raster-Beschriftung (AN15 ...)", "Meeres- und Ortsnamen", "Neutrale Länder", "Alliierte Länder", "Achsen-Länder" };

        public static void Draw()
        {
            Imgui.Label("Karten-Stil wählen: Einstellungen > Grafik > Kartenstil. Neu: „" + MapCfg.IvoryName + "“ und „" + MapCfg.CustomName + "“.");
            Imgui.Label("Farben für „" + MapCfg.CustomName + "“ (sofort sichtbar, wenn dieser Stil an ist):");
            for (int i = 0; i < keys.Length; i++)
            {
                Color32 c = MapSkins.Custom(keys[i]);
                Imgui.Label(names[i] + ":  " + MapCfg.Hex(c));
                Imgui.BeginH();
                byte r = Chan("R", c.r), g = Chan("G", c.g), b = Chan("B", c.b);
                Imgui.EndH();
                if (r != c.r || g != c.g || b != c.b)
                {
                    MapCfg.Write("Custom." + keys[i], MapCfg.Hex(new Color32(r, g, b, 255)));
                    Util.Safe("Map.Edit", () =>
                    {
                        var skins = MapSkins.Skins(MapSkins.Map);
                        if (skins == null) return;
                        foreach (var s in skins) if (s != null && MapSkins.IsCustom(s.name)) MapSkins.ApplyCustom(s);
                        MapSkins.ApplyLabelColors(skins);
                        if (MapSkins.IsCustom(MapSkins.Current)) { MapSkins.RetintOwn(); MapHost.RepaintPending = true; }
                    });
                }
            }
            Imgui.Space(8);
            Imgui.Label("Feine Einstellungen pro Karten-Stil: Datei " + MapCfg.File + " (wird im laufenden Spiel neu gelesen).");
            if (Imgui.Button("Karten-Bericht ins Log schreiben", 320)) Util.Safe("Map.Report", MapSkins.Report);
        }
        private static byte Chan(string n, byte v)
        {
            Imgui.Label(n + " " + v, 60);
            return (byte)Mathf.RoundToInt(Imgui.Slider(v, 0, 255, 140));
        }
    }
}
