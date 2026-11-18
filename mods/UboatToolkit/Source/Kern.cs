// UBOAT Toolkit - Kern: Start, Einstellungen, Menü, Suche, Tasten, Besitzer der Spielwerte, Log
// Diese Datei fasst 14 frühere Einzeldateien zusammen (0.4.30). Jeder Abschnitt nennt seine alte Datei.

// ====================================================================================================
// Abschnitt: Core/AddonTabs.cs
// ====================================================================================================
namespace UboatToolkit
{
    using global::System;
    using global::System.Collections.Generic;

    // Optional add-on tabs in the F10 menu for OTHER mods (e.g. Warthog), without any dependency in either direction:
    // the other mod looks this type up by name at runtime and, if the Toolkit is not installed, uses its own window.
    //   var t = System.Type.GetType("UboatToolkit.AddonTabs, UboatToolkit");
    //   t?.GetMethod("Register")?.Invoke(null, new object[] { "Warthog", (System.Action)MyDrawWithGUILayout });
    // The draw callback runs inside the menu area (plain GUILayout works). Errors in it are caught and shown once.
    public static class AddonTabs
    {
        internal static readonly List<KeyValuePair<string, Action>> Tabs = new List<KeyValuePair<string, Action>>();
        private static readonly HashSet<string> failed = new HashSet<string>();

        public static bool Register(string title, Action draw)
        {
            if (string.IsNullOrEmpty(title) || draw == null) return false;
            Tabs.RemoveAll(x => x.Key == title);
            Tabs.Add(new KeyValuePair<string, Action>(title, draw));
            Util.Log("Menü: Add-on-Reiter \"" + title + "\" angemeldet");
            return true;
        }

        public static bool Unregister(string title) => Tabs.RemoveAll(x => x.Key == title) > 0;

        internal static bool Draw(string title)
        {
            foreach (var kv in Tabs)
            {
                if (kv.Key != title) continue;
                try { kv.Value(); }
                catch (Exception e) { if (failed.Add(title)) Util.Log("Menü: Add-on-Reiter \"" + title + "\" Fehler: " + e.Message); }
                return true;
            }
            return false;
        }
    }
}

// ====================================================================================================
// Abschnitt: Core/BgFile.cs
// ====================================================================================================
namespace UboatToolkit
{
    using global::System.Collections.Generic;
    using global::System.IO;
    using global::System.Text;

    // 0.3.69 (DoMe: "Toolkit auf Multithreading umstellen"): all log lines are written by ONE background thread.
    // The game's main thread only puts the text into a list (no disk access, no file open/close per line).
    // Order per file is kept. Written every 0.3 s, and at once when the game quits.
    public static class BgFile
    {
        private static readonly object gate = new object();
        private static List<KeyValuePair<string, string>> queue = new List<KeyValuePair<string, string>>();
        private static System.Threading.Thread thread;
        private static readonly System.Threading.AutoResetEvent wake = new System.Threading.AutoResetEvent(false);
        private static bool hooked;

        public static void Append(string path, string text)
        {
            if (path == null || text == null) return;
            lock (gate)
            {
                queue.Add(new KeyValuePair<string, string>(path, text));
                if (thread == null)
                {
                    thread = new System.Threading.Thread(Loop) { IsBackground = true, Name = "UboatToolkit log" };
                    thread.Start();
                }
            }
            if (!hooked) { hooked = true; try { UnityEngine.Application.quitting += Flush; } catch { } }
        }

        private static void Loop()
        {
            while (true)
            {
                wake.WaitOne(300);
                Flush();
            }
        }

        private static readonly object writeGate = new object();
        public static void Flush()
        {
            List<KeyValuePair<string, string>> batch;
            lock (gate)
            {
                if (queue.Count == 0) return;
                batch = queue;
                queue = new List<KeyValuePair<string, string>>();
            }
            lock (writeGate)
            {
                // group by file, keep order
                var parts = new Dictionary<string, StringBuilder>();
                var order = new List<string>();
                foreach (var kv in batch)
                {
                    if (!parts.TryGetValue(kv.Key, out var sb)) { sb = new StringBuilder(); parts[kv.Key] = sb; order.Add(kv.Key); }
                    sb.Append(kv.Value);
                }
                foreach (var p in order)
                {
                    try { File.AppendAllText(p, parts[p].ToString(), new UTF8Encoding(false)); }
                    catch { /* logging must never break the game */ }
                }
            }
        }
    }
}

// ====================================================================================================
// Abschnitt: Core/DebugLog.cs
// ====================================================================================================
namespace UboatToolkit
{
    using global::System;
    using global::System.IO;
    using global::System.Linq;
    using global::UnityEngine;

    // Test logging: everything the mod does and every error goes into
    // ...\UBOAT\UboatToolkit\logs\toolkit-<date>.log  (one file per day, max 20 MB).
    // Also writes all settings at start and every time they are saved, plus game state every minute.
    public static class DebugLog
    {
        private const long MaxBytes = 20L * 1024 * 1024;
        private static string path;
        private static bool full;
        private static long size = -1;

        private static bool On => Settings.Get("DebugLog") == null || Settings.B("DebugLog");

        public static void Write(string level, string text)
        {
            if (!On || full) return;
            try
            {
                if (path == null)
                {
                    string dir = Path.Combine(Settings.Folder, "logs");
                    Directory.CreateDirectory(dir);
                    path = Path.Combine(dir, "toolkit-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                }
                // size check once at start, then counted here: no file access on the game's main thread
                if (size < 0) size = File.Exists(path) ? new FileInfo(path).Length : 0;
                if (size > MaxBytes) { full = true; return; }
                string line = DateTime.Now.ToString("HH:mm:ss") + " [" + level + "] " + text + "\n";
                size += line.Length;
                BgFile.Append(path, line);
            }
            catch { /* logging must never break the game */ }
        }

        public static void DumpSettings(string why)
        {
            Write("SETTINGS", why + ": " + string.Join(", ", Settings.All.Select(o => o.Key + "=" + (o.IsKey ? o.Text : o.IsBool ? (o.On ? "an" : "aus") : o.Value.ToString("0.###")))));
        }

        // Real frames per real second since the last STATE line. Time.smoothDeltaTime is game time: under time
        // compression it showed "FPS=2" although the game ran smoothly.
        private static int lastFrames = -1; private static float lastReal;
        private static float RealFps()
        {
            int f = Time.frameCount; float t = Time.realtimeSinceStartup;
            float fps = lastFrames >= 0 && t - lastReal > 0.5f ? (f - lastFrames) / (t - lastReal) : 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime);
            lastFrames = f; lastReal = t;
            return fps;
        }

        public static void DumpState()
        {
            try
            {
                var ship = Util.PlayerShip;
                if (ship == null) { Write("STATE", "kein Boot geladen"); return; }
                string res = string.Join(", ", new[] { "Fuel", "Energy", "Oxygen" }.Select(n =>
                {
                    var r = ship.GetResource(n);
                    return r == null ? n + "=?" : n + "=" + ((float)r.Amount).ToString("0.###");
                }));
                Write("STATE", "Zieltiefe=" + ship.TargetDepth.ToString("0.0") + " Tiefe(Deck, wie Tiefenmesser)=" + ship.DeckDepthUnclamped.ToString("0.0") + " Turm-Oberkante=" + ship.Depth.ToString("0.0") + " Alarm=" + ship.Alarmed + " Gejagt=" + ship.IsHunted +
                               " Hafen=" + ship.Docked + " " + res + " FPS=" + RealFps().ToString("0") + " Zeitraffer=" + Time.timeScale.ToString("0.#"));
            }
            catch (Exception e) { Write("ERROR", "DumpState: " + e.Message); }
        }
    }
}

// ====================================================================================================
// Abschnitt: Core/Imgui.cs
// ====================================================================================================
namespace UboatToolkit
{
    using global::System;
    using global::System.Linq;
    using global::System.Reflection;
    using global::UnityEngine;

    // The game does not give mods a direct reference to Unity's IMGUI (GUI/GUILayout),
    // so the menu calls it through reflection. Only a handful of calls are needed.
    public static class Imgui
    {
        private static Type gui, layout, option;
        private static MethodInfo textField, label, toggle, slider, button, beginH, endH, beginScroll, endScroll, beginArea, endArea, box, guiLabel, width, space, drawTex;
        private static PropertyInfo color;
        private static object noOptions;
        public static bool Ready;
        private static Action<Rect, string> fLabel, fBox;
        private static Action<Rect, Texture> fTex;
        private static Func<Color> fGetColor;
        private static Action<Color> fSetColor;
        private static Func<int> fEventType;
        private static bool eventTypeTried;
        // Kind of the current GUI event as number (Unity: 4 KeyDown, 5 KeyUp, 7 Repaint, 8 Layout); -1 = unknown.
        public static int EventKind()
        {
            if (!eventTypeTried)
            {
                eventTypeTried = true;
                try
                {
                    var et = Type.GetType("UnityEngine.Event, UnityEngine.IMGUIModule");
                    var cur = System.Linq.Expressions.Expression.Property(null, et.GetProperty("current"));
                    var body = System.Linq.Expressions.Expression.Condition(
                        System.Linq.Expressions.Expression.Equal(cur, System.Linq.Expressions.Expression.Constant(null, et)),
                        System.Linq.Expressions.Expression.Constant(-1),
                        System.Linq.Expressions.Expression.Convert(System.Linq.Expressions.Expression.Property(cur, et.GetProperty("type")), typeof(int)));
                    fEventType = System.Linq.Expressions.Expression.Lambda<Func<int>>(body).Compile();
                    fEventType();
                }
                catch (Exception e) { fEventType = null; Util.Log("Anzeige: Ereignis-Art nur über den langsamen Weg (" + e.Message + ")"); }
            }
            if (fEventType != null) { try { return fEventType(); } catch { return -1; } }
            try
            {
                if (evCurrent == null) { var et = Type.GetType("UnityEngine.Event, UnityEngine.IMGUIModule"); evCurrent = et?.GetProperty("current"); evType = et?.GetProperty("type"); }
                var e = evCurrent?.GetValue(null, null);
                return e == null || evType == null ? -1 : Convert.ToInt32(evType.GetValue(e, null));
            }
            catch { return -1; }
        }

        public static void Init()
        {
            if (Ready) return;
            gui = Type.GetType("UnityEngine.GUI, UnityEngine.IMGUIModule");
            layout = Type.GetType("UnityEngine.GUILayout, UnityEngine.IMGUIModule");
            option = Type.GetType("UnityEngine.GUILayoutOption, UnityEngine.IMGUIModule");
            if (gui == null || layout == null || option == null) return;
            noOptions = Array.CreateInstance(option, 0);
            MethodInfo L(string n, params Type[] p) => layout.GetMethod(n, BindingFlags.Public | BindingFlags.Static, null, p, null);
            var opts = option.MakeArrayType();
            label = L("Label", typeof(string), opts);
            textField = L("TextField", typeof(string), opts);
            toggle = L("Toggle", typeof(bool), typeof(string), opts);
            slider = L("HorizontalSlider", typeof(float), typeof(float), typeof(float), opts);
            button = L("Button", typeof(string), opts);
            beginH = L("BeginHorizontal", opts);
            endH = L("EndHorizontal");
            beginScroll = L("BeginScrollView", typeof(Vector2), opts);
            endScroll = L("EndScrollView");
            beginArea = L("BeginArea", typeof(Rect));
            endArea = L("EndArea");
            space = L("Space", typeof(float));
            width = L("Width", typeof(float));
            box = gui.GetMethod("Box", new[] { typeof(Rect), typeof(string) });
            guiLabel = gui.GetMethod("Label", new[] { typeof(Rect), typeof(string) });
            drawTex = gui.GetMethod("DrawTexture", new[] { typeof(Rect), typeof(Texture) });
            color = gui.GetProperty("color", BindingFlags.Public | BindingFlags.Static);
            // 0.4.7: the calls used every frame by the always-on displays go through direct delegates instead of
            // reflection (no boxing, no argument arrays). If one cannot be made, the old path stays.
            try
            {
                fLabel = (Action<Rect, string>)Delegate.CreateDelegate(typeof(Action<Rect, string>), guiLabel);
                fBox = (Action<Rect, string>)Delegate.CreateDelegate(typeof(Action<Rect, string>), box);
                if (drawTex != null) fTex = (Action<Rect, Texture>)Delegate.CreateDelegate(typeof(Action<Rect, Texture>), drawTex);
                if (color != null) { fGetColor = (Func<Color>)Delegate.CreateDelegate(typeof(Func<Color>), color.GetGetMethod()); fSetColor = (Action<Color>)Delegate.CreateDelegate(typeof(Action<Color>), color.GetSetMethod()); }
            }
            catch (Exception e) { fLabel = null; fBox = null; fTex = null; fGetColor = null; fSetColor = null; Util.Log("Anzeige: schneller Weg nicht möglich (" + e.Message + ")"); }
            Ready = new[] { label, toggle, slider, button, beginH, endH, beginScroll, endScroll, beginArea, endArea, box, guiLabel, width, space }.All(m => m != null);
            if (!Ready) Util.Log("menu could not be created (IMGUI not found).");
        }

        private static object Opts(float w)
        {
            if (w <= 0) return noOptions;
            var arr = Array.CreateInstance(option, 1);
            arr.SetValue(width.Invoke(null, new object[] { w }), 0);
            return arr;
        }

        // Take keyboard focus away from any text field (e.g. the search box), so a key pressed for
        // "Taste ändern" is not typed into it.
        private static PropertyInfo keyboardControl;
        public static void ClearFocus()
        {
            try
            {
                if (keyboardControl == null) keyboardControl = Type.GetType("UnityEngine.GUIUtility, UnityEngine.IMGUIModule")?.GetProperty("keyboardControl", BindingFlags.Public | BindingFlags.Static);
                if (keyboardControl != null && (int)keyboardControl.GetValue(null, null) != 0) keyboardControl.SetValue(null, 0, null);
            }
            catch { }
        }
        public static string TextField(string t, float w = 0) => textField == null ? t : (string)textField.Invoke(null, new[] { t ?? "", Opts(w) });
        public static void Label(string t, float w = 0) => label.Invoke(null, new[] { Lang.T(t), Opts(w) });
        public static bool Toggle(bool v, string t) => (bool)toggle.Invoke(null, new object[] { v, Lang.T(t), noOptions });
        public static bool Toggle(bool v, string t, float w) => (bool)toggle.Invoke(null, new object[] { v, Lang.T(t), Opts(w) });
        // Draw the following controls paler (1 = normal). Returns the old colour for Tint(old).
        public static Color Tint(float alpha)
        {
            if (color == null) return Color.white;
            var old = (Color)color.GetValue(null, null);
            color.SetValue(null, new Color(old.r, old.g, old.b, alpha), null);
            return old;
        }
        public static void Tint(Color c) { if (color != null) color.SetValue(null, c, null); }
        private static PropertyInfo evCurrent, evType;
        // True in the layout pass of OnGUI (also true when the event cannot be read).
        public static bool IsLayout()
        {
            try
            {
                if (evCurrent == null) { var et = Type.GetType("UnityEngine.Event, UnityEngine.IMGUIModule"); evCurrent = et?.GetProperty("current"); evType = et?.GetProperty("type"); }
                var e = evCurrent?.GetValue(null, null);
                return e == null || evType == null || evType.GetValue(e, null).ToString() == "Layout";
            }
            catch { return true; }
        }
        // Key event of the current OnGUI call for one key: 1 = down, 2 = up, 0 = none/other. (Event via reflection, see above.)
        private static PropertyInfo evKeyCode;
        public static int KeyEvent(KeyCode code)
        {
            try
            {
                if (evCurrent == null) { var et = Type.GetType("UnityEngine.Event, UnityEngine.IMGUIModule"); evCurrent = et?.GetProperty("current"); evType = et?.GetProperty("type"); }
                if (evKeyCode == null) evKeyCode = Type.GetType("UnityEngine.Event, UnityEngine.IMGUIModule")?.GetProperty("keyCode");
                var e = evCurrent?.GetValue(null, null);
                if (e == null || evKeyCode == null || evType == null) return 0;
                if ((KeyCode)evKeyCode.GetValue(e, null) != code) return 0;
                string t = evType.GetValue(e, null).ToString();
                return t == "KeyDown" ? 1 : t == "KeyUp" ? 2 : 0;
            }
            catch { return 0; }
        }

        private static bool IsWheelEvent()
        {
            try
            {
                if (evCurrent == null) { var et = Type.GetType("UnityEngine.Event, UnityEngine.IMGUIModule"); evCurrent = et?.GetProperty("current"); evType = et?.GetProperty("type"); }
                var e = evCurrent?.GetValue(null);
                return e != null && evType.GetValue(e).ToString() == "ScrollWheel";
            }
            catch { return false; }
        }

        // The mouse wheel never changes a slider (it only scrolls the menu); DoMe had values jump while scrolling.
        public static float Slider(float v, float min, float max, float w)
        {
            bool wheel = IsWheelEvent();
            float r = (float)slider.Invoke(null, new object[] { v, min, max, Opts(w) });
            return wheel ? v : r;
        }
        public static bool Button(string t, float w = 0) => (bool)button.Invoke(null, new[] { Lang.T(t), Opts(w) });
        public static void BeginH() => beginH.Invoke(null, new[] { noOptions });
        public static void EndH() => endH.Invoke(null, null);
        public static Vector2 BeginScroll(Vector2 pos) => (Vector2)beginScroll.Invoke(null, new object[] { pos, noOptions });
        public static void EndScroll() => endScroll.Invoke(null, null);
        public static void BeginArea(Rect r) => beginArea.Invoke(null, new object[] { r });
        public static void EndArea() => endArea.Invoke(null, null);
        public static void Space(float px) => space.Invoke(null, new object[] { px });
        public static void Box(Rect r, string t) { if (fBox != null) fBox(r, Lang.T(t)); else box.Invoke(null, new object[] { r, Lang.T(t) }); }
        // Solid dark background (alpha 0..1), so the menu stays readable over the game.
        public static void Backdrop(Rect r, float alpha)
        {
            if (drawTex == null || color == null || alpha <= 0f) return;
            if (fTex != null && fGetColor != null) { var o0 = fGetColor(); fSetColor(new Color(0.05f, 0.06f, 0.08f, Mathf.Clamp01(alpha))); fTex(r, Texture2D.whiteTexture); fSetColor(o0); return; }
            var old = (Color)color.GetValue(null, null);
            color.SetValue(null, new Color(0.05f, 0.06f, 0.08f, Mathf.Clamp01(alpha)), null);
            drawTex.Invoke(null, new object[] { r, Texture2D.whiteTexture });
            color.SetValue(null, old, null);
        }
        public static void Fill(Rect r, Color c)
        {
            if (drawTex == null || color == null) return;
            if (fTex != null && fGetColor != null) { var o0 = fGetColor(); fSetColor(c); fTex(r, Texture2D.whiteTexture); fSetColor(o0); return; }
            var old = (Color)color.GetValue(null, null);
            color.SetValue(null, c, null);
            drawTex.Invoke(null, new object[] { r, Texture2D.whiteTexture });
            color.SetValue(null, old, null);
        }
        public static void TextAt(Rect r, string t) { if (fLabel != null) fLabel(r, Lang.T(t)); else guiLabel.Invoke(null, new object[] { r, Lang.T(t) }); }
    }
}

// ====================================================================================================
// Abschnitt: Core/Keys.cs
// ====================================================================================================
namespace UboatToolkit
{
    using global::System;
    using global::UnityEngine;

    // Free key binding: every hotkey of the mod is a setting like "Ctrl+Shift+R" (tab "Tasten", INI section [Tasten]).
    // In the F10 menu: "Ändern" -> press the new key (with Ctrl/Shift/Alt). Esc = cancel, Backspace = no key.
    public static class Keys
    {
        public static Opt Capturing;

        private class Parsed { public string Text; public bool Ok, C, S, A; public KeyCode Code; }
        private static readonly System.Collections.Generic.Dictionary<string, Parsed> cache = new System.Collections.Generic.Dictionary<string, Parsed>();

        public static bool Down(string key)
        {
            var o = Settings.Get(key);
            if (o == null || string.IsNullOrEmpty(o.Text) || Capturing != null) return false;
            if (!cache.TryGetValue(key, out var k) || k.Text != o.Text)
            {
                k = new Parsed { Text = o.Text };
                k.Ok = Parse(o.Text, out k.Code, out k.C, out k.S, out k.A);
                cache[key] = k;
            }
            if (!k.Ok || !Input.GetKeyDown(k.Code)) return false;
            if (!(k.C == Ctrl && k.S == Shift && k.A == Alt)) return false;
            if (Typing) return false;
            return Owns(o, k);
        }

        // 0.4.28 (audit: hotkeys fired while typing in the F10 search field): a focused Toolkit text field blocks all hotkeys.
        public static bool Typing
        {
            get
            {
                if (typingFrame == Time.frameCount) return typing;
                typingFrame = Time.frameCount; typing = false;
                try
                {
                    if (kbControl == null) kbControl = Type.GetType("UnityEngine.GUIUtility, UnityEngine.IMGUIModule")?.GetProperty("keyboardControl", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (kbControl != null) typing = (int)kbControl.GetValue(null, null) != 0;
                }
                catch { }
                return typing;
            }
        }
        private static System.Reflection.PropertyInfo kbControl; private static int typingFrame = -1; private static bool typing;

        // 0.4.28 single owner per key combination (audit: Ctrl+Shift+F was "Fernschuss" AND "20 m tiefer", Ctrl+Shift+R
        // "Rohre laden" AND "20 m höher"; both fired). The first option in menu order with a combination owns it;
        // every other option with the same combination does nothing and is named in the log once.
        private static readonly System.Collections.Generic.HashSet<string> dupLogged = new System.Collections.Generic.HashSet<string>();
        // While "Kurs- und Tiefen-Tasten" is on, that part owns its keys (F, R, P, C, arrows with Ctrl/Shift). Any other
        // Toolkit hotkey on the same key does nothing then (audit: turns and depth were done twice, turn-about cancelled itself).
        private static readonly string[] CourseOwned = { "KeyCourseDeeper", "KeyCourseShallower", "KeyCoursePeriscope", "KeyCourseMaxDepth", "KeyCoursePort", "KeyCourseStarboard", "KeyCourseAbout", "KeyCourseAmidships" };
        private static bool Owns(Opt o, Parsed k)
        {
            if (Settings.B("CourseKeys") && !o.Key.StartsWith("KeyCourse", StringComparison.Ordinal))
                foreach (var ck in CourseOwned)
                    if (Code(ck, KeyCode.None) == k.Code && k.Code != KeyCode.None)
                    {
                        if (dupLogged.Add(o.Key + "|course")) Util.Log("Tasten: " + o.Text + " („" + o.Label + "“) ruht, solange „Kurs- und Tiefen-Tasten“ an ist. Diese Taste gehört dort zu „" + (Settings.Get(ck)?.Label ?? ck) + "“.");
                        return false;
                    }
            foreach (var other in Settings.All)
            {
                if (!other.IsKey || string.IsNullOrEmpty(other.Text)) continue;
                if (ReferenceEquals(other, o)) return true;
                if (!Parse(other.Text, out var c2, out var a, out var b, out var d) || c2 != k.Code || a != k.C || b != k.S || d != k.A) continue;
                if (dupLogged.Add(o.Key + "|" + other.Key + "|" + o.Text))
                {
                    Util.Log("Tasten: " + o.Text + " ist doppelt belegt. Es gilt nur „" + other.Label + "“, nicht „" + o.Label + "“. Bitte eine der beiden im Menü neu belegen.");
                    UboatToolkit.ToolkitRunner.ShowNotice("Taste " + o.Text + " doppelt belegt: gilt nur für „" + other.Label + "“", 6f);
                }
                return false;
            }
            return true;
        }

        // True while the bound key is held (modifier keys like LeftShift may be bound alone).
        public static bool Held(string key)
        {
            var o = Settings.Get(key);
            if (o == null || string.IsNullOrEmpty(o.Text) || Capturing != null || Typing) return false;
            if (Enum.TryParse<KeyCode>(o.Text.Trim(), true, out var single)) return Input.GetKey(single);
            // combos: the named modifiers must be held too (else Ctrl+Alt+PageUp would also fire on plain PageUp)
            return Parse(o.Text, out var code, out var c, out var sh, out var a) && Input.GetKey(code) && (!c || Ctrl) && (!sh || Shift) && (!a || Alt);
        }

        // The bound key as a KeyCode (single key names like "LeftAlt" or "Mouse3" allowed).
        public static KeyCode Code(string key, KeyCode fallback)
        {
            var o = Settings.Get(key);
            if (o == null || string.IsNullOrEmpty(o.Text)) return KeyCode.None;
            if (Enum.TryParse<KeyCode>(o.Text.Trim(), true, out var single)) return single;
            return Parse(o.Text, out var code, out _, out _, out _) ? code : fallback;
        }

        // 0.4.29 (audit: menu key "Ctrl+Alt+M" also fired on plain M): the modifiers named in the binding must be held
        // (more may be held, so the menu still closes while Shift is down).
        public static bool ModsHeld(string key)
        {
            var o = Settings.Get(key);
            if (o == null || string.IsNullOrEmpty(o.Text)) return false;
            if (Enum.TryParse<KeyCode>(o.Text.Trim(), true, out _)) return true;
            return Parse(o.Text, out _, out var c, out var sh, out var a) && (!c || Ctrl) && (!sh || Shift) && (!a || Alt);
        }
        // A modifier key bound alone (station cursor on LeftAlt) counts only when it was pressed and released with
        // no other key in between; before, every Alt combination toggled the cursor too.
        private static KeyCode loneKey = KeyCode.None; private static bool loneClean;
        public static bool LoneTap(KeyCode k)
        {
            if (k == KeyCode.None) return false;
            if (!IsModifier(k)) return Input.GetKeyDown(k);
            if (Input.GetKeyDown(k)) { loneKey = k; loneClean = true; return false; }
            if (loneKey != k) return false;
            if (Input.GetKey(k)) { if (Input.anyKeyDown) loneClean = false; return false; }
            bool ok = Input.GetKeyUp(k) && loneClean;
            loneKey = KeyCode.None;
            return ok;
        }

        public static bool Ctrl => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        public static bool Shift => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        public static bool Alt => Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt) || Input.GetKey(KeyCode.AltGr);

        public static bool Parse(string text, out KeyCode code, out bool ctrl, out bool shift, out bool alt)
        {
            code = KeyCode.None; ctrl = shift = alt = false;
            foreach (var raw in text.Split('+'))
            {
                var p = raw.Trim();
                if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || p.Equals("Strg", StringComparison.OrdinalIgnoreCase)) ctrl = true;
                else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase) || p.Equals("Umschalt", StringComparison.OrdinalIgnoreCase)) shift = true;
                else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase)) alt = true;
                else if (!Enum.TryParse(p, true, out code)) return false;
            }
            return code != KeyCode.None;
        }

        // Called every frame by the runner while a key is being chosen in the menu.
        public static void CaptureFrame()
        {
            if (Capturing == null) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Capturing = null; return; }
            if (Input.GetKeyDown(KeyCode.Backspace)) { Capturing.Text = ""; Capturing = null; return; }
            foreach (KeyCode k in Enum.GetValues(typeof(KeyCode)))
            {
                if (k == KeyCode.None || k >= KeyCode.Mouse0 || IsModifier(k) || !Input.GetKeyDown(k)) continue;
                Capturing.Text = (Ctrl ? "Ctrl+" : "") + (Shift ? "Shift+" : "") + (Alt ? "Alt+" : "") + k;
                Capturing = null;
                return;
            }
        }

        private static bool IsModifier(KeyCode k) =>
            k == KeyCode.LeftControl || k == KeyCode.RightControl || k == KeyCode.LeftShift || k == KeyCode.RightShift ||
            k == KeyCode.LeftAlt || k == KeyCode.RightAlt || k == KeyCode.AltGr || k == KeyCode.LeftCommand || k == KeyCode.RightCommand;
    }
}

// ====================================================================================================
// Abschnitt: Core/Lang.cs
// ====================================================================================================
namespace UboatToolkit
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Linq;
    using global::UnityEngine;

    // Language of the mod's own texts. Setting "Language": 0 = like the game (German game = German,
    // every other language = English), 1 = German, 2 = English. The texts in the code are German;
    // LangEn holds the English version of each. Texts built from several pieces (e.g. "Tiefe " + 50)
    // are translated piece by piece.
    public static class Lang
    {
        private static Dictionary<string, string> en;
        private static string[] pieces;
        private static readonly Dictionary<string, string> cache = new Dictionary<string, string>();
        private static bool gameGerman, gameKnown;
        private static float nextCheck;

        public static bool English
        {
            get
            {
                int v = Mathf.RoundToInt(Settings.F("Language"));
                if (v == 1) return false;
                if (v == 2) return true;
                if (!gameKnown || Time.unscaledTime >= nextCheck) CheckGame();
                return !gameGerman;
            }
        }

        private static void CheckGame()
        {
            nextCheck = Time.unscaledTime + 5f;
            try
            {
                var loc = DWS.Common.InjectionFramework.ScriptableObjectSingleton.LoadSingleton<UBOAT.Game.Core.Data.Locale>();
                string l = loc?.Language;
                if (string.IsNullOrEmpty(l)) return;
                l = l.ToLowerInvariant();
                gameGerman = l.Contains("german") || l.Contains("deutsch") || l == "de";
                gameKnown = true;
            }
            catch { }
        }

        public static string T(string s)
        {
            if (string.IsNullOrEmpty(s) || !English) return s;
            if (en == null)
            {
                en = LangEn.Build();
                pieces = en.Keys.Where(k => k.Trim().Length >= 3).OrderByDescending(k => k.Length).ToArray();
            }
            if (en.TryGetValue(s, out var r)) return r;
            if (cache.TryGetValue(s, out r)) return r;
            r = s;
            foreach (var p in pieces)
                if (r.IndexOf(p, StringComparison.Ordinal) >= 0) r = r.Replace(p, en[p]);
            if (cache.Count > 4000) cache.Clear();
            cache[s] = r;
            return r;
        }
    }
}

// ====================================================================================================
// Abschnitt: Core/LangEn.cs
// ====================================================================================================
namespace UboatToolkit
{
    using global::System.Collections.Generic;

    // English texts for the mod (generated from the German source texts). Key = German text.
    internal static class LangEn
    {
        public static Dictionary<string, string> Build()
        {
            var m = new Dictionary<string, string>();
            m["Ctrl+"] = "Ctrl+";
            m["Shift+"] = "Shift+";
            m["Tasten"] = "Keys";
            m["Vorgeflutet: Crew darf an Deckgeschütz + Flak (#24)"] = "Decks awash: crew may use deck gun + AA (#24)";
            m["Funk: so viel schneller senden (1 = normal) (#11)"] = "Radio: send this much faster (1 = normal) (#11)";
            m["Funk: Gegner kann dich nicht anpeilen (#12)"] = "Radio: enemy cannot direction-find you (#12)";
            m["Funk: alle Funksprüche lesbar (auch fremd verschlüsselte)"] = "Radio: all messages readable (also foreign-encrypted)";
            m["Geschütze: Schiffe und Flugzeuge zugleich angreifen (88/105 zuerst Schiffe, Flak zuerst Flugzeuge)"] = "Guns: attack ships and aircraft together (88/105 prefer ships, AA prefers aircraft)";
            m["Geschütze: Feuermodus bleibt, wenn ein Kanonier (neu) ans Geschütz geht (sonst stellt das Spiel auf Feuer einstellen)"] = "Guns: keep fire mode when a gunner (re)starts at a gun (otherwise the game sets hold fire)";
            m["Meeresboden in der Nähe immer voll laden (auch tiefer als 650 m, für Sicht + Sonar/Tiefenmesser)"] = "Always fully load the nearby sea floor (also deeper than 650 m, for view + sonar/depth gauge)";
            m["  Meeresboden: Radius in km"] = "  Sea floor: radius in km";
            m["Licht automatisch umschalten (#15)"] = "Switch lights automatically (#15)";
            m["  aufgetaucht Rotlicht"] = "  red light when surfaced";
            m["  getaucht Blaulicht"] = "  blue light when submerged";
            m["Suchscheinwerfer automatisch (nachts, aufgetaucht) (#16)"] = "Searchlight automatic (night, surfaced) (#16)";
            m["Luken automatisch schließen (Tauchen / gejagt) (#22)"] = "Close hatches automatically (diving / hunted) (#22)";
            m["Sehrohr + Fernglas: mehr Zoom (1 = normal) (#18)"] = "Periscope + binoculars: more zoom (1 = normal) (#18)";
            m["Sehrohr + Fernglas: Ruhe (0 = Spiel, 1 = ganz ruhig) (#19)"] = "Periscope + binoculars: steadiness (0 = game, 1 = fully steady) (#19)";
            m["Sehrohr: max. Ausfahrhöhe in m (0 = Spielwert) (#20)"] = "Periscope: max. extension height in m (0 = game value) (#20)";
            m["Matrosen dürfen an die Kartenstation (#14)"] = "Sailors may man the map station (#14)";
            m["Crew läuft durcheinander durch (#21)"] = "Crew walks through each other (#21)";
            m["Ballast: aufgetaucht Tanks mit Diesel-Abgas leerblasen (spart Pressluft) (#23)"] = "Ballast: surfaced, blow tanks with diesel exhaust (saves compressed air) (#23)";
            m["  Diesel-Ausblasen: % Tank pro Sekunde"] = "  Diesel blowing: % of tank per second";
            m["Ballast: schneller fluten beim Tauchen (1 = normal) (#23)"] = "Ballast: flood faster when diving (1 = normal) (#23)";
            m["Verletzte nicht automatisch wegtragen"] = "Do not carry the wounded away automatically";
            m["Verletzte/Kranke automatisch versorgen (Erste Hilfe) (#29)"] = "Treat wounded/sick automatically (first aid) (#29)";
            m["  Sekunden warten, bevor ein freier Mann hilft"] = "  Seconds to wait before a free man helps";
            m["  Helfer holt sich selbst einen Verbandskasten aus dem Lager"] = "  Helper fetches a first aid kit from storage himself";
            m["  Skills beachten: Sanitäts-Ausbildung, Funker, Läufer zuerst"] = "  Respect skills: medical training, radioman, runner first";
            m["Schadenskontrolle automatisch: bei Alarm + Schaden (#30)"] = "Damage control automatic: on alarm + damage (#30)";
            m["  So viele Offiziere schalten um"] = "  This many officers switch over";
            m["  Skills beachten: Handwerker + Sparsam zuerst, Motor-Spezialist bleibt"] = "  Respect skills: craftsman + thrifty first, engine specialist stays";
            m["  Auch der Funker darf umschalten (sonst nur Ingenieure)"] = "  The radioman may switch too (otherwise only engineers)";
            m["Werkstatt automatisch: Schrott wird eingeräumt, Ersatzteile werden gebaut (#31)"] = "Workshop automatic: scrap gets stored, spare parts get built (#31)";
            m["Zeitraffer stoppt, bevor das Boot auf Land fährt (#32)"] = "Time compression stops before the boat runs aground (#32)";
            m["  Warnzeit in Sekunden (echte Zeit) vor dem Land"] = "  Warning time in seconds (real time) before land";
            m["  Zeitraffer dann begrenzen auf x (1 = ganz aus)"] = "  Then limit time compression to x (1 = fully off)";
            m["  nur warnen, wenn Land näher als (km)"] = "  only warn when land is closer than (km)";
            m["Tiefenruder immer besetzt: freier Matrose geht hin (#34)"] = "Depth rudders always manned: a free sailor goes there (#34)";
            m["Kartentisch immer besetzt (spart Diesel): freier Matrose geht hin (#35)"] = "Chart table always manned (saves diesel): a free sailor goes there (#35)";
            m["  Sekunden leer, bevor jemand geschickt wird"] = "  Seconds empty before someone is sent";
            m["Echolot als Matrosen-Aufgabe: unter Wasser geht ein freier Matrose hin und pingt"] = "Echo sounder as a sailor task: underwater a free sailor goes there and pings";
            m["  Bei Alarm nicht pingen (verrät sonst das Boot)"] = "  Do not ping on alarm (it gives the boat away)";
            m["  Alle x Sekunden (Spielzeit läuft im Zeitraffer schneller)"] = "  Every x seconds (game time runs faster with time compression)";
            m["Deckgeschütz + Flak automatisch besetzen (aufgetaucht + Alarm)"] = "Man deck gun + AA automatically (surfaced + alarm)";
            m["Maschinenraum automatisch besetzen, wenn ein Motor läuft"] = "Man the engine room automatically when an engine is running";
            m["Horcher automatisch besetzen (getaucht oder bei Alarm)"] = "Man the hydrophone automatically (submerged or on alarm)";
            m["Funkraum automatisch besetzen (wenn das Funkgerät empfangen kann)"] = "Man the radio room automatically (when the radio can receive)";
            m["Matrosen pro Offizier automatisch: alle Matrosen an Bord passen in die Gruppen (kein 10/6 mehr)"] = "Sailors per officer automatic: all sailors aboard fit into the groups (no more 10/6)";
            m["Matrosen pro Offizier höchstens (0 = Spiel-Wert)"] = "Sailors per officer at most (0 = game value)";
            m["Offiziere dürfen alle freien Matrosen mitnehmen (ohne 60-%-Grenze)"] = "Officers may take all free sailors (without the 60 % limit)";
            m["Kein Stress in der Crew"] = "No stress in the crew";
            m["Erfolge (Achievements) immer erlaubt, auch nach Konsolen-Befehlen"] = "Achievements always allowed, even after console commands";
            m["Lüftung: Lautstärke/Geräusch x (1 = normal) (#36)"] = "Ventilation: volume/noise x (1 = normal) (#36)";
            m["  Absorber (Kalipatrone) hält x länger"] = "  Absorber (potash cartridge) lasts x longer";
            m["  E-Kompressor verbraucht keine Luft im Boot"] = "  E-compressor uses no air inside the boat";
            m["  Unter Wasser: Lüftung automatisch an + Absorber einsetzen"] = "  Underwater: ventilation on automatically + insert absorber";
            m["  Notfall-Sauerstoff: etwas mehr als der Verbrauch, wenn die Luft sinkt"] = "  Emergency oxygen: a little more than consumption when the air drops";
            m["Volle Besatzung ab Start (vor der ersten Feindfahrt) (#37)"] = "Full crew from the start (before the first patrol) (#37)";
            m["  Gewünschte Besatzung (Männer, in der INI ohne Obergrenze)"] = "  Wanted crew size (men, no upper limit in the INI)";
            m["Eindeutige Crew-Namen: neue Männer bekommen einen Nachnamen, den noch keiner an Bord hat (größere Namensliste)"] = "Unique crew names: new men get a last name nobody aboard has yet (bigger name list)";
            m["  >> Doppelte Namen jetzt ändern (einmal, für die jetzige Crew)"] = "  >> Change duplicate names now (once, for the current crew)";
            m["Zeitraffer stoppt bei neuem Schiff-Kontakt (Idee 1)"] = "Time compression stops on new ship contact (idea 1)";
            m["Zeitraffer stoppt bei Flugzeug (Idee 1)"] = "Time compression stops on aircraft (idea 1)";
            m["Schadensbericht: was hat uns getroffen, wo und warum (F10-Reiter \"Schaden\" + Datei)"] = "Damage report: what hit us, where and why (F10 tab \"Schaden\" + file)";
            m["  bei jedem Treffer kurze Meldung im Spiel"] = "  short message in game on every hit";
            m["  gleiche Ursache innerhalb x Sekunden zusammenfassen"] = "  merge the same cause within x seconds";
            m["  bei Treffer das Spiel pausieren"] = "  pause the game on a hit";
            m["Unter Beschuss: 0 aus, 1 fragen \"Herr Kaleun, was tun wir?\", 2 Vorschlag automatisch ausführen"] = "Under fire: 0 off, 1 ask \"Herr Kaleun, what do we do?\", 2 carry out suggestion automatically";
            m["  beim Fragen pausieren (aus = nur Symbol rechts am Rand)"] = "  pause when asking (off = only icon at the right edge)";
            m["  Knopf \"ignorieren\": so viele Minuten (Meldungen werden gesammelt)"] = "  Button \"ignore\": this many minutes (messages are collected)";
            m["  gleicher Gegner erst wieder nach x Sekunden melden"] = "  report the same enemy again only after x seconds";
            m["  nur melden, wenn die Ausguck den Schützen sehen"] = "  only report when the lookouts see the shooter";
            m["Zeitraffer stoppt bei Schaden (Treffer, neues Leck)"] = "Time compression stops on damage (hit, new leak)";
            m["Zeitraffer stoppt bei Funkspruch"] = "Time compression stops on radio message";
            m["  Zeitraffer ignoriert Fischerboote"] = "  time compression ignores fishing boats";
            m["  Zeitraffer ignoriert Verbündete (grün)"] = "  time compression ignores allies (green)";
            m["  Zeitraffer ignoriert Neutrale"] = "  time compression ignores neutrals";
            m["Zurück ins Wasser: festgefahren an Land -> Boot an letzte sichere Stelle"] = "Back into the water: stuck on land -> boat to the last safe spot";
            m["  So viele Sekunden festgefahren, bevor es passiert"] = "  This many seconds stuck before it happens";
            m["  >> Jetzt zurück ins Wasser"] = "  >> Back into the water now";
            m["Auto-Speichern alle x Minuten (0 = aus) (Idee 6)"] = "Auto-save every x minutes (0 = off) (idea 6)";
            m["Warnung + Zeitraffer-Stopp bei Diesel/Batterie/Luft unter x % (0 = aus) (Idee 8)"] = "Warning + time compression stop at diesel/battery/air below x % (0 = off) (idea 8)";
            m["Karte: Ankunftszeit + Restweg + Diesel-Reichweite oben links (Idee 7)"] = "Map: arrival time + remaining distance + diesel range top left (idea 7)";
            m["Tiefe per Taste (Tasten im Tab „Tasten“) (Idee 10)"] = "Depth by key (keys in tab „Keys“) (idea 10)";
            m["Fix: Fehler \"Default Tooltip ... GUI\" (Tooltip nicht unnötig umhängen)"] = "Fix: error \"Default Tooltip ... GUI\" (do not re-parent tooltip needlessly)";
            m["Fix: Fehler \"Requested asset Icons/Skills/...\" (fehlende Skill-Bilder)"] = "Fix: error \"Requested asset Icons/Skills/...\" (missing skill images)";
            m["Fix: Fehler in FlamesRegistry (Feuer an abgeschossenen Schiffsteilen)"] = "Fix: error in FlamesRegistry (fire on shot-off ship parts)";
            m["Fix: Fehler \"Pooled instance ... enabled animators\" (z. B. Zeitungsständer)"] = "Fix: error \"Pooled instance ... enabled animators\" (e.g. newspaper stand)";
            m["  Tiefen-Schritt in m (Shift = x10, Alt = x100, auch Mausrad an der Tiefenanzeige)"] = "  Depth step in m (Shift = x10, Alt = x100, also mouse wheel on the depth gauge)";
            m["Eigene Tiefenanzeige ohne 260-m-Grenze (Doppelklick/Mausrad setzt Zieltiefe)"] = "Own depth gauge without the 260 m limit (double click/mouse wheel sets target depth)";
            m["  Skala passt sich an (flaches Wasser = kurze Skala)"] = "  Scale adapts (shallow water = short scale)";
            m["  Skala höchstens bis (m)"] = "  Scale goes up to at most (m)";
            m["  Position von links (% vom Bildschirm, 100 = ganz rechts am Rand)"] = "  Position from left (% of screen, 100 = right edge)";
            m["  Position oben (% vom Bildschirm)"] = "  Position from top (% of screen)";
            m["  Verlauf über x Sekunden (echte Zeit)"] = "  History over x seconds (real time)";
            m["Bodenabstand-Autopilot: hält Abstand zum Meeresgrund"] = "Bottom-distance autopilot: keeps distance to the seabed";
            m["  Wasser unter dem Kiel (m)"] = "  Water under the keel (m)";
            m["  Voraus schauen (m)"] = "  Look ahead (m)";
            m["  Nie flacher als (m)"] = "  Never shallower than (m)";
            m["  Nie tiefer als (m)"] = "  Never deeper than (m)";
            m["  Aufsetzen: sanft auf den Grund legen + Schleichfahrt"] = "  Bottoming: settle gently on the seabed + silent running";
            m["  >> Alle leeren Rohre aus dem Lager laden (Idee 2)"] = "  >> Load all empty tubes from storage (idea 2)";
            m["  >> Wachen tauschen: Gruppen mit gleicher Aufgabe tauschen Schichten (Idee 4)"] = "  >> Swap watches: groups with the same job swap shifts (idea 4)";
            m["  >> Lager aufräumen: gleiche Sachen stapeln, nach vorne rücken (Idee 5)"] = "  >> Tidy storage: stack identical items, move them forward (idea 5)";
            m["  >> Nächste Milchkuh zu mir holen"] = "  >> Bring the nearest milk cow to me";
            m["Ballast-Befehle immer verfügbar (außer Ventil kaputt / Hafen)"] = "Ballast orders always available (except broken valve / port)";
            m["Luken zeigen Luftdruck beider Räume + sperren bei großem Unterschied"] = "Hatches show air pressure of both rooms + lock at a big difference";
            m["  Sperre ab Druck-Unterschied (bar, 0 = nie sperren)"] = "  Lock from pressure difference (bar, 0 = never lock)";
            m["  Druckausgleich: Luft strömt langsam nach, bis die Luke wieder aufgeht"] = "  Pressure equalisation: air slowly flows over until the hatch opens again";
            m["  Druckausgleich: % vom Unterschied pro Sekunde"] = "  Pressure equalisation: % of the difference per second";
            m["  Lüftung an: Druckausgleich x schneller (1 = kein Bonus)"] = "  Ventilation on: pressure equalisation x faster (1 = no bonus)";
            m["Schnorchel fährt automatisch aus auf Sehrohrtiefe (1-10 m) und wieder ein"] = "Snorkel extends automatically at periscope depth (1-10 m) and retracts";
            m["  Schnorchel oben: Lüftung an + Luftdruck in allen Räumen schnell auf Normaldruck"] = "  Snorkel up: ventilation on + air pressure in all rooms quickly back to normal";
            m["Alarmtauchen automatisch, wenn ein feindliches Flugzeug gesichtet wird"] = "Crash dive automatically when an enemy aircraft is spotted";
            m["Knopf \u201eHafen verlassen\u201c direkt nach dem Ablegen (nur offizieller Ausfahrt-Punkt)"] = "Button „Leave port“ right after casting off (only official exit point)";
            m["Patrouillen-Auftrag: Tonnage z\u00e4hlt schon auf dem Weg ins Gebiet"] = "Patrol order: tonnage already counts on the way to the area";
            m["Karte: genaue Wassertiefe unter der Maus anzeigen"] = "Map: show exact water depth under the mouse";
            m["H\u00e4fen haben eine gleichm\u00e4\u00dfige Mischung aller Torpedo-Typen"] = "Ports have an even mix of all torpedo types";
            m["Meeresboden immer laden, wenn das Wasser flacher als ~300 m ist (nur Reise-System \u201eRealistisch\u201c)"] = "Always load the seabed when the water is shallower than ~300 m (only travel system „Realistic“)";
            m["Realistische Sprungschicht (25-150 m je nach Wassertemperatur, darunter schwerer zu orten)"] = "Realistic thermocline (25-150 m depending on water temperature, harder to detect below)";
            m["  Unter der Sprungschicht h\u00f6rt der eigene Horcher Schiffe oben schlechter"] = "  Below the thermocline your own hydrophone hears ships above worse";
            m["  Kurze Meldung beim Unter-/\u00dcberqueren der Sprungschicht"] = "  Short message when passing below/above the thermocline";
            m["Fix: Gegenstände mit gesperrter Kollisionsform (Bin, Clock, Folder) zählen wieder, keine rote Meldung"] = "Fix: items with a locked collision shape (Bin, Clock, Folder) count again, no red message";
            m["Fix: Matrosen, die neben dem Laufweg feststecken, werden zurückgesetzt"] = "Fix: sailors stuck next to the walkway get reset";
            m["Fix: Fehler \"Unrecognized mode\" beim Umschalten des Geschütz-Ziels"] = "Fix: error \"Unrecognized mode\" when switching the gun target";
            m["Fix: Matrosen finden Ziele knapp neben dem Laufweg (bis 5 m statt 1,5 m)"] = "Fix: sailors find targets just next to the walkway (up to 5 m instead of 1.5 m)";
            m["Neue Karriere: Einf\u00fchrungs-Tutorial \u00fcberspringen"] = "New career: skip the introduction tutorial";
            m["Fehler-Fix: Einblend-Effekt von Figuren (z. B. Hafenarbeiter) beim Ein-/Ausschalten nicht mehr falsch umh\u00e4ngen"] = "Error fix: fade effect of figures (e.g. dock workers) no longer re-parented wrongly when switched on/off";
            m["Pause ohne Unsch\u00e4rfe und ohne dunkle Einblendung"] = "Pause without blur and without dark overlay";
            m["Entwickler-Konsole nicht automatisch öffnen (Fehler landen trotzdem im Log)"] = "Do not open the developer console automatically (errors still go to the log)";
            m["Beenden wartet, bis der Spielstand fertig geschrieben ist (sonst kaputter Spielstand)"] = "Quitting waits until the save is fully written (else a broken save)";
            m["Torpedos bleiben gewartet (Wartung läuft nicht ab, Crew muss nicht nachwarten)"] = "Torpedoes stay maintained (maintenance does not expire, crew need not redo it)";
            m["Schnittansicht rutscht nicht weg, wenn die Maus den Bildschirmrand berührt"] = "Cutaway view does not slide away when the mouse touches the screen edge";
            m["Kein Sprung zur Deck-Ansicht am oberen/unteren Bildschirmrand"] = "No jump to the deck view at the top/bottom screen edge";
            m["Fix: Konvois mit Flugzeugtr\u00e4ger fahren normal (geparkte Flugzeuge bremsen nicht)"] = "Fix: convoys with an aircraft carrier move normally (parked aircraft do not slow them)";
            m["Fix: kein Ruckeln beim Zoomen von Sehrohr/UZO (Bilder bleiben geladen)"] = "Fix: no stutter when zooming periscope/UZO (images stay loaded)";
            m["  Overlays geladen halten"] = "  keep overlays loaded";
            m["  bekannte Overlays im Hintergrund vorladen"] = "  preload known overlays in the background";
            m["  UZO-Zoom (Zusatz-Mod) nur auffrischen statt neu starten"] = "  UZO zoom (add-on mod) only refresh instead of restart";
            m["  höchstens ... MB festhalten"] = "  hold at most ... MB";
            m["Alle Uhren zeigen Berliner Zeit (keine Zeitzonen-Meldungen)"] = "All clocks show Berlin time (no time zone messages)";
            m["Fix: kein Tempo-/Reichweiten-Verlust bei hohem Zeitraffer (lernt Werte bei normaler Zeit)"] = "Fix: no speed/range loss at high time compression (learns values at normal time)";
            m["Fix: Kommandant tot/verwundet \u2192 Warnung vor dem Hafen + keine Gespr\u00e4che, die h\u00e4ngen"] = "Fix: commander dead/wounded → warning before port + no conversations that hang";
            m["Fix: Hafen-Gespr\u00e4che h\u00e4ngen nicht mehr fest"] = "Fix: port conversations no longer get stuck";
            m["Fix: vergessene Belohnungen von Funk-Auftr\u00e4gen werden ausgezahlt"] = "Fix: forgotten rewards from radio orders get paid out";
            m["Karte: Raster-Beschriftung auf jedem Kartenstil lesbar + Stil \u201eEigene Farben\u201c (Farben im Tab Karte)"] = "Map: grid labels readable on every map style + style „Own colours“ (colours in tab Map)";
            m["Karte: zus\u00e4tzlicher Kartenstil \u201eElfenbein\u201c"] = "Map: extra map style „Ivory“";
            m["Fadenkreuz-Licht: Helligkeit (1 = Spiel, gilt wenn „eigene Farbe“ aus)"] = "Reticle light: brightness (1 = game, applies when „own colour“ is off)";
            m["Fadenkreuz-Licht: eigene Farbe, Helligkeit pro Optik, Peilskala-Ausgleich"] = "Reticle light: own colour, brightness per optic, bearing scale compensation";
            m["  Farbe Rot (0-255)"] = "  Colour red (0-255)";
            m["  Farbe Grün (0-255)"] = "  Colour green (0-255)";
            m["  Farbe Blau (0-255)"] = "  Colour blue (0-255)";
            m["  Helligkeit 0-100 (20 = Spiel, 60 = 4-fach, 100 = 16-fach)"] = "  Brightness 0-100 (20 = game, 60 = 4x, 100 = 16x)";
            m["  Faktor Angriffs-Sehrohr"] = "  Factor attack periscope";
            m["  Faktor Beobachtungs-Sehrohr"] = "  Factor observation periscope";
            m["  Faktor UZO"] = "  Factor UZO";
            m["  Faktor Fernglas"] = "  Factor binoculars";
            m["  Peilskala-Helligkeit (1 = gleich hell wie das Fadenkreuz)"] = "  Bearing scale brightness (1 = as bright as the reticle)";
            m["  Zahlen der Peilskala mit beleuchten"] = "  Light up the bearing scale numbers too";
            m["  schwache Masken angleichen (alle Optiken gleich hell)"] = "  Even out weak masks (all optics equally bright)";
            m["  Klick-Ton beim Umschalten"] = "  Click sound when switching";
            m["    Lautstärke"] = "    Volume";
            m["Deckgeschütz und Seegang (Streuung, Ladezeit, Sperre bei Sturm, feindliche Kanonen)"] = "Deck gun and sea state (spread, reload time, lock in storms, enemy guns)";
            m["  ruhig bis Windstärke (keine Strafe)"] = "  calm up to wind force (no penalty)";
            m["  volle Strafe ab Windstärke"] = "  full penalty from wind force";
            m["  Geschütz gesperrt ab Windstärke (Leuchtgranaten gehen immer)"] = "  Gun locked from wind force (star shells always work)";
            m["  Streuung bei voller Strafe (x)"] = "  Spread at full penalty (x)";
            m["  Ladezeit bei voller Strafe (x)"] = "  Reload time at full penalty (x)";
            m["  gilt für alle Schiffe (auch Feinde)"] = "  applies to all ships (enemies too)";
            m["  gilt auch für Flak"] = "  also applies to AA";
            m["  Handzielen bekommt dieselbe Streuung und schießt sich ein"] = "  Manual aiming gets the same spread and ranges in";
            m["  Feind-Kanonen: Streuung in % (100 = Spiel)"] = "  Enemy guns: spread in % (100 = game)";
            m["  Feind-Kanonen: kleinster Fehlschuss (m pro km)"] = "  Enemy guns: smallest miss (m per km)";
            m["  Feind-Kanonen: Einschießen in % (100 = Spiel)"] = "  Enemy guns: ranging in % (100 = game)";
            m["  C3 und Tanker schießen sich ein wie andere Frachter"] = "  C3 and tankers range in like other freighters";
            m["  Feind-Einstellungen auch für Kriegsschiffe"] = "  Enemy settings also for warships";
            m["  eigene Granaten können Feind-Kanonen ausschalten"] = "  Own shells can knock out enemy guns";
            m["    Schaden an der Kanone in %"] = "    Damage to the gun in %";
            m["    Radius in Metern"] = "    Radius in metres";
            m["Sehrohr-Anzeige-Fix (Ansicht, Zoom, Stations-Knopf)"] = "Periscope display fix (view, zoom, station button)";
            m["  Ansicht und Zoom je Sehrohr merken"] = "  Remember view and zoom per periscope";
            m["  Stations-Knopf führt zum zuletzt benutzten Sehrohr"] = "  Station button leads to the last used periscope";
            m["Regenkleidung für die Wache an Deck (eigene Steuerung statt Spiel)"] = "Rain gear for the deck watch (own control instead of the game)";
            m["  Modus: 0 = nur bei Regen, 1 = immer an Deck, 2 = aus"] = "  Mode: 0 = only in rain, 1 = always on deck, 2 = off";
            m["  Mantel ab Regen-Stärke (bei Modus 0)"] = "  Coat from rain strength (in mode 0)";
            m["  Südwester ab Regen-Stärke"] = "  Sou'wester from rain strength";
            m["  Südwester ab Windstärke"] = "  Sou'wester from wind force";
            m["  auch den Mann anziehen, durch den du schaust"] = "  also dress the man you are looking through";
            m["  Kommandant trägt auch Südwester"] = "  Commander wears a sou'wester too";
            m["  auch im Hafen/geschützt anziehen"] = "  also dress in port/sheltered";
            m["  Prüfen alle ... Sekunden"] = "  Check every ... seconds";
            m["Neue/bef\u00f6rderte Offiziere bekommen 6-Stunden-Wachen (halb Dienst, halb Ruhe)"] = "New/promoted officers get 6-hour watches (half duty, half rest)";
            m["  Wachlänge in Stunden (6 oder 3)"] = "  Watch length in hours (6 or 3)";
            m["Ich-Sicht: Luken flüssig durchqueren (Lauf-Gang bleibt, schneller)"] = "First person: pass hatches smoothly (running gait stays, faster)";
            m["  Geh- oder Lauf-Durchgang bis zum Ende halten"] = "  Keep walking or running through to the end";
            m["  Durchgang: 0 = wie gedrückt, 1 = immer gehen, 2 = immer laufen"] = "  Passage: 0 = as pressed, 1 = always walk, 2 = always run";
            m["  beim Rennen ruhige Geh-Animation nehmen (aber schnell)"] = "  When running use the calm walking animation (but fast)";
            m["  Tempo beim Rennen (x)"] = "  Speed when running (x)";
            m["  Tempo beim Gehen (x)"] = "  Speed when walking (x)";
            m["  höchstes Animations-Tempo"] = "  Highest animation speed";
            m["  nach der Luke weiterlaufen (Sekunden)"] = "  keep moving after the hatch (seconds)";
            m["Ansagen zum Lichtwechsel stumm"] = "Mute announcements about light changes";
            m["Ruhige Oberfläche an (Einstellungen darunter, Meldungs-Stufen in toolkit-notifications.txt)"] = "Quiet interface on (settings below, message levels in toolkit-notifications.txt)";
            m["  Crew-Namen nur beim Zeigen oder Auswählen"] = "  Crew names only when pointing or selecting";
            m["    Name beim Zeigen mit der Maus"] = "    Name when pointing with the mouse";
            m["    Name bei ausgewähltem Mann"] = "    Name of the selected man";
            m["  auch Geräte- und Stations-Namen verstecken"] = "  also hide device and station names";
            m["  Tätigkeits-Symbol und Fortschritt verstecken (gleiche Regel)"] = "  hide activity icon and progress (same rule)";
            m["  Ruhe-Balken verstecken (gleiche Regel)"] = "  hide rest bar (same rule)";
            m["  Ereignis-Ausrufezeichen verstecken"] = "  hide event exclamation marks";
            m["  Countdown (z. B. Blutung) verstecken"] = "  hide countdown (e.g. bleeding)";
            m["  Band oben in der Mitte und Ton stumm (Symbole bleiben, wichtige Ausnahmen bleiben laut)"] = "  Banner at the top centre and sound muted (icons stay, important exceptions stay loud)";
            m["  auch direkte Hinweise stumm („Spiel gespeichert“, Fehler beim Speichern ...)"] = "  also mute direct hints („Game saved“, errors while saving ...)";
            m["  Stimmen von Hafen-Personal und fremden Kapitänen stumm (Untertitel bleiben)"] = "  Mute voices of port staff and foreign captains (subtitles stay)";
            m["Fix: gew\u00e4hlte Sprache der Crew bleibt nach Updates erhalten"] = "Fix: chosen crew language stays after updates";
            m["  Stimme festhalten auf: 0 = wie gerade eingestellt, 1 = Deutsch, 2 = Englisch"] = "  Keep the voice at: 0 = as currently set, 1 = German, 2 = English";
            m["  kaputte „letzte Version“ reparieren (sonst neue Spielstände nicht ladbar)"] = "  repair broken „last version“ (otherwise new saves cannot be loaded)";
            m["Fix: kein Einfrieren, wenn Crew-Aussehen sich w\u00e4hrend Textur-Arbeit \u00e4ndert"] = "Fix: no freeze when crew appearance changes during texture work";
            m["Fix: Texturen werden etwas sp\u00e4ter gel\u00f6scht (weniger Abst\u00fcrze)"] = "Fix: textures get deleted a little later (fewer crashes)";
            m["Fix für Mod \"Supply Price Editor\": sucht das Hauptmenü nicht mehr jedes Bild (weniger Ruckeln auf See)"] = "Fix for mod \"Supply Price Editor\": no longer searches the main menu every frame (less stutter at sea)";
            m["Fix für Mod \"Realistic Uniforms Revival\": keine unnötigen Uniform-Neuaufbauten"] = "Fix for mod \"Realistic Uniforms Revival\": no needless uniform rebuilds";
            m["Fix für Mod \"Projekt Bifrost\": Anzeigen gedrosselt (weniger Ruckeln)"] = "Fix for mod \"Projekt Bifrost\": displays throttled (less stutter)";
            m["  Wasserbomben-Zähler-Fehler verhindern"] = "  prevent depth charge counter error";
            m["Fix für Mods mit fehlender Kleidung (z.B. \"Cap NCO\"): fehlendes Teil wird weggelassen statt Fehlermeldung"] = "Fix for mods with missing clothes (e.g. \"Cap NCO\"): the missing piece is left out instead of an error";
            m["Kamera: Mausrad-Zoom und weiche freie Kamera"] = "Camera: mouse wheel zoom and smooth free camera";
            m["  freie Kamera: Start-Blickwinkel"] = "  free camera: start field of view";
            m["  Ich-Sicht: engster Blickwinkel"] = "  first person: narrowest field of view";
            m["  freie Kamera: engster Blickwinkel"] = "  free camera: narrowest field of view";
            m["  freie Kamera: weitester Blickwinkel"] = "  free camera: widest field of view";
            m["  freie Kamera: Zoom pro Rad-Raste (%)"] = "  free camera: zoom per wheel notch (%)";
            m["  schneller Zoom mit Taste (x)"] = "  fast zoom with key (x)";
            m["  freie Kamera: weich anfahren und bremsen"] = "  free camera: smooth start and stop";
            m["    Anfahren (Sekunden)"] = "    Start (seconds)";
            m["    Bremsen (Sekunden)"] = "    Stop (seconds)";
            m["    Schräglage in Kurven (Grad)"] = "    Banking in turns (degrees)";
            m["    tief über dem Wasser nicht zu langsam (ab Meter)"] = "    low over the water not too slow (from metres)";
            m["    Umschauen glätten (Sekunden)"] = "    Smooth looking around (seconds)";
            m["  freie Kamera: Mauszeiger ausblenden"] = "  free camera: hide mouse cursor";
            m["  bei Zwischensequenzen nicht eingreifen"] = "  do not interfere during cutscenes";
            m["Stationen (Horcher, Vorhaltrechner, Torpedoraum ...): Zoom, Umschauen, Drehknöpfe"] = "Stations (hydrophone, TDC, torpedo room ...): zoom, look around, dials";
            m["  Umschauen: 0 = wie Ich-Sicht (Mitte-Punkt), 1 = Blick dreht zum Mauszeiger"] = "  Look around: 0 = like first person (centre dot), 1 = view turns towards the mouse cursor";
            m["  Umschau-Tempo (1 = wie Ich-Sicht)"] = "  Look-around speed (1 = like first person)";
            m["  wie weit die Ansicht gleiten darf"] = "  how far the view may slide";
            m["  wie weit die Ansicht drehen darf (Grad)"] = "  how far the view may turn (degrees)";
            m["  Zoom bis (Grad, kleiner = näher)"] = "  Zoom up to (degrees, smaller = closer)";
            m["  Horcher: 0 = gleiten, 1 = drehen"] = "  Hydrophone: 0 = slide, 1 = turn";
            m["  Vorhaltrechner: 0 = gleiten, 1 = drehen"] = "  TDC: 0 = slide, 1 = turn";
            m["  Torpedoraum/Feuerleitung: 0 = gleiten, 1 = drehen"] = "  Torpedo room/fire control: 0 = slide, 1 = turn";
            m["  Schusswinkel-Empfänger: 0 = gleiten, 1 = drehen"] = "  Firing angle receiver: 0 = slide, 1 = turn";
            m["  andere Stationen: 0 = gleiten, 1 = drehen"] = "  other stations: 0 = slide, 1 = turn";
            m["  Vorhaltrechner: Gleiten seitlich (x)"] = "  TDC: slide sideways (x)";
            m["  Vorhaltrechner: Gleiten hoch/runter (x)"] = "  TDC: slide up/down (x)";
            m["  Schusswinkel-Empfänger: Gleiten seitlich (x)"] = "  Firing angle receiver: slide sideways (x)";
            m["  Schusswinkel-Empfänger: Gleiten hoch/runter (x)"] = "  Firing angle receiver: slide up/down (x)";
            m["  Torpedoraum: Standpunkt nach vorn (m)"] = "  Torpedo room: viewpoint forward (m)";
            m["  Tooltips erst zeigen, wenn Mauszeiger frei ist"] = "  Show tooltips only when the mouse cursor is free";
            m["  Horcher-Anzeige erst zeigen, wenn Mauszeiger frei ist"] = "  Show hydrophone display only when the mouse cursor is free";
            m["  Drehknöpfe folgen der Maus direkt"] = "  Dials follow the mouse directly";
            m["  Drehknopf-Empfindlichkeit (Bildschirm-Anteil für ganzen Bereich)"] = "  Dial sensitivity (screen share for the whole range)";
            m["  mit Shift fein einstellen (x)"] = "  fine adjust with Shift (x)";
            m["  Lagewinkel-Knopf folgt beim Ziehen sofort"] = "  AOB dial follows immediately when dragging";
            m["  Lagewinkel-Knopf: Tempo (Grad/s, 0 = Spiel)"] = "  AOB dial: speed (degrees/s, 0 = game)";
            m["Kamera: getaucht bleibt die Au\u00dfenkamera unter Wasser (realistisch)"] = "Camera: when submerged the outside camera stays underwater (realistic)";
            m["  Abstand unter der Wasseroberfläche (m)"] = "  Distance below the water surface (m)";
            m["  Sehrohr gerade über Wasser: Kamera frei für ... Sekunden"] = "  Periscope just above water: camera free for ... seconds";
            m["  Nachführ-Stärke"] = "  Follow strength";
            m["  Nachführen (Grad pro Sekunde)"] = "  Follow (degrees per second)";
            m["  Freigeben (Grad pro Sekunde)"] = "  Release (degrees per second)";
            m["  Mausbewegung nach oben vorher abfangen"] = "  Catch upward mouse movement beforehand";
            m["Fix: Wolfsrudel geht nicht verloren, wenn das BdU gerade funkt"] = "Fix: wolfpack is not lost when BdU is transmitting";
            m["Wolfsrudel-Einstellungen an (Datum, Chance, Boote, Ziel-BRT unten)"] = "Wolfpack settings on (date, chance, boats, target GRT below)";
            m["  Rudel ab: 0 = Sep 1940, 1 = Jul 1940, 2 = Nov 1940, 3 = Jan 1941, 4 = 8. Mai 1941 (Spiel)"] = "  Packs from: 0 = Sep 1940, 1 = Jul 1940, 2 = Nov 1940, 3 = Jan 1941, 4 = 8 May 1941 (game)";
            m["  Chance, dass sich ein Rudel bildet, in %"] = "  Chance that a pack forms, in %";
            m["  höchstens x Boote pro Rudel"] = "  at most x boats per pack";
            m["  erstes Rudel-Ziel: x BRT"] = "  first pack target: x GRT";
            m["Spielst\u00e4nde schneller laden (Bild ruckelt beim Laden etwas mehr)"] = "Load saves faster (image stutters a bit more while loading)";
            m["  Rechenzeit pro Bild beim Laden (ms, Spiel = 16)"] = "  Computing time per frame while loading (ms, game = 16)";
            m["  Schritte pro Bild (Spiel = 500)"] = "  Steps per frame (game = 500)";
            m["  Lade-Warteschlange: so viele Aufträge darf der Hintergrund-Leser vorausarbeiten (40 = Spiel)"] = "  Loading queue: this many jobs the background reader may work ahead (40 = game)";
            m["  beim Laden kein Speicher-Aufräumen (spart Zeit)"] = "  no memory clean-up while loading (saves time)";
            m["    außer Speicher über ... MB"] = "    except memory above ... MB";
            m["Spielstand-Liste: Reiter je Karriere (Spielstände einer Karriere zusammen)"] = "Save list: tab per career (saves of one career together)";
            m["Ausguck: weite Sicht + bessere Meldungen (Hauptschalter, wirkt sofort)"] = "Lookout: long sight + better reports (main switch, works immediately)";
            m["  Ausguck: Suchweite für Schiffsgruppen (1 = Spiel, 1.69 = Standard)"] = "  Lookout: search range for ship groups (1 = game, 1.69 = default)";
            m["  Ausguck: Schornstein-Rauch am Horizont (Konvoi früher sichtbar, auch dein Boot auf Diesel)"] = "  Lookout: funnel smoke on the horizon (convoy visible earlier, also your boat on diesel)";
            m["  Ausguck: schlechte Sicht nur einmal zählen (Spiel zählt sie doppelt)"] = "  Lookout: count bad visibility only once (game counts it twice)";
            m["  Ausguck: Untergrenze für Nacht/Wetter (Konvoi auch in dunkler Nacht auffindbar)"] = "  Lookout: lower limit for night/weather (convoy findable even on dark nights)";
            m["  Ausguck: Nebel versteckt Schiffe (aus, wenn ein Wetter-Mod eigenen Nebel hat)"] = "  Lookout: fog hides ships (off if a weather mod has its own fog)";
            m["  Ausguck: bei klarem Tag Reichweite und Rauch etwas kürzen"] = "  Lookout: on clear days shorten range and smoke a bit";
            m["  Ausguck: Zeitraffer bremsen, solange ein ungesehenes Schiff in Reichweite ist"] = "  Lookout: slow time compression while an unseen ship is in range";
            m["  Ausguck: nachts Konvois etwas weiter finden (bis x1.5, je mehr Schiffe)"] = "  Lookout: find convoys a bit farther at night (up to x1.5, the more ships)";
            m["  Ausguck: verdrehten Sicht-Würfel richtig herum (klare Ziele eher melden)"] = "  Lookout: turn the twisted sight dice the right way round (report clear targets sooner)";
            m["  Ausguck: auch im Dunkeln gesehene Schiffe laut melden"] = "  Lookout: report ships seen in the dark out loud too";
            m["  Ausguck: bekannte Kontakte erneut melden, wenn wieder gesehen"] = "  Lookout: report known contacts again when seen again";
            m["  Ausguck: verschluckte Meldungen wiederholen, sobald jemand frei ist"] = "  Lookout: repeat swallowed reports as soon as someone is free";
            m["  Ausguck: vorgeflutet etwas Sicht behalten (getaucht nie)"] = "  Lookout: keep some sight when awash (never when submerged)";
            m["Nacht drau\u00dfen heller (%, 0 = Spiel)"] = "Night outside brighter (%, 0 = game)";
            m["Boot innen heller (%, 0 = Spiel)"] = "Boat interior brighter (%, 0 = game)";
            m["Optik-Belichtung pro Sehrohr/UZO/Fernglas (nimmt dem Beobachtungs-Sehrohr den Extra-Hellbonus)"] = "Optics exposure per periscope/UZO/binoculars (removes the extra brightness bonus of the observation periscope)";
            m["  Angriffs-Sehrohr Belichtung in EV (unter dem Spiel-Bild)"] = "  Attack periscope exposure in EV (below the game image)";
            m["  Beobachtungs-Sehrohr Belichtung in EV (0 = Extra-Bonus weg)"] = "  Observation periscope exposure in EV (0 = extra bonus gone)";
            m["  auch UZO und Fernglas eigene Belichtung"] = "  own exposure for UZO and binoculars too";
            m["    UZO Belichtung in EV"] = "    UZO exposure in EV";
            m["    Fernglas Belichtung in EV"] = "    Binoculars exposure in EV";
            m["  Glas-Charakter Stärke in % (Dunst am Tag, Farbverlust nachts)"] = "  Glass character strength in % (haze by day, colour loss at night)";
            m["  Glas-Anteil Angriffs-Sehrohr in %"] = "  Glass share attack periscope in %";
            m["  Glas-Anteil Beobachtungs-Sehrohr in %"] = "  Glass share observation periscope in %";
            m["  Fadenkreuz-Ausgleich in % (hält das Fadenkreuz trotz dunklerem Bild lesbar)"] = "  Reticle compensation in % (keeps the reticle readable despite a darker image)";
            m["  Dunst bei Tag (Schwarzwert-Anhebung, 0-100)"] = "  Haze by day (black level lift, 0-100)";
            m["  Farbverlust bei Nacht (0-100)"] = "  Colour loss at night (0-100)";
            m["  Nacht-Anpassung Angriffs-Sehrohr in % (100 = Spiel)"] = "  Night adaptation attack periscope in % (100 = game)";
            m["  Nacht-Anpassung Beobachtungs-Sehrohr in %"] = "  Night adaptation observation periscope in %";
            m["  Nacht-Anpassung UZO in %"] = "  Night adaptation UZO in %";
            m["  Nacht-Anpassung Fernglas in %"] = "  Night adaptation binoculars in %";
            m["  Nacht-Anpassung alle anderen Ansichten in %"] = "  Night adaptation all other views in %";
            m["Crew-KI: ruhigere, klügere Crew (Hauptschalter, wirkt ab nächstem Spielstart)"] = "Crew AI: calmer, smarter crew (main switch, works from next game start)";
            m["  Crew-KI: freie Matrosen legen sich in eine freie Koje statt im Gang zu stehen"] = "  Crew AI: free sailors lie down in a free bunk instead of standing in the corridor";
            m["  Crew-KI: müde Männer nehmen die nächste freie Koje und reservieren sie"] = "  Crew AI: tired men take the nearest free bunk and reserve it";
            m["  Crew-KI: zwei Mann pro Matrosen-Koje, wenn alle belegt sind (an: Spielstand neu laden)"] = "  Crew AI: two men per sailor bunk when all are taken (on: reload save)";
            m["    so viele Männer pro Matrosen-Koje (1-50, 1 = keiner teilt, Änderung: Spielstand neu laden)"] = "    this many men per sailor bunk (1-50, 1 = nobody shares, change: reload save)";
            m["Offiziere ohne freie Offizierskoje schlafen in einer Mannschaftskoje"] = "Officers without a free officer bunk sleep in a crew bunk";
            m["  ohne Dienst hinlegen unter x Energie"] = "  lie down off duty below x energy";
            m["  aufstehen ab x Energie"] = "  get up from x energy";
            m["  erst nach x Sekunden ohne Arbeit hinlegen"] = "  lie down only after x seconds without work";
            m["Zu wenig Kojen: Schlaf erholt schneller (automatisch berechnet)"] = "Too few bunks: sleep recovers faster (calculated automatically)";
            m["  Schlaf pro Mann und Tag (Stunden)"] = "  Sleep per man and day (hours)";
            m["  erreichbare Kojen-Auslastung"] = "  achievable bunk usage";
            m["  höchstens x-mal schneller"] = "  at most x times faster";
            m["  zusätzlich x-mal schneller (immer, in jeder Koje)"] = "  additionally x times faster (always, in every bunk)";
            m["  gilt auch für Matrosen"] = "  also applies to sailors";
            m["  fester Faktor, wenn automatisch aus (1 = normal)"] = "  fixed factor when automatic is off (1 = normal)";
            m["Offiziere ermüden an Stationen x-mal so schnell (1 = normal)"] = "Officers tire at stations x times as fast (1 = normal)";
            m["Offiziere spielen in der Freizeit Karten (Vorrang, 0 = aus)"] = "Officers play cards in their free time (priority, 0 = off)";
            m["Aktives Sonar (Ping): findet alles in Reichweite sicher, Werte genau - macht uns aber lauter"] = "Active sonar (ping): finds everything in range for sure, exact values - but makes us louder";
            m["  Reichweite (m)"] = "  Range (m)";
            m["  Suchwinkel (Grad, 360 = rundum)"] = "  Search angle (degrees, 360 = all around)";
            m["  ein Ping wirkt x Sekunden"] = "  one ping lasts x seconds";
            m["  so viel lauter sind wir beim Ping (für Gegner)"] = "  this much louder we are when pinging (for enemies)";
            m["  automatisch pingen alle x Sekunden unter Wasser (0 = nur per Taste)"] = "  ping automatically every x seconds underwater (0 = only by key)";
            m["  automatisch nicht bei Alarm (leise bleiben)"] = "  not automatically on alarm (stay quiet)";
            m["  nur getaucht"] = "  only when submerged";
            m["  Horchraum muss besetzt sein"] = "  Hydrophone room must be manned";
            m["Luft: volle Besatzung taucht immer gleich lange, egal wie groß"] = "Air: full crew always stays down the same time, no matter how big";
            m["  Tauchzeit bei voller Besatzung (Stunden)"] = "  Dive time with full crew (hours)";
            m["  Gefangene verbrauchen Luft mit"] = "  Prisoners use up air too";
            m["  bei vollem Gefangenenraum x % kürzer"] = "  x % shorter with a full prisoner room";
            m["Essen: größere Besatzung isst die Vorräte nicht schneller"] = "Food: a bigger crew does not eat the supplies faster";
            m["  essen wie x Mann (0 = normale Bootsbesatzung)"] = "  eat like x men (0 = normal boat crew)";
            m["Fähigkeit Dichtes Packen: +x Stapelgröße pro Mann (0 = wie im Spiel)"] = "Skill Tight Packing: +x stack size per man (0 = like in game)";
            m["Hafen: Einschiffen-Häkchen ohne Ruckler (nacheinander ausführen)"] = "Port: embark checkbox without stutter (run one after another)";
            m["Fehlerschutz: Mann ohne Auftragsliste"] = "Error guard: man without job list";
            m["Fehlerschutz: Kartenspiel ohne Tisch"] = "Error guard: card game without table";
            m["Fehlerschutz: kaputter Torpedowerfer eines Gegners"] = "Error guard: broken enemy torpedo launcher";
            m["Fehlerschutz: Decken-Blende im Boot"] = "Error guard: ceiling fade in the boat";
            m["Fehlerschutz: Porträt-Leiste und Gefolgs-Knöpfe"] = "Error guard: portrait bar and follower buttons";
            m["Fehlerschutz: Dienstplan- und Trupp-Fenster"] = "Error guard: duty roster and squad windows";
            m["Fehlerschutz: Trupp-Knöpfe ändern Mann-Typ"] = "Error guard: squad buttons change man type";
            m["  Crew-KI: ausgeruhte Männer machen die Koje für Müde frei"] = "  Crew AI: rested men free the bunk for tired ones";
            m["  Crew-KI: beim Alarmtauchen nur wenige Männer zum Bug (aus = Spiel)"] = "  Crew AI: on crash dive only a few men run to the bow (off = game)";
            m["    so viele Männer zum Bug (0 = keiner)"] = "    this many men to the bow (0 = none)";
            m["  Crew-KI: Angriffssehrohr nur für den Kommandanten"] = "  Crew AI: attack periscope only for the commander";
            m["  Crew-KI: Offiziere gehen nicht von selbst ans Beobachtungs-Sehrohr (Befehl geht weiter)"] = "  Crew AI: officers do not go to the observation periscope by themselves (orders still work)";
            m["  Crew-KI: bei Alarm nur essen, wenn sehr hungrig"] = "  Crew AI: on alarm only eat when very hungry";
            m["  Crew-KI: Männer ohne Aufgabe bleiben aus der Zentrale"] = "  Crew AI: men without a task stay out of the control room";
            m["  Crew-KI: Aufgaben in der Nähe bevorzugen"] = "  Crew AI: prefer tasks nearby";
            m["  Crew-KI: Wachposten reservieren (nicht zwei Mann zum selben Platz)"] = "  Crew AI: reserve watch posts (not two men to the same spot)";
            m["  Crew-KI: laufende Arbeit nicht für fast gleiche Arbeit abbrechen"] = "  Crew AI: do not abort running work for almost the same work";
            m["  Crew-KI: deine Befehle gehen nicht verloren, wenn die Aufgabenliste voll ist"] = "  Crew AI: your orders are not lost when the task list is full";
            m["  Crew-KI: \"Alle Luken schließen\" bleibt geschlossen"] = "  Crew AI: \"Close all hatches\" stays closed";
            m["  Crew-KI: wer 3 Plätze nicht erreicht, versucht es 1 Minute nicht mehr"] = "  Crew AI: whoever cannot reach 3 spots stops trying for 1 minute";
            m["Lecks: abdichten wie mit zwei Matrosen"] = "Leaks: seal like with two sailors";
            m["Tacho + Kilometerzähler oben links (Knoten, Fahrt-km, gesamt)"] = "Speedometer + odometer top left (knots, trip km, total)";
            m["Kein automatischer Alarm (nur wenn du ihn selbst gibst)"] = "No automatic alarm (only when you give it yourself)";
            m["Kriegstagebuch: Kontakte, Funk, Schäden, Häfen in logs\\kriegstagebuch.txt"] = "War diary: contacts, radio, damage, ports in logs\\kriegstagebuch.txt";
            m["Rohr-Anzeige oben links: welcher Torpedo-Typ in welchem Rohr"] = "Tube display top left: which torpedo type in which tube";
            m["Strom sparen: unter Wasser bei Batterie unter x % Kompressor + Scheinwerfer aus (0 = aus)"] = "Save power: underwater with battery below x % compressor + searchlight off (0 = off)";
            m["Torpedos x schneller nachladen (1 = normal, 1000 = sofort)"] = "Reload torpedoes x faster (1 = normal, 1000 = instant)";
            m["Schneller laden: Ladebildschirm nutzt die volle Rechenzeit"] = "Load faster: loading screen uses the full computing time";
            m["Lenzpumpe schaltet sich bei Wasser im Boot selbst an (#38)"] = "Pump switches itself on when there is water in the boat (#38)";
            m["Hafen-Grundfix: kein Einfrieren beim Verladen, hängende Aufträge werden gelöst (#39)"] = "Port base fix: no freeze while loading cargo, stuck orders get solved (#39)";
            m["  Hängt das Verladen so viele Sekunden: neu anstoßen, dann abbrechen"] = "  If loading hangs this many seconds: restart it, then cancel";
            m["  >> Verladen jetzt abbrechen (Schalter springt zurück)"] = "  >> Cancel loading now (switch jumps back)";
            m["  >> FIX MY DAMN SHIPMENT! Alles Bestellte sofort ins Boot zwingen"] = "  >> FIX MY DAMN SHIPMENT! Force everything ordered into the boat now";
            m["Hafen: alles sofort im Boot, keine Wartezeit"] = "Port: everything in the boat instantly, no waiting";
            m["Atemgeräte halten x-mal länger"] = "Breathing apparatus lasts x times longer";
            m["Milchkühe ab Spielstart unterwegs (#25)"] = "Milk cows on the way from game start (#25)";
            m["  Funk-Nachschub: nächste Milchkuh kommt in Reichweite"] = "  Radio resupply: nearest milk cow comes within reach";
            m["    Abstand in km"] = "    Distance in km";
            m["Torpedo-Rechner einfach: Lösung wird automatisch berechnet + angezeigt (#17)"] = "Simple torpedo calculator: solution is calculated + shown automatically (#17)";
            m["88mm: Dreh-/Neige-Tempo (1 = normal) (#27)"] = "88mm: traverse/elevation speed (1 = normal) (#27)";
            m["88mm: Schuss pro Minute (0 = Spielwert) (#27)"] = "88mm: rounds per minute (0 = game value) (#27)";
            m["88mm: Magazin (0 = Spielwert; groß = Dauerfeuer) (#28)"] = "88mm: magazine (0 = game value; big = continuous fire) (#28)";
            m["Flak: Dreh-/Neige-Tempo (1 = normal) (#27)"] = "AA: traverse/elevation speed (1 = normal) (#27)";
            m["Flak: Schuss pro Minute (0 = Spielwert) (#27)"] = "AA: rounds per minute (0 = game value) (#27)";
            m["Flak: Magazin (0 = Spielwert) (#28)"] = "AA: magazine (0 = game value) (#28)";
            m["88mm: Geschossgeschwindigkeit (1 = normal)"] = "88mm: shell speed (1 = normal)";
            m["Flak: Geschossgeschwindigkeit (1 = normal)"] = "AA: shell speed (1 = normal)";
            m["KI-Schützen: Treffsicherheit (0 = Spiel, 1 = keine Streuung)"] = "AI gunners: accuracy (0 = game, 1 = no spread)";
            m["Geschütz-Kreisel: KI-Schützen gleichen Wellen sofort aus (kein Nachzittern)"] = "Gun gyro: AI gunners compensate waves instantly (no jitter)";
            m["Geschütz-Kreisel: wenn du selbst zielst, bleibt der Zielpunkt trotz Wellen stehen"] = "Gun gyro: when you aim yourself the aim point stays put despite waves";
            m["KI-Schützen: schneller einschießen (1 = Spiel)"] = "AI gunners: range in faster (1 = game)";
            m["KI-Schützen: beim Zielwechsel so viel % vom Einschießen behalten (55 = Spiel)"] = "AI gunners: keep this many % of ranging when switching targets (55 = game)";
            m["KI-Schützen: Zusatz-Streuung, wenn Ziel nicht mit Augen gesehen, in % (100 = Spiel)"] = "AI gunners: extra spread when the target is not seen by eye, in % (100 = game)";
            m["Horcher besetzt: hört so viel % weiter (0 = Spiel)"] = "Hydrophone manned: hears this many % farther (0 = game)";
            m["Horcher besetzt: Peilung so viel % genauer (0 = Spiel)"] = "Hydrophone manned: bearing this many % more exact (0 = game)";
            m["Granaten schleudern getroffene Schiffe nicht weg (Schaden bleibt voll)"] = "Shells do not fling hit ships away (damage stays full)";
            m["88mm: Rückstoß nach oben (0 = keiner, 1 = Spiel)"] = "88mm: upward recoil (0 = none, 1 = game)";
            m["Flak: Rückstoß nach oben (0 = keiner, 1 = Spiel)"] = "AA: upward recoil (0 = none, 1 = game)";
            m["88mm: KI-Schützen greifen bis x km an (0 = Spielwert ~12 km; Granaten werden dafür schnell genug)"] = "88mm: AI gunners attack up to x km (0 = game value ~12 km; shells get fast enough for it)";
            m["Flak: KI-Schützen greifen bis x km an (0 = Spielwert)"] = "AA: AI gunners attack up to x km (0 = game value)";
            m["88mm: so viele Grad nach unten neigen (0 = Spielwert)"] = "88mm: depress this many degrees (0 = game value)";
            m["88mm: Mündungsgeschwindigkeit in m/s (0 = Spielwert, dann x Geschossgeschwindigkeit)"] = "88mm: muzzle velocity in m/s (0 = game value, then x shell speed)";
            m["88mm: Feuerrate x (1 = normal, wirkt auch auf Schuss pro Minute)"] = "88mm: rate of fire x (1 = normal, also affects rounds per minute)";
            m["88mm: Geschossgewicht in kg (0 = Spielwert)"] = "88mm: shell weight in kg (0 = game value)";
            m["88mm: Geschossgewicht x (1 = normal)"] = "88mm: shell weight x (1 = normal)";
            m["88mm: Schaden am Schiff x (1 = normal)"] = "88mm: damage to ships x (1 = normal)";
            m["88mm: Schaden an der Besatzung x (1 = normal)"] = "88mm: damage to crew x (1 = normal)";
            m["88mm: Schaden wächst mit Gewicht x Geschwindigkeit (wie früher)"] = "88mm: damage grows with weight x speed (as before)";
            m["Flak: so viele Grad nach unten neigen (0 = Spielwert)"] = "AA: depress this many degrees (0 = game value)";
            m["Flak: Mündungsgeschwindigkeit in m/s (0 = Spielwert, dann x Geschossgeschwindigkeit)"] = "AA: muzzle velocity in m/s (0 = game value, then x shell speed)";
            m["Flak: Feuerrate x (1 = normal, wirkt auch auf Schuss pro Minute)"] = "AA: rate of fire x (1 = normal, also affects rounds per minute)";
            m["Flak: Geschossgewicht in kg (0 = Spielwert)"] = "AA: shell weight in kg (0 = game value)";
            m["Flak: Geschossgewicht x (1 = normal)"] = "AA: shell weight x (1 = normal)";
            m["Flak: Schaden am Schiff x (1 = normal)"] = "AA: damage to ships x (1 = normal)";
            m["Flak: Schaden an der Besatzung x (1 = normal)"] = "AA: damage to crew x (1 = normal)";
            m["Flak: Schaden wächst mit Gewicht x Geschwindigkeit (wie früher)"] = "AA: damage grows with weight x speed (as before)";
            m["Flak so stark wie die 88: dreht so schnell und neigt so tief"] = "AA as strong as the 88: traverses as fast and depresses as low";
            m["Zielhilfe beim Selbstzielen: Schiff bis x Grad neben dem Rohr wird mit Vorhalt getroffen (0 = aus)"] = "Aim assist when aiming yourself: a ship up to x degrees beside the barrel gets hit with lead (0 = off)";
            m["Flak schießt automatisch auch auf Schiffe"] = "AA also fires at ships automatically";
            m["Schussgeräusch höchstens alle x Sek. (0 = aus, gegen Tonprobleme)"] = "Firing sound at most every x sec. (0 = off, against sound problems)";
            m["Sehrohr bis zu x m ausfahren (0 = aus) (Cheat #34)"] = "Extend periscope up to x m (0 = off) (cheat #34)";
            m["Boot unentdeckbar (Sicht, Sonar, Horchgerät, Radar)"] = "Boat undetectable (sight, sonar, hydrophone, radar)";
            m["Aimbot: 100 % Einschuss, keine Streuung, eigene Schüsse treffen mit Vorhalt (bis 30 Grad)"] = "Aimbot: 100 % ranging, no spread, own shots hit with lead (up to 30 degrees)";
            m["Deckgeschütz + Flak: Munition unendlich"] = "Deck gun + AA: unlimited ammo";
            m["Torpedos unendlich: Rohr lädt nach dem Schuss sofort neu (#26)"] = "Unlimited torpedoes: tube reloads instantly after firing (#26)";
            m["  Nachgeladen wird immer ein T5 Zaunkönig (zielsuchend)"] = "  Always reloads a T5 Zaunkönig (homing)";
            m["Keine Torpedo-Versager"] = "No torpedo duds";
            m["Boot schneller x (über und unter Wasser) (1 = normal) (#33)"] = "Boat faster x (surfaced and submerged) (1 = normal) (#33)";
            m["Lenzpumpe x stärker (1 = normal) (#38)"] = "Pump x stronger (1 = normal) (#38)";
            m["Lenzpumpe arbeitet in jeder Tiefe voll"] = "Pump works fully at any depth";
            m["Boot kann nie volllaufen (Wasser wird sofort abgepumpt)"] = "Boat can never flood (water is pumped out instantly)";
            m["Geleitschutz leicht gemacht: keine neuen Angreifer, Geleit fährt volle Fahrt und nimmt keinen Schaden"] = "Convoy gets easy: no new attackers, convoy sails at full speed and takes no damage";
            m["Testwasser: so viele m³ (1-10)"] = "Test water: this many m³ (1-10)";
            m[">> Testwasser ins Boot: Grundreinigung durch Flutung (in den Raum der Kamera, sonst Zentrale)"] = ">> Test water into the boat: deep cleaning by flooding (into the camera's room, else control room)";
            m["Crew läuft x schneller (1 = normal)"] = "Crew walks x faster (1 = normal)";
            m["Crew unsterblich + unverletzbar (keine Wunden, fällt nicht um)"] = "Crew immortal + invulnerable (no wounds, does not fall over)";
            m["Vorräte gehen nie aus (Essen, Ersatzteile, Verbandskästen ...)"] = "Supplies never run out (food, spare parts, first aid kits ...)";
            m["Munition + Gegenstände fast ohne Gewicht (0,0000001)"] = "Ammo + items almost weightless (0.0000001)";
            m["Lager wächst mit: fast voll = neue Plätze (Haupt-, Item-Lager, Küche)"] = "Storage grows: almost full = new slots (main, item storage, kitchen)";
            m["  So viele Plätze pro Erweiterung"] = "  This many slots per extension";
            m["  Höchstens so viele Plätze pro Lager"] = "  At most this many slots per storage";
            m["  Lager mindestens x Plätze, sofort (0 = aus)"] = "  Storage at least x slots, instantly (0 = off)";
            m["Lager: x mehr pro Platz stapeln (1 = normal, INI ohne Obergrenze)"] = "Storage: stack x more per slot (1 = normal, INI without upper limit)";
            m["  Lager: mindestens so viele pro Platz (0 = aus)"] = "  Storage: at least this many per slot (0 = off)";
            m["Physik-Takt folgt der Bildrate (bei wenig FPS längere Schritte, Spiel läuft nicht mehr in Zeitlupe)"] = "Physics rate follows the frame rate (longer steps at low FPS, game no longer runs in slow motion)";
            m["nie weniger als x Schritte pro Sekunde (Spiel nutzt 25 bei Zeitraffer x5)"] = "never fewer than x steps per second (game uses 25 at time compression x5)";
            m["Luken-Sicherung: Turmluke unter Wasser immer schließen (auch wenn noch jemand an Deck ist)"] = "Hatch safety: always close the tower hatch under water (even if someone is still on deck)";
            m["Reichweite Horchgerät x (1 = Spiel)"] = "Hydrophone range x (1 = game)";
            m["Reichweite aktives Sonar (Bordgerät) x (1 = Spiel)"] = "Active sonar (boat device) range x (1 = game)";
            m["Reichweite Radar x (1 = Spiel)"] = "Radar range x (1 = game)";
            m["Reichweite Radarwarner x (1 = Spiel)"] = "Radar detector range x (1 = game)";
            m["Reichweite Ausguck und Sehrohr x (1 = Spiel)"] = "Lookout and periscope range x (1 = game)";
            m["Kein Wasserdruck-Schaden in der Tiefe (Rumpf hält)"] = "No water pressure damage at depth (hull holds)";
            m["Fix: leere Tooltip-Kästen nicht anzeigen (z. B. am Auge)"] = "Fix: do not show empty tooltip boxes (e.g. at the eye)";
            m["Diagnose: Tooltips ins Log schreiben (leerer Kasten am Auge)"] = "Diagnostics: write tooltips to the log (empty box at the eye)";
            m["Rumpf verliert keine Gesundheit"] = "Hull loses no health";
            m["Eigene Granaten und Flak beschädigen das eigene Boot und die Crew nicht"] = "Own shells and flak do not damage your own boat or crew";
            m["Diesel wird nicht verbraucht (Tank bleibt voll)"] = "Diesel is not used (tank stays full)";
            m["Batterie bleibt voll"] = "Battery stays full";
            m["Luft unter Wasser unbegrenzt"] = "Unlimited air underwater";
            m["Crew wird nie müde"] = "Crew never gets tired";
            m["Moral (Disziplin) immer voll und sinkt nie, kein Stress"] = "Morale (discipline) always full and never drops, no stress";
            m["  >> Alle Steam-Erfolge freischalten"] = "  >> Unlock all Steam achievements";
            m["Geld: nie weniger als (0 = aus)"] = "Money: never less than (0 = off)";
            m["Ansehen (Ruf-Punkte): nie weniger als (0 = aus)"] = "Reputation (Ansehen): never less than (0 = off)";
            m["Preise im Hafen x (1 = normal, 0,01 = fast gratis)"] = "Port prices x (1 = normal, 0.01 = almost free)";
            m["Reparieren x schneller (1 = normal, 1000 = ca. 1 Sek.)"] = "Repair x faster (1 = normal, 1000 = approx. 1 sec.)";
            m["Forschung/Aufgaben im Hauptquartier x schneller"] = "Research/tasks at headquarters x faster";
            m["Entwickler-Konsole des Spiels einschalten (mit „Erfolge immer erlaubt“ ohne Achievement-Verlust)"] = "Enable the game's developer console (with „Achievements always allowed“ no achievement loss)";
            m["Forschung + Aufgaben im Hauptquartier sofort fertig"] = "Research + tasks at headquarters finish instantly";
            m["  >> Alles erforschen"] = "  >> Research everything";
            m["  Punkte pro Klick (für die zwei Knöpfe unten)"] = "  Points per click (for the two buttons below)";
            m["  >> Ansehen (Ruf-Punkte) dazu (damit bezahlst du Forschung + Matrosen)"] = "  >> Add reputation (Ansehen) (pays for research + sailors)";
            m["  >> Upgrade-Punkte dazu"] = "  >> Add upgrade points";
            m["Matrosen pro Klick (für den Knopf darunter)"] = "Sailors per click (for the button below)";
            m["  >> Matrosen anwerben (neue Männer kommen an Bord)"] = "  >> Recruit sailors (new men come aboard)";
            m["  >> Alle Upgrades + alle Boote freischalten"] = "  >> Unlock all upgrades + all boats";
            m["Torpedos x schneller (Rechner zielt mit, Treffer sicherer)"] = "Torpedoes x faster (calculator aims with it, hits more certain)";
            m["Schnellreise ans Kursziel per Taste (Tab „Tasten“)"] = "Quick travel to the course target by key (tab „Keys“)";
            m["  >> Jetzt ans Kursziel springen"] = "  >> Jump to the course target now";
            m["Deck-Crew bleibt beim Tauchen draußen, schießt weiter + ertrinkt nie"] = "Deck crew stays outside when diving, keeps firing + never drowns";
            m["  >> Alle heilen (Wunden, Blutungen, Krankheiten, wieder aufstehen)"] = "  >> Heal everyone (wounds, bleeding, illness, get back up)";
            m["Funk-Empfang in jeder Tiefe"] = "Radio reception at any depth";
            m["Horchgerät hört voll, auch aufgetaucht und bei hoher Fahrt"] = "Hydrophone hears fully, even surfaced and at high speed";
            m["Zielerfassung genau: Tempo, Kurs, Entfernung exakt statt geschätzt"] = "Exact target data: speed, course, range exact instead of estimated";
            m["Panzer-Rumpf: Schaden am Boot x (1 = normal, 0 = kein Schaden)"] = "Armoured hull: damage to the boat x (1 = normal, 0 = no damage)";
            m["Rumpf schützt die Crew: keine Verletzungen durch Treffer"] = "Hull protects the crew: no injuries from hits";
            m["Echolot pingt unter Wasser von selbst"] = "Echo sounder pings by itself underwater";
            m["Offiziere: x Skillpunkte extra"] = "Officers: x extra skill points";
            m["Offiziere: Skillpunkte unbegrenzt (alle Skills lernbar)"] = "Officers: unlimited skill points (all skills learnable)";
            m["Alle Skills ab Stufe 1 lernbar (keine Stufen-Sperre)"] = "All skills learnable from level 1 (no level lock)";
            m["Wer eine Fremdsprache spricht, wird Sprachgenie (Linguist: alle Sprachen)"] = "Anyone who speaks a foreign language becomes a language genius (Linguist: all languages)";
            m["Offiziere dürfen Skills aller Klassen wählen"] = "Officers may choose skills of all classes";
            m["  >> Sink the convoy: alle Gegner-Schiffe in Reichweite bekommen Torpedos"] = "  >> Sink the convoy: all enemy ships in range get torpedoes";
            m["  Reichweite in km"] = "  Range in km";
            m["  Torpedos pro Schiff"] = "  Torpedoes per ship";
            m["Fernschuss per Taste (auch aus der Koje): Torpedo auf nächsten Gegner"] = "Remote shot by key (even from the bunk): torpedo at the nearest enemy";
            m["Backup-Spielstand, sobald ein Cheat eingeschaltet wird"] = "Backup save as soon as a cheat is switched on";
            m["Mehr Offiziere an Bord erlaubt (+x, 0 = Spiel)"] = "More officers allowed aboard (+x, 0 = game)";
            m["Mehr Matrosen an Bord erlaubt (+x, 0 = Spiel)"] = "More sailors allowed aboard (+x, 0 = game)";
            m["Matrosen-Plätze passen sich \"Volle Crew\" an (ganze gewünschte Besatzung darf an Bord)"] = "Sailor slots follow \"Full crew\" (the whole wanted crew may come aboard)";
            m["Aktive Cheats oben links anzeigen"] = "Show active cheats top left";
            m["Menü öffnen/schließen"] = "Open/close menu";
            m["Tiefe: tiefer (allein = Schritt, Shift = x10, Alt = x100)"] = "Depth: deeper (alone = step, Shift = x10, Alt = x100)";
            m["Tiefe: höher (allein = Schritt, Shift = x10, Alt = x100)"] = "Depth: shallower (alone = step, Shift = x10, Alt = x100)";
            m["Bodenabstand-Autopilot an/aus"] = "Bottom-distance autopilot on/off";
            m["Aufsetzen auf den Grund an/aus"] = "Settle on the seabed on/off";
            m["Tempo-Cheat: schneller (+0,5)"] = "Speed cheat: faster (+0.5)";
            m["Tempo-Cheat: langsamer (-0,5)"] = "Speed cheat: slower (-0.5)";
            m["Alle leeren Rohre laden"] = "Load all empty tubes";
            m["Schnellreise ans Kursziel (Cheat muss an sein)"] = "Quick travel to course target (cheat must be on)";
            m["Zurück ins Wasser"] = "Back into the water";
            m["Lager aufräumen"] = "Tidy storage";
            m["Wachen tauschen"] = "Swap watches";
            m["Milchkuh herholen"] = "Call milk cow";
            m["Fernschuss: Torpedo auf nächsten Gegner (Cheat muss an sein)"] = "Remote shot: torpedo at nearest enemy (cheat must be on)";
            m["Sink the convoy"] = "Sink the convoy";
            m["Alle heilen"] = "Heal everyone";
            m["Maschinen-Tasten am Sehrohr/UZO/Fernglas und in freier Kamera ignorieren"] = "Ignore engine keys at periscope/UZO/binoculars and in free camera";
            m["Kurs- und Tiefen-Tasten (Pfeile, F/R mit Strg/Shift, Strg+P/C)"] = "Course and depth keys (arrows, F/R with Ctrl/Shift, Ctrl+P/C)";
            m["  am Sehrohr nur Pfeile, in freier Kamera keine"] = "  at the periscope only arrows, none in free camera";
            m["Kurs/Tiefe: fein (5° / 5 m)"] = "Course/depth: fine (5° / 5 m)";
            m["Kurs/Tiefe: grob (45° / 20 m)"] = "Course/depth: coarse (45° / 20 m)";
            m["Kurs: nach Backbord"] = "Course: to port";
            m["Kurs: nach Steuerbord"] = "Course: to starboard";
            m["Kurs: wenden (180°)"] = "Course: turn around (180°)";
            m["Hinweis, wenn das Ruder von Hand festgestellt ist und das Boot im Kreis fährt"] = "Notice when the rudder is set by hand and the boat runs in circles";
            m["Steuerstand immer besetzt: freier Matrose geht hin (ohne Steuermann reagiert das Ruder nicht)"] = "Helm always manned: a free sailor goes there (without a helmsman the rudder does not respond)";
            m["Ruder geht auch ohne Steuermann"] = "Rudder works without a helmsman";
            m["  Lange Lade-Bilder: mindestens 0,5 s laden pro Bild (Ladebild ruckelt, Laden deutlich kürzer)"] = "  Long loading frames: at least 0.5 s of loading per frame (loading screen stutters, loading much shorter)";
            m["  Steuerstand wechselt nicht ständig den Platz (Zentrale/Turm), wenn das Angriffssehrohr besetzt wird"] = "  Helm does not keep changing place (control room/tower) when the attack periscope gets manned";
            m["    frühestens nach ... Sekunden wieder wechseln"] = "    change again after ... seconds at the earliest";
            m["  Findet ein Mann keinen Weg zu seinem Posten, wird er hingesetzt (statt aufzugeben)"] = "  If a man finds no way to his post he is set there (instead of giving up)";
            m["Obergrenze Horchgerät, Sonar, Radar in km (mehr geht nie)"] = "Upper limit hydrophone, sonar, radar in km (never more)";
            m["Obergrenze Sichtweite (Ausguck, Sehrohr) in km (mehr geht nie)"] = "Upper limit sight range (lookout, periscope) in km (never more)";
            m["Ruder mittschiffs"] = "Rudder amidships";
            m["Kurs-Sperre (mit fein)"] = "Course lock (with fine)";
            m["Tiefer (mit fein/grob, beim Loslassen)"] = "Deeper (with fine/coarse, on release)";
            m["Flacher (mit fein/grob, beim Loslassen)"] = "Shallower (with fine/coarse, on release)";
            m["Sehrohrtiefe (mit fein; fein+grob = 20 m)"] = "Periscope depth (with fine; fine+coarse = 20 m)";
            m["größte sichere Tiefe (mit fein)"] = "greatest safe depth (with fine)";
            m["Maschine: Stopp"] = "Engine: stop";
            m["Maschine: Voraus 1"] = "Engine: ahead 1";
            m["Maschine: Voraus 2"] = "Engine: ahead 2";
            m["Maschine: Voraus 3"] = "Engine: ahead 3";
            m["Maschine: Voraus 4"] = "Engine: ahead 4";
            m["Maschine: Voraus 5 (\u00c4u\u00dferste)"] = "Engine: ahead 5 (flank)";
            m["Maschine: Zur\u00fcck 1"] = "Engine: astern 1";
            m["Maschine: Zur\u00fcck 2"] = "Engine: astern 2";
            m["Maschine: Zur\u00fcck 3"] = "Engine: astern 3";
            m["Maschine: Zur\u00fcck 4"] = "Engine: astern 4";
            m["Kurs: 1\u00b0 nach Backbord"] = "Course: 1° to port";
            m["Kurs: 1\u00b0 nach Steuerbord"] = "Course: 1° to starboard";
            m["Kurs: 5\u00b0 nach Backbord"] = "Course: 5° to port";
            m["Kurs: 5\u00b0 nach Steuerbord"] = "Course: 5° to starboard";
            m["Kurs: 45\u00b0 nach Backbord"] = "Course: 45° to port";
            m["Kurs: 45\u00b0 nach Steuerbord"] = "Course: 45° to starboard";
            m["Kurs: wenden (180\u00b0)"] = "Course: turn around (180°)";
            m["Tiefe: 5 m tiefer"] = "Depth: 5 m deeper";
            m["Tiefe: 5 m h\u00f6her"] = "Depth: 5 m shallower";
            m["Tiefe: 20 m tiefer"] = "Depth: 20 m deeper";
            m["Tiefe: 20 m h\u00f6her"] = "Depth: 20 m shallower";
            m["Tiefe: Sehrohrtiefe"] = "Depth: periscope depth";
            m["Tiefe: maximale sichere Tiefe"] = "Depth: maximum safe depth";
            m["Tiefe: aktuelle Tiefe halten"] = "Depth: hold current depth";
            m["Fadenkreuz-Licht an/aus (im Sehrohr/Fernglas)"] = "Reticle light on/off (in periscope/binoculars)";
            m["Kamera: Zoom zurücksetzen"] = "Camera: reset zoom";
            m["Kamera: schneller Zoom (halten)"] = "Camera: fast zoom (hold)";
            m["Station: Mauszeiger frei/fest"] = "Station: mouse cursor free/locked";
            m["Station: halten zum Umschauen (Modus 1)"] = "Station: hold to look around (mode 1)";
            m["Bremsen-Finder an/aus"] = "Slowdown finder on/off";
            m["Menü: Hintergrund-Deckkraft (1 = ganz dunkel)"] = "Menu: background opacity (1 = fully dark)";
            m["Menü: Breite in Pixeln (oder rechte untere Ecke ziehen)"] = "Menu: width in pixels (or drag the bottom right corner)";
            m["Menü: Höhe in Pixeln (0 = ganze Bildschirmhöhe)"] = "Menu: height in pixels (0 = full screen height)";
            m["Menü: Abstand von links (oder Titel ziehen)"] = "Menu: distance from left (or drag the title)";
            m["Menü: Abstand von oben (oder Titel ziehen)"] = "Menu: distance from top (or drag the title)";
            m["Test-Log: alles, was der Mod tut + Fehler (logs\\toolkit-Datum.log)"] = "Test log: everything the mod does + errors (logs\\toolkit-date.log)";
            m["Spiel-Log klein halten: nur Warnungen + Fehler in Player.log (spart Festplatte + RAM)"] = "Keep the game log small: only warnings + errors in Player.log (saves disk + RAM)";
            m["Karte: Pixel-Messung der Raster-Beschriftung ins Log (Kontrast, Lesbarkeit, je Stil und Ebene)"] = "Map: pixel measurement of the grid labels into the log (contrast, readability, per style and level)";
            m["Ausguck: ausführliches Log (Sicht, Rauch, Meldungen)"] = "Lookout: detailed log (sight, smoke, reports)";
            m["Schützen/Horcher: Log"] = "Gunners/hydrophone: log";
            m["Crew-KI: ausführliches Log (große Dateien, Ordner UboatToolkit/CrewKI, ab nächstem Start)"] = "Crew AI: detailed log (big files, folder UboatToolkit/CrewKI, from next start)";
            m["Speicher-Müllabfuhr: max. ms pro Bild (0 = Spielwert, kleiner = weniger Ruckler)"] = "Memory garbage collection: max. ms per frame (0 = game value, smaller = less stutter)";
            m["Cache aufräumen beim Spielstart: alte Daten-Cache, Absturzberichte, Mod-Logs (Spielstände nie)"] = "Clean cache at game start: old data cache, crash reports, mod logs (never saves)";
            m["  Dateien älter als x Tage"] = "  Files older than x days";
            m["Mod-Cache prüfen: Mods, die das Spiel still ablehnen würde, neu bauen lassen (wirkt beim nächsten Start)"] = "Check mod cache: let mods be rebuilt that the game would silently reject (works on next start)";
            m["  erst mit der Spiel-Prüfung testen (aus = jeden Start alle Mods neu bauen, langsam)"] = "  test with the game's check first (off = rebuild all mods every start, slow)";
            m["Speicher aufräumen alle x Minuten (gegen wachsenden RAM, 0 = aus)"] = "Clean memory every x minutes (against growing RAM, 0 = off)";
            m["Höchstens x Schiffe pro Konvoi gleichzeitig in 3D (0 = Spiel: 28, weniger = schneller)"] = "At most x ships per convoy in 3D at once (0 = game: 28, fewer = faster)";
            m["Höchstens x Gruppen gleichzeitig im Gefecht (0 = Spiel: 4, weniger = schneller)"] = "At most x groups in combat at once (0 = game: 4, fewer = faster)";
            m["Ferne Schiffe: KI-Takt in Sekunden (30 = Spiel-Standard, 60 = weniger CPU)"] = "Distant ships: AI tick in seconds (30 = game default, 60 = less CPU)";
            m["Log: jede Stunde Offiziere mit Energie und Dienst"] = "Log: every hour officers with energy and duty";
            m["Tooltip-Schlüssel mitschreiben (toolkit-tooltip-keys.txt, zum Sperren)"] = "Record tooltip keys (toolkit-tooltip-keys.txt, for blocking)";
            m["Log: jede leise/stumme Meldung und Stimme"] = "Log: every quiet/muted message and voice";
            m["Log: Nacht-Belichtung und Ansicht"] = "Log: night exposure and view";
            m["Log: Deckgeschütz gesperrt wegen Seegang"] = "Log: deck gun locked because of sea state";
            m["Log: Regenkleidung an/aus je Mann"] = "Log: rain gear on/off per man";
            m["Log: Luken-Durchgang Bild für Bild"] = "Log: hatch passage frame by frame";
            m["Log: Kamera-Zoom"] = "Log: camera zoom";
            m["Log: Außenkamera unter Wasser"] = "Log: outside camera underwater";
            m["Log: Stationen (Zoom, Drehknöpfe)"] = "Log: stations (zoom, dials)";
            m["Log: Karriere-Reiter in der Spielstand-Liste"] = "Log: career tabs in the save list";
            m["Log: Sehrohr-Overlays laden"] = "Log: loading periscope overlays";
            m["  langsam ab ... ms"] = "  slow from ... ms";
            m["Log: spätes Textur-Löschen (Zusammenfassung pro Minute)"] = "Log: late texture deletion (summary per minute)";
            m["Log: Stocken-Fix (übersprungene Wartezeiten)"] = "Log: stall fix (skipped waits)";
            m["Log: Sehrohr-Anzeige (Ansicht/Zoom wiederhergestellt)"] = "Log: periscope display (view/zoom restored)";
            m["Log: Maschinen-Befehle per Taste"] = "Log: engine orders by key";
            m["Log: Kurs- und Tiefen-Tasten"] = "Log: course and depth keys";
            m["Log: Uniform-Fix (pro Minute)"] = "Log: uniform fix (per minute)";
            m["Log: Bifrost UI-Pfade einmal ins Log"] = "Log: Bifrost UI paths once into the log";
            m["Performance-Logger (schreibt CSV, kostet wenig Leistung)"] = "Performance logger (writes CSV, costs little performance)";
            m["  Bremsen-Finder (Taste im Tab „Tasten“, kostet Leistung)"] = "  Slowdown finder (key in tab „Keys“, costs performance)";
            m["  Bremsen-Finder Spiel-Warteschlange (findet die echten Bremsen, kostet Leistung)"] = "  Slowdown finder game queue (finds the real slowdowns, costs performance)";
            m["Licht"] = "Lights";
            m["Licht-Modi an (Stil pro Schalter-Stellung, oben wählen)"] = "Light modes on (style per switch position, choose above)";
            m["Warme Glühbirnen-Farbe für „warm“ (aus = Spiel-Weiß)"] = "Warm bulb colour for „warm“ (off = game white)";
            m["Helligkeit „warm“ auf Stellung Normal in % (100 = Spiel)"] = "Brightness „warm“ at position Normal in % (100 = game)";
            m["Helligkeit „warm“ auf Stellung Rot/Blau (Nachtlicht) in % (0 = Licht aus)"] = "Brightness „warm“ at position Red/Blue (night light) in % (0 = light off)";
            m["Helligkeit „Notlicht“ in %"] = "Brightness „emergency light“ in %";
            m["Nachtlicht: Turm bleibt rot (Nachtsicht der Wache)"] = "Night light: tower stays red (night vision of the watch)";
            m["Eigene Notlicht-Farbe statt Spiel-Rot (Farbe oben)"] = "Own emergency light colour instead of game red (colour above)";
            m["Nachtlicht: Crew-Leistung minus x Punkte"] = "Night light: crew performance minus x points";
            m["Nachtlicht: Schlaf erholt x % schneller"] = "Night light: sleep recovers x % faster";
            m["Notlicht: E-Maschinen brauchen x % weniger Strom"] = "Emergency light: e-motors need x % less power";
            m["Licht-Automatik des Spiels: nach schwerem Schaden Notlicht"] = "Game's light automatic: emergency light after heavy damage";
            m["  schwerer Schaden ab x fehlender Haltbarkeit (alle Geräte zusammen)"] = "  heavy damage from x missing durability (all devices together)";
            m["  oder mehr als x Lecks (außer in geräumten Abteilungen)"] = "  or more than x leaks (except in evacuated compartments)";
            m["Auto-Schleichfahrt (auch am Lichtschalter umschaltbar)"] = "Auto silent running (also switchable at the light switch)";
            m["  Schleichfahrt an unter x m, aus darüber"] = "  Silent running on below x m, off above";
            m["  nur bei Alarm"] = "  only on alarm";
            m["Schalter-Namen passend umbenennen (z. B. „Nacht“, „Notlicht“)"] = "Rename switch names to match (e.g. „Night“, „Emergency light“)";
            m["LichtStil"] = "Light style";
            m["Stil Stellung Normal (0 = Spiel, 1 = warm, 2 = Notlicht)"] = "Style position Normal (0 = game, 1 = warm, 2 = emergency light)";
            m["Stil Stellung Rot (0 = Spiel, 1 = warm, 2 = Notlicht)"] = "Style position Red (0 = game, 1 = warm, 2 = emergency light)";
            m["Stil Stellung Blau (0 = Spiel, 1 = warm, 2 = Notlicht)"] = "Style position Blue (0 = game, 1 = warm, 2 = emergency light)";
            m["Farbe LightDay R (0-255)"] = "Colour LightDay R (0-255)";
            m["Farbe LightDay G (0-255)"] = "Colour LightDay G (0-255)";
            m["Farbe LightDay B (0-255)"] = "Colour LightDay B (0-255)";
            m["Farbe LightNight R (0-255)"] = "Colour LightNight R (0-255)";
            m["Farbe LightNight G (0-255)"] = "Colour LightNight G (0-255)";
            m["Farbe LightNight B (0-255)"] = "Colour LightNight B (0-255)";
            m["Farbe LightTower R (0-255)"] = "Colour LightTower R (0-255)";
            m["Farbe LightTower G (0-255)"] = "Colour LightTower G (0-255)";
            m["Farbe LightTower B (0-255)"] = "Colour LightTower B (0-255)";
            m["Farbe LightEmerg R (0-255)"] = "Colour LightEmerg R (0-255)";
            m["Farbe LightEmerg G (0-255)"] = "Colour LightEmerg G (0-255)";
            m["Farbe LightEmerg B (0-255)"] = "Colour LightEmerg B (0-255)";
            m["Crew & Gesundheit"] = "Crew & health";
            m["Kampagne & Missionen"] = "Campaign & missions";
            m["Spielstand & Leistung"] = "Saves & performance";
            m["Hafen, Lager & Geld"] = "Port, storage & money";
            m["Navigation, Karte & Zeit"] = "Navigation, map & time";
            m["Sicht, Sensoren & Funk"] = "Sight, sensors & radio";
            m["Boot & Technik"] = "Boat & technology";
            m["Waffen & Torpedos"] = "Weapons & torpedoes";
            m["Anzeige, Kamera & Menü"] = "Display, camera & menu";
            m["(keine)"] = "(none)";
            m["Taste drücken ... (Esc = Abbruch, Rück = keine)"] = "Press a key ... (Esc = cancel, Back = none)";
            m["Ändern"] = "Change";
            m["Cheats: "] = "Cheats: ";
            m["UBOAT Toolkit  (F10 schließt, Titel ziehen = verschieben, Ecke unten rechts = Größe)"] = "UBOAT Toolkit  (F10 closes, drag title = move, bottom right corner = resize)";
            m["Suche:"] = "Search:";
            m[" nur geänderte zeigen"] = " show changed only";
            m["alle auf"] = "open all";
            m["alle zu"] = "close all";
            m["Nichts gefunden."] = "Nothing found.";
            m[" Einstellungen"] = " settings";
            m[" geändert"] = " changed";
            m["INI neu laden"] = "Reload INI";
            m["Steam nicht gefunden"] = "Steam not found";
            m[" Erfolge freigeschaltet"] = " achievements unlocked";
            m["Sonar: nur getaucht"] = "Sonar: submerged only";
            m["Sonar: Horchraum ist nicht besetzt"] = "Sonar: hydrophone room is not manned";
            m["Werte aus UboatToolkit.ini"] = "Values from UboatToolkit.ini";
            m["Einstellungen lesen fehlgeschlagen: "] = "Reading settings failed: ";
            m["Matrosen anwerben fehlgeschlagen"] = "Recruiting sailors failed";
            m["Neue Matrosen sind an Bord"] = "New sailors are aboard";
            m[" von "] = " of ";
            m[" - Treffer: "] = " - hits: ";
            m["Schadensbericht: was hat uns getroffen, wo und warum (neueste oben). Datei: UboatToolkit/schadensbericht.txt"] = "Damage report: what hit us, where and why (newest on top). File: UboatToolkit/schadensbericht.txt";
            m["Liste leeren"] = "Clear list";
            m["Noch kein Schaden in dieser Sitzung."] = "No damage yet in this session.";
            m[", zusammen Rumpf "] = ", hull total ";
            m[", Crew "] = ", crew ";
            m[" Mann geheilt"] = " men healed";
            m["See zu rau für das Geschütz (Windstärke "] = "Sea too rough for the gun (wind force ";
            m["Zieltiefe "] = "Target depth ";
            m["Bodenabstand-Autopilot "] = "Bottom-distance autopilot ";
            m["Aufsetzen auf den Grund"] = "Settling on the seabed";
            m["Aufsetzen beendet"] = "Settling ended";
            m["Aufsetzen: Grund unbekannt"] = "Settling: seabed unknown";
            m["Aufsetzen auf "] = "Settling at ";
            m["Autopilot: Grund unbekannt"] = "Autopilot: seabed unknown";
            m["Autopilot: Ziel "] = "Autopilot: target ";
            m[" m, Grund "] = " m, seabed ";
            m["Tiefe "] = "Depth ";
            m[" m  Ziel "] = " m  target ";
            m["Grund: ?"] = "Seabed: ?";
            m["Grund "] = "Seabed ";
            m[" m, frei "] = " m, clear ";
            m["Strom sparen: "] = "Saving power: ";
            m[" Geräte aus (Batterie "] = " devices off (battery ";
            m["Rohre  "] = "Tubes  ";
            m["Nur im Spiel verfügbar."] = "Only available in game.";
            m["Offiziere: Rolle mit Klick wechseln. „Skills bearbeiten“ öffnet den Skill-Editor (alle Skills aller Klassen)."] = "Officers: click to change role. „Edit skills“ opens the skill editor (all skills of all classes).";
            m["  (Skillpunkte frei: "] = "  (free skill points: ";
            m["Rolle: "] = "Role: ";
            m["Skills zu"] = "Close skills";
            m["Skills bearbeiten"] = "Edit skills";
            m["  Skill-Liste nicht gefunden."] = "  Skill list not found.";
            m["  Klick = Skill dazu (+) oder weg (✓)."] = "  Click = add skill (+) or remove (✓).";
            m["Alle geben"] = "Give all";
            m["Alle weg"] = "Remove all";
            m[" Ansehen (jetzt "] = " reputation (now ";
            m[" Upgrade-Punkte (jetzt "] = " upgrade points (now ";
            m[" Rohr(e) nachgeladen"] = " tube(s) reloaded";
            m["Kein leeres Rohr oder kein Torpedo im Lager"] = "No empty tube or no torpedo in storage";
            m["Wachen getauscht"] = "Watches swapped";
            m["Keine Gruppen zum Tauschen"] = "No groups to swap";
            m["Lager aufgeräumt ("] = "Storage tidied (";
            m[" Schritte)"] = " steps)";
            m["Erforscht: "] = "Researched: ";
            m[" Forschungen (laufende bleiben, gehen mit „sofort“ gleich fertig)"] = " research items (running ones stay, finish right away with „instant“)";
            m["Freigeschaltet: "] = "Unlocked: ";
            m[" Ausrüstungen + alle Boote"] = " equipment + all boats";
            m["Erst einen Kurs auf der Karte setzen"] = "Set a course on the map first";
            m["Schnellreise: am Ziel"] = "Quick travel: at the target";
            m["Keine Milchkuh auf See"] = "No milk cow at sea";
            m["Milchkuh ist da (3 km östlich)"] = "Milk cow is here (3 km east)";
            m["Licht-Modi: jede Schalter-Stellung bekommt ihren eigenen Stil. Erst oben „Licht-Modi an“ anhaken (unten in der Liste)."] = "Light modes: every switch position gets its own style. First tick „Light modes on“ above (lower in the list).";
            m["(Licht-Modi sind gerade AUS: das Spiel zeigt sein normales Licht.)"] = "(Light modes are OFF right now: the game shows its normal light.)";
            m["Stellung "] = "Position ";
            m["Jetzt umschalten:"] = "Switch now:";
            m["Farben auf Standard"] = "Colours to default";
            m["Licht-Werte ins Log schreiben"] = "Write light values to the log";
            m["Verladen abgebrochen. Du kannst neu bestellen."] = "Loading cancelled. You can order again.";
            m["Verladen hing fest und wurde abgebrochen. Bitte neu bestellen."] = "Loading was stuck and got cancelled. Please order again.";
            m[" Lieferungen erzwungen. Ruhe jetzt."] = " deliveries forced. Quiet now.";
            m[": Raster-Text "] = ": grid text ";
            m[", Orte "] = ", places ";
            m[", neutral "] = ", neutral ";
            m[", Alliierte "] = ", allies ";
            m[", Achse "] = ", axis ";
            m["Karten-Bericht ins Toolkit-Log geschrieben"] = "Map report written to the toolkit log";
            m["Karten-Stil wählen: Einstellungen > Grafik > Kartenstil. Neu: „"] = "Choose map style: Settings > Graphics > Map style. New: „";
            m["“ und „"] = "“ and „";
            m["Farben für „"] = "Colours for „";
            m["“ (sofort sichtbar, wenn dieser Stil an ist):"] = "“ (visible right away when this style is on):";
            m["Feine Einstellungen pro Karten-Stil: Datei "] = "Fine settings per map style: file ";
            m[" (wird im laufenden Spiel neu gelesen)."] = " (re-read while the game runs).";
            m["Karten-Bericht ins Log schreiben"] = "Write map report to the log";
            m["Land voraus! Zeitraffer gestoppt."] = "Land ahead! Time compression stopped.";
            m["Land voraus! Zeitraffer auf x"] = "Land ahead! Time compression limited to x";
            m[" begrenzt."] = " .";
            m["Fadenkreuz-Licht an"] = "Reticle light on";
            m["Fadenkreuz-Licht aus"] = "Reticle light off";
            m["Sink the convoy: "] = "Sink the convoy: ";
            m[" Schiffe im Visier"] = " ships targeted";
            m["Kein Gegner in Reichweite"] = "No enemy in range";
            m["Kein Rohr geladen"] = "No tube loaded";
            m["Torpedo los auf "] = "Torpedo away at ";
            m["Keine Torpedo-Typen gefunden."] = "No torpedo types found.";
            m["Hafen-Nachschub: Haken = dieser Typ kommt ins Hafenlager. Nichts angehakt = normal (oder Mischung, wenn eingeschaltet)."] = "Port resupply: tick = this type goes into the port storage. Nothing ticked = normal (or mix, if switched on).";
            m["Gilt für Lager, die sich neu auffüllen."] = "Applies to storages that refill.";
            m["Auswahl löschen"] = "Clear selection";
            m["Cheat: Torpedo direkt ins Boot legen (in freie Torpedo-Plätze)."] = "Cheat: put a torpedo straight into the boat (into free torpedo slots).";
            m["Anzahl: "] = "Count: ";
            m["Erzeugen: "] = "Create: ";
            m[" ins Boot gelegt"] = " put into the boat";
            m["Kein freier Torpedo-Platz"] = "No free torpedo slot";
            m["Cheat: Munition direkt ins Boot legen (stapelt sich ins Lager wie beim Beladen im Hafen)."] = "Cheat: put ammo straight into the boat (stacks into storage like loading in port).";
            m["Keine Munitions-Typen gefunden."] = "No ammo types found.";
            m["Menge: "] = "Amount: ";
            m[" ins Lager gelegt"] = " put into storage";
            m["Kein Platz im Lager (oder zu schwer)"] = "No room in storage (or too heavy)";
            m["Keine sichere Stelle gemerkt"] = "No safe spot remembered";
            m["Zurück ins Wasser ("] = "Back into the water (";
            m["Befehl: "] = "Order: ";
            m["Herr Kaleun, was tun wir?"] = "Herr Kaleun, what do we do?";
            m["Vorschlag: "] = "Suggestion: ";
            m[" Minuten ignorieren (Meldungen werden gesammelt)"] = " minutes ignore (messages are collected)";
            m["⚠ ignoriert noch "] = "⚠ still ignoring ";
            m[" min\n"] = " min\n";
            m[" Meldungen gesammelt"] = " messages collected";
            m["⚠ Beschuss!\nVorschlag: "] = "⚠ Under fire!\nSuggestion: ";
            m["Beschuss-Meldungen (neueste oben):"] = "Under-fire messages (newest on top):";
            m[" - Vorschlag: "] = " - suggestion: ";
            m[" Männer umbenannt, jetzt hat jeder einen eigenen Nachnamen"] = " men renamed, now everyone has his own last name";
            m["Keine doppelten Namen gefunden"] = "No duplicate names found";
            m[" (0 = verbraucht, 1 = kostenlos, 2 = erzeugt)"] = " (0 = consumes, 1 = free, 2 = generates)";
            m["Alle Warthog-Funktionen nur auf U-1337 (Type XXX), nicht auf anderen Booten"] = "All Warthog features only on U-1337 (Type XXX), not on other boats";
            m["Gummibeschichtung: Sonar-Ortbarkeit x (1 = ohne, 0 = unsichtbar)"] = "Rubber coating: sonar detectability x (1 = none, 0 = invisible)";
            m["Gummibeschichtung: Horch-Ortbarkeit x (1 = normal)"] = "Rubber coating: hydrophone detectability x (1 = normal)";
            m["Finnen: engere Kurven, weniger Rollen, schneller Tiefe wechseln"] = "Fins: tighter turns, less rolling, faster depth changes";
            m["  Kurven: Drehkraft x"] = "  Turns: turning force x";
            m["  Rollen: Dämpfung x"] = "  Rolling: damping x";
            m["  Tiefenruder-Wirkung x"] = "  Dive plane effect x";
            m["Strahlruder vorn + achtern"] = "Bow + stern thrusters";
            m["  Strahlruder helfen automatisch beim Ruderlegen"] = "  Thrusters help automatically when the rudder is put over";
            m["  Stärke (6 = ca. 0,6 Grad/s² Drehbeschleunigung)"] = "  Strength (6 = about 0.6 deg/s² turning acceleration)";
            m["  Strom pro Sekunde volle Leistung (Anteil Batterie x 0,0001)"] = "  Power per second at full output (share of battery x 0.0001)";
            m["  Kurs halten, während Geschütze feuern (gegen Rückstoß)"] = "  Hold course while the guns fire (against recoil)";
            m["Geschwindigkeit: Faktor auf die Schraubenleistung (1,0 = wie gebaut)"] = "Speed: factor on propeller power (1.0 = as built)";
            m["Tauch-/Steiggeschwindigkeit: Faktor auf die Tiefenruder-Kraft (1,0 = wie ein IXC, auch in großer Tiefe)"] = "Dive/rise speed: factor on diving-plane force (1.0 = like an IXC, also at great depth)";
            m["Zerstörungstiefe in m (befohlene Tiefe wird auf 100 m darüber begrenzt)"] = "Crush depth in m (ordered depth is limited to 100 m above it)";
            m["  Shift + Z/X: ganzes Boot seitwärts (Anlegen); Z/X ohne Shift: auf der Stelle drehen"] = "  Shift + Z/X: whole boat sideways (docking); Z/X without Shift: turn on the spot";
            m["  Bug-Strahlruder einzeln (-100 = Bb, +100 = Stb, 0 = aus)"] = "  Bow thruster alone (-100 = port, +100 = starboard, 0 = off)";
            m["  Heck-Strahlruder einzeln (-100 = Bb, +100 = Stb, 0 = aus)"] = "  Stern thruster alone (-100 = port, +100 = starboard, 0 = off)";
            m["Teleskop-Schnorchel: tiefer als Sehrohrtiefe"] = "Telescopic snorkel: deeper than periscope depth";
            m["  zusätzliche Länge in m"] = "  extra length in m";
            m["  fährt bei Wellen automatisch weiter aus"] = "  extends further automatically in waves";
            m["  Abgas unter Wasser: Sichtbarkeit x (0 = unsichtbar)"] = "  Exhaust underwater: visibility x (0 = invisible)";
            m["Eine Lenzpumpe pro Abteil, mit Schwimmer (springt selbst an)"] = "One bilge pump per compartment, with float switch (starts by itself)";
            m["  Schwimmer: an ab x m³ Wasser im Abteil"] = "  Float: on from x m³ of water in the compartment";
            m["  Leistung je Pumpe in m³/s (offenes Turmluk getaucht ≈ 0,2)"] = "  Output per pump in m³/s (open tower hatch submerged ≈ 0.2)";
            m["  Leistung steigt mit der Tiefe (gegen den Wasserdruck)"] = "  Output rises with depth (against water pressure)";
            m["  Strom je Pumpe bei voller Leistung (Anteil x 0,0001/s)"] = "  Power per pump at full output (share x 0.0001/s)";
            m["Lüftung immer an und lautlos, auch bei Schleichfahrt"] = "Ventilation always on and silent, even in silent running";
            m["Sauerstoff per Elektrolyse: keine Kalipatronen mehr"] = "Oxygen by electrolysis: no more potash cartridges";
            m["Alle Verbraucher generieren Strom"] = "All consumers generate power";
            m["Passives Sonar 360°: immer an, auch ohne Horcher"] = "Passive sonar 360°: always on, even without a hydrophone operator";
            m["  aufgetaucht und bei hoher Fahrt genauso gut"] = "  just as good surfaced and at high speed";
            m["  Reichweite x"] = "  Range x";
            m["  Kontakte auf der Karte zeigen"] = "  Show contacts on the map";
            m["  Alarm bei neuem Kontakt"] = "  Alarm on new contact";
            m["Aktives Sonar Bug (nach vorn + unten)"] = "Active sonar bow (forward + down)";
            m["Sonar Mitte (nur passiv)"] = "Sonar centre (passive only)";
            m["Aktives Sonar Heck (nach hinten + unten)"] = "Active sonar stern (backward + down)";
            m["  Ping verrät uns (Gegner hören den Ping)"] = "  Ping gives us away (enemies hear the ping)";
            m["Automatischer Torpedolader: Rohre laden ohne Crew, Außenbehälter füllen innen nach"] = "Automatic torpedo loader: tubes load without crew, outer containers refill inside";
            m["  Nachladezeit pro Rohr in Sekunden (0 = sofort, Typ XXI hydraulisch ≈ 120)"] = "  Reload time per tube in seconds (0 = instant, Type XXI hydraulic ≈ 120)";
            m["Deckgeschütze: Kaliber (88 oder 105 mm)"] = "Deck guns: calibre (88 or 105 mm)";
            m["Overkill: Vierlingsrohre statt Zwillinge"] = "Overkill: quad barrels instead of twins";
            m["Flak: Absenkung bis x Grad (unter Horizont)"] = "AA: depress down to x degrees (below horizon)";
            m["  Feuerschutz: nie ins eigene Boot schießen"] = "  Fire guard: never shoot into your own boat";
            m["Geschütze aus dem Feuerleitstand fernbedienen (aus = klassisch an Deck)"] = "Remote-control the guns from the fire control station (off = classic on deck)";
            m["Flugzeuge orten bis x km"] = "Detect aircraft up to x km";
            m["Flugzeuge beschießen ab x km"] = "Fire at aircraft from x km";
            m["Warthog: Strahlruder links"] = "Warthog: thruster left";
            m["Warthog: Strahlruder rechts"] = "Warthog: thruster right";
            m["Mehr Frachter: Konvois + einzelne Handelsschiffe x so oft (1 = Spiel)"] = "More freighters: convoys + single merchants x as often (1 = game)";
            m["Crew-Spezialisierungen (Tauchgang, Schleichfahrt, Disziplin): immer genug Punkte"] = "Crew specializations (diving, silent running, discipline): always enough points";
            m["  >> Konvoi spawnen (Gegner-Konvoi taucht vor dir auf)"] = "  >> Spawn convoy (enemy convoy appears ahead of you)";
            m["  Abstand voraus in km"] = "  Distance ahead in km";
            m["Konvoi spawnen"] = "Spawn convoy";
            m["  nur bis Zeitraffer x (darüber kein Nachladen = keine Ruckler/Flicken)"] = "  only up to time compression x (above: no loading = no stutter/patches)";
            m["  Strahlruder-Anzeige (wie die Tiefen-Anzeige)"] = "  Thruster display (like the depth display)";
            m["    Anzeige: Position von links in %"] = "    Display: position from the left in %";
            m["    Anzeige: Position von oben in %"] = "    Display: position from the top in %";
            m["Warthog: Anker werfen / lichten"] = "Warthog: drop / raise anchor";
            m["Luken: alle Luken benutzbar (2. Turmluk, Seitentüren, Torpedoluken, Kombüse; aus = Spiel mit einem Luk)"] = "Hatches: all hatches usable (2nd tower hatch, side doors, torpedo hatches, galley; off = game's single hatch)";
            m["  Luken einzeln steuern (Liste oben im Reiter; aus = alle folgen Turmluk 1)"] = "  Control hatches one by one (list at the top of the tab; off = all follow tower hatch 1)";
            m["Liegeplatz: Boot x m nach vorn schieben (IXC = 5; 14 = Heck wie beim IXC)"] = "Berth: move boat x m forward (IXC = 5; 14 = stern like the IXC)";
            m["Liegeplatz: Boot x m seitlich schieben (+ = Steuerbord)"] = "Berth: move boat x m sideways (+ = starboard)";
            m["Netzsäge: U-Boot-Netze durchschneiden (Bug + Heck)"] = "Net saw: cut anti-submarine nets (bow + stern)";
            m["Anker benutzen (Knopf oben im Reiter oder Taste)"] = "Use anchor (button at the top of the tab or key)";
            m["  welcher Anker: 0 = Bug, 1 = Heck, 2 = beide"] = "  which anchor: 0 = bow, 1 = stern, 2 = both";
            m["  Kettenlänge in m"] = "  chain length in m";
            m["  Anker lichten dauert x Sekunden (0 = sofort)"] = "  raising the anchor takes x seconds (0 = instant)";
            m["Feuerleitstand: Geschützgruppe nur bereit, wenn ihr Platz besetzt ist"] = "Fire control: gun group only ready when its station is manned";
            m["Deckgeschütze: Kaliber 8,8 cm statt 10,5 cm"] = "Deck guns: 8.8 cm calibre instead of 10.5 cm";
            m["Sonar: neuer Kontakt, Peilung "] = "Sonar: new contact, bearing ";
            m["Warthog übernehmen"] = "Take over Warthog";
            m["Im Spiel selbst: im Hafen den Depot-Arbeiter ansprechen → \"Kommando über anderes U-Boot übernehmen\"."] = "In the game itself: in port talk to the depot worker → \"Take command of another U-boat\".";
            m["Nur im Hafen möglich"] = "Only possible in port";
            m["Bootsauswahl nicht gefunden"] = "Boat selection not found";
            m["Bootsauswahl geöffnet"] = "Boat selection opened";
            m["Radar: Flugzeug, Peilung "] = "Radar: aircraft, bearing ";
            m["Backup gespeichert (UboatToolkit_Backup), Cheat ist an."] = "Backup saved (UboatToolkit_Backup), cheat is on.";
            m["Warnung: "] = "Warning: ";
            m[" nur noch "] = " only";
            m["    >> Bremsen-Finder jetzt starten/stoppen (misst 1 Minute, dann Liste ins Log)"] = "    >> Start/stop slowdown finder now (measures 1 minute, then list into the log)";
            m["Fix: Fehler \"Requested asset Icons/Activators/... / Icons/Equipment/...\" (fehlende Bilder von Depots und Gegenständen)"] = "Fix: error \"Requested asset Icons/Activators/... / Icons/Equipment/...\" (missing pictures of depots and items)";
            m["Fix: Fehler \"GameObject is already being activated or deactivated\" nach dem Charakter-Bildschirm"] = "Fix: error \"GameObject is already being activated or deactivated\" after the character screen";
            m[" Forschungen ("] = " research items (";
            m[" davon liefen gerade)"] = " of them were running)";
            m["Forschung ohne Voraussetzungen: keine Vorgänger-Forschung, kein Datum, kein Offizier nötig"] = "Research without requirements: no earlier research, no date needed";
            m["  Lager: gleiche Sachen automatisch zu einem Stapel zusammenlegen"] = "  Storage: automatically merge identical items into one stack";
            m["  Lager: auch Sachen stapeln, die im Spiel nur einzeln gehen (alle Waren außer Diesel)"] = "  Storage: also stack items the game keeps single (all goods except diesel)";
            m["  Händler im Hafen hat von jeder Ware mindestens so viel (0 = wie im Spiel)"] = "  Port dealer has at least this many of every good (0 = game value)";
            m["Zweites Turmluk + Seitentüren Deck <-> Turm (Crew nimmt den kürzesten Weg)"] = "Second tower hatch + side doors deck <-> tower (crew takes the shortest way)";
            m["Crew-Detailstufen: Männer, die du nicht siehst, überlegen seltener (mehr FPS, Arbeit läuft normal weiter)"] = "Crew detail levels: men you cannot see think less often (more FPS, work goes on normally)";
            m["  nicht sichtbare Männer überlegen nur jedes x. Bild (1 = jedes Bild)"] = "  men out of sight think only every x-th frame (1 = every frame)";
            m["  Männer ohne Arbeit (stehen, ruhen, rauchen, schlafen) überlegen nur jedes x. Bild, auch sichtbar (1 = aus)"] = "  men without work (standing, resting, smoking, sleeping) think only every x-th frame, even when visible (1 = off)";
            m["Start-Videos (Logos) beim Spielstart überspringen"] = "Skip the start videos (logos) when the game starts";
            m["Start-Intros übersprungen"] = "Start videos skipped";
            m["Strahlruder gibt es nur auf dem Warthog (U-1337)"] = "Thrusters exist only on the Warthog (U-1337)";
            m["Strahlruder sind ausgeschaltet (F10 > Warthog)"] = "Thrusters are switched off (F10 > Warthog)";
            m["Strahlruder: im Hafen festgemacht, erst ablegen"] = "Thrusters: moored in port, cast off first";
            m["Strahlruder: Boot wird gerade vom Spiel gehalten (Schnellreise/Andocken)"] = "Thrusters: the game is holding the boat right now (fast travel/docking)";
            return m;
        }
    }
}

// ====================================================================================================
// Abschnitt: Core/Owners.cs
// ====================================================================================================
namespace UboatToolkit
{
    using global::System.Collections.Generic;
    using global::UBOAT.Game.Scene.Entities;
    using global::UnityEngine;

    // 0.4.28 (DoMe 2026-11-18: "ein single global owner"): every game value that several Toolkit parts want to set has
    // exactly ONE place that writes it. The parts only say what they want; this place decides by a fixed order and
    // writes into the log who decides now and who was overruled.
    //
    // First value: the ordered depth of our boat. Order: 3 = your own order (keys, depth gauge),
    // 2 = safety (snorkel head, crash dive under fire), 1 = automatic (ground-clearance autopilot).
    // After an order of a higher rank, lower ranks are refused for a while (your order 20 s, safety 10 s).
    internal static class Owners
    {
        public const int Player = 3, Safety = 2, Auto = 1;

        // Second value: propeller power of OUR boat. Two parts wanted to write it (speed cheat and Warthog speed factor) and
        // overwrote each other every second. Now both only state their factor here; Navigation writes game value x both.
        public static float WarthogSpeed = 1f;
        private static float lastSpeedLog = -1f;
        public static float PropellerFactor(float cheat)
        {
            float w = Mathf.Clamp(WarthogSpeed, 0.5f, 2f), f = Mathf.Max(1f, cheat) * w;
            if (Mathf.Abs(f - lastSpeedLog) > 0.001f)
            {
                lastSpeedLog = f;
                Util.Log("Schraubenkraft (nur eigenes Boot): Spielwert x " + (f * f).ToString("0.##") + " (Tempo-Cheat x" + Mathf.Max(1f, cheat).ToString("0.##") + ", Warthog-Tempo x" + w.ToString("0.##") + ")");
            }
            return f * f;
        }
        private static int rankNow; private static float until; private static string whoNow = "";
        private static readonly Dictionary<string, float> told = new Dictionary<string, float>();

        private static bool Allow(string who, int rank, string what)
        {
            float now = Time.unscaledTime;
            if (now < until && rank < rankNow && who != whoNow)
            {
                float last;
                if (!told.TryGetValue(who, out last) || now - last > 10f)
                {
                    told[who] = now;
                    Util.Log("Tiefe: " + who + " will " + what + ", überstimmt von " + whoNow + " (noch " + (until - now).ToString("0") + " s)");
                }
                return false;
            }
            if (who != whoNow) Util.Log("Tiefe: bestimmt jetzt " + who + " (" + what + ")" + (whoNow != "" && now < until ? ", vorher " + whoNow : ""));
            whoNow = who; rankNow = rank;
            until = now + (rank >= Player ? 20f : rank == Safety ? 10f : 0f);
            return true;
        }

        // Ordered depth in metres (deck line, like the game's depth meter). Returns false when overruled.
        public static bool Depth(PlayerShip ship, string who, int rank, float depth, bool silent, bool allowAlarm)
        {
            if (ship == null) return false;
            float t = Mathf.Max(0f, depth);
            if (!Allow(who, rank, "Zieltiefe " + t.ToString("0.0") + " m")) return false;
            ship.SetDepthPreset(DepthPreset.None, true);
            ship.SetTargetDepth(t, silent, allowAlarm);
            return true;
        }

        public static bool Preset(PlayerShip ship, string who, int rank, DepthPreset preset, bool silent)
        {
            if (ship == null) return false;
            if (!Allow(who, rank, "Tiefe " + preset)) return false;
            ship.SetDepthPreset(preset, silent);
            return true;
        }
    }
}

// ====================================================================================================
// Abschnitt: Core/PatchOrder.cs
// ====================================================================================================
namespace UboatToolkit
{
    // 0.4.30 (DoMe 2026-11-18: fewer, bigger source files). The game applies our patch classes in the order in which
    // they stand in the compiled program, and that order came from the order of the source FILES. Two patches on the
    // same game method run in that order, so moving code between files could silently change behaviour.
    // This list fixes the order as it was with the single files (by folder and file name). New patch classes that are
    // not listed run after these, sorted by name.
    internal static class PatchOrder
    {
        internal static readonly string[] Names =
        {
            "UboatToolkit.Modules.AchievementTools_LockPatch",
            "UboatToolkit.Modules.AchievementTools_ConsolePatch",
            "UboatToolkit.Modules.AchievementTools_StartDiag",
            "UboatToolkit.Modules.AchievementTools_UnlockLog",
            "UboatToolkit.Modules.ActiveSonar_SpotPatch",
            "UboatToolkit.Modules.ActiveSonar_ExactPatch",
            "UboatToolkit.Modules.ActiveSonar_ObsPatch",
            "UboatToolkit.Modules.AutoDetail_NearbyGroupsPatch",
            "UboatToolkit.Modules.AutoDetail_VirtualShipsPatch",
            "UboatToolkit.Modules.AutoDetail_CompressionBasePatch",
            "UboatToolkit.Modules.AwashGuns_HatchPatch",
            "UboatToolkit.Modules.CamZoom_Patch",
            "UboatToolkit.Modules.CamZoom_Cursor",
            "UboatToolkit.Modules.CamFreeFly",
            "UboatToolkit.Modules.CarrierConvoy_Patch",
            "UboatToolkit.Modules.Cheats_HullPatch",
            "UboatToolkit.Modules.Cheats_DeepDivePatch",
            "UboatToolkit.Modules.Cheats_NeverTiredPatch",
            "UboatToolkit.Modules.Cheats_NoEnergyLossPatch",
            "UboatToolkit.Modules.Controls_TonnagePatch",
            "UboatToolkit.Modules.Controls_TonnageStagePatch",
            "UboatToolkit.Modules.Controls_MapDepthPatch",
            "UboatToolkit.Modules.Controls_TorpedoMixPatch",
            "UboatToolkit.Modules.Controls_EdgeScrollPatch",
            "UboatToolkit.Modules.Controls_EdgeDeckPatch",
            "UboatToolkit.Modules.Fix_OrderEngineGearRange",
            "UboatToolkit.Modules.CourseKeys_Patch",
            "UboatToolkit.Modules.CourseKeys_SilentPreset",
            "UboatToolkit.Modules.CourseKeys_CompassStep",
            "UboatToolkit.Modules.Extras_OfficerNewPatch",
            "UboatToolkit.Modules.Extras_OfficerPromotedPatch",
            "UboatToolkit.Modules.Extras_OfficerCrewPatch",
            "UboatToolkit.Modules.Extras_OfficerHourPatch",
            "UboatToolkit.Modules.Extras_VoicePatch",
            "UboatToolkit.Modules.Extras_AbortStallPatch",
            "UboatToolkit.Modules.CrewAiThrottle_Patch",
            "UboatToolkit.Modules.CrewPathInvalid_Patch",
            "UboatToolkit.Modules.CrewWarp_Patch",
            "UboatToolkit.Modules.CrewCheats_HealthPatch",
            "UboatToolkit.Modules.CrewCheats_BleedoutPatch",
            "UboatToolkit.Modules.CrewCheats_SupplyPatch",
            "UboatToolkit.Modules.CrewGear_NoAutoCarryPatch",
            "UboatToolkit.Modules.CrewGear_NoFreeBedPatch",
            "UboatToolkit.Modules.CrewLod_FrameCall",
            "UboatToolkit.Modules.CrewLod_Background",
            "UboatToolkit.Modules.CrewLod_Analyze",
            "UboatToolkit.Modules.CrewLod_AnalyzeFast",
            "UboatToolkit.Modules.CrewLod_Deformer",
            "UboatToolkit.Modules.CrewSkills_BearingPatch",
            "UboatToolkit.Modules.CrewSkills_RangingPatch",
            "UboatToolkit.Modules.CrewSkills_TargetPatch",
            "UboatToolkit.Modules.AirNorm_Patch",
            "UboatToolkit.Modules.TightPack_NoSlot",
            "UboatToolkit.Modules.DeckCrew_HatchPatch",
            "UboatToolkit.Modules.DeckCrew_NoDrownPatch",
            "UboatToolkit.Modules.DeckCrew_GunPatch",
            "UboatToolkit.Modules.DeckCrew_NoRagdollPatch",
            "UboatToolkit.Modules.DeckGun_FireTranspiler",
            "UboatToolkit.Modules.DeckGun_ReloadTranspiler",
            "UboatToolkit.Modules.DeckGun_FireGate",
            "UboatToolkit.Modules.DeckGun_CanFire",
            "UboatToolkit.Modules.DeckGun_UseGunState",
            "UboatToolkit.Modules.EnemyHitLog",
            "UboatToolkit.Modules.Events_RadioPatch",
            "UboatToolkit.Modules.Events_NoAutoAlarmPatch",
            "UboatToolkit.Modules.ExplosionLimit_Patch",
            "UboatToolkit.Modules.ExtraFeatures_PromotePatch",
            "UboatToolkit.Modules.ExtraFeatures_SkillPointsPatch",
            "UboatToolkit.Modules.ExtraFeatures_TorpedoLoadPatch",
            "UboatToolkit.Modules.ExtraFeatures_LoadingBoostPatch",
            "UboatToolkit.Modules.Extras_PricePatch",
            "UboatToolkit.Modules.Extras_RepairPatch",
            "UboatToolkit.Modules.Extras_TorpedoHitPatch",
            "UboatToolkit.Modules.NoResearchRequirements_Patch",
            "UboatToolkit.Modules.NoResearchOfficer_Patch",
            "UboatToolkit.Modules.FadeReparent_Patch",
            "UboatToolkit.Modules.FarAIThrottle",
            "UboatToolkit.Modules.Fix_DisallowFollowersNull",
            "UboatToolkit.Modules.Fix_AdvancedEquipmentNoDetector",
            "UboatToolkit.Modules.Fix_DestinationReachedOffMesh",
            "UboatToolkit.Modules.Fix_DockRecruitsOffMesh",
            "UboatToolkit.Modules.Fix_ResourceGuiBoatChange",
            "UboatToolkit.Modules.SkipStartupIntros",
            "UboatToolkit.Modules.Fix_RemainingDistanceSafe",
            "UboatToolkit.Modules.Fix_LaunchersUiMissing",
            "UboatToolkit.Modules.Fix_MorphOutOfRange",
            "UboatToolkit.Modules.Fix_PortWindowSaveToggle",
            "UboatToolkit.Modules.Fix_MissingCargoSlot",
            "UboatToolkit.Modules.Fix_PeriscopeNoMaterial",
            "UboatToolkit.Modules.Fix_BerlinOffset",
            "UboatToolkit.Modules.Fix_BerlinZone",
            "UboatToolkit.Modules.Fix_SkipperPortTalk",
            "UboatToolkit.Modules.Fix_SkipperStory",
            "UboatToolkit.Modules.Fix_StoryContinue",
            "UboatToolkit.Modules.Fix_StoryTag",
            "UboatToolkit.Modules.Fix_RadioRewards",
            "UboatToolkit.Modules.Fix_SpeedCompression",
            "UboatToolkit.Modules.Fix_SpeedSpray",
            "UboatToolkit.Modules.Fix_SpeedDrag",
            "UboatToolkit.Modules.Fix_SpeedDragFull",
            "UboatToolkit.Modules.Fix_PauseBlur",
            "UboatToolkit.Modules.Fix_PauseOverlay",
            "UboatToolkit.Modules.GameFixes_SkipIntroTutorialPatch",
            "UboatToolkit.Modules.GameFixes_UnreadableColliderPatch",
            "UboatToolkit.Modules.GameFixes_NavmeshSnapPatch",
            "UboatToolkit.Modules.Fix_GunUnknownMode",
            "UboatToolkit.Modules.Fix_UpgradeNoSlot",
            "UboatToolkit.Modules.Guard_NoQueueOrders",
            "UboatToolkit.Modules.Guard_NoQueueJob",
            "UboatToolkit.Modules.Guard_CardsWithoutTable",
            "UboatToolkit.Modules.Guard_EnemyLauncher",
            "UboatToolkit.Modules.Guard_CeilingFade",
            "UboatToolkit.Modules.Guard_PortraitsMark",
            "UboatToolkit.Modules.Guard_PortraitsRun",
            "UboatToolkit.Modules.Guard_ScheduleGroup",
            "UboatToolkit.Modules.Guard_SquadTypeFence",
            "UboatToolkit.Modules.Guard_ScheduleSettings",
            "UboatToolkit.Modules.Guard_FollowerButtons",
            "UboatToolkit.Modules.Boarding_TogglePatch",
            "UboatToolkit.Modules.Boarding_ClosePatch",
            "UboatToolkit.Modules.GuardSkillTree",
            "UboatToolkit.Modules.GuardCreationSkillTab",
            "UboatToolkit.Modules.GameTuning_DevConsolePatch",
            "UboatToolkit.Modules.GameTuning_DevConsolePopupPatch",
            "UboatToolkit.Modules.GunShells",
            "UboatToolkit.Modules.GunShells_Collect",
            "UboatToolkit.Modules.GunAim_FirePatch",
            "UboatToolkit.Modules.GunGyro_Patch",
            "UboatToolkit.Modules.FppNavigatorTracker",
            "UboatToolkit.Modules.PassageStatePatch",
            "UboatToolkit.Modules.PassageSpeedPatch",
            "UboatToolkit.Modules.HatchDiagnostics",
            "UboatToolkit.Modules.Helm_WolfpackRetryPatch",
            "UboatToolkit.Modules.HelmCalm_Patch",
            "UboatToolkit.Modules.Hull_BallastAlwaysPatch",
            "UboatToolkit.Modules.Hull_HatchTextPatch",
            "UboatToolkit.Modules.Hull_HatchLockPatch",
            "UboatToolkit.Modules.Hull_LeakTwoMenPatch",
            "UboatToolkit.Modules.IconFix",
            "UboatToolkit.Modules.CameraParentFix",
            "UboatToolkit.Modules.IconFix_MissingLog",
            "UboatToolkit.Modules.Light_SetPresetPatch",
            "UboatToolkit.Modules.Light_StartPatch",
            "UboatToolkit.Modules.Light_ApplyColourPatch",
            "UboatToolkit.Modules.Light_EmergencyColourPatch",
            "UboatToolkit.Modules.Light_SwitchUpdatePatch",
            "UboatToolkit.Modules.Light_CrewNoisePatch",
            "UboatToolkit.Modules.Light_SleepPatch",
            "UboatToolkit.Modules.Light_ElectricPatch",
            "UboatToolkit.Modules.Light_BeforeSavePatch",
            "UboatToolkit.Modules.Light_PreferredPatch",
            "UboatToolkit.Modules.Light_RegisterActionPatch",
            "UboatToolkit.Modules.Light_SwitchPanelPatch",
            "UboatToolkit.Modules.Loading_ResupplyPatch",
            "UboatToolkit.Modules.Map_SkinsPatch",
            "UboatToolkit.Modules.Map_CurrentSkinPatch",
            "UboatToolkit.Modules.Map_LabelsPatch",
            "UboatToolkit.Modules.Map_BuildSquaresPatch",
            "UboatToolkit.Modules.Map_SquarePatch",
            "UboatToolkit.Modules.Map_SettingsListPatch",
            "UboatToolkit.Modules.MilkCows_RadioRequest",
            "UboatToolkit.Modules.Night_AdaptationPatch",
            "UboatToolkit.Modules.OrbitWater_Patch",
            "UboatToolkit.Modules.SupplyPriceMenu_Patch",
            "UboatToolkit.Modules.UniformFix_Apply",
            "UboatToolkit.Modules.UniformFix_Dirty",
            "UboatToolkit.Modules.Bifrost_Throttle",
            "UboatToolkit.Modules.Bifrost_DepthCharge",
            "UboatToolkit.Modules.MissingWearable_Patch",
            "UboatToolkit.Modules.PortDefense_HitPatch",
            "UboatToolkit.Modules.PortMines_TubeLog",
            "UboatToolkit.Modules.Pump_Patch",
            "UboatToolkit.Modules.Pump_TooltipPatch",
            "UboatToolkit.Modules.QueueProfiler",
            "UboatToolkit.Modules.Quiet_NotificationPatch",
            "UboatToolkit.Modules.Quiet_BarOpenPatch",
            "UboatToolkit.Modules.Quiet_RecentOpenPatch",
            "UboatToolkit.Modules.Quiet_TooltipPatch",
            "UboatToolkit.Modules.Quiet_InteriorTooltipPatch",
            "UboatToolkit.Modules.Quiet_VoicePatch",
            "UboatToolkit.Modules.Quiet_BannerVisibilityPatch",
            "UboatToolkit.Modules.Quiet_BannerHoverStopPatch",
            "UboatToolkit.Modules.Quiet_BannerDisplayModePatch",
            "UboatToolkit.Modules.Radio_FasterPatch",
            "UboatToolkit.Modules.Radio_UndetectablePatch",
            "UboatToolkit.Modules.RadioDecrypt_StoryPatch",
            "UboatToolkit.Modules.RadioDecrypt_JournalPatch",
            "UboatToolkit.Modules.RainGear_Patch",
            "UboatToolkit.Modules.Reticle_ColourPatch",
            "UboatToolkit.Modules.Reticle_OverlayPatch",
            "UboatToolkit.Modules.RouteLeadPatch",
            "UboatToolkit.Modules.SaveTools_LoadSpeedPatch",
            "UboatToolkit.Modules.SaveTools_IdlePatch",
            "UboatToolkit.Modules.SaveTools_QueueCapPatch",
            "UboatToolkit.Modules.SaveTools_MemoryPatch",
            "UboatToolkit.Modules.LoadSpin_WorkerPatch",
            "UboatToolkit.Modules.LoadSpin_SceneDelayPatch",
            "UboatToolkit.Modules.ScopeRetainer",
            "UboatToolkit.Modules.ScopeUzoZoom",
            "UboatToolkit.Modules.ScopeBundleTimer",
            "UboatToolkit.Modules.SeaFloor_TerrainPatch",
            "UboatToolkit.Modules.SeaFloor_EncounterPatch",
            "UboatToolkit.Modules.SeaFloor_BottomPatch",
            "UboatToolkit.Modules.SeafloorLoad_ScanPatch",
            "UboatToolkit.Modules.SeaLayer_DepthPatch",
            "UboatToolkit.Modules.SeaLayer_ShadowPatch",
            "UboatToolkit.Modules.SeaLayer_HearingPatch",
            "UboatToolkit.Modules.Sensor_RadioDepthPatch",
            "UboatToolkit.Modules.Sensor_ExactTargetsPatch",
            "UboatToolkit.Modules.Sensor_InstantSolutionPatch",
            "UboatToolkit.Modules.Sensor_ArmourPatch",
            "UboatToolkit.Modules.Sensor_OwnShellsPatch",
            "UboatToolkit.Modules.Sensor_OwnShellsCollectPatch",
            "UboatToolkit.Modules.HydroSame_Patches",
            "UboatToolkit.Modules.SkillIconFix",
            "UboatToolkit.Modules.Stations_FollowersPatch",
            "UboatToolkit.Modules.Stations_FollowerDiagPatch",
            "UboatToolkit.Modules.Strike_TubeTooltipPatch",
            "UboatToolkit.Modules.TextureRace_Patch",
            "UboatToolkit.Modules.Torpedoes_NoDudPatch",
            "UboatToolkit.Modules.Torpedoes_ReloadPatch",
            "UboatToolkit.Modules.Torpedoes_KeepMaintainedPatch",
            "UboatToolkit.Modules.Torpedoes_KeepProgressPatch",
            "UboatToolkit.Modules.Traffic_ObservationPatch",
            "UboatToolkit.Modules.UiFixes_TooltipParent",
            "UboatToolkit.Modules.UiFixes_PoolAnimators",
            "UboatToolkit.Modules.UiFixes_FlamesDetached",
            "UboatToolkit.Modules.UiFixes_FlamesSpawn",
            "UboatToolkit.Modules.TooltipDiag_Open",
            "UboatToolkit.Modules.TooltipDiag_Eye",
            "UboatToolkit.Modules.UiFixes_EmptyTooltip",
            "UboatToolkit.Modules.UiFixes_PlainTooltipDiag",
            "UboatToolkit.Modules.UnderFire_GunPatch",
            "UboatToolkit.Modules.UniqueNames_Patch",
            "UboatToolkit.Modules.VoiceLimit_SayPatch",
            "UboatToolkit.Modules.Weapons_EnemyFirePatch",
            "UboatToolkit.Modules.Weapons_FirePatch",
            "UboatToolkit.Modules.Weapons_SoundPatch",
            "UboatToolkit.Modules.Weapons_InfiniteReload",
            "UboatToolkit.Modules.Weapons_PreferPatch",
            "UboatToolkit.Modules.Weapons_KeepGunModePatch",
            "UboatToolkit.Modules.WolfpackStep_Near",
            "UboatToolkit.Modules.WolfpackStep_Far",
            "UboatToolkit.Modules.Wolfpack_ParseReportPatch",
            "UboatToolkit.Modules.Wolfpack_StartPatch",
            "UboatToolkit.Modules.Wolfpack_MissionPatch",
            "UboatToolkit.Modules.Warthog.WarthogAir_SilentPatch",
            "UboatToolkit.Modules.Warthog.WarthogAir_OxygenPatch",
            "UboatToolkit.Modules.Warthog.WarthogHatches_AirPatch",
            "UboatToolkit.Modules.Warthog.WarthogHatches_LadderLog",
            "UboatToolkit.Modules.Warthog.WarthogNetCutter_CollisionPatch",
            "UboatToolkit.Modules.Warthog.WarthogSnorkel_UpdatePatch",
            "UboatToolkit.Modules.Warthog.WarthogSonar_UnmannedPatch",
            "UboatToolkit.Modules.Warthog.WarthogSonar_ExactPatch",
            "UboatToolkit.Modules.Warthog.WarthogSonar_ArcPatch",
            "UboatToolkit.Modules.Warthog.WarthogSonar_QuietPingPatch",
            "UboatToolkit.Modules.Warthog.WarthogWeapons_RemotePatch",
            "UboatToolkit.Modules.Warthog.WarthogWeapons_FirePatch",
            "UboatToolkit.Modules.StationCtl.StationCursorZoomPatch",
            "UboatToolkit.Modules.StationCtl.DeviceStationZoomPatch",
            "UboatToolkit.Modules.StationCtl.HydrophoneZoomPatch",
            "UboatToolkit.Modules.StationCtl.DialDragPatch",
            "UboatToolkit.Modules.StationCtl.DialScrollSuppressionPatch",
            "UboatToolkit.Modules.StationCtl.TooltipSuppressionPatch",
            "UboatToolkit.Modules.StationCtl.HydrophoneBearingUiPatch",
            "UboatToolkit.Modules.StationCtl.AobRealtimeDragPatch",
            "UboatToolkit.Modules.SaveCats.PlayerCareerStartDatePatch",
            "UboatToolkit.Modules.SaveCats.SavesManagerLoadPatch",
            "UboatToolkit.Modules.SaveCats.SavesManagerSavePatch",
            "UboatToolkit.Modules.SaveCats.SavesManagerGetSavesPatch",
            "UboatToolkit.Modules.SaveCats.GameStateBaseUiFetchSavesPatch",
            "UboatToolkit.Modules.SaveCats.GameStateItemUiDuplicatePatch",
            "UboatToolkit.Modules.SaveCats.CscUiProbe",
            "UboatToolkit.Modules.ScopeUi.RecordViewModePatch",
            "UboatToolkit.Modules.ScopeUi.RecordZoomPatch",
            "UboatToolkit.Modules.ScopeUi.RestoreOnOpenPatch",
            "UboatToolkit.Modules.ScopeUi.LightStationButtonPatch",
            "UboatToolkit.Modules.ScopeUi.StationButtonTargetPatch"
        };

        internal static System.Collections.Generic.List<System.Type> Sort(System.Collections.Generic.IEnumerable<System.Type> types)
        {
            var pos = new System.Collections.Generic.Dictionary<string, int>();
            for (int i = 0; i < Names.Length; i++) pos[Names[i]] = i;
            var list = new System.Collections.Generic.List<System.Type>(types);
            list.Sort(delegate (System.Type a, System.Type b)
            {
                int ia, ib;
                if (!pos.TryGetValue(a.FullName ?? a.Name, out ia)) ia = int.MaxValue;
                if (!pos.TryGetValue(b.FullName ?? b.Name, out ib)) ib = int.MaxValue;
                return ia != ib ? ia.CompareTo(ib) : string.CompareOrdinal(a.FullName, b.FullName);
            });
            return list;
        }
    }
}

// ====================================================================================================
// Abschnitt: Core/Search.cs
// ====================================================================================================
namespace UboatToolkit
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Text;

    // 0.4.3 menu search, rebuilt (DoMe 2026-11-17: "überarbeite mal deine Suchfunktion, das trifft nicht nur das
    // Beispiel"). One place for everything the search knows:
    //  * searched text per setting: German name, English name, main switch it belongs to, topic, sub-topic, tab, ini key
    //  * upper/lower case, ä/ae, ß/ss, hyphens and brackets do not matter
    //  * parts of compound words are found ("ruder" -> Tiefenruder, Strahlruder), word endings do not matter
    //  * typing mistakes are forgiven (1 wrong letter from 5 letters, 2 from 8)
    //  * words that mean the same are found too (one table, both directions)
    //  * several words: every word must fit somewhere, any order
    //  * best hits first; with no hit the closest known word is offered
    // The result is kept until the search text changes, so the menu does not get slower while a search is shown.
    internal static class Search
    {
        private class Entry { public Opt O; public string Label, Other; public string[] LabelTokens; }

        private static List<Entry> index;
        private static List<string> vocab;
        private static string cachedQuery;
        private static readonly Dictionary<Opt, int> cached = new Dictionary<Opt, int>();
        public static string Suggestion = "";

        // Words that mean the same (already without umlauts). Both directions.
        private static readonly string[][] synonyms =
        {
            new[] { "lenz", "pumpe", "bilge", "pump" },
            new[] { "wasser", "flut", "leck", "wassereinbruch", "water", "flood", "leak" },
            new[] { "rohr", "torpedorohr", "tube", "launcher" },
            new[] { "torpedo", "aal", "aale", "zaunkoenig" },
            new[] { "strahlruder", "thruster", "querstrahl", "manoevrier" },
            new[] { "ruder", "steuer", "rudder", "lenk" },
            new[] { "schleichfahrt", "leise", "silent", "schleich", "geraeusch", "laerm", "noise" },
            new[] { "flak", "aa", "flugabwehr", "luftabwehr", "flugzeug", "aircraft" },
            new[] { "tauchen", "tiefe", "dive", "depth", "abtauchen", "tauchtiefe" },
            new[] { "auftauchen", "oberflaeche", "surface", "aufgetaucht" },
            new[] { "akku", "batterie", "strom", "energie", "battery", "energy", "elektro" },
            new[] { "crew", "mannschaft", "matrose", "besatzung", "offizier", "leute", "sailor", "officer" },
            new[] { "geschuetz", "kanone", "deckgeschuetz", "gun", "bordkanone", "artillerie" },
            new[] { "munition", "ammo", "granate", "geschoss", "shell", "magazin" },
            new[] { "sehrohr", "periskop", "periscope" },
            new[] { "fernglas", "binocular", "uzo" },
            new[] { "diesel", "treibstoff", "sprit", "fuel", "kraftstoff", "tank" },
            new[] { "motor", "maschine", "antrieb", "engine", "schraube", "propeller" },
            new[] { "geschwindigkeit", "speed", "tempo", "fahrt", "schneller", "knoten" },
            new[] { "erfolg", "achievement", "errungenschaft", "steam" },
            new[] { "auftrag", "mission", "aufgabe", "einsatz", "assignment" },
            new[] { "geleit", "konvoi", "convoy", "eskorte", "geleitschutz", "escort" },
            new[] { "lager", "stapel", "vorrat", "inventar", "storage", "stack", "platz", "slot" },
            new[] { "essen", "nahrung", "proviant", "food", "verpflegung", "kueche" },
            new[] { "horch", "hydrophon", "sonar", "ghg", "hydrophone", "lausch" },
            new[] { "radar", "funkmess", "ortung", "detect", "entdeck", "sichtbar", "kontakt" },
            new[] { "funk", "radio", "funkspruch", "enigma" },
            new[] { "licht", "lampe", "beleuchtung", "light", "rotlicht", "blaulicht", "scheinwerfer" },
            new[] { "reparatur", "repar", "schaden", "damage", "instand", "kaputt", "defekt" },
            new[] { "geld", "budget", "preis", "kosten", "money", "kauf", "haendler", "shop" },
            new[] { "ansehen", "ruf", "reputation", "prestige" },
            new[] { "erfahrung", "xp", "level", "skill", "faehigkeit", "talent" },
            new[] { "karte", "map", "navigation", "kurs", "wegpunkt" },
            new[] { "zeit", "time", "zeitraffer", "beschleunig", "compression" },
            new[] { "kamera", "camera", "ansicht", "sicht", "view", "zoom" },
            new[] { "pazifist", "friedlich", "gegner", "feind", "enemy" },
            new[] { "luft", "sauerstoff", "air", "oxygen", "co2", "lueftung", "lüftung", "ventilation", "absorber", "kalipatrone", "atmen" },
            new[] { "druck", "pressure", "druckkoerper", "rumpf", "hull", "huelle" },
            new[] { "hafen", "port", "dock", "bunker", "basis", "stuetzpunkt" },
            new[] { "laden", "nachladen", "reload", "beladen", "loading" },
            new[] { "gegner", "feind", "enemy", "zerstoerer", "eskorte" },
            new[] { "mine", "minen", "sprengladung" },
            new[] { "wasserbombe", "wabo", "depthcharge", "bombe" },
            new[] { "krank", "verletzt", "wunde", "sani", "arzt", "medic", "verband", "heilen" },
            new[] { "muede", "schlaf", "ruhe", "erschoepf", "sleep", "tired", "moral", "stimmung" },
            new[] { "taste", "tastatur", "key", "hotkey", "kuerzel", "shortcut" },
            new[] { "log", "protokoll", "diagnose", "fehler", "debug", "konsole", "console" },
            new[] { "ton", "sound", "geraeusch", "lautstaerke", "audio", "stimme" },
            new[] { "speichern", "save", "spielstand", "autosave", "sicherung" },
            new[] { "leistung", "fps", "ruckel", "lag", "performance", "schnell" },
            new[] { "anzeige", "hud", "instrument", "tacho", "messer", "gauge" },
            new[] { "luke", "luken", "hatch", "schott", "tuer" },
            new[] { "anker", "anchor" },
            new[] { "schnorchel", "snorkel" },
            new[] { "trimm", "trim", "ballast", "tauchzelle", "regelzelle" },
            new[] { "unsterblich", "unverwundbar", "gott", "god", "immortal", "unzerstoerbar" },
            new[] { "unendlich", "unbegrenzt", "infinite", "unlimited", "endlos" },
            new[] { "automatisch", "auto", "selbst", "von allein" },
        };

        public static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.ToLowerInvariant().Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss");
            var sb = new StringBuilder(s.Length);
            foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
            return sb.ToString();
        }

        // "CheatPumpPower" -> "cheat pump power cheatpumppower"
        private static string SplitKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < key.Length; i++)
            {
                if (i > 0 && char.IsUpper(key[i]) && !char.IsUpper(key[i - 1])) sb.Append(' ');
                sb.Append(key[i]);
            }
            return sb.ToString() + " " + key;
        }

        private static string Stem(string w)
        {
            if (w.Length > 5 && (w.EndsWith("en") || w.EndsWith("er") || w.EndsWith("es") || w.EndsWith("em"))) return w.Substring(0, w.Length - 2);
            if (w.Length > 4 && (w.EndsWith("e") || w.EndsWith("n") || w.EndsWith("s"))) return w.Substring(0, w.Length - 1);
            return w;
        }

        private static void Build()
        {
            index = new List<Entry>();
            var seen = new HashSet<string>();
            vocab = new List<string>();
            Dictionary<string, string> en = null;
            try { en = LangEn.Build(); } catch { }
            Func<string, string> En = t => { string v; return en != null && t != null && en.TryGetValue(t, out v) ? v : ""; };
            foreach (var o in Settings.All)
            {
                var e = new Entry { O = o };
                e.Label = " " + Norm(o.Label) + " ";
                e.LabelTokens = e.Label.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                e.Other = " " + Norm(En(o.Label) + " " + SplitKey(o.Key) + " " + o.Group + " " + o.Sub + " " + o.Tab
                    + (o.Parent != null ? " " + o.Parent.Label + " " + En(o.Parent.Label) : "") + (o.Tab == Settings.Cheat ? " cheat" : "") + (o.IsKey ? " taste" : "")) + " ";
                index.Add(e);
                foreach (var t in (e.Label + e.Other).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    if (t.Length >= 3 && !char.IsDigit(t[0]) && seen.Add(t)) vocab.Add(t);
            }
        }

        // Edit distance with swapped neighbours counted as one mistake; stops early above "max".
        private static int Distance(string a, string b, int max)
        {
            int n = a.Length, m = b.Length;
            if (Math.Abs(n - m) > max) return max + 1;
            var d = new int[n + 1, m + 1];
            for (int i = 0; i <= n; i++) d[i, 0] = i;
            for (int j = 0; j <= m; j++) d[0, j] = j;
            for (int i = 1; i <= n; i++)
            {
                int best = int.MaxValue;
                for (int j = 1; j <= m; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    int v = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                    if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) v = Math.Min(v, d[i - 2, j - 2] + 1);
                    d[i, j] = v;
                    if (v < best) best = v;
                }
                if (best > max) return max + 1;
            }
            return d[n, m];
        }

        private static int Allowed(string w) => w.Length >= 8 ? 2 : w.Length >= 5 ? 1 : 0;

        // Known words that look like "w" (the whole word, or a piece of a compound word of about the same length).
        private static List<string> Fuzzy(string w)
        {
            var res = new List<string>();
            int max = Allowed(w);
            if (max == 0) return res;
            foreach (var t in vocab)
            {
                if (t.Length < w.Length - max) continue;
                bool hit = false;
                if (Math.Abs(t.Length - w.Length) <= max) hit = Distance(w, t, max) <= max;
                if (!hit && t.Length > w.Length)
                    for (int len = w.Length - 1; len <= w.Length + 1 && !hit; len++)
                        for (int s = 0; s + len <= t.Length && !hit; s++)
                            if (t[s] == w[0] || (s + 1 < t.Length && t[s + 1] == w[0]) || t[s] == w[1])
                                hit = Distance(w, t.Substring(s, len), max) <= max;
                if (hit) res.Add(t);
                if (res.Count >= 40) break;
            }
            return res;
        }

        // 0.4.25 (DoMe 2026-11-18: "lenz" and "pumpe" gave different hits). The words that stand for the typed word:
        // the word itself and, if it belongs to a row of the table, every word of that row. Every word of a row gives
        // exactly the same list, so the hits and their order no longer depend on which of the words was typed.
        private static List<string> Group(string w, string stem)
        {
            var res = new List<string> { w };
            bool exact = false;
            foreach (var row in synonyms)
                foreach (var x in row)
                    if (x == w || x == stem) { exact = true; foreach (var y in row) if (!res.Contains(y)) res.Add(y); break; }
            if (exact) return res;
            // no row has the word itself: a row fits when the word is the beginning of one of its words or the other way round
            foreach (var row in synonyms)
            {
                bool fits = false;
                foreach (var x in row)
                    if ((w.Length >= 4 && x.StartsWith(w)) || (x.Length >= 4 && w.StartsWith(x))) { fits = true; break; }
                if (fits) foreach (var y in row) if (!res.Contains(y)) res.Add(y);
            }
            return res;
        }

        // How well one word fits one setting, without any helpers.
        private static int Direct(Entry e, string w)
        {
            foreach (var t in e.LabelTokens) if (t == w) return 120;
            // very short words ("aa", "t5", "88") only count as whole words, else they sit inside half the menu
            if (w.Length < 3) return e.Other.Contains(" " + w + " ") ? 50 : 0;
            foreach (var t in e.LabelTokens) if (t.StartsWith(w)) return 100;
            if (e.Label.Contains(w)) return 80;
            string stem = Stem(w);
            if (stem.Length >= 4 && stem != w && e.Label.Contains(stem)) return 70;
            if (e.Other.Contains(w)) return 50;
            if (stem.Length >= 4 && stem != w && e.Other.Contains(stem)) return 45;
            return 0;
        }

        // Best fit over all words of the group. With "fuzzy" (only used when the word found nothing at all) look-alikes count too.
        private static int ScoreWord(Entry e, List<string> group, List<string> fuzzy)
        {
            int best = 0;
            foreach (var x in group) { int s = Direct(e, x); if (s > best) best = s; }
            if (best > 0 || fuzzy == null) return best;
            foreach (var x in fuzzy) if (e.Label.Contains(x)) return 25;
            foreach (var x in fuzzy) if (e.Other.Contains(x)) return 15;
            return 0;
        }

        // Proof in the log, once per start: every word of a row must give the same hits in the same order.
        private static bool tested;
        private static void SelfTest()
        {
            if (tested) return;
            tested = true;
            try
            {
                var count = new Dictionary<string, int>();
                foreach (var row in synonyms) foreach (var x in row) { int n; count.TryGetValue(x, out n); count[x] = n + 1; }
                int rows = 0, same = 0; var bad = new StringBuilder();
                foreach (var row in synonyms)
                {
                    string first = null, firstWord = null; bool ok = true; int hits = 0;
                    foreach (var x in row)
                    {
                        if (count[x] != 1) continue;                       // a word that stands in two rows belongs to both topics
                        var res = Compute(x);
                        var keys = new List<string>();
                        foreach (var kv in res) keys.Add(kv.Value.ToString("000") + kv.Key.Key);
                        keys.Sort(StringComparer.Ordinal);
                        string sig = string.Join("|", keys.ToArray());
                        if (first == null) { first = sig; firstWord = x; hits = res.Count; }
                        else if (sig != first) { ok = false; bad.Append(" [").Append(firstWord).Append(" / ").Append(x).Append("]"); break; }
                    }
                    if (first == null) continue;
                    rows++; if (ok) same++;
                    if (row[0] == "lenz") Util.Log("Suche: \"lenz\", \"pumpe\", \"bilge\", \"pump\" liefern " + (ok ? "dieselben " + hits + " Treffer in derselben Reihenfolge" : "NICHT dieselben Treffer"));
                }
                Util.Log("Suche: Selbsttest, " + same + " von " + rows + " Wortgruppen liefern für jedes ihrer Wörter dieselben Treffer" + (bad.Length > 0 ? ". Abweichend:" + bad : ""));
            }
            catch (Exception ex) { Util.Log("Suche: Selbsttest Fehler " + ex.GetType().Name); }
        }

        private static Dictionary<Opt, int> Compute(string q) { return Compute(q, null); }
        private static Dictionary<Opt, int> Compute(string q, bool[] anyOut)
        {
            var res = new Dictionary<Opt, int>();
            var words = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return res;
            var groups = new List<string>[words.Length];
            var fuzz = new List<string>[words.Length];
            var any = new bool[words.Length];
            for (int i = 0; i < words.Length; i++) groups[i] = Group(words[i], Stem(words[i]));
            // first without look-alikes: which words find something by themselves or through their row?
            foreach (var e in index)
                for (int i = 0; i < words.Length; i++)
                    if (!any[i] && ScoreWord(e, groups[i], null) > 0) any[i] = true;
            // typing mistakes are only forgiven for a word that found nothing at all
            for (int i = 0; i < words.Length; i++) if (!any[i]) fuzz[i] = Fuzzy(words[i]);
            foreach (var e in index)
            {
                int total = 0; bool all = true;
                for (int i = 0; i < words.Length; i++)
                {
                    int sc = ScoreWord(e, groups[i], fuzz[i]);
                    if (sc > 0) any[i] = true;
                    if (sc == 0) all = false;
                    total += sc;
                }
                if (all) res[e.O] = total;
            }
            if (anyOut != null) for (int i = 0; i < words.Length && i < anyOut.Length; i++) anyOut[i] = any[i];
            return res;
        }

        // All settings that fit the search text, with a score (higher = better hit).
        public static Dictionary<Opt, int> Run(string query)
        {
            if (index == null || index.Count != Settings.All.Count) { Build(); cachedQuery = null; }
            SelfTest();
            string q = Norm(query).Trim();
            if (q == cachedQuery) return cached;
            cachedQuery = q; cached.Clear(); Suggestion = "";
            var words = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return cached;
            var any = new bool[words.Length];
            foreach (var kv in Compute(q, any)) cached[kv.Key] = kv.Value;
            if (cached.Count == 0)
            {
                // which word found nothing at all? offer the closest known word for it
                var sb = new StringBuilder();
                for (int i = 0; i < words.Length; i++)
                {
                    string best = words[i];
                    if (!any[i])
                    {
                        int bestD = 4;
                        foreach (var t in vocab)
                        {
                            if (Math.Abs(t.Length - words[i].Length) > 3) continue;
                            int d = Distance(words[i], t, 3);
                            if (d < bestD) { bestD = d; best = t; }
                        }
                    }
                    sb.Append(i > 0 ? " " : "").Append(best);
                }
                Suggestion = sb.ToString() == q ? "" : sb.ToString();
            }
            return cached;
        }
    }
}

// ====================================================================================================
// Abschnitt: Core/Settings.cs
// ====================================================================================================
namespace UboatToolkit
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Globalization;
    using global::System.IO;
    using global::System.Text;
    using global::UnityEngine;

    // One setting. Bool settings use Value 0/1.
    public class Opt
    {
        public string Key, Tab, Label, Group = "";
        public bool IsBool, IsKey;
        public string Text = "";
        public float Value, Default, Min, Max, Step;
        public bool On => Value > 0.5f;
        // 0.4.22 (option audit): ">>" switches are one-time actions. They are never written as "on" into the file and
        // never read as "on" (a stored click flooded a room / spawned a convoy again at the next load).
        public bool IsAction => IsBool && Label != null && Label.TrimStart().StartsWith(">>");
        // 0.3.75 menu: sub-topic inside the topic, the main switch a fine setting belongs to, its fine settings,
        // and (for settings with only a few values) the names of the values, e.g. 0 = aus, 1 = fragen.
        public string Sub = "";
        public Opt Parent;
        public readonly List<Opt> Children = new List<Opt>();
        public string[] ChoiceNames;
        public string SearchText;
        public Func<string> Extra;   // 0.4.4: live text shown behind the value in the menu
    }

    // All settings of the mod. Stored in UboatToolkit.ini (optional; defaults are used without it).
    // The in-game menu (F10) and the INI file both read and write this list.
    public static class Settings
    {
        public const string Topics = "Einstellungen";
        public const string QoL = "QoL";
        public const string Cheat = "Cheat";
        public const string Tools = "Tools";
        public const string KeysTab = "Tasten";

        public static readonly List<Opt> All = new List<Opt>();
        private static readonly Dictionary<string, Opt> byKey = new Dictionary<string, Opt>(StringComparer.OrdinalIgnoreCase);

        public static string Folder => Path.Combine(Application.persistentDataPath, "UboatToolkit");
        public static string FilePath => Path.Combine(Application.persistentDataPath, "UboatToolkit.ini");

        static Settings()
        {
            // ---------- QoL ----------
            Bool(QoL, "AwashGuns", "Vorgeflutet: Turmluke bleibt offen, Crew darf an Deck (#24)", true);
            Num(QoL, "RadioSpeed", "Funk: so viel schneller senden (1 = normal) (#11)", 10, 1, 100, 1);
            Bool(QoL, "RadioUndetectable", "Funk: Gegner kann dich nicht anpeilen (#12)", true);
            Bool(QoL, "RadioDecryptAll", "Funk: alle Funksprüche lesbar (auch fremd verschlüsselte)", true);
            Bool(QoL, "SeafloorAlways", "Meeresboden in der Nähe immer voll laden (auch tiefer als 650 m, für Sicht + Sonar/Tiefenmesser)", true);
            Num(QoL, "SeafloorRadius", "  Meeresboden: Radius in km", 2, 0.5f, 4, 0.5f);
            Bool(QoL, "AutoLight", "Licht automatisch umschalten (#15)", true);
            Bool(QoL, "AutoLightNightRed", "  aufgetaucht Rotlicht", true);
            Bool(QoL, "AutoLightHuntedBlue", "  getaucht Blaulicht", true);
            Bool(QoL, "AutoSearchlight", "Suchscheinwerfer automatisch (nachts, aufgetaucht) (#16)", false);
            Bool(QoL, "AutoHatches", "Luken automatisch schließen (Tauchen / gejagt) (#22)", true);
            Num(QoL, "OpticsZoom", "Sehrohr + Fernglas: mehr Zoom (1 = normal) (#18)", 2, 1, 10, 0.5f);
            Num(QoL, "OpticsStabilization", "Sehrohr + Fernglas: Ruhe (0 = Spiel, 1 = ganz ruhig) (#19)", 1, 0, 1, 0.1f);
            Num(QoL, "PeriscopeHeight", "Sehrohr: max. Ausfahrhöhe in m (0 = Spielwert) (#20)", 12, 0, 12, 0.5f);
            Bool(QoL, "SailorsNavTable", "Matrosen dürfen an die Kartenstation (#14)", true);
            Bool(QoL, "NoCrewCollisions", "Crew läuft durcheinander durch (#21)", true);
            Bool(QoL, "CrewLod", "Crew-Detailstufen: Männer, die du nicht siehst, überlegen seltener (mehr FPS, Arbeit läuft normal weiter)", true);
            Num(QoL, "CrewLodFrames", "  nicht sichtbare Männer überlegen nur jedes x. Bild (1 = jedes Bild)", 6, 1, 60, 1);
            Num(QoL, "CrewLodIdleFrames", "  Männer ohne Arbeit (stehen, ruhen, rauchen, schlafen) überlegen nur jedes x. Bild, auch sichtbar (1 = aus)", 10, 1, 60, 1);
            Bool(QoL, "BallastDieselBlow", "Ballast: aufgetaucht Tanks mit Diesel-Abgas leerblasen (spart Pressluft) (#23)", true);
            Num(QoL, "BallastDieselBlowRate", "  Diesel-Ausblasen: % Tank pro Sekunde", 2, 0.5f, 20, 0.5f);
            Bool(QoL, "SnorkelKeepHead", "Schnorchel: liegt der Kopf unter Wasser, Zieltiefe in kleinen Schritten anheben (nur bei aktivem Schnorchel)", true);
            Num(QoL, "SnorkelAirPlus", "Schnorchel: liefert immer den Luft-Verbrauch und x dazu (Zahl wie in der Luft-Anzeige des Spiels, pro Minute; 0 = Spiel)", 10, 0, 100, 1);
            Num(QoL, "BallastFastDive", "Ballast: schneller fluten beim Tauchen (1 = normal) (#23)", 1, 1, 5, 0.25f);
            Bool(QoL, "NoAutoCarryWounded", "Verletzte nicht automatisch wegtragen", false);
            Bool(QoL, "AutoFirstAid", "Verletzte/Kranke automatisch versorgen (Erste Hilfe) (#29)", true);
            Num(QoL, "AutoFirstAidDelay", "  Sekunden warten, bevor ein freier Mann hilft", 10, 0, 120, 5);
            Bool(QoL, "AutoFetchMedKit", "  Helfer holt sich selbst einen Verbandskasten aus dem Lager", true);
            Bool(QoL, "AutoFirstAidSkills", "  Skills beachten: Sanitäts-Ausbildung, Funker, Läufer zuerst", true);
            Bool(QoL, "AutoDamageControl", "Schadenskontrolle automatisch: bei Alarm + Schaden (#30)", true);
            Num(QoL, "AutoDamageControlCount", "  So viele Offiziere schalten um", 1, 1, 4, 1);
            Bool(QoL, "AutoDamageControlSkills", "  Skills beachten: Handwerker + Sparsam zuerst, Motor-Spezialist bleibt", true);
            Bool(QoL, "AutoDamageControlRadioman", "  Auch der Funker darf umschalten (sonst nur Ingenieure)", false);
            Bool(QoL, "AutoWorkshop", "Werkstatt automatisch: Schrott wird eingeräumt, Ersatzteile werden gebaut (#31)", true);
            Bool(QoL, "AntiGrounding", "Zeitraffer stoppt, bevor das Boot auf Land fährt (#32)", true);
            Num(QoL, "AntiGroundingSeconds", "  Warnzeit in Sekunden (echte Zeit) vor dem Land", 10, 3, 60, 1);
            Num(QoL, "AntiGroundingMaxX", "  Zeitraffer dann begrenzen auf x (1 = ganz aus)", 60, 1, 1000, 1);
            Num(QoL, "AntiGroundingKm", "  nur warnen, wenn Land näher als (km)", 5, 0.5f, 50, 0.5f);
            Bool(QoL, "KeepHelm", "Steuerstand immer besetzt: freier Matrose geht hin (ohne Steuermann reagiert das Ruder nicht)", true);
            Bool(QoL, "HelmCalm", "  Steuerstand wechselt nicht ständig den Platz (Zentrale/Turm), wenn das Angriffssehrohr besetzt wird", true);
            Num(QoL, "HelmCalmSeconds", "    frühestens nach ... Sekunden wieder wechseln", 60f, 5f, 600f, 5f);
            Bool(QoL, "KeepDepthStation", "Tiefenruder immer besetzt: freier Matrose geht hin (#34)", true);
            Bool(QoL, "KeepNavTable", "Kartentisch immer besetzt (spart Diesel): freier Matrose geht hin (#35)", true);
            Num(QoL, "StationDelay", "  Sekunden leer, bevor jemand geschickt wird", 15, 0, 120, 5);
            Bool(QoL, "EchoSailor", "Echolot als Matrosen-Aufgabe: unter Wasser geht ein freier Matrose hin und pingt", false);
            Bool(QoL, "EchoSailorQuiet", "  Bei Alarm nicht pingen (verrät sonst das Boot)", true);
            Num(QoL, "EchoSailorSeconds", "  Alle x Sekunden (Spielzeit läuft im Zeitraffer schneller)", 60, 10, 600, 10);
            Bool(QoL, "AutoManGuns", "Deckgeschütz automatisch besetzen (aufgetaucht bei Alarm)", false);
            Bool(QoL, "AutoManFlak", "Flak automatisch besetzen (aufgetaucht bei Alarm)", false);
            Bool(QoL, "AutoManEngines", "Maschinenraum automatisch besetzen, wenn ein Motor läuft", false);
            Bool(QoL, "RudderLockNote", "Hinweis, wenn das Ruder von Hand festgestellt ist und das Boot im Kreis fährt", true);
            Bool(QoL, "HatchSafety", "Luken-Sicherung: Turmluke unter Wasser immer schließen (auch wenn noch jemand an Deck ist)", true);
            Bool(QoL, "AutoManHydrophone", "Horcher automatisch besetzen (getaucht oder bei Alarm)", true);
            Bool(QoL, "AutoManRadio", "Funkraum automatisch besetzen (wenn das Funkgerät empfangen kann)", true);
            Bool(QoL, "FollowersAuto", "Matrosen pro Offizier automatisch: alle Matrosen an Bord passen in die Gruppen (kein 10/6 mehr)", true);
            Num(QoL, "MaxFollowers", "Matrosen pro Offizier höchstens (0 = Spiel-Wert)", 0, 0, 20, 1);
            Bool(QoL, "FollowersNoLimit", "Offiziere dürfen alle freien Matrosen mitnehmen (ohne 60-%-Grenze)", false);
            Bool(QoL, "NoStress", "Kein Stress in der Crew", false);
            Bool(QoL, "AchievementsOn", "Erfolge (Achievements) immer erlaubt, auch nach Konsolen-Befehlen", false);
            Num(QoL, "VentilationNoise", "Lüftung: Lautstärke/Geräusch x (1 = normal) (#36)", 0.3f, 0.05f, 1, 0.05f);
            Num(QoL, "AbsorberDuration", "  Absorber (Kalipatrone) hält x länger", 2, 1, 10, 0.5f);
            Bool(QoL, "CompressorNoAir", "  E-Kompressor verbraucht keine Luft im Boot", true);
            Bool(QoL, "AutoVentilation", "  Unter Wasser: Lüftung automatisch einschalten", true);
            Bool(QoL, "AutoAbsorber", "  Unter Wasser: Absorber (Kalipatrone) automatisch in die Lüftung einsetzen", true);
            Bool(QoL, "EmergencyOxygen", "  Notfall-Sauerstoff: etwas mehr als der Verbrauch, wenn die Luft sinkt", true);
            Bool(QoL, "FullCrewAtStart", "Volle Besatzung ab Start (vor der ersten Feindfahrt) (#37)", true);
            Num(QoL, "FullCrewSize", "  Gewünschte Besatzung (Männer, in der INI ohne Obergrenze)", 90, 10, 200, 1);
            Bool(QoL, "UniqueCrewNames", "Eindeutige Crew-Namen: neue Männer bekommen einen Nachnamen, den noch keiner an Bord hat (größere Namensliste)", true);
            Bool(QoL, "RenameDuplicateNames", "  >> Doppelte Namen jetzt ändern (einmal, für die jetzige Crew)", false);
            Bool(QoL, "StopTimeOnShip", "Zeitraffer stoppt bei neuem Schiff-Kontakt (Idee 1)", true);
            Bool(QoL, "StopTimeOnAircraft", "Zeitraffer stoppt bei Flugzeug (Idee 1)", true);
            Bool(QoL, "DamageReport", "Schadensbericht: was hat uns getroffen, wo und warum (F10-Reiter \"Schaden\" + Datei)", true);
            Bool(QoL, "DamageReportNotify", "  bei jedem Treffer kurze Meldung im Spiel", true);
            Num(QoL, "DamageReportMerge", "  gleiche Ursache innerhalb x Sekunden zusammenfassen", 5, 1, 60, 1);
            Bool(QoL, "DamageReportPause", "  bei Treffer das Spiel pausieren", false);
            Num(QoL, "UnderFireMode", "Unter Beschuss: 0 aus, 1 fragen \"Herr Kaleun, was tun wir?\", 2 Vorschlag automatisch ausführen", 1, 0, 2, 1);
            Bool(QoL, "UnderFirePause", "  beim Fragen pausieren (aus = nur Symbol rechts am Rand)", true);
            Num(QoL, "UnderFireIgnoreMin", "  Knopf \"ignorieren\": so viele Minuten (Meldungen werden gesammelt)", 5, 1, 60, 1);
            Num(QoL, "UnderFireCooldown", "  gleicher Gegner erst wieder nach x Sekunden melden", 30, 5, 300, 5);
            Bool(QoL, "UnderFireNeedSighting", "  nur melden, wenn die Ausguck den Schützen sehen", true);
            Bool(QoL, "StopTimeOnDamage", "Zeitraffer stoppt bei Schaden (Treffer, neues Leck)", true);
            Bool(QoL, "StopTimeOnRadio", "Zeitraffer stoppt bei Funkspruch", true);
            Bool(QoL, "IgnoreFishingBoats", "  Zeitraffer ignoriert Fischerboote", true);
            Bool(QoL, "IgnoreAlliedShips", "  Zeitraffer ignoriert Verbündete (grün)", true);
            Bool(QoL, "IgnoreNeutralShips", "  Zeitraffer ignoriert Neutrale", false);
            Bool(QoL, "BackToWater", "Zurück ins Wasser: festgefahren an Land -> Boot an letzte sichere Stelle", true);
            Num(QoL, "BackToWaterSeconds", "  So viele Sekunden festgefahren, bevor es passiert", 8, 3, 60, 1);
            Bool(QoL, "BackToWaterNow", "  >> Jetzt zurück ins Wasser", false);
            Num(QoL, "AutoSaveMinutes", "Auto-Speichern alle x Minuten (0 = aus) (Idee 6)", 15, 0, 120, 5);
            Num(QoL, "LowWarnPercent", "Warnung + Zeitraffer-Stopp bei Diesel/Batterie/Luft unter x % (0 = aus) (Idee 8)", 15, 0, 50, 5);
            Bool(QoL, "NavInfo", "Karte: Ankunftszeit + Restweg + Diesel-Reichweite oben links (Idee 7)", true);
            Num(QoL, "InfoTopX", "  Infozeilen: Position von links (% vom Bild)", 30, 0, 90, 1);
            Num(QoL, "InfoTopY", "  Infozeilen: Position von oben (% vom Bild)", 2, 0, 95, 1);
            Bool(QoL, "InfoSpeed", "  Infozeile: Geschwindigkeit (kn)", true);
            Bool(QoL, "InfoTrip", "  Infozeile: Fahrt (km)", true);
            Bool(QoL, "InfoTotal", "  Infozeile: gesamt (km)", true);
            Bool(QoL, "InfoEta", "  Infozeile: Ziel + Ankunft", true);
            Bool(QoL, "InfoDiesel", "  Infozeile: Diesel-Reichweite", true);
            Bool(QoL, "TankDisplay", "  Infozeile: Füllstand Tauchzellen + Trimmtanks", true);
            Bool(QoL, "DepthHotkeys", "Tiefe per Taste (Tasten im Tab „Tasten“) (Idee 10)", true);
            Bool(QoL, "FixTooltipParent", "Fix: Fehler \"Default Tooltip ... GUI\" (Tooltip nicht unnötig umhängen)", true);
            Bool(Tools, "DiagTooltip", "Log: Tooltips (leerer Kasten am Auge)", true);
            Bool(QoL, "FixEmptyTooltip", "Fix: leere Tooltip-Kästen nicht anzeigen (z. B. am Auge)", true);
            Bool(QoL, "FixSkillIcons", "Fix: Fehler \"Requested asset Icons/Skills/...\" (fehlende Skill-Bilder)", true);
            Bool(QoL, "FixCameraParent", "Fix: Fehler \"GameObject is already being activated or deactivated\" nach dem Charakter-Bildschirm", true);
            Bool(QoL, "FixMissingIcons", "Fix: fehlende Bilder (Icons/..., Images/...) ohne roten Fehler und ohne aufpoppende Konsole", true);
            Bool(QoL, "FixFlames", "Fix: Fehler in FlamesRegistry (Feuer an abgeschossenen Schiffsteilen)", true);
            Bool(QoL, "FixPoolAnimators", "Fix: Fehler \"Pooled instance ... enabled animators\" (z. B. Zeitungsständer)", true);
            Num(QoL, "DepthStep", "  Tiefen-Schritt in m (Shift = x10, Alt = x100, auch Mausrad an der Tiefenanzeige)", 1, 0.5f, 50, 0.5f);
            Bool(QoL, "DepthGauge", "Eigene Tiefenanzeige ohne 260-m-Grenze (Doppelklick/Mausrad setzt Zieltiefe)", true);
            Bool(QoL, "DepthGaugeAutoZoom", "  Skala passt sich an (flaches Wasser = kurze Skala)", true);
            Num(QoL, "DepthGaugeMax", "  Skala höchstens bis (m)", 500, 100, 5000, 50);
            Num(QoL, "DepthGaugeXPos", "  Position von links (% vom Bildschirm, 100 = ganz rechts am Rand)", 100, 0, 100, 1);
            Num(QoL, "DepthGaugeY", "  Position oben (% vom Bildschirm)", 35, 0, 90, 1);
            Num(QoL, "DepthGaugeSeconds", "  Verlauf über x Sekunden (echte Zeit)", 120, 30, 600, 10);
            Bool(QoL, "AutoDepth", "Bodenabstand-Autopilot: hält Abstand zum Meeresgrund", false);
            Num(QoL, "AutoDepthClearance", "  Wasser unter dem Kiel (m, mindestens 5 gegen Physik-Hänger)", 10, 5, 50, 0.5f);
            Num(QoL, "AutoDepthLookAhead", "  Voraus schauen (m)", 400, 0, 2000, 50);
            Num(QoL, "AutoDepthMin", "  Nie flacher als (m); ist das Wasser zu flach, geht das Boot so tief wie möglich und warnt", 12, 0, 50, 1);
            Num(QoL, "AutoDepthMax", "  Nie tiefer als (m)", 300, 20, 5000, 10);
            Bool(QoL, "AutoDepthBottom", "  Aufsetzen: sanft auf den Grund legen", false);
            Bool(QoL, "AutoDepthBottomSilent", "  Aufsetzen: dabei Schleichfahrt einschalten", false);
            Bool(QoL, "AutoReloadTubes", "Leere Rohre automatisch aus dem Lager laden", true);
            Num(QoL, "AutoReloadSeconds", "  nach x Sekunden leer", 20, 0, 600, 5);
            Bool(QoL, "ReloadAllNow", "  >> Alle leeren Rohre aus dem Lager laden (Idee 2)", false);
            Bool(QoL, "SwapShiftsNow", "  >> Wachen tauschen: Gruppen mit gleicher Aufgabe tauschen Schichten (Idee 4)", false);
            Bool(QoL, "SortStorageNow", "  >> Lager aufräumen: gleiche Sachen stapeln, nach vorne rücken (Idee 5)", false);
            Bool(QoL, "CallMilkCow", "  >> Nächste Milchkuh zu mir holen", false);
            Bool(QoL, "BallastAlwaysAvailable", "Ballast-Befehle immer verfügbar (außer Ventil kaputt / Hafen)", true);
            Bool(QoL, "HatchPressure", "Luken zeigen den Luftdruck beider Räume", true);
            Bool(QoL, "HatchPressureLock", "Luken sperren bei großem Druckunterschied", true);
            Num(QoL, "HatchPressureLimit", "  Sperre ab Druck-Unterschied (bar, 0 = nie sperren)", 0.5f, 0, 5, 0.1f);
            Bool(QoL, "HatchEqualize", "  Druckausgleich: Luft strömt langsam nach, bis die Luke wieder aufgeht", true);
            Num(QoL, "HatchEqualizeSpeed", "  Druckausgleich: % vom Unterschied pro Sekunde", 10, 1, 100, 1);
            Num(QoL, "VentEqualizeFaster", "  Lüftung an: Druckausgleich x schneller (1 = kein Bonus)", 3, 1, 10, 0.5f);
            Bool(QoL, "AutoSnorkel", "Schnorchel fährt automatisch aus auf Sehrohrtiefe (1-10 m) und wieder ein", true);
            Bool(QoL, "SnorkelAir", "  Schnorchel oben: Luftdruck in allen Räumen schnell auf Normaldruck", true);
            Bool(QoL, "SnorkelVent", "  Schnorchel oben: Lüftung einschalten", true);
            Bool(QoL, "AutoCrashDive", "Alarmtauchen automatisch, wenn ein feindliches Flugzeug gesichtet wird", false);
            Bool(QoL, "DiveSpeedCompensate", "Tiefenruder bei Speed-Cheat abschwächen (wirken wie bei normaler Fahrt)", true);
            Bool(QoL, "RouteLead", "Kurs: rechtzeitig vor dem Wegpunkt eindrehen (kein Bogen hinter dem Punkt)", true);
            Num(QoL, "RouteLeadRadius", "  Wendekreis-Radius für das Eindrehen (m)", 400, 50, 3000, 50);
            Bool(QoL, "LeavePortButton", "Knopf \u201eHafen verlassen\u201c direkt nach dem Ablegen (nur offizieller Ausfahrt-Punkt)", false);
            Bool(QoL, "TonnageBeforePatrol", "Patrouillen-Auftrag: Tonnage z\u00e4hlt schon auf dem Weg ins Gebiet", true);
            Bool(QoL, "MapActualDepth", "Karte: genaue Wassertiefe unter der Maus anzeigen", true);
            Bool(QoL, "PortT5Min", "Häfen: immer mindestens 4 T5 Zaunkönig im Laden (wenn es T5 schon gibt)", true);
            Bool(QoL, "TorpedoLatestPistol", "Häfen: von jedem Torpedo-Typ nur die neueste Zündpistole", true);
            Bool(QoL, "TorpedoMix", "H\u00e4fen haben eine gleichm\u00e4\u00dfige Mischung aller Torpedo-Typen", false);
            Bool(QoL, "SeaFloorShallow", "Meeresboden immer laden, wenn das Wasser flacher als ~300 m ist (nur Reise-System \u201eRealistisch\u201c)", true);
            Num(QoL, "SeaFloorMaxCompression", "  nur bis Zeitraffer x (darüber kein Nachladen = keine Ruckler/Flicken)", 100, 1, 5000, 10);
            Bool(QoL, "SeaLayer", "Realistische Sprungschicht (25-150 m je nach Wassertemperatur, darunter schwerer zu orten)", true);
            Bool(QoL, "SeaLayerHearing", "  Unter der Sprungschicht h\u00f6rt der eigene Horcher Schiffe oben schlechter", true);
            Bool(QoL, "SeaLayerNote", "  Kurze Meldung beim Unter-/\u00dcberqueren der Sprungschicht", true);
            Bool(QoL, "FixUnreadableColliders", "Fix: Gegenstände mit gesperrter Kollisionsform (Bin, Clock, Folder) zählen wieder, keine rote Meldung", true);
            Bool(QoL, "FixOffNavmeshCrew", "Fix: Matrosen, die neben dem Laufweg feststecken, werden zurückgesetzt", true);
            Bool(QoL, "FixGunModeError", "Fix: Fehler \"Unrecognized mode\" beim Umschalten des Geschütz-Ziels", true);
            Bool(QoL, "FixNavmeshSnap", "Fix: Matrosen finden Ziele knapp neben dem Laufweg (bis 5 m statt 1,5 m)", true);
            Bool(QoL, "SkipStartupIntros", "Start-Videos (Logos) beim Spielstart überspringen", true);
            Bool(QoL, "SkipIntroTutorial", "Neue Karriere: Einf\u00fchrungs-Tutorial \u00fcberspringen", true);
            Bool(QoL, "FixFadeReparent", "Fehler-Fix: Einblend-Effekt von Figuren (z. B. Hafenarbeiter) beim Ein-/Ausschalten nicht mehr falsch umh\u00e4ngen", true);
            Bool(QoL, "NoPauseBlur", "Pause ohne Unsch\u00e4rfe", false);
            Bool(QoL, "NoPauseDark", "Pause ohne dunkle Einblendung", false);
            Bool(QoL, "HideDevConsole", "Entwickler-Konsole nicht automatisch öffnen (Fehler landen trotzdem im Log)", true);
            Bool(QoL, "QuitWaitsForSave", "Beenden wartet, bis der Spielstand fertig geschrieben ist (sonst kaputter Spielstand)", true);
            Bool(QoL, "NoEdgeScroll", "Schnittansicht rutscht nicht weg, wenn die Maus den Bildschirmrand berührt", false);
            Bool(QoL, "NoEdgeDeck", "Kein Sprung zur Deck-Ansicht am oberen/unteren Bildschirmrand", false);
            Bool(QoL, "FixCarrierConvoy", "Fix: Konvois mit Flugzeugtr\u00e4ger fahren normal (geparkte Flugzeuge bremsen nicht)", true);
            Bool(QoL, "FixScopeStall", "Fix: kein Ruckeln beim Zoomen von Sehrohr/UZO (Bilder bleiben geladen)", true);
            Bool(QoL, "ScopeRetain", "  Overlays geladen halten", true);
            Bool(QoL, "ScopePreload", "  bekannte Overlays im Hintergrund vorladen", true);
            Bool(QoL, "ScopeUzoRefresh", "  UZO-Zoom (Zusatz-Mod) nur auffrischen statt neu starten", true);
            Num(QoL, "ScopeMaxMb", "  höchstens ... MB festhalten", 768, 0, 4096, 64);
            Bool(QoL, "BerlinTime", "Alle Uhren zeigen Berliner Zeit (keine Zeitzonen-Meldungen)", false);
            Bool(QoL, "FixSpeedRange", "Fix: kein Tempo-/Reichweiten-Verlust bei hohem Zeitraffer (lernt Werte bei normaler Zeit)", true);
            Bool(QoL, "FixSkipperDead", "Fix: Kommandant tot/verwundet \u2192 Warnung vor dem Hafen + keine Gespr\u00e4che, die h\u00e4ngen", true);
            Bool(QoL, "FixPortDialogue", "Fix: Hafen-Gespr\u00e4che h\u00e4ngen nicht mehr fest", true);
            Bool(QoL, "FixRadioRewards", "Fix: vergessene Belohnungen von Funk-Auftr\u00e4gen werden ausgezahlt", true);
            Bool(QoL, "MapReadable", "Karte: Raster-Beschriftung auf jedem Kartenstil lesbar + Stil \u201eEigene Farben\u201c (Farben im Tab Karte)", true);
            Bool(QoL, "MapIvory", "Karte: zus\u00e4tzlicher Kartenstil \u201eElfenbein\u201c", true);
            Num(QoL, "ReticleBright", "Fadenkreuz-Licht: Helligkeit (1 = Spiel, gilt wenn „eigene Farbe“ aus)", 1, 0.2f, 3, 0.1f);
            Bool(QoL, "ReticleCustom", "Fadenkreuz-Licht: eigene Farbe, Helligkeit pro Optik, Peilskala-Ausgleich", false);
            Num(QoL, "ReticleR", "  Farbe Rot (0-255)", 27, 0, 255, 1);
            Num(QoL, "ReticleG", "  Farbe Grün (0-255)", 255, 0, 255, 1);
            Num(QoL, "ReticleB", "  Farbe Blau (0-255)", 30, 0, 255, 1);
            Num(QoL, "ReticleStep", "  Helligkeit 0-100 (20 = Spiel, 60 = 4-fach, 100 = 16-fach)", 60, 0, 100, 1);
            Num(QoL, "ReticleMulAttack", "  Faktor Angriffs-Sehrohr", 2, 0, 12, 0.25f);
            Num(QoL, "ReticleMulObservation", "  Faktor Beobachtungs-Sehrohr", 2, 0, 12, 0.25f);
            Num(QoL, "ReticleMulUzo", "  Faktor UZO", 1, 0, 12, 0.25f);
            Num(QoL, "ReticleMulBino", "  Faktor Fernglas", 2, 0, 12, 0.25f);
            Num(QoL, "ReticleBearing", "  Peilskala-Helligkeit (1 = gleich hell wie das Fadenkreuz)", 1, 0.2f, 2, 0.1f);
            Bool(QoL, "ReticleLabels", "  Zahlen der Peilskala mit beleuchten", true);
            Bool(QoL, "ReticleNormalize", "  schwache Masken angleichen (alle Optiken gleich hell)", true);
            Bool(QoL, "ReticleSound", "  Klick-Ton beim Umschalten", true);
            Num(QoL, "ReticleSoundVolume", "    Lautstärke", 1, 0, 2, 0.1f);
            Bool(QoL, "GunSmokeOff", "Eigene Geschütze ohne Mündungsfeuer und Rauch (freie Sicht beim Schießen)", true);
            Bool(QoL, "DeckGunSeaState", "Eigene Geschütze und Seegang (Streuung, Ladezeit, Sperre bei Sturm; je Wert einzeln abschaltbar)", true);
            Num(QoL, "DgsCalm", "  ruhig bis Windstärke (keine Strafe)", 4, 0, 12, 0.5f);
            Num(QoL, "DgsRough", "  volle Strafe ab Windstärke", 8, 0, 12, 0.5f);
            Num(QoL, "DgsDisable", "  Geschütz gesperrt ab Windstärke (12 = nie; Leuchtgranaten gehen immer)", 7, 0, 12, 0.5f);
            Num(QoL, "DgsMaxSpread", "  Streuung bei voller Strafe (x, 1 = aus)", 3, 1, 10, 0.25f);
            Num(QoL, "DgsMaxReload", "  Ladezeit bei voller Strafe (x, 1 = aus)", 2, 1, 5, 0.25f);
            Bool(QoL, "DgsAA", "  gilt auch für Flak (eigene; Gegner-Flak nur mit dem Gegner-Schalter)", false);
            Bool(QoL, "DgsManualAim", "  Handzielen bekommt dieselbe Streuung und schießt sich ein", true);
            // 0.4.31 (DoMe 2026-11-18: weakening the enemy is fine, but every effect with its own switch; nothing here ever helps an enemy)
            Bool(QoL, "DgsAllShips", "Gegner schwächen: Seegang wirkt auch auf Gegner-Kanonen (Werte wie oben)", true);
            Bool(QoL, "DgsNpcSpreadOn", "Gegner schwächen: Gegner-Kanonen streuen mehr", true);
            Num(QoL, "DgsNpcSpread", "  Streuung in % (100 = Spiel; vergrößert auch den kleinsten Fehlschuss)", 200, 100, 500, 25);
            Num(QoL, "DgsNpcFloor", "  kleinster Fehlschuss (m pro km, 0 = aus)", 4, 0, 20, 1);
            Bool(QoL, "DgsNpcRangingOn", "Gegner schwächen: Gegner-Kanonen schießen sich langsamer ein", true);
            Num(QoL, "DgsNpcRanging", "  Einschießen in % (100 = Spiel)", 50, 10, 100, 5);
            Bool(QoL, "DgsNpcEvenOut", "Gegner schwächen: C3 und Tanker schießen sich ein wie andere Frachter", true);
            Bool(QoL, "EnemyShellSpeedOn", "Gegner schwächen: Gegner-Granaten fliegen langsamer", true);
            Num(QoL, "EnemyShellSpeedPct", "  Mündungsgeschwindigkeit in % vom Spielwert (100 = Spiel)", 100, 20, 100, 5);
            Bool(QoL, "DgsKnockout", "Eigene Granaten können Gegner-Kanonen ausschalten", true);
            Num(QoL, "DgsKnockoutPct", "  Schaden an der Kanone in %", 200, 50, 500, 25);
            Num(QoL, "DgsKnockoutRadius", "  Radius in Metern", 6, 1, 20, 1);
            Bool(QoL, "ScopeRemember", "Sehrohr-Anzeige-Fix (Ansicht, Zoom, Stations-Knopf)", true);
            Bool(QoL, "ScopeRememberView", "  Ansicht und Zoom je Sehrohr merken", true);
            Bool(QoL, "ScopeStationLast", "  Stations-Knopf führt zum zuletzt benutzten Sehrohr", true);
            Bool(QoL, "RainGearWind", "Regenkleidung für die Wache an Deck (eigene Steuerung statt Spiel)", true);
            Num(QoL, "RainGearMode", "  Modus: 0 = nur bei Regen, 1 = immer an Deck, 2 = aus", 1, 0, 2, 1);
            Num(QoL, "RainGearRain", "  Mantel ab Regen-Stärke (bei Modus 0)", 0.05f, 0, 1, 0.05f);
            Num(QoL, "RainGearCapRain", "  Südwester ab Regen-Stärke", 0.05f, 0, 1, 0.05f);
            Num(QoL, "RainGearCapWind", "  Südwester ab Windstärke", 6, 0, 12, 0.5f);
            Bool(QoL, "RainGearImpersonated", "  auch den Mann anziehen, durch den du schaust", true);
            Bool(QoL, "RainGearSkipperCap", "  Kommandant trägt auch Südwester", true);
            Bool(QoL, "RainGearSheltered", "  auch im Hafen/geschützt anziehen", false);
            Num(QoL, "RainGearInterval", "  Prüfen alle ... Sekunden", 2, 0.25f, 10, 0.25f);
            Bool(QoL, "OfficerWatches", "Neue/bef\u00f6rderte Offiziere bekommen 6-Stunden-Wachen (halb Dienst, halb Ruhe)", true);
            Num(QoL, "OfficerWatchHours", "  Wachlänge in Stunden (6 oder 3)", 6, 3, 6, 3);
            Bool(QoL, "FastHatch", "Ich-Sicht: Luken flüssig durchqueren (Lauf-Gang bleibt, schneller)", true);
            Bool(QoL, "HatchHoldState", "  Geh- oder Lauf-Durchgang bis zum Ende halten", true);
            Num(QoL, "HatchVariant", "  Durchgang: 0 = wie gedrückt, 1 = immer gehen, 2 = immer laufen", 0, 0, 2, 1);
            Bool(QoL, "HatchWalkForSprint", "  beim Rennen ruhige Geh-Animation nehmen (aber schnell)", false);
            Num(QoL, "HatchSprintSpeed", "  Tempo beim Rennen (x)", 1.6f, 1, 3, 0.1f);
            Num(QoL, "HatchWalkSpeed", "  Tempo beim Gehen (x)", 1, 1, 3, 0.1f);
            Num(QoL, "HatchMaxSpeed", "  höchstes Animations-Tempo", 3, 1, 5, 0.25f);
            Num(QoL, "HatchRunTail", "  nach der Luke weiterlaufen (Sekunden)", 0.35f, 0, 2, 0.05f);
            Bool(QoL, "MuteLightCallouts", "Ansagen zum Lichtwechsel stumm", false);
            Num(QoL, "DiveAngleMax", "Tauchwinkel: größter Neigungswinkel bei Zieltiefe über 5 m in Grad (0 = Spiel; an der Oberfläche gilt der Spielwert)", 30, 0, 45, 1);
            Bool(QoL, "AutoVirtualShips", "Auto-Detail: bei wenig FPS ferne Schiffe nur virtuell führen (bleiben auf Karte/Sensoren, werden nicht voll berechnet)", true);
            Num(QoL, "AutoVirtualMin", "  kleinster Simulations-Abstand bei sehr wenig FPS (1 = Spiel, 0.5 = halb)", 0.5f, 0.2f, 1f, 0.1f);
            Bool(QoL, "ExplosionLimit", "Explosions-Effekte begrenzen: kleiner + höchstens x pro Sekunde (Schaden bleibt gleich)", true);
            Num(QoL, "CrewAiSleepEvery", "Schlafende, nicht sichtbare Matrosen denken nur jedes x-te Bild (1 = Spiel, jedes Bild)", 3f, 1f, 10f, 1f);
            Bool(QoL, "CrewWarpNoPath", "  Findet ein Mann keinen Weg zu seinem Posten, wird er hingesetzt (statt aufzugeben)", true);
            Bool(QoL, "CrewAiWaitLoad", "  Crew-KI wartet, bis das Boot fertig geladen ist (gegen Fehler beim Laden)", true);
            Num(QoL, "PhysicsHz", "Physik-Takt pro Sekunde (0 = Spielwert 60; 50 spart ein Sechstel Physik-Rechenzeit)", 50f, 0f, 75f, 5f);
            Bool(QoL, "WolfpackOncePerFrame", "Schiffsgruppen um das Boot nur einmal pro Bild suchen (Spiel: in jedem Physik-Schritt, also 2-3 mal pro Bild)", true);
            Bool(QoL, "PhysicsFollowFps", "  Physik-Takt folgt der Bildrate (bei wenig FPS längere Schritte, Spiel läuft nicht mehr in Zeitlupe)", false);
            Num(QoL, "PhysicsAutoMinHz", "    nie weniger als x Schritte pro Sekunde (Spiel nutzt 25 bei Zeitraffer x5)", 25f, 15f, 50f, 5f);
            Num(QoL, "PhysicsMaxSteps", "  bei wenig FPS höchstens so viele Physik-Schritte pro Bild (Spiel = 5; weniger = flüssiger, Spiel läuft dann kurz langsamer)", 2f, 1f, 5f, 1f);
            Bool(QoL, "ExplosionAuto", "Automatische Detailstufe nach Bildrate (Explosionen + Stimmen): flüssig = voll, je weniger FPS desto sparsamer", true);
            Num(QoL, "ExplosionTargetFps", "  Ziel-FPS für die Automatik", 40, 15, 120, 5);
            Num(QoL, "ExplosionScale", "  Größe der Granaten-Explosionen (1 = Spiel)", 0.5f, 0.1f, 1f, 0.1f);
            Num(QoL, "ExplosionMaxPerSec", "  höchstens x Explosions-Effekte pro Sekunde", 10, 1, 100, 1);
            Bool(QoL, "VoiceLimit", "Stimmen begrenzen: gleiche Ansage höchstens alle x Sekunden, kurze Warteschlange (weniger Gequassel + weniger Ruckeln)", true);
            Num(QoL, "VoiceRepeatSec", "  gleiche Ansage frühestens nach x Sekunden wieder", 20, 0, 120, 5);
            Num(QoL, "VoiceMaxQueue", "  höchstens x Ansagen warten (mehr werden verworfen)", 3, 1, 20, 1);
            Bool(QoL, "VoiceMuteGunCalls", "  Geschütz-Ansagen (Zielen / Feuer!) stumm", true);
            Bool(QoL, "QuietUI", "Ruhige Oberfläche an (Einstellungen darunter, Meldungs-Stufen in toolkit-notifications.txt)", false);
            Bool(QoL, "QuietNames", "  Crew-Namen nur beim Zeigen oder Auswählen", true);
            Bool(QoL, "QuietShowHover", "    Name beim Zeigen mit der Maus", true);
            Bool(QoL, "QuietShowSelected", "    Name bei ausgewähltem Mann", true);
            Bool(QoL, "QuietDeviceNames", "  auch Geräte- und Stations-Namen verstecken", false);
            Bool(QoL, "QuietActionIcons", "  Tätigkeits-Symbol und Fortschritt verstecken (gleiche Regel)", true);
            Bool(QoL, "QuietEnergyBars", "  Ruhe-Balken verstecken (gleiche Regel)", true);
            Bool(QoL, "QuietEventIcons", "  Ereignis-Ausrufezeichen verstecken", false);
            Bool(QoL, "QuietTimers", "  Countdown (z. B. Blutung) verstecken", false);
            Bool(QoL, "QuietBanner", "  Band oben in der Mitte und Ton stumm (Symbole bleiben, wichtige Ausnahmen bleiben laut)", true);
            Bool(QoL, "QuietDirectPopups", "  auch direkte Hinweise stumm („Spiel gespeichert“, Fehler beim Speichern ...)", false);
            Bool(QoL, "QuietMutePort", "  Stimmen von Hafen-Personal und fremden Kapitänen stumm (Untertitel bleiben)", false);
            Bool(QoL, "KeepVoiceLanguage", "Fix: gew\u00e4hlte Sprache der Crew bleibt nach Updates erhalten", true);
            Num(QoL, "VoiceLang", "  Stimme festhalten auf: 0 = wie gerade eingestellt, 1 = Deutsch, 2 = Englisch", 0, 0, 2, 1);
            Bool(QoL, "VoiceRepairVersion", "  kaputte „letzte Version“ reparieren (sonst neue Spielstände nicht ladbar)", true);
            Bool(QoL, "FixAbortStall", "Fix: kein Einfrieren, wenn Crew-Aussehen sich w\u00e4hrend Textur-Arbeit \u00e4ndert", true);
            Bool(QoL, "FixTextureRace", "Fix: Texturen werden etwas sp\u00e4ter gel\u00f6scht (weniger Abst\u00fcrze)", true);
            Bool(QoL, "FixSupplyPriceMenu", "Fix für Mod \"Supply Price Editor\": sucht das Hauptmenü nicht mehr jedes Bild (weniger Ruckeln auf See)", true);
            Bool(QoL, "FixUniformRebuild", "Fix für Mod \"Realistic Uniforms Revival\": keine unnötigen Uniform-Neuaufbauten", true);
            Bool(QoL, "FixBifrost", "Fix für Mod \"Projekt Bifrost\": Anzeigen gedrosselt (weniger Ruckeln)", true);
            Bool(QoL, "FixBifrostDepthCharge", "  Wasserbomben-Zähler-Fehler verhindern", true);
            Bool(QoL, "FixMissingWearables", "Fix für Mods mit fehlender Kleidung (z.B. \"Cap NCO\"): fehlendes Teil wird weggelassen statt Fehlermeldung", true);
            Bool(QoL, "CamWheelZoom", "Kamera: Mausrad-Zoom und weiche freie Kamera", true);
            Num(QoL, "CamFreeFov", "  freie Kamera: Start-Blickwinkel", 70, 1, 150, 1);
            Num(QoL, "CamFppMinFov", "  Ich-Sicht: engster Blickwinkel", 20, 1, 150, 1);
            Num(QoL, "CamFreeMinFov", "  freie Kamera: engster Blickwinkel", 10, 1, 150, 1);
            Num(QoL, "CamFreeMaxFov", "  freie Kamera: weitester Blickwinkel", 90, 1, 150, 1);
            Num(QoL, "CamZoomPercent", "  freie Kamera: Zoom pro Rad-Raste (%)", 3, 0.1f, 50, 0.5f);
            Num(QoL, "CamFastZoomMul", "  schneller Zoom mit Taste (x)", 4, 1, 20, 0.5f);
            Bool(QoL, "CamSmoothMove", "  freie Kamera: weich anfahren und bremsen", true);
            Num(QoL, "CamAccel", "    Anfahren (Sekunden)", 0.35f, 0, 3, 0.05f);
            Num(QoL, "CamDecel", "    Bremsen (Sekunden)", 0.25f, 0, 3, 0.05f);
            Num(QoL, "CamBank", "    Schräglage in Kurven (Grad)", 3, -15, 15, 0.5f);
            Num(QoL, "CamMinAlt", "    tief über dem Wasser nicht zu langsam (ab Meter)", 6, 1, 500, 1);
            Num(QoL, "CamLookSmooth", "    Umschauen glätten (Sekunden)", 0.08f, 0, 2, 0.02f);
            Bool(QoL, "CamHideCursor", "  freie Kamera: Mauszeiger ausblenden", true);
            Bool(QoL, "CamProtectScripted", "  bei Zwischensequenzen nicht eingreifen", true);
            Bool(QoL, "StationZoom", "Stationen (Horcher, Vorhaltrechner, Torpedoraum ...): Zoom, Umschauen, Drehknöpfe", true);
            Num(QoL, "StLookMode", "  Umschauen: 0 = wie Ich-Sicht (Mitte-Punkt), 1 = Blick dreht zum Mauszeiger", 0, 0, 1, 1);
            Num(QoL, "StPanSpeed", "  Umschau-Tempo (1 = wie Ich-Sicht)", 1, 0.1f, 5, 0.1f);
            Num(QoL, "StMaxPan", "  wie weit die Ansicht gleiten darf", 1.25f, 0, 4, 0.05f);
            Num(QoL, "StMaxLook", "  wie weit die Ansicht drehen darf (Grad)", 35, 0, 90, 1);
            Num(QoL, "StMinFov", "  Zoom bis (Grad, kleiner = näher)", 22, 5, 60, 1);
            Num(QoL, "StHydroLook", "  Horcher: 0 = gleiten, 1 = drehen", 1, 0, 1, 1);
            Num(QoL, "StTdcLook", "  Vorhaltrechner: 0 = gleiten, 1 = drehen", 0, 0, 1, 1);
            Num(QoL, "StTorpLook", "  Torpedoraum/Feuerleitung: 0 = gleiten, 1 = drehen", 1, 0, 1, 1);
            Num(QoL, "StGyroLook", "  Schusswinkel-Empfänger: 0 = gleiten, 1 = drehen", 0, 0, 1, 1);
            Num(QoL, "StOtherLook", "  andere Stationen: 0 = gleiten, 1 = drehen", 0, 0, 1, 1);
            Num(QoL, "StTdcX", "  Vorhaltrechner: Gleiten seitlich (x)", 1, 0, 2, 0.05f);
            Num(QoL, "StTdcY", "  Vorhaltrechner: Gleiten hoch/runter (x)", 1, 0, 2, 0.05f);
            Num(QoL, "StGyroX", "  Schusswinkel-Empfänger: Gleiten seitlich (x)", 0.35f, 0, 2, 0.05f);
            Num(QoL, "StGyroY", "  Schusswinkel-Empfänger: Gleiten hoch/runter (x)", 0.35f, 0, 2, 0.05f);
            Num(QoL, "StTorpForward", "  Torpedoraum: Standpunkt nach vorn (m)", 0.6f, -2, 2, 0.1f);
            Bool(QoL, "StHideTooltips", "  Tooltips erst zeigen, wenn Mauszeiger frei ist", true);
            Bool(QoL, "StHideReadout", "  Horcher-Anzeige erst zeigen, wenn Mauszeiger frei ist", true);
            Bool(QoL, "StDialDirect", "  Drehknöpfe folgen der Maus direkt", true);
            Num(QoL, "StDialRange", "  Drehknopf-Empfindlichkeit (Bildschirm-Anteil für ganzen Bereich)", 0.4f, 0.05f, 2, 0.05f);
            Num(QoL, "StDialFine", "  mit Shift fein einstellen (x)", 0.05f, 0.01f, 1, 0.01f);
            Bool(QoL, "StAobLive", "  Lagewinkel-Knopf folgt beim Ziehen sofort", true);
            Num(QoL, "StAobSpeed", "  Lagewinkel-Knopf: Tempo (Grad/s, 0 = Spiel)", 240, 0, 1000, 10);
            Bool(QoL, "CamOrbitUnderwater", "Kamera: getaucht bleibt die Au\u00dfenkamera unter Wasser (realistisch)", false);
            Num(QoL, "OrbitMargin", "  Abstand unter der Wasseroberfläche (m)", 1.5f, 0, 10, 0.25f);
            Num(QoL, "OrbitGrace", "  Sehrohr gerade über Wasser: Kamera frei für ... Sekunden", 1.5f, 0, 10, 0.25f);
            Num(QoL, "OrbitGain", "  Nachführ-Stärke", 1, 0.1f, 5, 0.1f);
            Num(QoL, "OrbitTrack", "  Nachführen (Grad pro Sekunde)", 90, 1, 360, 5);
            Num(QoL, "OrbitRelax", "  Freigeben (Grad pro Sekunde)", 30, 1, 360, 5);
            Bool(QoL, "OrbitAnticipate", "  Mausbewegung nach oben vorher abfangen", true);
            Bool(QoL, "FixWolfpackRetry", "Fix: Wolfsrudel geht nicht verloren, wenn das BdU gerade funkt", true);
            Bool(QoL, "Wolfpack", "Wolfsrudel-Einstellungen an (Datum, Chance, Boote, Ziel-BRT unten)", false);
            Num(QoL, "WolfpackFrom", "  Rudel ab: 0 = Sep 1940, 1 = Jul 1940, 2 = Nov 1940, 3 = Jan 1941, 4 = 8. Mai 1941 (Spiel)", 0, 0, 4, 1);
            Num(QoL, "WolfpackChance", "  Chance, dass sich ein Rudel bildet, in %", 95, 5, 100, 5);
            Num(QoL, "WolfpackBoats", "  höchstens x Boote pro Rudel", 6, 2, 8, 1);
            Num(QoL, "WolfpackGrt", "  erstes Rudel-Ziel: x BRT", 10000, 1000, 50000, 1000);
            Bool(QoL, "FastLoad", "Spielst\u00e4nde schneller laden (Bild ruckelt beim Laden etwas mehr)", true);
            Bool(QoL, "FastLoadLongFrames", "  Lange Lade-Bilder: mindestens 0,5 s laden pro Bild (Ladebild ruckelt, Laden deutlich kürzer)", true);
            Num(QoL, "FastLoadBudgetMs", "  Rechenzeit pro Bild beim Laden (ms, Spiel = 16)", 100, 16, 1000, 10);
            Num(QoL, "FastLoadIdleMs", "  so lange wartet das Bild beim Laden auf den Hintergrund-Leser (ms, alt = 16)", 100, 2, 1000, 10);
            Num(QoL, "FastLoadMaxActions", "  Schritte pro Bild (Spiel = 500)", 5000, 500, 100000, 500);
            Num(QoL, "FastLoadQueueCap", "  Lade-Warteschlange: so viele Aufträge darf der Hintergrund-Leser vorausarbeiten (40 = Spiel)", 2000, 40, 100000, 100);
            Num(QoL, "FastLoadSpinUs", "  Übergabe zwischen Leser und Hauptthread: so lange ohne Schlafen warten (Mikrosekunden, 0 = aus = Spiel)", 300, 0, 5000, 50);
            Bool(QoL, "FastLoadNoSceneDelay", "  Szenen ohne die feste Wartezeit von 0,5 s starten", true);
            Bool(QoL, "FastLoadNoGc", "  beim Laden kein Speicher-Aufräumen (spart Zeit)", true);
            Num(QoL, "FastLoadGcCapMb", "    außer Speicher über ... MB", 3000, 500, 32000, 250);
            Bool(QoL, "SaveGroupCareer", "Spielstand-Liste: Reiter je Karriere (Spielstände einer Karriere zusammen)", true);
            Bool(QoL, "Lookout", "Ausguck: weite Sicht + bessere Meldungen (Hauptschalter, wirkt sofort)", true);
            Num(QoL, "LookoutGroupRange", "  Ausguck: Suchweite für Schiffsgruppen (1 = Spiel, 1.69 = Standard)", 1.6923f, 1, 3, 0.05f);
            Bool(QoL, "LookoutSmoke", "  Ausguck: Schornstein-Rauch am Horizont (Konvoi früher sichtbar; dein eigenes Boot zählt nicht)", true);
            Bool(QoL, "LookoutWeatherOnce", "  Ausguck: schlechte Sicht nur einmal zählen (Spiel zählt sie doppelt)", true);
            Bool(QoL, "LookoutWeatherFloor", "  Ausguck: Untergrenze für Nacht/Wetter (Konvoi auch in dunkler Nacht auffindbar)", true);
            Bool(QoL, "LookoutFog", "  Ausguck: Nebel versteckt Schiffe (aus, wenn ein Wetter-Mod eigenen Nebel hat)", true);
            Bool(QoL, "LookoutDaylightTrim", "  Ausguck: bei klarem Tag Reichweite und Rauch etwas kürzen", true);
            Bool(QoL, "LookoutTimeGuard", "  Ausguck: Zeitraffer bremsen, solange ein ungesehenes Schiff in Reichweite ist", true);
            Bool(QoL, "LookoutConvoyNight", "  Ausguck: nachts Konvois etwas weiter finden (bis x1.5, je mehr Schiffe)", true);
            Bool(QoL, "LookoutFixRoll", "  Ausguck: verdrehten Sicht-Würfel richtig herum (klare Ziele eher melden)", true);
            Bool(QoL, "LookoutDarkReports", "  Ausguck: auch im Dunkeln gesehene Schiffe laut melden", true);
            Bool(QoL, "LookoutKnownAgain", "  Ausguck: bekannte Kontakte erneut melden, wenn wieder gesehen", true);
            Bool(QoL, "LookoutRetry", "  Ausguck: verschluckte Meldungen wiederholen, sobald jemand frei ist", true);
            Bool(QoL, "LookoutAwashView", "  Ausguck: vorgeflutet etwas Sicht behalten (getaucht nie)", true);
            Num(QoL, "NightBright", "Nacht drau\u00dfen heller (%, 0 = Spiel)", 0, 0, 300, 10);
            Num(QoL, "InteriorBright", "Boot innen heller (%, 0 = Spiel)", 0, 0, 300, 10);
            Bool(QoL, "NightOptics", "Optik-Belichtung pro Sehrohr/UZO/Fernglas (nimmt dem Beobachtungs-Sehrohr den Extra-Hellbonus)", false);
            Num(QoL, "NightAttackEv", "  Angriffs-Sehrohr Belichtung in EV (unter dem Spiel-Bild)", -1.3f, -4, 1, 0.1f);
            Num(QoL, "NightObservationEv", "  Beobachtungs-Sehrohr Belichtung in EV (0 = Extra-Bonus weg)", 0, -4, 1, 0.1f);
            Bool(QoL, "NightUzoBinoGrading", "  auch UZO und Fernglas eigene Belichtung", false);
            Num(QoL, "NightUzoEv", "    UZO Belichtung in EV", 0, -4, 1, 0.1f);
            Num(QoL, "NightBinoEv", "    Fernglas Belichtung in EV", 0, -4, 1, 0.1f);
            Num(QoL, "NightOpticStrength", "  Glas-Charakter Stärke in % (Dunst am Tag, Farbverlust nachts)", 100, 0, 200, 5);
            Num(QoL, "NightAttackGlass", "  Glas-Anteil Angriffs-Sehrohr in %", 100, 0, 100, 5);
            Num(QoL, "NightObservationGlass", "  Glas-Anteil Beobachtungs-Sehrohr in %", 35, 0, 100, 5);
            Num(QoL, "NightReticleComp", "  Fadenkreuz-Ausgleich in % (hält das Fadenkreuz trotz dunklerem Bild lesbar)", 100, 0, 200, 5);
            Num(QoL, "NightOpticDayLift", "  Dunst bei Tag (Schwarzwert-Anhebung, 0-100)", 30, 0, 100, 5);
            Num(QoL, "NightOpticColourLoss", "  Farbverlust bei Nacht (0-100)", 45, 0, 100, 5);
            Num(QoL, "NightGainAttack", "  Nacht-Anpassung Angriffs-Sehrohr in % (100 = Spiel)", 100, 0, 300, 10);
            Num(QoL, "NightGainObservation", "  Nacht-Anpassung Beobachtungs-Sehrohr in %", 100, 0, 300, 10);
            Num(QoL, "NightGainUzo", "  Nacht-Anpassung UZO in %", 100, 0, 300, 10);
            Num(QoL, "NightGainBino", "  Nacht-Anpassung Fernglas in %", 100, 0, 300, 10);
            Num(QoL, "NightGainOther", "  Nacht-Anpassung alle anderen Ansichten in %", 100, 0, 300, 10);
            Bool(QoL, "CrewAIOverhaul", "Crew-KI: ruhigere, klügere Crew (Hauptschalter, wirkt ab nächstem Spielstart)", true);
            Bool(QoL, "CrewRestInBunks", "  Crew-KI: freie Matrosen legen sich in eine freie Koje statt im Gang zu stehen", true);
            Bool(QoL, "CrewNearestBed", "  Crew-KI: müde Männer nehmen die nächste freie Koje und reservieren sie", true);
            Bool(QoL, "CrewBedSharing", "  Crew-KI: zwei Mann pro Matrosen-Koje, wenn alle belegt sind (an: Spielstand neu laden)", false);
            Num(QoL, "CrewBedMen", "    so viele Männer pro Matrosen-Koje (1-50, 1 = keiner teilt, Änderung: Spielstand neu laden)", 2, 1, 50, 1);
            Bool(QoL, "OfficerOverflowSleep", "Offiziere ohne freie Offizierskoje schlafen in einer Mannschaftskoje", true);
            Num(QoL, "OfficerRestBelow", "  ohne Dienst hinlegen unter x Energie", 0.30f, 0.02f, 0.9f, 0.01f);
            Num(QoL, "OfficerWakeAt", "  aufstehen ab x Energie", 0.95f, 0.5f, 1f, 0.01f);
            Num(QoL, "OfficerIdleBefore", "  erst nach x Sekunden ohne Arbeit hinlegen", 30, 25, 3600, 5);
            Bool(QoL, "HotBunkAuto", "Zu wenig Kojen: Schlaf erholt schneller (automatisch berechnet)", true);
            Num(QoL, "HotBunkHours", "  Schlaf pro Mann und Tag (Stunden)", 8, 1, 24, 1);
            Num(QoL, "HotBunkUse", "  erreichbare Kojen-Auslastung", 0.70f, 0.1f, 1f, 0.05f);
            Num(QoL, "HotBunkMax", "  höchstens x-mal schneller", 10, 1, 100, 0.5f);
            Num(QoL, "HotBunkBoost", "  zusätzlich x-mal schneller (immer, in jeder Koje)", 3, 1, 100, 0.5f);
            Bool(QoL, "HotBunkSailors", "  gilt auch für Matrosen", true);
            Num(QoL, "HotBunkManual", "  fester Faktor, wenn automatisch aus (1 = normal)", 1, 0.1f, 100, 0.5f);
            Num(QoL, "OfficerEndurance", "Offiziere ermüden an Stationen x-mal so schnell (1 = normal)", 1, 0.05f, 5, 0.05f);
            Num(QoL, "OfficerCards", "Offiziere spielen in der Freizeit Karten (Vorrang, 0 = aus)", 3, 0, 11, 1);
            Bool(QoL, "ActiveSonar", "Aktives Sonar (Ping): findet alles in Reichweite sicher, Werte genau - macht uns aber lauter", true);
            Num(QoL, "SonarRange", "  Reichweite (m)", 3000, 500, 20000, 100);
            Num(QoL, "SonarArc", "  Suchwinkel (Grad, 360 = rundum)", 360, 10, 360, 5);
            Num(QoL, "SonarPingSeconds", "  ein Ping wirkt x Sekunden", 8, 1, 60, 1);
            Num(QoL, "SonarLoudness", "  so viel lauter sind wir beim Ping (für Gegner)", 0.5f, 0, 5, 0.05f);
            Bool(QoL, "SonarScope", "Sonar-/Radar-Anzeige: runde Scheibe mit laufendem Zeiger (nur eigene Kontakte; mit der Maus verschiebbar)", true);
            Num(QoL, "SonarScopeKm", "  Bereich der Scheibe in km (äußerer Ring)", 10, 1, 200, 1);
            Num(QoL, "SonarScopeSize", "  Größe in Pixel", 220, 120, 600, 10);
            Num(QoL, "SonarScopeSweep", "  Zeiger: Sekunden pro Umlauf", 4, 1, 30, 1);
            Num(QoL, "SonarScopeX", "  Platz von links in %", 2, 0, 100, 1);
            Num(QoL, "SonarScopeY", "  Platz von oben in %", 30, 0, 100, 1);
            Bool(QoL, "SonarPingText", "  Ping-Ergebnis zusätzlich als Textzeile zeigen", true);
            Num(QoL, "SonarAutoSeconds", "  automatisch pingen alle x Sekunden unter Wasser (0 = nur per Taste)", 0, 0, 600, 5);
            Bool(QoL, "SonarAutoQuiet", "  automatisch nicht bei Alarm (leise bleiben)", true);
            Bool(QoL, "SonarSubmergedOnly", "  nur getaucht", true);
            Bool(QoL, "SonarNeedsOperator", "  Horchraum muss besetzt sein", true);
            Bool(QoL, "AirByCrew", "Luft: volle Besatzung taucht immer gleich lange, egal wie groß", true);
            Num(QoL, "AirHours", "  Tauchzeit bei voller Besatzung (Stunden)", 11, 0.25f, 48, 0.25f);
            Bool(QoL, "AirPrisoners", "  Gefangene verbrauchen Luft mit", false);
            Num(QoL, "AirPrisonerPenalty", "  bei vollem Gefangenenraum x % kürzer", 25, 1, 90, 1);
            Bool(QoL, "FoodByCrew", "Essen: größere Besatzung isst die Vorräte nicht schneller", true);
            Num(QoL, "FoodRefCrew", "  essen wie x Mann (0 = normale Bootsbesatzung)", 0, 0, 200, 1);
            Num(QoL, "TightPackPerMan", "Fähigkeit Dichtes Packen: +x Stapelgröße pro Mann (0 = wie im Spiel)", 10, 0, 100, 1);
            Bool(QoL, "BoardingQueue", "Hafen: Einschiffen-Häkchen ohne Ruckler (nacheinander ausführen)", true);
            Bool(QoL, "GuardNoQueue", "Fehlerschutz: Mann ohne Auftragsliste", true);
            Bool(QoL, "GuardCards", "Fehlerschutz: Kartenspiel ohne Tisch", true);
            Bool(QoL, "GuardEnemyLauncher", "Fehlerschutz: kaputter Torpedowerfer eines Gegners", true);
            Bool(QoL, "GuardCeiling", "Fehlerschutz: Decken-Blende im Boot", true);
            Bool(QoL, "GuardPortraits", "Fehlerschutz: Porträt-Leiste und Gefolgs-Knöpfe", true);
            Bool(QoL, "GuardSchedule", "Fehlerschutz: Dienstplan- und Trupp-Fenster", true);
            Bool(QoL, "GuardSquadType", "Fehlerschutz: Trupp-Knöpfe ändern Mann-Typ", true);
            Bool(QoL, "CrewBedYield", "  Crew-KI: ausgeruhte Männer machen die Koje für Müde frei", true);
            Bool(QoL, "CrewBowRunCap", "  Crew-KI: beim Alarmtauchen nur wenige Männer zum Bug (aus = Spiel)", true);
            Num(QoL, "CrewBowRunMen", "    so viele Männer zum Bug (0 = keiner)", 8, 0, 30, 1);
            Bool(QoL, "CrewSkipperAttackScope", "  Crew-KI: Angriffssehrohr nur für den Kommandanten", true);
            Bool(QoL, "CrewScopeNoOfficers", "  Crew-KI: Offiziere gehen nicht von selbst ans Beobachtungs-Sehrohr (Befehl geht weiter)", true);
            Bool(QoL, "CrewNoEatAlarm", "  Crew-KI: bei Alarm nur essen, wenn sehr hungrig", true);
            Bool(QoL, "CrewControlRoom", "  Crew-KI: Männer ohne Aufgabe bleiben aus der Zentrale", true);
            Bool(QoL, "CrewNearbyJobs", "  Crew-KI: Aufgaben in der Nähe bevorzugen", true);
            Bool(QoL, "CrewWatchClaims", "  Crew-KI: Wachposten reservieren (nicht zwei Mann zum selben Platz)", true);
            Bool(QoL, "CrewKeepJobs", "  Crew-KI: laufende Arbeit nicht für fast gleiche Arbeit abbrechen", true);
            Bool(QoL, "CrewOrderRoom", "  Crew-KI: deine Befehle gehen nicht verloren, wenn die Aufgabenliste voll ist", true);
            Bool(QoL, "CrewHatchOrders", "  Crew-KI: \"Alle Luken schließen\" bleibt geschlossen", true);
            Bool(QoL, "CrewStuckCooldown", "  Crew-KI: wer 3 Plätze nicht erreicht, versucht es 1 Minute nicht mehr", true);
            Bool(QoL, "LeakTwoSailors", "Lecks: abdichten wie mit zwei Matrosen", true);
            Bool(QoL, "Speedometer", "Tacho + Kilometerzähler oben links (Knoten, Fahrt-km, gesamt)", true);
            Bool(QoL, "NoAutoAlarm", "Kein automatischer Alarm (nur wenn du ihn selbst gibst)", false);
            Bool(QoL, "WarDiary", "Kriegstagebuch: Kontakte, Funk, Schäden, Häfen in logs\\kriegstagebuch.txt", true);
            Bool(QoL, "TubeDisplay", "Rohr-Anzeige oben links: welcher Torpedo-Typ in welchem Rohr", true);
            Num(QoL, "EnergySaverPercent", "Strom sparen: unter Wasser bei Batterie unter x % Kompressor + Scheinwerfer aus (0 = aus)", 20, 0, 50, 5);
            Num(QoL, "TorpedoLoadSpeed", "Torpedos x schneller nachladen (1 = normal, 1000 = sofort)", 1, 1, 1000, 1);
            Bool(QoL, "FastLoading", "Schneller laden: Ladebildschirm nutzt die volle Rechenzeit", true);
            Bool(QoL, "AutoPump", "Lenzpumpe schaltet sich bei Wasser im Boot selbst an (#38)", true);
            Bool(QoL, "PortLoadingFix", "Hafen-Grundfix: kein Einfrieren beim Verladen, hängende Aufträge werden gelöst (#39)", true);
            Num(QoL, "PortLoadingStuckSeconds", "  Hängt das Verladen so viele Sekunden: neu anstoßen, dann abbrechen", 30, 5, 300, 5);
            Bool(QoL, "CancelPortLoading", "  >> Verladen jetzt abbrechen (Schalter springt zurück)", false);
            Bool(QoL, "FixMyDamnShipment", "  >> FIX MY DAMN SHIPMENT! Alles Bestellte sofort ins Boot zwingen", false);
            Bool(QoL, "InstantPortLoading", "Hafen: alles sofort im Boot, keine Wartezeit", true);
            Num(QoL, "RebreatherDurability", "Atemgeräte halten x-mal länger", 2, 1, 10, 0.5f);
            Num(QoL, "MerchantTraffic", "Mehr Frachter: Konvois + einzelne Handelsschiffe x so oft (1 = Spiel)", 3, 0.1f, 50, 0.5f);
            Bool(QoL, "MilkCowsAtStart", "Milchkühe ab Spielstart unterwegs (#25)", true);
            Bool(QoL, "MilkCowRadio", "  Funk-Nachschub: nächste Milchkuh kommt in Reichweite", true);
            Num(QoL, "MilkCowRadioKm", "    Abstand in km", 30, 5, 500, 5);
            Bool(QoL, "EasyTDC", "Torpedo-Rechner einfach: Lösung wird automatisch berechnet + angezeigt (#17)", true);
            Bool(QoL, "TorpedoStayMaintained", "Torpedos bleiben gewartet (Wartung läuft nicht ab, Crew muss nicht nachwarten)", true);
            Num(QoL, "DeckGunTurnSpeed", "88mm: Dreh-/Neige-Tempo (1 = normal) (#27)", 1, 0.5f, 10, 0.5f);
            Num(QoL, "DeckGunRPM", "88mm: Schuss pro Minute (0 = Spielwert) (#27)", 0, 0, 120, 5);
            Num(QoL, "DeckGunMagazine", "88mm: Magazin (0 = Spielwert; groß = Dauerfeuer; Einschießen bleibt so schnell wie im Spiel) (#28)", 0, 0, 200, 1);
            Num(QoL, "AAGunTurnSpeed", "Flak: Dreh-/Neige-Tempo (1 = normal) (#27)", 1, 0.5f, 10, 0.5f);
            Num(QoL, "AAGunRPM", "Flak: Schuss pro Minute (0 = Spielwert; der Magazinwechsel bleibt wie im Spiel) (#27)", 0, 0, 600, 10);
            Num(QoL, "AAGunMagazine", "Flak: Magazin (0 = Spielwert) (#28)", 0, 0, 500, 5);
            Num(QoL, "DeckGunShellSpeed", "88mm: Geschossgeschwindigkeit (1 = normal)", 1, 0.5f, 5, 0.25f);
            Num(QoL, "AAGunShellSpeed", "Flak: Geschossgeschwindigkeit (1 = normal)", 1, 0.5f, 5, 0.25f);
            Num(QoL, "GunnerAccuracy", "KI-Schützen: Treffsicherheit mindestens (0 = Spiel, 0.5 = nie schlechter als halb eingeschossen, 1 = keine Streuung)", 0, 0, 1, 0.1f);
            Bool(QoL, "GunGyroCrew", "Geschütz-Kreisel: KI-Schützen gleichen Wellen sofort aus (kein Nachzittern)", true);
            Bool(QoL, "GunGyroManual", "Geschütz-Kreisel: wenn du selbst zielst, bleibt der Zielpunkt trotz Wellen stehen", true);
            Num(QoL, "GunnerRangingSpeed", "KI-Schützen: schneller einschießen (1 = Spiel)", 2, 1, 5, 0.25f);
            Num(QoL, "GunnerKeepRanging", "KI-Schützen: beim Zielwechsel so viel % vom Einschießen behalten (55 = Spiel)", 75, 0, 100, 5);
            Num(QoL, "GunnerNightPenalty", "KI-Schützen: Zusatz-Streuung, wenn Ziel nicht mit Augen gesehen, in % (100 = Spiel)", 50, 0, 100, 10);
            Num(QoL, "HorcherRange", "Horcher besetzt: hört so viel % weiter (0 = Spiel)", 25, 0, 100, 5);
            Num(QoL, "HorcherBearing", "Horcher besetzt: Peilung so viel % genauer (0 = Spiel)", 30, 0, 90, 5);
            Bool(QoL, "NoShellPush", "Granaten schleudern getroffene Schiffe nicht weg (Schaden bleibt voll)", true);
            Num(QoL, "DeckGunRecoil", "88mm: Rückstoß nach oben (0 = keiner, 1 = Spiel)", 0, 0, 1, 0.1f);
            Num(QoL, "AAGunRecoil", "Flak: Rückstoß nach oben (0 = keiner, 1 = Spiel)", 0, 0, 1, 0.1f);
            Num(QoL, "DeckGunRange", "88mm: KI-Schützen greifen bis x km an (0 = Spielwert ~12 km; Granaten werden dafür schnell genug)", 30, 0, 100, 1);
            Num(QoL, "AAGunRange", "Flak: KI-Schützen greifen bis x km an (0 = Spielwert)", 0, 0, 50, 0.5f);
            Num(QoL, "DeckGunDepress", "88mm: so viele Grad nach unten neigen (0 = Spielwert)", 0, 0, 60, 1);
            Num(QoL, "DeckGunShellMps", "88mm: Mündungsgeschwindigkeit in m/s (0 = Spielwert, dann x Geschossgeschwindigkeit; bei eigener Reichweite mindestens so schnell, dass die Granate so weit fliegt)", 0, 0, 5000, 50);
            Num(QoL, "DeckGunRateMul", "88mm: Feuerrate x (1 = normal, wirkt auch auf Schuss pro Minute)", 1, 0.25f, 10, 0.25f);
            Num(QoL, "DeckGunShellMass", "88mm: Geschossgewicht in kg (0 = Spielwert)", 0, 0, 5000, 5);
            Num(QoL, "DeckGunShellMassMul", "88mm: Geschossgewicht x (1 = normal)", 1, 0.1f, 100, 0.5f);
            Num(QoL, "DeckGunShellDamageMul", "88mm: Schaden am Schiff x (1 = normal)", 1, 0.1f, 100, 0.5f);
            Bool(QoL, "DeckGunKineticDamage", "88mm: Schaden wächst mit Gewicht x Geschwindigkeit (wie früher)", false);
            Num(QoL, "AAGunDepress", "Flak: so viele Grad nach unten neigen (0 = Spielwert)", 30, 0, 60, 1);
            Bool(QoL, "AAGunBothTargets", "Geschütze: Schiffe und Flugzeuge zugleich angreifen (88/105 zuerst Schiffe, Flak zuerst Flugzeuge)", true);
            Bool(Cheat, "PortDefenseDestroyable", "Hafenverteidigung zerstörbar (Treffer in der Nähe schaltet Geschütz aus)", true);
            Num(Cheat, "PortDefenseMinutes", "  Hafengeschütz repariert nach (Minuten Spielzeit)", 30, 1, 240, 1);
            Num(Cheat, "PortDefenseHitM", "  Trefferradius (m)", 15, 3, 60, 1);
            Bool(QoL, "AASaveAmmo", "Flugzeuge nur beschießen, wenn sie nah genug sind (spart Munition)", true);
            Num(QoL, "AAEngageKm", "  Flugzeuge: höchstens so weit beschießen (km)", 2.5f, 0.5f, 10f, 0.5f);
            Bool(QoL, "KeepGunMode", "Geschütze: Feuermodus bleibt, wenn ein Kanonier (neu) ans Geschütz geht (sonst stellt das Spiel auf Feuer einstellen)", true);
            Num(QoL, "AAGunShellMps", "Flak: Mündungsgeschwindigkeit in m/s (0 = Spielwert, dann x Geschossgeschwindigkeit; bei eigener Reichweite mindestens so schnell, dass die Granate so weit fliegt)", 0, 0, 5000, 50);
            Num(QoL, "AAGunRateMul", "Flak: Feuerrate x (1 = normal, wirkt auch auf Schuss pro Minute)", 1, 0.25f, 10, 0.25f);
            Num(QoL, "AAGunShellMass", "Flak: Geschossgewicht in kg (0 = Spielwert)", 0, 0, 5000, 5);
            Num(QoL, "AAGunShellMassMul", "Flak: Geschossgewicht x (1 = normal)", 1, 0.1f, 100, 0.5f);
            Num(QoL, "AAGunShellDamageMul", "Flak: Schaden am Schiff x (1 = normal)", 1, 0.1f, 100, 0.5f);
            Bool(QoL, "AAGunKineticDamage", "Flak: Schaden wächst mit Gewicht x Geschwindigkeit (wie früher)", false);
            Bool(QoL, "AAGunLike88", "Flak so stark wie die 88: dreht so schnell und neigt so tief", false);
            Num(QoL, "GunAimAssist", "Zielhilfe beim Selbstzielen: Schiff bis x Grad neben dem Rohr wird mit Vorhalt getroffen (0 = aus)", 0, 0, 15, 1);
            Bool(QoL, "AAvsShips", "Flak schießt automatisch auch auf Schiffe", false);
            Num(QoL, "GunSoundGap", "Schussgeräusch höchstens alle x Sek. (0 = aus, gegen Tonprobleme)", 0.12f, 0, 1, 0.02f);

            // ---------- Cheats (single player) ----------
            Num(Cheat, "CheatPeriscopeHeight", "Sehrohr bis zu x m ausfahren (0 = aus) (Cheat #34)", 0, 0, 100, 1);
            Bool(Cheat, "CheatUndetectable", "Boot unentdeckbar (Sicht, Sonar, Horchgerät, Radar)", false);
            // 0.4.32 (DoMe 2026-11-18): enemy-weakening cheats, one switch per weapon kind, all off by default. Own weapons are never touched.
            Bool(Cheat, "CheatEnemyNoRanging", "Gegner schwächen: Gegner-Kanonen können sich nicht einschießen (werden nie genauer)", false);
            Bool(Cheat, "CheatPacifistGuns", "Gegner ist Pazifist: Gegner-Kanonen und Gegner-Flak feuern nicht (Schiffe, Häfen, Flugzeuge)", false);
            Bool(Cheat, "CheatPacifistDepthCharges", "Gegner ist Pazifist: keine Wasserbomben", false);
            Bool(Cheat, "CheatPacifistHedgehog", "Gegner ist Pazifist: keine Hedgehog-Salven", false);
            Bool(Cheat, "CheatPacifistBombs", "Gegner ist Pazifist: Flugzeuge werfen keine Bomben", false);
            Bool(Cheat, "CheatPacifistRockets", "Gegner ist Pazifist: Flugzeuge schießen keine Raketen", false);
            Bool(Cheat, "CheatPacifistTorpedoes", "Gegner ist Pazifist: Gegner schießen keine Torpedos", false);
            Bool(Cheat, "CheatPerfectAim", "Aimbot: 100 % Einschuss, keine Streuung, eigene Schüsse treffen mit Vorhalt (bis 30 Grad)", false);
            Bool(Cheat, "CheatInfiniteGunAmmo", "Deckgeschütz: Munition unendlich", false);
            Bool(Cheat, "CheatInfiniteFlakAmmo", "Flak: Munition unendlich", false);
            Bool(Cheat, "CheatSpecPoints", "Crew-Spezialisierungen (Tauchgang, Schleichfahrt, Disziplin): immer genug Punkte", false);
            Bool(Cheat, "CheatInfiniteTorpedoes", "Torpedos unendlich: Rohr lädt nach dem Schuss sofort neu (#26)", false);
            Bool(Cheat, "CheatAllT5", "  Nachgeladen wird immer ein T5 Zaunkönig (zielsuchend)", false);
            Num(Cheat, "ReloadType", "  Nachladen mit: 0 = wie zuletzt, 1-5 = T1-T5, 6 = Mine", 0, 0, 6, 1);
            Bool(Cheat, "CheatNoDuds", "Keine Torpedo-Versager", false);
            Num(Cheat, "CheatSpeed", "Boot schneller x (über und unter Wasser) (1 = normal) (#33)", 1, 1, 5, 0.25f);
            Num(Cheat, "CheatPumpPower", "Lenzpumpe x stärker (1 = normal) (#38)", 1, 1, 50, 1);
            Bool(Cheat, "CheatPumpAnyDepth", "Lenzpumpe arbeitet in jeder Tiefe voll", false);
            Bool(Cheat, "CheatNeverFlood", "Boot kann nie volllaufen (Wasser wird sofort abgepumpt)", false);
            Num(Cheat, "CheatPumpTestWaterM3", "Testwasser: so viele m³ (1-10)", 5, 1, 10, 1);
            Bool(Cheat, "CheatPumpTestWater", ">> Testwasser ins Boot: Grundreinigung durch Flutung (in den Raum der Kamera, sonst Zentrale)", false);
            Num(Cheat, "CheatCrewWalkSpeed", "Crew läuft x schneller (1 = normal)", 1, 1, 3, 0.25f);
            Bool(Cheat, "CheatImmortalCrew", "Crew unsterblich (stirbt nie)", false);
            Bool(Cheat, "CheatNoWounds", "Crew unverletzbar (keine Wunden, verliert keine Gesundheit)", false);
            Bool(Cheat, "CheatNoRagdoll", "Crew fällt bei Treffern nicht um", false);
            Bool(Cheat, "CheatNoSupplyUse", "Vorräte gehen nie aus (Essen, Ersatzteile, Verbandskästen ...)", false);
            Bool(Cheat, "CheatNoWeight", "Munition und Gegenstände fast ohne Gewicht (0,0000001)", false);
            Bool(Cheat, "CheatNoMassLimit", "Lager ohne Gewichtsgrenze", false);
            Bool(Cheat, "AutoExpandStorage", "Lager wächst mit: fast voll = neue Plätze (Haupt-, Item-Lager, Küche)", false);
            Num(Cheat, "AutoExpandStep", "  So viele Plätze pro Erweiterung", 2, 1, 10, 1);
            Num(Cheat, "AutoExpandMax", "  Höchstens so viele Plätze pro Lager", 100, 10, 300, 10);
            Num(Cheat, "StorageMinSlots", "  Lager mindestens x Plätze, sofort (0 = aus)", 0, 0, 200, 5);
            Num(Cheat, "CheatStackSize", "Lager: x mehr pro Platz stapeln (1 = normal, INI ohne Obergrenze)", 1, 1, 1000, 10);
            Num(Cheat, "CheatMinStackSize", "  Lager: mindestens so viele pro Platz (0 = aus)", 0, 0, 1000000, 1000);
            Bool(Cheat, "CheatMergeStacks", "  Lager: gleiche Sachen automatisch zu einem Stapel zusammenlegen", true);
            Bool(Cheat, "CheatStackAll", "  Lager: auch Sachen stapeln, die im Spiel nur einzeln gehen (alle Waren außer Diesel)", true);
            Num(Cheat, "CheatShopStock", "  Händler im Hafen hat von jeder Ware mindestens so viel (0 = wie im Spiel)", 0, 0, 1000000, 1000);
            Num(Cheat, "CheatDetect", "Kontakte dauerhaft auf der Karte (0 = aus, 1 = nur Ziele, 2 = alle)", 0, 0, 2, 1);
            Num(Cheat, "CheatDetectKm", "  nur im Umkreis von x km (0 = überall)", 0, 0, 2000, 50);
            // 0.4.4 (DoMe 2026-11-17): one factor per sensor instead of one for all. 1 = game value.
            Num(Cheat, "CheatRangeHydro", "Reichweite Horchgerät x (1 = Spiel)", 1, 0.25f, 10, 0.25f);
            Num(Cheat, "CheatRangeSonar", "Reichweite aktives Sonar (Bordgerät) x (1 = Spiel)", 1, 0.25f, 10, 0.25f);
            Num(Cheat, "CheatRangeRadar", "Reichweite Radar x (1 = Spiel)", 1, 0.25f, 10, 0.25f);
            Num(Cheat, "CheatRangeRadarWarn", "Reichweite Radarwarner x (1 = Spiel)", 1, 0.25f, 10, 0.25f);
            Num(Cheat, "CheatRangeMaxKm", "Obergrenze Horchgerät, Sonar, Radar in km (mehr geht nie)", 1000, 50, 20000, 50);
            Num(Cheat, "CheatRangeMaxKmLookout", "Obergrenze Sichtweite (Ausguck, Sehrohr) in km (mehr geht nie)", 1000, 10, 5000, 10);
            Num(Cheat, "CheatRangeLookout", "Reichweite Ausguck und Sehrohr x (1 = Spiel)", 1, 0.25f, 50, 0.25f);
            Bool(Cheat, "CheatDeepDive", "Kein Wasserdruck-Schaden in der Tiefe (Rumpf hält)", false);
            Bool(Cheat, "CheatRudderNoHelmsman", "Ruder geht auch ohne Steuermann", false);
            Bool(Cheat, "CheatNoHullDamage", "Rumpf verliert keine Gesundheit", false);
            Bool(Cheat, "CheatOwnShellsSafe", "Eigene Granaten und Flak beschädigen das eigene Boot und die Crew nicht", true);
            Bool(Cheat, "CheatFullFuel", "Diesel wird nicht verbraucht (Tank bleibt voll)", false);
            Bool(Cheat, "CheatFullBattery", "Batterie bleibt voll", false);
            Bool(Cheat, "CheatFullAir", "Luft unter Wasser unbegrenzt", false);
            Bool(Cheat, "CheatCrewNeverTired", "Crew wird nie müde", false);
            Bool(Cheat, "CheatFullDiscipline", "Moral (Disziplin) immer voll und sinkt nie", false);
            Bool(Cheat, "CheatAllAchievements", "  >> Alle Steam-Erfolge freischalten", false);
            Bool(Cheat, "CheatMissionDone", ">> Aktuellen Auftrag als erfüllt setzen (danach im Heimathafen abgeben)", false);
            Bool(Cheat, "CheatEscortEasy", "Geleitschutz: keine neuen Angreifer", false);
            Bool(Cheat, "CheatEscortFullSpeed", "Geleitschutz: Geleit fährt volle Fahrt", false);
            Bool(Cheat, "CheatEscortNoDamage", "Geleitschutz: Geleit nimmt keinen Schaden", false);
            Bool(Cheat, "CheatMissionStep", ">> Aktuellen Auftrag einen Schritt weiter schalten", false);
            Num(Cheat, "CheatMoney", "Geld: nie weniger als (0 = aus)", 0, 0, 1000000, 10000);
            Num(Cheat, "CheatReputation", "Ansehen (Ruf-Punkte): nie weniger als (0 = aus)", 0, 0, 1000, 10);
            Num(Cheat, "CheatPriceFactor", "Preise im Hafen x (1 = normal, 0,01 = fast gratis)", 1, 0.01f, 1, 0.01f);
            Num(Cheat, "CheatRepairSpeed", "Reparieren x schneller (1 = normal, 1000 = ca. 1 Sek.)", 1, 1, 1000, 10);
            Num(Cheat, "CheatResearchSpeed", "Forschung/Aufgaben im Hauptquartier x schneller", 1, 1, 1000, 10);
            Bool(Cheat, "DevConsole", "Entwickler-Konsole des Spiels einschalten (mit „Erfolge immer erlaubt“ ohne Achievement-Verlust)", false);
            Bool(Cheat, "DevConsoleNoErrorPopup", "  ohne die kurze Meldung „Fehler protokolliert“ bei jedem internen Fehler (steht weiter im Log)", true);
            Bool(Cheat, "CheatInstantResearch", "Aufgaben im Hauptquartier (Forschung, Produktion) sofort fertig", false);
            Bool(Cheat, "CheatNoResearchRequirements", "Forschung ohne Voraussetzungen: keine Vorgänger-Forschung, kein Datum, kein Offizier nötig", false);
            Bool(Cheat, "CheatResearchAll", "  >> Alles erforschen", false);
            Num(Cheat, "CheatAddReputationAmount", "  Punkte pro Klick (für die zwei Knöpfe unten)", 10, 1, 1000000, 1);
            Bool(Cheat, "CheatAddReputation", "  >> Ansehen (Ruf-Punkte) dazu (damit bezahlst du Forschung + Matrosen)", false);
            Bool(Cheat, "CheatAddUpgradePoints", "  >> Upgrade-Punkte dazu", false);
            Num(Cheat, "CheatAddSailorsAmount", "Matrosen pro Klick (für den Knopf darunter)", 5, 1, 200, 1);
            Bool(Cheat, "CheatAddSailors", "  >> Matrosen anwerben (neue Männer kommen an Bord)", false);
            Bool(Cheat, "CheatUnlockAll", "  >> Alle Upgrades freischalten", false);
            Bool(Cheat, "CheatUnlockBoats", "  >> Alle Boote freischalten", false);
            Num(Cheat, "CheatTorpedoSpeed", "Torpedos x schneller (Rechner zielt mit, Treffer sicherer)", 1, 1, 5, 0.25f);
            Bool(Cheat, "CheatQuickTravel", "Schnellreise ans Kursziel per Taste (Tab „Tasten“)", false);
            Bool(Cheat, "CheatQuickTravelNow", "  >> Jetzt ans Kursziel springen", false);
            Bool(Cheat, "CheatDeckCrewStays", "Deck-Crew bleibt beim Tauchen draußen", false);
            Bool(Cheat, "CheatGunsUnderWater", "Eigene Geschütze bleiben beim Tauchen benutzbar", false);
            Bool(Cheat, "CheatNoDrown", "Crew ertrinkt draußen nie", false);
            Bool(Cheat, "CheatHealNow", "  >> Alle heilen (Wunden, Blutungen, Krankheiten, wieder aufstehen)", false);
            Bool(Cheat, "CheatRadioAnyDepth", "Funk-Empfang in jeder Tiefe", false);
            Bool(Cheat, "CheatHydrophoneAlways", "Horchgerät hört voll, auch aufgetaucht", false);
            Bool(Cheat, "CheatHydrophoneFast", "Horchgerät hört voll, auch bei hoher Fahrt", false);
            Bool(Cheat, "CheatInstantSolution", "Schusslösung sofort: Ziel ist beim ersten Sichten voll erfasst (Typ, Tempo, Kurs, Entfernung)", false);
            Bool(Cheat, "CheatExactTargets", "Zielerfassung genau: Tempo, Kurs, Entfernung exakt statt geschätzt", false);
            Num(Cheat, "CheatHullArmour", "Panzer-Rumpf: Schaden am Boot x (1 = normal, 0 = kein Schaden)", 1, 0, 1, 0.05f);
            Bool(Cheat, "CheatCrewProtection", "Rumpf schützt die Crew: keine Verletzungen durch Treffer", false);
            Bool(Cheat, "CheatSonarPing", "Echolot pingt unter Wasser von selbst", false);
            Num(Cheat, "CheatExtraSkillPoints", "Offiziere: x Skillpunkte extra", 0, 0, 50, 1);
            Bool(Cheat, "CheatUnlimitedSkillPoints", "Offiziere: Skillpunkte unbegrenzt (alle Skills lernbar)", false);
            Bool(Cheat, "CheatSkillsAnyLevel", "Alle Skills ab Stufe 1 lernbar (keine Stufen-Sperre)", false);
            Bool(Cheat, "CheatLinguist", "Wer eine Fremdsprache spricht, wird Sprachgenie (Linguist: alle Sprachen)", false);
            Bool(Cheat, "CheatAllSkills", "Offiziere dürfen Skills aller Klassen wählen", false);
            Bool(Cheat, "CheatSinkConvoy", "  >> Sink the convoy: alle Gegner-Schiffe in Reichweite bekommen Torpedos", false);
            Num(Cheat, "SinkConvoyRange", "  Reichweite in km", 20, 1, 100, 1);
            Num(Cheat, "SinkConvoyTorpedoes", "  Torpedos pro Schiff", 3, 1, 10, 1);
            Bool(Cheat, "DepthChargeCounter", "Wasserbomben-Zähler für den Erfolg „Survivor“ anzeigen (100 in einer Begegnung, näher als 1,5 km)", true);
            Bool(Cheat, "CheatSpawnConvoy", "  >> Konvoi spawnen (Gegner-Konvoi taucht vor dir auf)", false);
            Num(Cheat, "SpawnConvoyKm", "  Abstand voraus in km", 15, 2, 100, 1);
            Bool(Cheat, "CheatRemoteTorpedo", "Fernschuss per Taste (auch aus der Koje): Torpedo auf nächsten Gegner", false);
            Bool(Cheat, "BackupBeforeCheats", "Backup-Spielstand, sobald ein Cheat eingeschaltet wird", true);
            Num(Cheat, "ExtraOfficers", "Mehr Offiziere an Bord erlaubt (+x, 0 = Spiel)", 0, 0, 12, 1);
            Num(Cheat, "ExtraSailors", "Mehr Matrosen an Bord erlaubt (+x, 0 = Spiel)", 0, 0, 200, 1);
            Bool(Cheat, "SailorsFollowFullCrew", "Matrosen-Plätze passen sich \"Volle Crew\" an (ganze gewünschte Besatzung darf an Bord)", true);
            Bool(Cheat, "CheatShowActive", "Aktive Cheats oben links anzeigen", true);

            // ---------- Tasten (frei belegbar) ----------
            Key("KeyMenu", "Menü öffnen/schließen", "F10");
            Key("KeyDepthDeeper", "Tiefe: tiefer (allein = Schritt, Shift = x10, Alt = x100)", "PageDown");
            Key("KeyDepthShallower", "Tiefe: höher (allein = Schritt, Shift = x10, Alt = x100)", "PageUp");
            Key("KeyAutoDepth", "Bodenabstand-Autopilot an/aus", "Ctrl+B");
            Key("KeyAutoDepthBottom", "Aufsetzen auf den Grund an/aus", "Ctrl+Shift+B");
            Key("KeyUnderFire1", "Unter Beschuss: Alarmtauchen", "Ctrl+Shift+Alpha1");
            Key("KeyUnderFire2", "Unter Beschuss: Ausweichen", "Ctrl+Shift+Alpha2");
            Key("KeyUnderFire3", "Unter Beschuss: Zurückschießen", "Ctrl+Shift+Alpha3");
            Key("KeyUnderFire4", "Unter Beschuss: Nichts tun", "Ctrl+Shift+Alpha4");
            Key("KeyUnderFireIgnore", "Unter Beschuss: Meldungen ignorieren", "Ctrl+Shift+Alpha5");
            Key("KeySpeedFaster", "Tempo-Cheat: schneller (+0,5)", "Ctrl+PageUp");
            Key("KeySpeedSlower", "Tempo-Cheat: langsamer (-0,5)", "Ctrl+PageDown");
            Key("KeyReloadAll", "Alle leeren Rohre laden", "Ctrl+Shift+R");
            Key("KeyQuickTravel", "Schnellreise ans Kursziel (Cheat muss an sein)", "Ctrl+Shift+T");
            Key("KeyBackToWater", "Zurück ins Wasser", "Ctrl+Shift+W");
            Key("KeySortStorage", "Lager aufräumen", "");
            Key("KeySwapShifts", "Wachen tauschen", "");
            Key("KeyCallMilkCow", "Milchkuh herholen", "");
            Key("KeyRemoteTorpedo", "Fernschuss: Torpedo auf nächsten Gegner (Cheat muss an sein)", "Ctrl+Shift+F");
            Key("KeySinkConvoy", "Sink the convoy", "");
            Key("KeySpawnConvoy", "Konvoi spawnen", "");
            Key("KeyHealAll", "Alle heilen", "");
            Key("KeySonarPing", "Sonar-Ping", "Ctrl+Shift+S");
            Bool(KeysTab, "TelegraphBlockScopes", "Maschinen-Tasten am Sehrohr/UZO/Fernglas und in freier Kamera ignorieren", true);
            Bool(KeysTab, "CourseKeys", "Kurs- und Tiefen-Tasten (Pfeile, F/R mit Strg/Shift, Strg+P/C)", false);
            Bool(KeysTab, "CourseBlockScopes", "  am Sehrohr nur Pfeile, in freier Kamera keine", true);
            Key("KeyCourseFine", "Kurs/Tiefe: fein (5° / 5 m)", "LeftControl");
            Key("KeyCourseCoarse", "Kurs/Tiefe: grob (45° / 20 m)", "LeftShift");
            Key("KeyCoursePort", "Kurs: nach Backbord", "LeftArrow");
            Key("KeyCourseStarboard", "Kurs: nach Steuerbord", "RightArrow");
            Key("KeyCourseAbout", "Kurs: wenden (180°)", "DownArrow");
            Key("KeyCourseAmidships", "Ruder mittschiffs", "UpArrow");
            Key("KeyCourseBlocker", "Kurs-Sperre (mit fein)", "CapsLock");
            Key("KeyCourseDeeper", "Tiefer (mit fein/grob, beim Loslassen)", "F");
            Key("KeyCourseShallower", "Flacher (mit fein/grob, beim Loslassen)", "R");
            Key("KeyCoursePeriscope", "Sehrohrtiefe (mit fein; fein+grob = 20 m)", "P");
            Key("KeyCourseMaxDepth", "größte sichere Tiefe (mit fein)", "C");
            Key("KeyGearStop", "Maschine: Stopp", "Alt+Alpha0");
            Key("KeyGear1", "Maschine: Voraus 1", "Alt+Alpha1");
            Key("KeyGear2", "Maschine: Voraus 2", "Alt+Alpha2");
            Key("KeyGear3", "Maschine: Voraus 3", "Alt+Alpha3");
            Key("KeyGear4", "Maschine: Voraus 4", "Alt+Alpha4");
            Key("KeyGear5", "Maschine: Voraus 5 (\u00c4u\u00dferste)", "Alt+Alpha5");
            Key("KeyGearBack1", "Maschine: Zur\u00fcck 1", "Ctrl+Alt+Alpha1");
            Key("KeyGearBack2", "Maschine: Zur\u00fcck 2", "Ctrl+Alt+Alpha2");
            Key("KeyGearBack3", "Maschine: Zur\u00fcck 3", "Ctrl+Alt+Alpha3");
            Key("KeyGearBack4", "Maschine: Zur\u00fcck 4", "Ctrl+Alt+Alpha4");
            Key("KeyTurnL1", "Kurs: 1\u00b0 nach Backbord", "Ctrl+Shift+LeftArrow");
            Key("KeyTurnR1", "Kurs: 1\u00b0 nach Steuerbord", "Ctrl+Shift+RightArrow");
            Key("KeyTurnL5", "Kurs: 5\u00b0 nach Backbord", "Ctrl+LeftArrow");
            Key("KeyTurnR5", "Kurs: 5\u00b0 nach Steuerbord", "Ctrl+RightArrow");
            Key("KeyTurnL45", "Kurs: 45\u00b0 nach Backbord", "Shift+LeftArrow");
            Key("KeyTurnR45", "Kurs: 45\u00b0 nach Steuerbord", "Shift+RightArrow");
            Key("KeyTurnAbout", "Kurs: wenden (180\u00b0)", "Ctrl+DownArrow");
            Key("KeyAmidships", "Ruder mittschiffs", "Ctrl+UpArrow");
            Key("KeyDeeper5", "Tiefe: 5 m tiefer", "Ctrl+F");
            Key("KeyShallower5", "Tiefe: 5 m h\u00f6her", "Ctrl+R");
            Key("KeyDeeper20", "Tiefe: 20 m tiefer", "Ctrl+Shift+PageDown");
            Key("KeyShallower20", "Tiefe: 20 m h\u00f6her", "Ctrl+Shift+PageUp");
            Key("KeyPeriscopeDepth", "Tiefe: Sehrohrtiefe", "Ctrl+P");
            Key("KeyMaxDepth", "Tiefe: maximale sichere Tiefe", "Ctrl+C");
            Key("KeyHoldDepth", "Tiefe: aktuelle Tiefe halten", "Ctrl+H");
            Key("KeyReticleLight", "Fadenkreuz-Licht an/aus (im Sehrohr/Fernglas)", "F");
            Key("KeyCamZoomReset", "Kamera: Zoom zurücksetzen", "R");
            Key("KeyCamFastZoom", "Kamera: schneller Zoom (halten)", "LeftShift");
            Key("KeyStationCursor", "Station: Mauszeiger frei/fest", "LeftAlt");
            Key("KeyStationDragLook", "Station: halten zum Umschauen (Modus 1)", "Mouse3");
            Key("KeyHotspotFinder2", "Bremsen-Finder an/aus", "Ctrl+Shift+H");
            Key("KeyTanksEmpty", "Not-Taste: alle Tauchzellen leer, Trimmtanks leer (Notauftauchen)", "Ctrl+Alt+T");
            Key("KeyPortMines", "Im Hafen: Minen sofort in den Laden legen", "Ctrl+Alt+M");

            // ---------- Tools ----------
            Num(Tools, "Language", "Sprache / Language: 0 = wie das Spiel / like the game (Deutsch nur bei deutschem Spiel, sonst English), 1 = Deutsch, 2 = English", 0, 0, 2, 1);
            Num(Tools, "MenuOpacity", "Menü: Hintergrund-Deckkraft (1 = ganz dunkel)", 0.92f, 0f, 1f, 0.02f);
            Num(Tools, "MenuWidth", "Menü: Breite in Pixeln (oder rechte untere Ecke ziehen)", 1000f, 500f, 3840f, 10f);
            Num(Tools, "MenuHeight", "Menü: Höhe in Pixeln (0 = ganze Bildschirmhöhe)", 0f, 0f, 2160f, 10f);
            Num(Tools, "MenuX", "Menü: Abstand von links (oder Titel ziehen)", 40f, 0f, 3840f, 10f);
            Num(Tools, "MenuY", "Menü: Abstand von oben (oder Titel ziehen)", 40f, 0f, 2160f, 10f);
            Bool(Tools, "DebugLog", "Test-Log: alles, was der Mod tut + Fehler (logs\\toolkit-Datum.log)", true);
            Bool(Tools, "GameLogFilter", "Spiel-Log klein halten: nur Warnungen + Fehler in Player.log (spart Festplatte + RAM)", true);
            Bool(Tools, "MapPixelSample", "Karte: Pixel-Messung der Raster-Beschriftung ins Log (Kontrast, Lesbarkeit, je Stil und Ebene)", false);
            Bool(Tools, "LookoutLog", "Ausguck: ausführliches Log (Sicht, Rauch, Meldungen)", false);
            Bool(Tools, "CrewSkillLog", "Schützen/Horcher: Log", false);
            Bool(Tools, "DiagLogs", "Fehlersuche: Crew-Logs eingeschaltet halten (Crew-KI + Offiziers-Wachen; aus = die einzelnen Schalter gelten)", true);
            Bool(Tools, "CrewAILog", "Crew-KI: ausführliches Log (große Dateien, Ordner UboatToolkit/CrewKI, ab nächstem Start)", false);
            Num(Tools, "GcSliceMs", "Speicher-Müllabfuhr: max. ms pro Bild (0 = Spielwert, kleiner = weniger Ruckler)", 0, 0, 10, 0.5f);
            Bool(Tools, "AutoCleanCache", "Cache aufräumen beim Spielstart: alte Daten-Cache, Absturzberichte, Mod-Logs (Spielstände nie)", true);
            Num(Tools, "AutoCleanDays", "  Dateien älter als x Tage", 14, 1, 90, 1);
            Bool(Tools, "CacheCheck", "Mod-Cache prüfen: Mods, die das Spiel still ablehnen würde, neu bauen lassen (wirkt beim nächsten Start)", true);
            Bool(Tools, "CacheCheckFirst", "  erst mit der Spiel-Prüfung testen (aus = jeden Start alle Mods neu bauen, langsam)", true);
            Num(Tools, "MemoryCleanMinutes", "Speicher aufräumen alle x Minuten (gegen wachsenden RAM, 0 = aus)", 30, 0, 120, 5);
            Num(Tools, "MaxShipsPerGroup", "Höchstens x Schiffe pro Konvoi gleichzeitig in 3D (0 = Spiel: 28, weniger = schneller)", 0, 0, 60, 1);
            Num(Tools, "MaxGroupsPerBattle", "Höchstens x Gruppen gleichzeitig im Gefecht (0 = Spiel: 4, weniger = schneller)", 0, 0, 10, 1);
            Num(Tools, "FarAIInterval", "Ferne Schiffe: KI-Takt in Sekunden (30 = Spiel-Standard, 60 = weniger CPU)", 30, 5, 300, 5);
            Bool(Tools, "OfficerWatchLog", "Log: jede Stunde Offiziere mit Energie und Dienst", false);
            Bool(Tools, "QuietTooltipLog", "Tooltip-Schlüssel mitschreiben (toolkit-tooltip-keys.txt, zum Sperren)", false);
            Bool(Tools, "QuietLog", "Log: jede leise/stumme Meldung und Stimme", false);
            Bool(Tools, "NightLog", "Log: Nacht-Belichtung und Ansicht", false);
            Bool(Tools, "DgsLog", "Log: Deckgeschütz gesperrt wegen Seegang", false);
            Bool(Tools, "RainGearLog", "Log: Regenkleidung an/aus je Mann", false);
            Bool(Tools, "HatchLog", "Log: Luken-Durchgang Bild für Bild", false);
            Bool(Tools, "CamLog", "Log: Kamera-Zoom", false);
            Bool(Tools, "OrbitLog", "Log: Außenkamera unter Wasser", false);
            Bool(Tools, "StationLog", "Log: Stationen (Zoom, Drehknöpfe)", false);
            Bool(Tools, "SaveCatLog", "Log: Karriere-Reiter in der Spielstand-Liste", false);
            Bool(Tools, "ScopeLog", "Log: Sehrohr-Overlays laden", false);
            Num(Tools, "ScopeLogSlowMs", "  langsam ab ... ms", 20, 0, 1000, 5);
            Bool(Tools, "TextureRaceLog", "Log: spätes Textur-Löschen (Zusammenfassung pro Minute)", false);
            Bool(Tools, "AbortStallLog", "Log: Stocken-Fix (übersprungene Wartezeiten)", false);
            Bool(Tools, "ScopeUiLog", "Log: Sehrohr-Anzeige (Ansicht/Zoom wiederhergestellt)", false);
            Bool(Tools, "TelegraphLog", "Log: Maschinen-Befehle per Taste", false);
            Bool(Tools, "CourseLog", "Log: Kurs- und Tiefen-Tasten", false);
            Bool(Tools, "UniformLog", "Log: Uniform-Fix (pro Minute)", false);
            Bool(Tools, "BifrostUiLog", "Log: Bifrost UI-Pfade einmal ins Log", true);
            Bool(Tools, "PerfLogger", "Performance-Logger (schreibt CSV, kostet wenig Leistung)", false);
            Bool(Tools, "PerfHotspots", "  Bremsen-Finder (Taste im Tab „Tasten“, kostet Leistung)", false);
            Bool(Tools, "PerfHotspotsNow", "    >> Bremsen-Finder jetzt starten/stoppen (misst 1 Minute, dann Liste ins Log)", false);
            Bool(Tools, "PerfQueue", "  Bremsen-Finder Spiel-Warteschlange (findet die echten Bremsen, kostet Leistung)", false);

            // ---------- Licht-Modi (Tab "Licht") ----------
            Bool("Licht", "LightModes", "Licht-Modi an (Stil pro Schalter-Stellung, oben wählen)", false);
            Bool("Licht", "LightWarm", "Warme Glühbirnen-Farbe für „warm“ (aus = Spiel-Weiß)", true);
            Num("Licht", "LightLevelNormal", "Helligkeit „warm“ auf Stellung Normal in % (100 = Spiel)", 79, 0, 100, 1);
            Num("Licht", "LightLevelNight", "Helligkeit „warm“ auf Stellung Rot/Blau (Nachtlicht) in % (0 = Licht aus)", 58, 0, 100, 1);
            Num("Licht", "LightLevelEmerg", "Helligkeit „Notlicht“ in %", 65, 0, 100, 1);
            Bool("Licht", "LightRedTower", "Nachtlicht: Turm bleibt rot (Nachtsicht der Wache)", false);
            Bool("Licht", "LightCustomEmerg", "Eigene Notlicht-Farbe statt Spiel-Rot (Farbe oben)", false);
            Num("Licht", "LightNightPerf", "Nachtlicht: Crew-Leistung minus x Punkte", 10, 0, 50, 5);
            Num("Licht", "LightNightSleep", "Nachtlicht: Schlaf erholt x % schneller", 10, 0, 50, 5);
            Num("Licht", "LightEmergPower", "Notlicht: E-Maschinen brauchen x % weniger Strom", 10, 0, 50, 5);
            Bool("Licht", "LightAutoEmerg", "Licht-Automatik des Spiels: nach schwerem Schaden Notlicht", true);
            Num("Licht", "LightDamage", "  schwerer Schaden ab x fehlender Haltbarkeit (alle Geräte zusammen)", 5, 1, 100, 1);
            Num("Licht", "LightLeaks", "  oder mehr als x Lecks (außer in geräumten Abteilungen)", 3, 1, 20, 1);
            Bool("Licht", "LightAutoSilent", "Auto-Schleichfahrt (auch am Lichtschalter umschaltbar)", false);
            Num("Licht", "LightSwapDepth", "  Schleichfahrt an unter x m, aus darüber", 9, 5, 60, 1);
            Bool("Licht", "LightSilentAlarmOnly", "  nur bei Alarm", true);
            Bool("Licht", "LightLabels", "Schalter-Namen passend umbenennen (z. B. „Nacht“, „Notlicht“)", true);
            Num("LichtStil", "LightStyleNormal", "Stil Stellung Normal (0 = Spiel, 1 = warm, 2 = Notlicht)", 1, 0, 2, 1);
            Num("LichtStil", "LightStyleRed", "Stil Stellung Rot (0 = Spiel, 1 = warm, 2 = Notlicht)", 1, 0, 2, 1);
            Num("LichtStil", "LightStyleBlue", "Stil Stellung Blau (0 = Spiel, 1 = warm, 2 = Notlicht)", 2, 0, 2, 1);
            Num("LichtStil", "LightDayR", "Farbe LightDay R (0-255)", 255, 0, 255, 1);
            Num("LichtStil", "LightDayG", "Farbe LightDay G (0-255)", 232, 0, 255, 1);
            Num("LichtStil", "LightDayB", "Farbe LightDay B (0-255)", 184, 0, 255, 1);
            Num("LichtStil", "LightNightR", "Farbe LightNight R (0-255)", 255, 0, 255, 1);
            Num("LichtStil", "LightNightG", "Farbe LightNight G (0-255)", 214, 0, 255, 1);
            Num("LichtStil", "LightNightB", "Farbe LightNight B (0-255)", 130, 0, 255, 1);
            Num("LichtStil", "LightTowerR", "Farbe LightTower R (0-255)", 255, 0, 255, 1);
            Num("LichtStil", "LightTowerG", "Farbe LightTower G (0-255)", 0, 0, 255, 1);
            Num("LichtStil", "LightTowerB", "Farbe LightTower B (0-255)", 0, 0, 255, 1);
            Num("LichtStil", "LightEmergR", "Farbe LightEmerg R (0-255)", 255, 0, 255, 1);
            Num("LichtStil", "LightEmergG", "Farbe LightEmerg G (0-255)", 0, 0, 255, 1);
            Num("LichtStil", "LightEmergB", "Farbe LightEmerg B (0-255)", 0, 0, 255, 1);
            UboatToolkit.Modules.Warthog.WarthogSettings.Register();
        }

        private static void Bool(string tab, string key, string label, bool def) => Add(new Opt { Tab = tab, Key = key, Label = label, IsBool = true, Default = def ? 1 : 0, Value = def ? 1 : 0, Min = 0, Max = 1, Step = 1 });
        private static void Num(string tab, string key, string label, float def, float min, float max, float step) => Add(new Opt { Tab = tab, Key = key, Label = label, Default = def, Value = def, Min = min, Max = max, Step = step });
        private static void Key(string key, string label, string def) => Add(new Opt { Tab = KeysTab, Key = key, Label = label, IsKey = true, Text = def });
        public static void AddOpt(Opt o) => Add(o);
        private static void Add(Opt o)
        {
            o.Group = GroupOf(o);
            if (!o.IsKey)
            {
                if (o.Label.StartsWith(" ") && lastMain != null && lastMain.Tab == o.Tab) { o.Parent = lastMain; o.Sub = lastMain.Sub; lastMain.Children.Add(o); }
                else { o.Sub = SubOf(o); lastMain = o; }
            }
            All.Add(o); byKey[o.Key] = o;
        }
        private static Opt lastMain;

        // Sub-topics (DoMe 2026-11-16: "gleichgeartetes soll gleich sein"): things of one kind stand together under one
        // headline inside the topic. First matching key prefix wins; no match = "Allgemein".
        private static readonly string[][] subRules =
        {
            new[] { "Gesundheit & Moral", "AutoFirstAid", "NoAutoCarry", "CheatImmortal", "CheatCrewProtection", "NoStress", "CheatFullDiscipline", "CheatCrewNeverTired", "HotBunk", "FoodByCrew", "AutoDamageControl", "LeakTwoSailors" },
            new[] { "Strahlruder", "WhThrust", "WhVThrust" },
            new[] { "88mm", "DeckGun" },
            new[] { "Flak", "AAGun", "AASaveAmmo", "AAvsShips" },
            new[] { "KI-Schützen", "Gunner", "GunGyro", "KeepGunMode", "CheatPerfectAim" },
            new[] { "Torpedos", "AutoReload", "ReloadAllNow", "Torpedo", "EasyTDC", "TubeDisplay", "CheatInfiniteTorpedoes", "CheatNoDuds", "CheatTorpedoSpeed", "CheatRemoteTorpedo", "CheatInstantSolution", "CheatExactTargets", "PortT5Min" },
            new[] { "Funk", "Radio", "CheatRadio" },
            new[] { "Sehrohr & Optik", "Optics", "Periscope", "Reticle", "ScopeRemember", "NightOptics", "CheatPeriscope" },
            new[] { "Horcher & Sonar", "Horcher", "ActiveSonar", "CheatHydrophone", "CheatSonar", "CheatRange" },
            new[] { "Licht & Helligkeit", "AutoLight", "AutoSearchlight", "NightBright", "InteriorBright", "MuteLightCallouts" },
            new[] { "Zeitraffer", "StopTime", "AntiGrounding" },
            new[] { "Karte", "Map", "NavInfo", "SeafloorAlways", "SeaFloorShallow" },
            new[] { "Tauchen & Tiefe", "Ballast", "AutoDepth", "Depth", "DiveAngle", "AutoCrashDive", "AutoSnorkel", "SeaLayer", "DiveSpeed" },
            new[] { "Pumpen & Wasser", "AutoPump", "CheatPump", "CheatNeverFlood" },
            new[] { "Diesel, Strom & Luft", "CheatFull", "EnergySaver", "AirByCrew", "LowWarn", "Rebreather", "Ventilation" },
            new[] { "Luken", "AutoHatches", "HatchPressure", "FastHatch" },
            new[] { "Rumpf & Schaden", "CheatDeepDive", "CheatNoHull", "CheatHullArmour", "CheatRepair", "DamageReport" },
            new[] { "Stationen besetzen", "AutoMan", "KeepHelm", "HelmCalm", "KeepDepthStation", "KeepNavTable", "SailorsNavTable", "AwashGuns", "EchoSailor" },
            new[] { "Offiziere", "Follower", "MaxFollowers", "Officer", "CheatExtraSkill", "CheatUnlimitedSkill", "CheatSkills", "CheatAllSkills", "CheatLinguist", "ExtraOfficers", "CheatSpecPoints" },
            new[] { "Crew-KI", "CrewAI", "CrewAi", "CrewLod", "NoCrewCollisions" },
            new[] { "Lager", "AutoWorkshop", "TightPack", "CheatNoSupply", "CheatNoWeight", "AutoExpandStorage", "CheatStack" },
            new[] { "Geld & Forschung", "CheatMoney", "CheatReputation", "CheatPrice", "CheatResearch", "CheatInstantResearch", "CheatNoResearch" },
            new[] { "Hafen", "Port", "Boarding", "InstantPort", "LeavePort" },
            new[] { "Laden & Speichern", "AutoSave", "FastLoad", "SaveGroup", "QuitWaits", "SkipStartup", "BackupBefore" },
            new[] { "Leistung", "AutoVirtual", "Explosion", "Physics", "VoiceLimit", "WolfpackOnce" },
            new[] { "Kamera", "Cam", "NoEdge", "StationZoom", "NoPauseBlur" },
            new[] { "Fehlerschutz", "Guard" },
            new[] { "Fixes für andere Mods", "FixSupplyPrice", "FixUniform", "FixBifrost", "FixMissingWearables" },
        };
        private static string SubOf(Opt o)
        {
            if (o.Tab != QoL && o.Tab != Cheat) return "";
            foreach (var r in subRules)
                for (int j = 1; j < r.Length; j++)
                    if (o.Key.StartsWith(r[j], StringComparison.Ordinal)) return r[0];
            return "Allgemein";
        }

        // Search text: lower case, umlauts spelled out, so "geschuetz", "Geschütz" and "GESCHÜTZ" all match.
        public static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.ToLowerInvariant().Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss");
        }
        public static string SearchTextOf(Opt o)
        {
            if (o.SearchText == null)
                o.SearchText = Norm(o.Label + " " + Lang.T(o.Label) + " " + o.Key + " " + o.Group + " " + o.Sub + " " + o.Tab
                    + (o.Parent != null ? " " + o.Parent.Label + " " + Lang.T(o.Parent.Label) : "") + (o.Tab == Cheat ? " cheat" : "") + (o.IsKey ? " taste" : ""));
            return o.SearchText;
        }

        // Names for settings with a few fixed values, read from the label: "0 aus, 1 fragen" or "(0 = aus, 1 = an)".
        private static readonly System.Text.RegularExpressions.Regex choiceRx = new System.Text.RegularExpressions.Regex(@"(?<![\d.,])(\d)\s*=?\s*([^\d,;()=][^,;()=]*)");
        public static string[] Choices(Opt o)
        {
            if (o.ChoiceNames != null) return o.ChoiceNames;
            int n = o.Step > 0 ? Mathf.RoundToInt((o.Max - o.Min) / o.Step) + 1 : 0;
            if (o.IsBool || o.IsKey || n < 2 || n > 6 || Mathf.Abs(o.Step - Mathf.Round(o.Step)) > 0.001f) return o.ChoiceNames = new string[0];
            var names = new string[n];
            int named = 0;
            foreach (System.Text.RegularExpressions.Match m in choiceRx.Matches(o.Label))
            {
                int v = m.Groups[1].Value[0] - '0';
                int i = Mathf.RoundToInt((v - o.Min) / o.Step);
                string t = m.Groups[2].Value.Trim().Trim('"', '„', '“');
                if (i < 0 || i >= n || names[i] != null || t.Length == 0) continue;
                if (t.Length > 16) t = t.Substring(0, 15) + "…";
                names[i] = t; named++;
            }
            for (int i = 0; i < n; i++)
            {
                string num = (o.Min + i * o.Step).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                names[i] = named >= 2 && names[i] != null ? num + " " + names[i] : num;
            }
            return o.ChoiceNames = names;
        }

        // Groups for the F10 menu (QoL + Cheat tabs). Sub-options (label starts with spaces) stay in
        // the group of the option above them.
        private static readonly string[][] groupRules =
        {
            new[] { "Fehler-Fixes", "fix", "fehlerschutz" },
            new[] { "Crew & Gesundheit", "crew", "matros", "offizier", "kapitän", "kommandant", "verletz", "erste hilfe", "heil", "müde", "ermüd", "schlaf", "koje", "moral", "stress", "disziplin", "skill", "essen", "koch", "nahrung", "kranke", "sanit", "wache", "besatzung", "rekrut", "uniform", "regenkleid", "männer", "alarm" },
            new[] { "Kampagne & Missionen", "rudel", "milchk", "patrouill", "auftrag", "erfolge", "achievement", "kampagne", "karriere" },
            new[] { "Spielstand & Leistung", "spielst", "speicher", "ruckel", "rechenzeit", "fps", "leistung" },
            new[] { "Hafen, Lager & Geld", "hafen", "häfen", "lager", "verlad", "stapel", "gewicht", "inventar", "storage", "stack", "kauf", "preis", "geld", "ruf", "werkstatt", "schrott", "ersatzteil", "upgrade", "erforsch", "forschung", "freischalt", "versorg" },
            new[] { "Navigation, Karte & Zeit", "karte", "navigation", "kurs", "zeitraffer", "reise", "quick", "wetter", "land", "auf grund", "kollision", "teleport", "wegpunkt" },
            new[] { "Sicht, Sensoren & Funk", "sonar", "horch", "radar", "sehrohr", "fernglas", "optik", "uzo", "sicht", "entdeck", "ausguck", "funk", "sensor", "licht", "heller", "scheinwerfer", "geräusch", "echolot" },
            new[] { "Boot & Technik", "tauch", "ballast", "pump", "tiefe", "luft", "sauerstoff", "atemger", "lüftung", "druck", "leck", "schaden", "reparatur", "batterie", "diesel", "motor", "strom", "energie", "kraftstoff", "schnorchel", "luke", "rumpf", "wasser", "kompressor", "geschwindigkeit", "speed" },
            new[] { "Waffen & Torpedos", "torpedo", "geschütz", "flak", "88mm", "kanone", "munition", "gun", "waffe", "treffer", "feuer", "schuss", "zielen", "streuung", "vorhalt" },
            new[] { "Anzeige, Kamera & Menü", "kamera", "menü", "anzeige", "sound", "musik", "hinweis", "meldung", "log", "fenster", "schrift", "intro", "tutorial", "video", "unschärfe", "blur", "zeit" },
        };
        // Checked first (label start or exact word), before the keyword rules above.
        private static readonly string[][] groupFirst =
        {
            new[] { "Waffen & Torpedos", "88mm", "flak", "ki-schützen", "deckgeschütz", "geschütz", "schussgeräusch", "torpedo", "rohr-anzeige", "zielhilfe" },
            new[] { "Fehler-Fixes", "fehler-fix", "fehlerschutz" },
            new[] { "Anzeige, Kamera & Menü", "schnittansicht", "ich-sicht", "kein sprung", "ansagen", "kriegstagebuch", "stationen (" },
            new[] { "Boot & Technik", "strom sparen", "ballast", "schnorchel", "lüftung" },
        };
        // 0.3.65 (DoMe: options are scattered, same function must be in the same place): the index. Fixed topic per
        // option key (checked first), so the keyword rules above cannot put an option into the wrong topic.
        private const string GCrew = "Crew & Gesundheit", GPort = "Hafen, Lager & Geld", GWeap = "Waffen & Torpedos", GBoat = "Boot & Technik",
            GSens = "Sicht, Sensoren & Funk", GNav = "Navigation, Karte & Zeit", GCamp = "Kampagne & Missionen", GPerf = "Spielstand & Leistung",
            GView = "Anzeige, Kamera & Menü";
        private static readonly Dictionary<string, string> groupFix = new Dictionary<string, string>
        {
            { "AutoReloadTubes", GWeap },
            { "WhThrusters", GBoat }, { "WhVThrusters", GBoat },
            { "AutoDepth", GBoat }, { "AutoCrashDive", GBoat }, { "AirByCrew", GBoat }, { "LowWarnPercent", GBoat },
            { "MerchantTraffic", GCamp }, { "ExplosionLimit", GPerf }, { "SaveGroupCareer", GPerf },
            { "NoShellPush", GWeap }, { "AwashGuns", GWeap }, { "CheatRemoteTorpedo", GWeap }, { "CheatOwnShellsSafe", GWeap }, { "CheatExactTargets", GWeap }, { "CheatInstantSolution", GWeap },
            { "HideDevConsole", GView }, { "DevConsole", GView }, { "CamOrbitUnderwater", GView }, { "Speedometer", GView },
            { "SeafloorAlways", GNav }, { "CheatLinguist", GCrew }, { "CheatNoSupplyUse", GPort },
            // 0.3.75: all "station is manned automatically" switches in one place, smoke with the guns
            { "AutoManGuns", GCrew }, { "AutoManRadio", GCrew }, { "AutoManEngines", GCrew }, { "AutoManHydrophone", GCrew },
            { "GunSmokeOff", GWeap }, { "SeaFloorShallow", GNav }, { "MuteLightCallouts", GSens }, { "FastHatch", GBoat },
        };
        // Keys: topic by key-name prefix (longest match first in this order).
        private static readonly string[][] keyGroups =
        {
            new[] { GBoat, "KeyCourseDeeper", "KeyCourseShallower", "KeyCoursePeriscope", "KeyCourseMaxDepth", "KeyDepth", "KeyAutoDepth", "KeyDeeper", "KeyShallower", "KeyPeriscopeDepth", "KeyMaxDepth", "KeyHoldDepth", "KeyTanksEmpty", "KeyGear", "KeySpeed", "KeyWhThrust", "KeyWhVThrust" },
            new[] { GNav, "KeyCourse", "KeyTurn", "KeyAmidships", "KeyQuickTravel", "KeyBackToWater" },
            new[] { GWeap, "KeyUnderFire", "KeyReloadAll", "KeyRemoteTorpedo", "KeySinkConvoy" },
            new[] { GPort, "KeySortStorage", "KeyPortMines" },
            new[] { GCrew, "KeySwapShifts", "KeyHealAll" },
            new[] { GCamp, "KeyCallMilkCow", "KeySpawnConvoy" },
            new[] { GSens, "KeySonarPing", "KeyReticleLight" },
            new[] { GPerf, "KeyHotspotFinder" },
            new[] { GView, "KeyMenu", "KeyCam", "KeyStation" },
        };
        private static Opt lastAdded;
        private static string GroupOf(Opt o)
        {
            string g = "";
            if (o.IsKey)
            {
                foreach (var r in keyGroups)
                    for (int j = 1; j < r.Length && g == ""; j++)
                        if (o.Key.StartsWith(r[j])) g = r[0];
                return g;   // keys do not change lastAdded
            }
            if (o.Tab == QoL || o.Tab == Cheat)
            {
                if (groupFix.TryGetValue(o.Key, out var fixedGroup)) g = fixedGroup;
                else if (o.Label.StartsWith(" ") && lastAdded != null && lastAdded.Tab == o.Tab) g = lastAdded.Group;
                else
                {
                    string text = (o.Key + " " + o.Label).ToLowerInvariant();
                    string start = o.Label.ToLowerInvariant();
                    foreach (var r in groupFirst)
                        for (int j = 1; j < r.Length && g == ""; j++)
                            if (start.StartsWith(r[j])) g = r[0];
                    if (g != "") { }
                    else if (o.Key.StartsWith("Fix") || o.Label.StartsWith("Fix")) g = groupRules[0][0];
                    else
                    {
                        g = "Sonstiges";
                        for (int i = 1; i < groupRules.Length && g == "Sonstiges"; i++)
                            for (int j = 1; j < groupRules[i].Length; j++)
                                if (text.Contains(groupRules[i][j])) { g = groupRules[i][0]; break; }
                    }
                }
            }
            lastAdded = o;
            return g;
        }

        public static readonly string[] GroupOrder =
        {
            "Crew & Gesundheit", "Hafen, Lager & Geld", "Waffen & Torpedos", "Boot & Technik",
            "Sicht, Sensoren & Funk", "Navigation, Karte & Zeit", "Kampagne & Missionen", "Spielstand & Leistung",
            "Anzeige, Kamera & Menü", "Fehler-Fixes", "Sonstiges",
        };

        public static bool Changed(Opt o) => !o.IsKey && Math.Abs(o.Value - o.Default) > 0.0001f;

        public static bool B(string key) => byKey.TryGetValue(key, out var o) && o.On;
        public static float F(string key) => byKey.TryGetValue(key, out var o) ? o.Value : 0f;
        public static Opt Get(string key) => byKey.TryGetValue(key, out var o) ? o : null;
        public static void Set(string key, float v) { if (byKey.TryGetValue(key, out var o)) o.Value = Math.Max(o.Min, Math.Min(o.Max, v)); }

        // old joint switch, new separate switch (the old key keeps the first effect)
        private static readonly string[] Split = { "AutoVentilation", "AutoAbsorber", "SeafloorAlways", "SeaFloorShallow",
            "SnorkelAir", "SnorkelVent", "NoPauseBlur", "NoPauseDark", "AutoManGuns", "AutoManFlak", "AutoDepthBottom", "AutoDepthBottomSilent",
            "HatchPressure", "HatchPressureLock", "CheatInfiniteGunAmmo", "CheatInfiniteFlakAmmo", "CheatImmortalCrew", "CheatNoWounds",
            "CheatImmortalCrew", "CheatNoRagdoll", "CheatDeckCrewStays", "CheatGunsUnderWater", "CheatDeckCrewStays", "CheatNoDrown",
            "CheatEscortEasy", "CheatEscortFullSpeed", "CheatEscortEasy", "CheatEscortNoDamage", "CheatHydrophoneAlways", "CheatHydrophoneFast",
            "CheatNoWeight", "CheatNoMassLimit", "WhSilentVent", "WhVentAlwaysOn" };
        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) { Save(); return; }
                float legacySensor = 0f; bool newSensorKeys = false; var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var raw in File.ReadAllLines(FilePath))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line[0] == '[') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    var keyName = line.Substring(0, eq).Trim();
                    // 0.4.4: the old single factor "CheatSensorRange" is carried over once into the new single factors.
                    if (keyName == "CheatSensorRange") { float.TryParse(line.Substring(eq + 1).Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out legacySensor); continue; }
                    if (keyName.StartsWith("CheatRange")) newSensorKeys = true;
                    seen.Add(keyName);
                    var o = Get(keyName);
                    if (o == null) continue;
                    if (o.IsKey) { o.Text = line.Substring(eq + 1).Trim(); continue; }
                    var v = line.Substring(eq + 1).Trim().ToLowerInvariant();
                    if (o.IsAction) { o.Value = 0; continue; }
                    if (o.IsBool) o.Value = (v == "1" || v == "true" || v == "on" || v == "ja") ? 1 : 0;
                    else if (float.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) o.Value = o.Min < 0f ? Mathf.Max(o.Min, f) : Mathf.Max(0f, f); // no upper cap: INI may go beyond the menu range
                }
                // 0.3.69 (DoMe 2026-11-16: "freigabe für jede log die du brauchst"): while DiagLogs is on, the crew logs
                // needed for the open crew problems are switched on at every start.
                if (legacySensor > 0f && !newSensorKeys)
                    foreach (var k in new[] { "CheatRangeHydro", "CheatRangeSonar", "CheatRangeRadar", "CheatRangeRadarWarn", "CheatRangeLookout" }) Set(k, legacySensor);
                // 0.4.28 (DoMe: separate "switch ventilation on" and "insert absorber"): the old joint switch is carried over once.
                // "Moral voll" also removed stress; that part is the existing switch "Kein Stress" now (carried over once).
                if (!seen.Contains("NoPauseDark") && B("CheatFullDiscipline") && !B("NoStress")) { Set("NoStress", 1); Util.Log("Schalter geteilt: „Kein Stress“ an, weil „Moral immer voll“ das bisher mit erledigt hat"); }
                // 0.4.31: the enemy-gun effects were all under "Deckgeschütz und Seegang". They are separate switches now and
                // no longer need that main switch; if it was off, they start off, so nothing changes by itself.
                if (!seen.Contains("DgsNpcSpreadOn") && seen.Contains("DeckGunSeaState") && !B("DeckGunSeaState"))
                { foreach (var k in new[] { "DgsAllShips", "DgsNpcSpreadOn", "DgsNpcRangingOn", "DgsNpcEvenOut", "DgsKnockout" }) Set(k, 0); Util.Log("Schalter geteilt: Gegner-Kanonen-Schalter starten aus, weil „Geschütze und Seegang“ aus war"); }
                // Rule (DoMe 2026-11-18): one switch = one effect. Split[] lists old joint switch -> new separate switch.
                for (int i = 0; i + 1 < Split.Length; i += 2)
                    if (seen.Contains(Split[i]) && !seen.Contains(Split[i + 1]) && Get(Split[i + 1]) != null)
                    { Set(Split[i + 1], F(Split[i])); Util.Log("Schalter geteilt: „" + Get(Split[i + 1]).Label.Trim() + "“ übernimmt den Wert des alten Kombi-Schalters (" + (B(Split[i]) ? "an" : "aus") + ")"); }
                if (B("DiagLogs")) { Set("CrewAILog", 1); Set("OfficerWatchLog", 1); }
                // Write back so new settings from a mod update appear in the file.
                Save();
            }
            catch (Exception e)
            {
                Debug.Log(ToolkitLoader.Tag + " could not read INI, using defaults.");
                Debug.LogException(e);
            }
        }

        public static void Save()
        {
            DebugLog.DumpSettings("gespeichert");
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("; UBOAT Toolkit. 1 = an, 0 = aus. Zahlen mit Punkt (z.B. 2.5).");
                sb.AppendLine("; Menü im Spiel: F10. Diese Datei ist optional.");
                string tab = null;
                foreach (var o in All)
                {
                    if (o.Tab != tab) { tab = o.Tab; sb.AppendLine(); sb.AppendLine("[" + tab + "]"); }
                    sb.AppendLine("; " + o.Label.Trim());
                    sb.AppendLine(o.Key + "=" + (o.IsKey ? o.Text : o.IsAction ? "0" : o.IsBool ? (o.On ? "1" : "0") : o.Value.ToString("0.###", CultureInfo.InvariantCulture)));
                }
                File.WriteAllText(FilePath, sb.ToString());
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}

// ====================================================================================================
// Abschnitt: Core/ToolkitLoader.cs
// ====================================================================================================
namespace UboatToolkit
{
    using global::System;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game;
    using global::UBOAT.Game.Core.Serialization;

    // Entry point: the game calls OnLoaded() once when the mod is loaded.
    [NonSerializedInGameState]
    public class ToolkitLoader : IUserMod
    {
        public const string Tag = "[UboatToolkit]";
        public static Harmony Harmony;

        public void OnLoaded()
        {
            try
            {
                Settings.Load();
                DebugLog.Write("START", "UBOAT Toolkit wird geladen, Unity " + Application.unityVersion + ", Spiel " + Application.version);
                DebugLog.DumpSettings("Start");
                try { UboatToolkit.Modules.CacheCheck.Install(); } catch (Exception ce) { Debug.Log(Tag + " cache check: " + ce.Message); }
                Harmony = new Harmony("djdomi.uboat.toolkit");
                // Patch class by class: if one game method changed, only that feature fails, the rest still works.
                int ok = 0, bad = 0;
                var slow = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, double>>();
                foreach (var t in PatchOrder.Sort(AccessTools.GetTypesFromAssembly(typeof(ToolkitLoader).Assembly)))
                {
                    if (t.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0) continue;
                    if (t.Namespace == "UboatToolkit.Modules.CrewAi") continue; // the crew AI patches itself below (sorted, own log)
                    if (t.Namespace == "UboatToolkit.Modules.Lrl") continue;    // the lookout patches its own list below
                    long pt = UboatToolkit.Modules.LoadClock.Now();
                    try { Harmony.CreateClassProcessor(t).Patch(); ok++; }
                    catch (Exception pe)
                    {
                        bad++;
                        var inner = pe; while (inner.InnerException != null) inner = inner.InnerException;
                        if (inner != pe) pe = new Exception(pe.Message + " -> " + inner.GetType().Name + ": " + inner.Message);
                        Debug.Log(Tag + " patch failed: " + t.Name + ": " + pe.Message);
                        DebugLog.Write("ERROR", "Patch " + t.Name + " fehlgeschlagen: " + pe.Message);
                    }
                    slow.Add(new System.Collections.Generic.KeyValuePair<string, double>(t.Name, UboatToolkit.Modules.LoadClock.Seconds(UboatToolkit.Modules.LoadClock.Now() - pt)));
                }
                DebugLog.Write("START", "Patches ok: " + ok + ", fehlgeschlagen: " + bad);
                // 0.3.71: measured 16 s for this loop at game start. Log the slowest patch classes to cut them down.
                slow.Sort((a, b) => b.Value.CompareTo(a.Value));
                double sum = 0; foreach (var kv in slow) sum += kv.Value;
                var top = new System.Text.StringBuilder();
                for (int i = 0; i < slow.Count && i < 15; i++) top.Append(slow[i].Key).Append(' ').Append((slow[i].Value * 1000).ToString("0")).Append(" ms, ");
                DebugLog.Write("START", "Patch-Zeit gesamt " + sum.ToString("0.0") + " s; langsamste: " + top);
                // Lookout: always installed, every switch is read live (as in the original).
                try { UboatToolkit.Modules.Lrl.LrlInstall.Install(Harmony); } catch (Exception le) { Debug.Log(Tag + " lookout: " + le.Message); }
                // Crew AI: switch takes effect at the next game start (as in the original).
                if (Settings.B("CrewAIOverhaul")) { try { UboatToolkit.Modules.CrewAi.CrewAIInstall.Install(Harmony); } catch (Exception ce) { Debug.Log(Tag + " crew AI: " + ce.Message); } }
                try { UboatToolkit.Modules.VoiceRepair.Run(); } catch (Exception ve) { Debug.Log(Tag + " voice: " + ve.Message); }
                var go = new GameObject(Tag);
                go.AddComponent<ToolkitRunner>();
                UnityEngine.Object.DontDestroyOnLoad(go);
                Debug.Log(Tag + " loaded.");
            }
            catch (Exception e)
            {
                Debug.Log(Tag + " failed to load.");
                Debug.LogException(e);
            }
        }
    }
}

// ====================================================================================================
// Abschnitt: Core/ToolkitRunner.cs
// ====================================================================================================
namespace UboatToolkit
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Globalization;
    using global::System.Linq;
    using global::UnityEngine;

    // Runs all modules about once per second and draws the F10 menu.
    public class ToolkitRunner : MonoBehaviour
    {
        public static readonly List<IModule> Modules = new List<IModule>();

        private float nextTick;
        private bool menuOpen, menuShown;
        private string tab = Settings.GroupOrder[0];
        private Vector2 scroll;

        private void Awake()
        {
            Modules.Add(new Modules.Radio());
            Modules.Add(new Modules.AutoDetail());
            Modules.Add(new Modules.WolfpackStep());
            Modules.Add(new Modules.CrewAiThrottle());
            Modules.Add(new Modules.GunSmoke());
            Modules.Add(new Modules.DiveAngle());
            Modules.Add(new Modules.PortDefense());
            Modules.Add(new Modules.EnemyCheck());
            Modules.Add(new Modules.TankReset());
            Modules.Add(new Modules.PortMines());
            Modules.Add(new Modules.Lighting());
            Modules.Add(new Modules.Hatches());
            Modules.Add(new Modules.Optics());
            Modules.Add(new Modules.Crew());
            Modules.Add(new Modules.Weapons());
            Modules.Add(new Modules.MilkCows());
            Modules.Add(new Modules.Ballast());
            Modules.Add(new Modules.SnorkelGuard());
            Modules.Add(new Modules.CrewGear());
            Modules.Add(new Modules.Medical());
            Modules.Add(new Modules.Repairs());
            Modules.Add(new Modules.Navigation());
            Modules.Add(new Modules.Stations());
            Modules.Add(new Modules.CrewSkills());
            Modules.Add(new Modules.DamageReport());
            Modules.Add(new Modules.BoardingQueue());
            Modules.Add(new Modules.CrewSupplies());
            Modules.Add(new Modules.OfficerRest());
            Modules.Add(new Modules.ActiveSonar());
            Modules.Add(new Modules.Air());
            Modules.Add(new Modules.Pump());
            Modules.Add(new Modules.CrewFill());
            Modules.Add(new Modules.FadeReparent());
            Modules.Add(new Modules.NavRescue());
            Modules.Add(new Modules.DepthGauge());
            Modules.Add(new Modules.UniqueNames());
            Modules.Add(new Modules.CrewLod());
            Modules.Add(new Modules.MoreShips());
            Modules.Add(new Modules.CameraParentFixModule());
            Modules.Add(new Modules.TooltipDiag());
            Modules.Add(new Modules.DevConsoleHide());
            Modules.Add(new Modules.SceneTiming());
            Modules.Add(new Modules.QuitGuard());
            foreach (var m in UboatToolkit.Modules.Warthog.WarthogModules.All()) Modules.Add(m);
            Modules.Add(new Modules.CrewCheats());
            Modules.Add(new Modules.Inventory());
            Modules.Add(new Modules.Loading());
            Modules.Add(new Modules.Torpedoes());
            Modules.Add(new Modules.Cheats());
            Modules.Add(new Modules.Watch());
            Modules.Add(new Modules.Extras());
            Modules.Add(new Modules.HatchEqualizer());
            Modules.Add(new Modules.Traffic());
            Modules.Add(new Modules.Events());
            Modules.Add(new Modules.ExtraFeatures());
            Modules.Add(new Modules.Strike());
            Modules.Add(new Modules.AchievementTools());
            Modules.Add(new Modules.MissionCheat());
            Modules.Add(new Modules.DetectCheat());
            Modules.Add(new Modules.SensorCheats());
            Modules.Add(new Modules.HealAll());
            Modules.Add(new Modules.StorageExpander());
            Modules.Add(new Modules.MemoryCleaner());
            Modules.Add(new Modules.CacheCleaner());
            Modules.Add(new Modules.GameFixes());
            Modules.Add(new Modules.Controls());
            Modules.Add(new Modules.Lrl.LrlHost());
            Modules.Add(new Modules.QuietUIHost());
            Modules.Add(new Modules.Helm());
            Modules.Add(new Modules.SeaGun());
            Modules.Add(new Modules.StationCtl.StationHost());
            Modules.Add(new Modules.ScopePreloadHost());
            Modules.Add(new Modules.CarrierConvoyHost());
            Modules.Add(new Modules.BifrostProbe());
            Modules.Add(new Modules.Reticle());
            Modules.Add(new Modules.GameTuning());
            Modules.Add(new Modules.PerfLogger());
            Modules.Add(new Modules.QueueProfiler());
        }

        private void Update()
        {
            // 0.3.47: a key capture can only run while the menu is open. A capture left over from a closed menu blocked
            // EVERY hotkey (Keys.Down returns false while capturing) and the menu key (DoMe: Ctrl+Alt+M no effect, F10 several times).
            if (!menuOpen && Keys.Capturing != null) { Util.Log("Tasten-Aufnahme hing bei geschlossenem Menü, beendet"); Keys.Capturing = null; }
            Keys.CaptureFrame();
            if (saveAt > 0f && (Time.unscaledTime >= saveAt || !menuOpen) && !Input.GetMouseButton(0) && Keys.Capturing == null)
            { saveAt = 0f; Settings.Save(); Util.Log("Einstellungen von selbst gespeichert (Änderung im Menü)"); }
            // Menu key: works even with Ctrl/Shift/Alt still held and while a key capture hangs (F10 must always close).
            var mk = Keys.Code("KeyMenu", KeyCode.F10);
            if (mk != KeyCode.None && Input.GetKeyDown(mk) && Keys.ModsHeld("KeyMenu")) MenuKey("Update");
            if (mk == KeyCode.None || !Input.GetKey(mk)) guiKeyHeld = false;
            if (names == null) { names = new string[Modules.Count]; frameNames = new string[Modules.Count]; for (int i = 0; i < Modules.Count; i++) { names[i] = Modules[i].GetType().Name; frameNames[i] = names[i] + ".Frame"; } }
            for (int i = 0; i < Modules.Count; i++) { if (whRest && IsWh(i)) continue; Timed(frameNames[i], Modules[i].Frame, 20.0); }

            if (Time.unscaledTime >= nextState) { nextState = Time.unscaledTime + 60f; DebugLog.DumpState(); }
            if (Util.PlayerShip == null) return;
            // Each module runs once per second, but spread over the frames (one module per frame),
            // so there is no single heavy frame = no stutter.
            if (Time.unscaledTime < nextTick) return;
            // 0.4.29 (contradiction audit): with the separate Warthog mod installed and its boat (Type XXX) in use, the
            // Toolkit's own Warthog parts rest completely. Before, their "off" paths still wrote values on that boat.
            if (tickIndex == 0)
            {
                bool rest = Util.WarthogModOwns(Util.PlayerShip);
                if (rest != whRest)
                {
                    whRest = rest; int n = 0; for (int i = 0; i < Modules.Count; i++) if (IsWh(i)) n++;
                    Util.Log(rest ? "Warthog-Mod ist installiert und sein Boot ist in Benutzung: " + n + " Warthog-Teile des Toolkits ruhen ganz (Wirkung kommt allein vom Warthog-Mod)" : "Warthog-Teile des Toolkits arbeiten wieder (anderes Boot)");
                }
            }
            if (!(whRest && IsWh(tickIndex))) Timed(names[tickIndex] + ".Tick", Modules[tickIndex].Tick, 40.0);
            tickIndex++;
            if (tickIndex >= Modules.Count) { tickIndex = 0; nextTick = Time.unscaledTime + 1f; }
        }

        private string[] names, frameNames;
        private bool whRest; private bool[] whFlags;
        private bool IsWh(int i)
        {
            if (whFlags == null || whFlags.Length != Modules.Count)
            {
                whFlags = new bool[Modules.Count];
                for (int k = 0; k < Modules.Count; k++) { var ns = Modules[k].GetType().Namespace ?? ""; whFlags[k] = ns.EndsWith(".Warthog", System.StringComparison.Ordinal); }
            }
            return whFlags[i];
        }
        // Stutter finder: any module step slower than the limit is logged with its name and the number of
        // garbage collections during it (max once per 30 s per step), so slow modules show up in the log.
        // Timing via LoadClock (reflection): naming the diagnostics namespace makes the game's mod check reject the mod.
        private static readonly System.Collections.Generic.Dictionary<string, float> slowLogged = new System.Collections.Generic.Dictionary<string, float>();
        private static void Timed(string name, System.Action a, double limitMs)
        {
            int gc = System.GC.CollectionCount(0);
            long swStart = UboatToolkit.Modules.LoadClock.Now();
            Util.Safe(name, a);
            double ms = UboatToolkit.Modules.LoadClock.Seconds(UboatToolkit.Modules.LoadClock.Now() - swStart) * 1000.0;
            ModuleSeconds += ms / 1000.0;
            if (ms < limitMs) return;
            float now = Time.unscaledTime;
            if (slowLogged.TryGetValue(name, out var t) && now - t < 30f) return;
            slowLogged[name] = now;
            Util.Log("Langsam: " + name + " " + ms.ToString("0") + " ms (GC +" + (System.GC.CollectionCount(0) - gc) + ")");
        }

        private int tickIndex;

        private float nextState;

        // 0.3.67 (DoMe: F10 must be pressed 4-5 times): the log showed that every press that reached Update toggled
        // correctly, so presses were lost before Update saw them (the game clears the key state in some screens).
        // Second, independent path: the key event of the window system in OnGUI. Both paths end here; a press is
        // counted once (0.25 s lock), key repeat while holding is ignored.
        private float lastMenuKey = -10f;
        private bool guiKeyHeld;
        private void MenuKey(string src)
        {
            if (Time.unscaledTime - lastMenuKey < 0.25f) return;
            lastMenuKey = Time.unscaledTime;
            bool blocked = !(Keys.Capturing == null || menuOpen);
            Util.Log("Menütaste (" + src + "): " + (blocked ? "ignoriert (Tasten-Aufnahme läuft)" : menuOpen ? "zu" : "auf") + ", Bild " + Time.frameCount);
            if (!blocked) { Keys.Capturing = null; menuOpen = !menuOpen; }
        }

        private void OnGUI()
        {
            // 0.4.7: Unity calls OnGUI several times per frame (layout, repaint, every mouse and key event). While the
            // menu is closed the displays are only text and bars, which exist in the repaint pass alone; all other
            // passes now end here. Measured before: 4.2 ms per frame on average for the displays.
            int kind = Imgui.EventKind();
            var mk = (kind == 4 || kind == 5 || kind < 0) ? Keys.Code("KeyMenu", KeyCode.F10) : KeyCode.None;
            if (mk != KeyCode.None)
            {
                int k = Imgui.KeyEvent(mk);   // 1 = key down event for this key, 2 = key up, 0 = other
                if (k == 1 && !guiKeyHeld && Keys.ModsHeld("KeyMenu")) { guiKeyHeld = true; MenuKey("Fenster"); }
                else if (k == 2) guiKeyHeld = false;
            }
            UboatToolkit.Modules.DevConsoleHide.Close();
            Imgui.Init();
            if (!Imgui.Ready) return;
            if (kind == 8) menuShown = menuOpen;
            if (kind >= 0 && kind != 7 && !menuShown && !menuOpen && !UboatToolkit.Modules.UnderFire.NeedsEvents) { GuiSkipped++; return; }
            double g0 = Time.realtimeSinceStartupAsDouble;
            Util.Safe("menu", DrawGui);
            double gd = Time.realtimeSinceStartupAsDouble - g0;
            GuiSeconds += gd; GuiCalls++;
            if (menuShown) { MenuSeconds += gd; MenuCalls++; }
        }

        // 0.3.72: the Toolkit's own cost per frame (drawing + modules), logged once per minute by the frame logger.
        internal static double GuiSeconds, ModuleSeconds, MenuSeconds;
        internal static int GuiCalls, MenuCalls, GuiSkipped;

        // Short on-screen message from a module (e.g. "Land voraus").
        public static string Notice;
        public static string InfoLine;
        public static float NoticeUntil;
        public static void ShowNotice(string text, float seconds = 6f) { Notice = Lang.T(text); NoticeUntil = Time.unscaledTime + seconds; Util.Log(text); }

        // Menu position and size (saved in the INI), moved by dragging the title, resized by the bottom-right corner.
        private static int dragMode; private static Vector2 dragStart; private static Rect dragRect;
        internal static Rect MenuRect()
        {
            float w = Mathf.Clamp(Settings.F("MenuWidth"), 500, Screen.width - 20);
            float h = Settings.F("MenuHeight") <= 0 ? Screen.height - 80 : Mathf.Clamp(Settings.F("MenuHeight"), 300, Screen.height - 20);
            float x = Mathf.Clamp(Settings.F("MenuX"), 0, Mathf.Max(0, Screen.width - w));
            float y = Mathf.Clamp(Settings.F("MenuY"), 0, Mathf.Max(0, Screen.height - h));
            return new Rect(x, y, w, h);
        }
        private void DragMenu()
        {
            var r = MenuRect();
            var m = Input.mousePosition; var p = new Vector2(m.x, Screen.height - m.y);
            if (Input.GetMouseButtonDown(0))
            {
                if (new Rect(r.xMax - 24, r.yMax - 24, 24, 24).Contains(p)) dragMode = 2;
                else if (new Rect(r.x, r.y, r.width, 24).Contains(p)) dragMode = 1;
                else dragMode = 0;
                dragStart = p; dragRect = r;
            }
            if (!Input.GetMouseButton(0)) { if (dragMode != 0) Settings.Save(); dragMode = 0; return; }
            var d = p - dragStart;
            if (dragMode == 1) { Settings.Set("MenuX", dragRect.x + d.x); Settings.Set("MenuY", dragRect.y + d.y); }
            if (dragMode == 2) { Settings.Set("MenuWidth", dragRect.width + d.x); Settings.Set("MenuHeight", dragRect.height + d.y); }
        }

        private string search = "";
        private bool onlyChanged;
        private bool searchThisTab;
        private readonly Dictionary<string, string> placeHome = new Dictionary<string, string>(), placeGroup = new Dictionary<string, string>();
        private readonly HashSet<string> openGroups = new HashSet<string>();

        private readonly HashSet<string> openOpts = new HashSet<string>();
        private string pendingTab;

        // 0.3.80 (DoMe: "Buttons immer unterschiedlich platziert"): every row uses the same grid.
        //   [fold arrow 44] [indent] [name ...........] | control column, same x and width on every page |
        // on/off = tick box in front of the name; fixed values = buttons; range = [-] slider [+]; key = [Ändern] key name.
        private const float Gutter = 48f;
        private static readonly Dictionary<string, string> keyNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "LeftShift", "Shift links" }, { "RightShift", "Shift rechts" }, { "LeftControl", "Strg links" }, { "RightControl", "Strg rechts" },
            { "LeftAlt", "Alt links" }, { "RightAlt", "Alt rechts" }, { "Ctrl", "Strg" }, { "Control", "Strg" },
            { "Mouse0", "Maus links" }, { "Mouse1", "Maus rechts" }, { "Mouse2", "Mausrad-Klick" }, { "Mouse3", "Maustaste 4" }, { "Mouse4", "Maustaste 5" },
            { "Return", "Enter" }, { "Home", "Pos1" }, { "End", "Ende" }, { "PageUp", "Bild auf" }, { "PageDown", "Bild ab" },
            { "UpArrow", "Pfeil hoch" }, { "DownArrow", "Pfeil runter" }, { "LeftArrow", "Pfeil links" }, { "RightArrow", "Pfeil rechts" },
            { "Space", "Leertaste" }, { "Backspace", "Rück" }, { "Delete", "Entf" }, { "Insert", "Einfg" }, { "Escape", "Esc" },
        };
        private static string KeyName(string t)
        {
            if (string.IsNullOrEmpty(t)) return "(keine)";
            var parts = t.Split('+');
            for (int i = 0; i < parts.Length; i++)
            {
                string k = parts[i].Trim();
                if (keyNames.TryGetValue(k, out var n)) k = n;
                else if (k.StartsWith("Alpha", StringComparison.OrdinalIgnoreCase) && k.Length == 6) k = k.Substring(5);
                else if (k.StartsWith("Keypad", StringComparison.OrdinalIgnoreCase) && k.Length > 6) k = "Num " + k.Substring(6);
                parts[i] = k;
            }
            return string.Join(" + ", parts);
        }

        // 0.4.5: every change made by a click in the menu is written to the log (proof that clicks arrive).
        private static int clickLines;
        private void DrawOpt(Opt o) { DrawOpt(o, false, null); }
        private void DrawOpt(Opt o, bool fold, string suffix)
        {
            float before = o.Value; string beforeText = o.Text;
            DrawOptRow(o, fold, suffix);
            // 0.4.11: every change in the menu is saved by itself 2 s later (DoMe's change was lost: only the button saved)
            if (o.Value != before || o.Text != beforeText) saveAt = Time.unscaledTime + 2f;
            if (o.Value != before && !o.IsKey && clickLines < 400 && dragSliderKey != o.Key)
            { clickLines++; Util.Log("Menü-Klick: " + o.Key + " " + before.ToString("0.##", CultureInfo.InvariantCulture) + " -> " + o.Value.ToString("0.##", CultureInfo.InvariantCulture)); }
            dragSliderKey = o.Value != before ? o.Key : dragSliderKey;
        }
        private static string dragSliderKey;
        private static float saveAt;
        // 0.4.28 (audit: a change made less than 2 s before quitting was lost)
        private void OnApplicationQuit()
        {
            if (saveAt <= 0f) return;
            saveAt = 0f;
            try { Settings.Save(); Util.Log("Einstellungen beim Beenden gespeichert (offene Änderung im Menü)"); } catch (System.Exception e) { Util.Log("Einstellungen beim Beenden: " + e.Message); }
        }
        // fold: this row gets the fold arrow of its fine settings; suffix: text behind the name
        private void DrawOptRow(Opt o, bool fold, string suffix)
        {
            float W = Mathf.Max(440f, MenuRect().width - 60);
            float C = Mathf.Min(440f, W * 0.5f);
            string text = o.Label.TrimStart();
            int depth = (o.Label.Length - text.Length) / 2;
            if (o.Parent != null && depth < 1) depth = 1;
            float indent = depth * 22f;
            float L = Mathf.Max(120f, W - C - Gutter - indent);
            if (suffix != null) text += suffix;
            // fine settings of a switched-off main switch are drawn pale
            bool dim = o.Parent != null && o.Parent.IsBool && !o.Parent.On;
            var oldColor = dim ? Imgui.Tint(0.45f) : Color.white;
            Imgui.BeginH();
            try
            {
                if (fold)
                {
                    bool open = openOpts.Contains(o.Key);
                    if (Imgui.Button((open ? "▾ " : "▸ ") + o.Children.Count, Gutter - 6)) { if (open) openOpts.Remove(o.Key); else openOpts.Add(o.Key); }
                    Imgui.Space(2 + indent);
                }
                else Imgui.Space(Gutter + indent);
                if (o.IsKey)
                {
                    bool cap = Keys.Capturing == o;
                    Imgui.Label(text, L);
                    if (Imgui.Button(cap ? "Taste drücken ..." : "Ändern", 130)) { Keys.Capturing = o; Imgui.ClearFocus(); }
                    Imgui.Label(cap ? "  Esc = Abbruch, Rück = keine" : "  " + KeyName(o.Text), C - 134);
                    return;
                }
                // 0.3.90 (DoMe: tick boxes tiny, on/off hard to tell): on/off is a button in the control column like every
                // other control; ">>" one-shot actions get a "Ausführen" button.
                if (o.IsBool)
                {
                    Imgui.Label(text, L);
                    bool action = text.StartsWith(">>");
                    if (Imgui.Button(action ? "Ausführen" : o.On ? "●  AN" : "aus", 110)) o.Value = action ? 1 : (o.On ? 0 : 1);
                    return;
                }
                var names = Settings.Choices(o);
                float cur = Mathf.Clamp(o.Value, o.Min, o.Max);
                if (names.Length > 0)
                {
                    Imgui.Label(text, L);
                    float bw = C / names.Length - 4f;
                    for (int i = 0; i < names.Length; i++)
                    {
                        float v = o.Min + i * o.Step;
                        bool sel = Mathf.Abs(cur - v) < o.Step * 0.5f;
                        if (Imgui.Button((sel ? "● " : "") + names[i], bw) && !sel) o.Value = v;
                    }
                }
                else
                {
                    Imgui.Label(text + ":  " + o.Value.ToString("0.##", CultureInfo.InvariantCulture) + (o.Extra != null ? "   " + o.Extra() : ""), L);
                    float step = o.Step > 0 ? o.Step : (o.Max - o.Min) / 100f;
                    float v = cur;
                    if (Imgui.Button("−", 28)) v = Mathf.Max(o.Min, cur - step);
                    float sl = Imgui.Slider(v, o.Min, o.Max, C - 72f);
                    if (Mathf.Abs(sl - v) > 0.0001f) v = sl;
                    if (Imgui.Button("+", 28)) v = Mathf.Min(o.Max, v + step);
                    if (o.Step > 0) v = o.Min + Mathf.Round((v - o.Min) / o.Step) * o.Step;
                    if (Mathf.Abs(v - cur) > 0.0001f) o.Value = v;
                }
            }
            finally { Imgui.EndH(); if (dim) Imgui.Tint(oldColor); }
        }

        // A main switch with its fine settings folded in (small arrow with their number in front of the row).
        // With "nur geänderte" the changed ones show directly.
        private void DrawBlock(Opt o)
        {
            if (onlyChanged)
            {
                if (Settings.Changed(o)) DrawOpt(o);
                foreach (var c in o.Children) if (Settings.Changed(c)) DrawOpt(c);
                return;
            }
            if (o.Children.Count == 0) { DrawOpt(o); return; }
            // the fold state is read before the row is drawn, so a click on the arrow shows at the next GUI event
            bool open = openOpts.Contains(o.Key);
            int ch = 0; foreach (var c in o.Children) if (Settings.Changed(c)) ch++;
            DrawOpt(o, true, ch > 0 ? "   (" + ch + " Feinwerte geändert)" : null);
            if (open) { foreach (var c in o.Children) DrawOpt(c); Imgui.Space(4); }
        }

        // 0.3.77 (DoMe: "zu viel auf einem Haufen, Mannschaftssachen verstreut"): one tab per subject. Everything about
        // that subject is in this one tab, on pages: Komfort / Cheats / Tasten / the subject's special page (crew list,
        // torpedo loading, map colours, light, damage report). Inside a page the sub-topics fold.
        private static readonly Dictionary<string, string> shortName = new Dictionary<string, string>
        {
            { "Crew & Gesundheit", "Crew" }, { "Hafen, Lager & Geld", "Hafen & Lager" }, { "Waffen & Torpedos", "Waffen" }, { "Boot & Technik", "Boot & Antrieb" },
            { "Sicht, Sensoren & Funk", "Sicht & Funk" }, { "Navigation, Karte & Zeit", "Navigation" }, { "Kampagne & Missionen", "Kampagne" },
            { "Spielstand & Leistung", "Leistung" }, { "Anzeige, Kamera & Menü", "Anzeige" }, { "Fehler-Fixes", "Fixes" }, { "Tools", "Fehlersuche" },
        };
        // subject -> { page name, old tab name of the special page }
        private static readonly Dictionary<string, string[]> special = new Dictionary<string, string[]>
        {
            { "Crew & Gesundheit", new[] { "Mannschaft", "Crew" } }, { "Waffen & Torpedos", new[] { "Torpedo-Laden", "Torpedos" } },
            { "Navigation, Karte & Zeit", new[] { "Karten-Farben", "Karte" } }, { "Sicht, Sensoren & Funk", new[] { "Licht", "Licht" } },
            { "Boot & Technik", new[] { "Schadensbericht", "Schaden" } },
        };
        private readonly Dictionary<string, string> pageOf = new Dictionary<string, string>();
        private const string PComfort = "Komfort", PCheats = "Cheats", PKeys = "Tasten";
        private static string Short(string t) => shortName.TryGetValue(t, out var n) ? n : t;
        private static bool IsTopic(string t) => System.Array.IndexOf(Settings.GroupOrder, t) >= 0;
        // subject that an old special tab now belongs to ("Crew" -> "Crew & Gesundheit"), or null
        private static string TopicOfOldTab(string oldTab)
        {
            foreach (var kv in special) if (kv.Value[1] == oldTab || (oldTab == "LichtStil" && kv.Value[1] == "Licht")) return kv.Key;
            return null;
        }

        private static int CountOf(string g, string page)
        {
            int n = 0;
            foreach (var x in Settings.All)
            {
                if (x.Group != g) continue;
                if (page == PKeys ? x.IsKey : !x.IsKey && x.Tab == (page == PCheats ? Settings.Cheat : Settings.QoL)) n++;
            }
            return n;
        }

        private void DrawSpecial(string oldTab)
        {
            if (oldTab == "Crew") UboatToolkit.Modules.ExtraFeatures.DrawCrewTab();
            if (oldTab == "Torpedos") UboatToolkit.Modules.TorpedoTab.Draw();
            if (oldTab == "Karte") UboatToolkit.Modules.MapTab.Draw();
            if (oldTab == "Licht") UboatToolkit.Modules.LightTab.Draw();
            if (oldTab == "Schaden") UboatToolkit.Modules.DamageReport.Draw();
            foreach (var o in Settings.All) if (o.Tab == oldTab && o.Parent == null) DrawBlock(o);
        }

        private void DrawTopicTab(string g)
        {
            float W = Mathf.Max(300f, MenuRect().width - 60);
            var pages = new List<string>();
            if (CountOf(g, PComfort) > 0) pages.Add(PComfort);
            if (CountOf(g, PCheats) > 0) pages.Add(PCheats);
            if (CountOf(g, PKeys) > 0) pages.Add(PKeys);
            if (special.TryGetValue(g, out var sp)) pages.Add(sp[0]);
            if (pages.Count == 0) { Imgui.Label("Hier gibt es keine Einstellungen."); return; }
            if (!pageOf.TryGetValue(g, out var page) || !pages.Contains(page)) page = pages[0];
            Imgui.BeginH();
            foreach (var p in pages)
            {
                int n = p == PComfort || p == PCheats || p == PKeys ? CountOf(g, p) : 0;
                if (Imgui.Button((p == page ? "● " : "") + p + (n > 0 ? "  (" + n + ")" : ""), 170)) pageOf[g] = p;
            }
            Imgui.EndH();
            Imgui.Space(6);
            if (sp != null && page == sp[0]) { DrawSpecial(sp[1]); return; }
            if (page == PKeys) { foreach (var x in Settings.All) if (x.IsKey && x.Group == g) DrawOpt(x); return; }
            string kind = page == PCheats ? Settings.Cheat : Settings.QoL;
            var subs = new List<string>();
            foreach (var x in Settings.All)
                if (x.Group == g && x.Tab == kind && x.Parent == null && !subs.Contains(x.Sub)) subs.Add(x.Sub);
            if (subs.Remove("Allgemein")) subs.Insert(0, "Allgemein");
            foreach (var sname in subs)
            {
                int count = 0, changed = 0;
                foreach (var x in Settings.All)
                {
                    if (x.Group != g || x.Tab != kind || x.Sub != sname) continue;
                    bool c = Settings.Changed(x);
                    if (onlyChanged && !c) continue;
                    count++; if (c) changed++;
                }
                if (count == 0) continue;
                string key = kind + "/" + g + "/" + sname;
                bool open = subs.Count == 1 || onlyChanged || openGroups.Contains(key);
                if (subs.Count > 1 && !onlyChanged
                    && Imgui.Button((open ? "▼  " : "▶  ") + sname + "   (" + count + (changed > 0 ? ", " + changed + " geändert" : "") + ")", W))
                { if (open) openGroups.Remove(key); else openGroups.Add(key); }
                if (!open) continue;
                foreach (var x in Settings.All)
                {
                    if (x.Group != g || x.Tab != kind || x.Parent != null || x.Sub != sname) continue;
                    if (onlyChanged && !Settings.Changed(x) && !x.Children.Any(Settings.Changed)) continue;
                    DrawBlock(x);
                }
                Imgui.Space(8);
            }
        }

        private static string cheatLine; private static float cheatLineAt;
        private void DrawGui()
        {
            if (Notice != null && Time.unscaledTime < NoticeUntil)
                Imgui.TextAt(new Rect(Screen.width / 2f - 300, 60, 600, 24), ">>> " + Notice + " <<<");
            // Info lines no longer sit on top of the game's top-left widgets (DoMe 2026-11-14): position in % of the screen (F10).
            float ix = Screen.width * Settings.F("InfoTopX") / 100f, iy = Screen.height * Settings.F("InfoTopY") / 100f;
            if (!string.IsNullOrEmpty(InfoLine)) Imgui.TextAt(new Rect(ix, iy, 1100, 22), InfoLine);
            if (!string.IsNullOrEmpty(UboatToolkit.Modules.TankReset.Line)) Imgui.TextAt(new Rect(ix, iy + 40, 1100, 22), UboatToolkit.Modules.TankReset.Line);
            if (!string.IsNullOrEmpty(UboatToolkit.Modules.ExtraFeatures.TubeLine)) Imgui.TextAt(new Rect(ix, iy + 20, 1100, 22), UboatToolkit.Modules.ExtraFeatures.TubeLine);
            if (Settings.B("CheatShowActive"))
            {
                // 0.4.7: the list of active cheats is rebuilt once per second, not in every GUI pass (it walks all ~800 settings)
                if (Time.unscaledTime >= cheatLineAt)
                {
                    cheatLineAt = Time.unscaledTime + 1f;
                    var active = Settings.All.Where(o => o.Tab == Settings.Cheat && o.Key != "CheatShowActive" && (o.IsBool ? o.Value > 0f : o.Value != o.Default)).Select(o => o.Key.Replace("Cheat", "")).ToArray();
                    cheatLine = active.Length > 0 ? "Cheats: " + string.Join(", ", active) : null;
                }
                if (cheatLine != null) Imgui.TextAt(new Rect(ix, iy + 40, 900, 22), cheatLine);
            }
            if (Util.InGameView())   // not over the port menu / ship selection / HQ etc.
            {
                UboatToolkit.Modules.DepthGauge.Draw();
                UboatToolkit.Modules.Warthog.WarthogThrusterGauge.Draw();
                UboatToolkit.Modules.SonarScope.Draw();
            }
            UboatToolkit.Modules.UnderFire.Draw();
            // open/close takes effect at a layout pass; a change between layout and repaint breaks the menu drawing
            if (Imgui.IsLayout()) menuShown = menuOpen;
            if (!menuShown) return;
            DragMenu();

            // a tab change takes effect at the start of the next GUI event (always a layout pass), never in the middle of one
            if (pendingTab != null) { tab = pendingTab; pendingTab = null; scroll = Vector2.zero; }
            var r = MenuRect();
            Imgui.Backdrop(r, Settings.F("MenuOpacity"));
            Imgui.Box(r, "UBOAT Toolkit  (F10 schließt, Titel ziehen = verschieben, Ecke unten rechts = Größe)");
            Imgui.TextAt(new Rect(r.xMax - 18, r.yMax - 20, 16, 18), "◢");
            Imgui.BeginArea(new Rect(r.x + 10, r.y + 28, r.width - 20, r.height - 38));
            // tabs: as wide as fits, second row when the window is narrow
            var tabList = new List<string>();
            foreach (var g in Settings.GroupOrder) if (special.ContainsKey(g) || Settings.All.Any(x => x.Group == g)) tabList.Add(g);
            if (!Util.WarthogModPresent) tabList.Add(UboatToolkit.Modules.Warthog.WarthogSettings.Tab);
            foreach (var kv in AddonTabs.Tabs) if (!tabList.Contains(kv.Key)) tabList.Add(kv.Key);
            tabList.Add(Settings.KeysTab); tabList.Add(Settings.Tools);
            var tabs = tabList.ToArray();
            if (System.Array.IndexOf(tabs, tab) < 0) tab = tabs[0];
            int perRow = Mathf.Clamp(Mathf.FloorToInt((r.width - 20) / 125f), 1, tabs.Length);
            float tw = (r.width - 30) / perRow - 4;
            for (int i = 0; i < tabs.Length; i += perRow)
            {
                Imgui.BeginH();
                for (int j = i; j < Mathf.Min(i + perRow, tabs.Length); j++)
                    if (Imgui.Button((tabs[j] == tab ? "● " : "") + Short(tabs[j]), tw)) { pendingTab = tabs[j]; }
                Imgui.EndH();
            }
            Imgui.Space(6);
            Imgui.BeginH();
            Imgui.Label("Suche:", 60);
            // while a key is being captured the search box is only shown, not editable (else the key is typed into it)
            if (Keys.Capturing != null) { Imgui.ClearFocus(); Imgui.Label(string.IsNullOrEmpty(search) ? "-" : search, 300); }
            else search = Imgui.TextField(search, 300);
            if (Imgui.Button("x", 30)) search = "";
            onlyChanged = Imgui.Toggle(onlyChanged, " nur geänderte zeigen");
            searchThisTab = Imgui.Toggle(searchThisTab, " Suche nur in diesem Reiter");
            if (Imgui.Button("alle auf", 90)) foreach (var x in Settings.All) if (x.Group == tab && !x.IsKey) { openGroups.Add(x.Tab + "/" + x.Group + "/" + x.Sub); }
            if (Imgui.Button("alle zu", 90)) { openGroups.Clear(); openOpts.Clear(); }
            Imgui.EndH();
            scroll = Imgui.BeginScroll(scroll);
            bool builtinWh = !Util.WarthogModPresent && tab == UboatToolkit.Modules.Warthog.WarthogSettings.Tab;
            if (builtinWh) UboatToolkit.Modules.Warthog.WarthogTab.Draw();
            if (!builtinWh) AddonTabs.Draw(tab);
            if (!string.IsNullOrEmpty(search) && search.Trim().Length >= 2)
            {
                // 0.3.66 (DoMe: search hits cannot be told apart, clicking a tab does not filter): hits are sorted into
                // sections "tab / topic" with a headline each; "nur dieser Reiter" limits the hits to the tab clicked above.
                // 0.3.75 better search: several words (all must match), umlauts and case do not matter, the topic, sub-topic
                // and the main switch of a fine setting are searched too ("flak schaden", "88 rauch", "cheat torpedo").
                // 0.4.3: the matching itself lives in Core/Search.cs (word parts, endings, typing mistakes, same-meaning
                // words, best hits first). The result is kept until the search text changes.
                var scores = Search.Run(search);
                Opt lastParent = null;
                var shown = new HashSet<Opt>();
                int hits = 0;
                string lastPlace = null;
                var found = new List<KeyValuePair<string, Opt>>();
                foreach (var o in Settings.All)
                {
                    if (!scores.ContainsKey(o)) continue;
                    if (onlyChanged && !Settings.Changed(o)) continue;
                    bool topic = o.Group != "" && (o.Tab == Settings.QoL || o.Tab == Settings.Cheat || o.IsKey);
                    string oldTopic = topic ? null : TopicOfOldTab(o.Tab);
                    string home = topic ? o.Group : oldTopic ?? o.Tab;
                    if (searchThisTab && home != tab && !(o.IsKey && tab == Settings.KeysTab)) continue;
                    string pg = topic ? (o.IsKey ? PKeys : o.Tab == Settings.Cheat ? PCheats : PComfort) : oldTopic != null ? special[oldTopic][0] : "";
                    string place = Short(home) + (pg != "" ? "  >  " + pg : "") + (topic && !o.IsKey && o.Sub != "" ? "  >  " + o.Sub : "");
                    found.Add(new KeyValuePair<string, Opt>(place, o));
                    placeHome[place] = home; placeGroup[place] = pg;
                }
                // best place first (the place of the best hit), inside a place the best hits first; a fine setting stays
                // below its main switch when both are hits
                var placeBest = new Dictionary<string, int>();
                foreach (var kv in found) { int sc = scores[kv.Value], old; if (!placeBest.TryGetValue(kv.Key, out old) || sc > old) placeBest[kv.Key] = sc; }
                Func<Opt, int> rank = o => o.Parent != null && scores.ContainsKey(o.Parent) ? Math.Max(scores[o.Parent], scores[o]) : scores[o];
                var order = new Dictionary<Opt, int>();
                for (int i = 0; i < Settings.All.Count; i++) order[Settings.All[i]] = i;
                foreach (var kv in found.OrderByDescending(x => placeBest[x.Key]).ThenBy(x => x.Key, StringComparer.Ordinal)
                    .ThenByDescending(x => rank(x.Value)).ThenBy(x => order[x.Value.Parent != null && scores.ContainsKey(x.Value.Parent) ? x.Value.Parent : x.Value]).ThenBy(x => order[x.Value]))
                {
                    if (kv.Key != lastPlace)
                    {
                        lastPlace = kv.Key; Imgui.Space(8);
                        // click on the headline = jump to that place in the menu
                        if (Imgui.Button("▶  " + kv.Key + "   (klicken = dorthin springen)", Mathf.Max(300f, MenuRect().width - 60)))
                        {
                            pendingTab = placeHome[kv.Key];
                            if (placeGroup[kv.Key] != "") pageOf[pendingTab] = placeGroup[kv.Key];
                            openGroups.Add(kv.Value.Tab + "/" + kv.Value.Group + "/" + kv.Value.Sub);
                            if (kv.Value.Parent != null) openOpts.Add(kv.Value.Parent.Key);
                            search = ""; scroll = Vector2.zero;
                            break;
                        }
                        lastParent = null;
                    }
                    // a fine setting says which main switch it belongs to
                    // (not when that main switch is itself a hit and stands right above)
                    if (kv.Value.Parent != null && kv.Value.Parent != lastParent && !shown.Contains(kv.Value.Parent))
                    { lastParent = kv.Value.Parent; Imgui.BeginH(); Imgui.Space(Gutter); Imgui.Label("gehört zu: " + kv.Value.Parent.Label.Trim()); Imgui.EndH(); }
                    shown.Add(kv.Value);
                    DrawOpt(kv.Value);
                    hits++;
                }
                if (hits == 0)
                {
                    Imgui.Label("Nichts gefunden.");
                    if (Search.Suggestion != "" && Imgui.Button("Meintest du: " + Search.Suggestion + " ?", 420)) search = Search.Suggestion;
                }
            }
            else if (IsTopic(tab)) DrawTopicTab(tab);
            else
                foreach (var o in Settings.All) if (o.Tab == tab && o.Parent == null) DrawBlock(o);
            Imgui.EndScroll();
            Imgui.BeginH();
            if (Imgui.Button("Speichern", 150)) Settings.Save();
            if (Imgui.Button("INI neu laden", 150)) Settings.Load();
            Imgui.EndH();
            Imgui.EndArea();
        }
    }

    public interface IModule
    {
        void Tick();   // about once per second while the player's boat exists
        void Frame();  // every frame (keep cheap)
    }

    public abstract class ModuleBase : IModule
    {
        public virtual void Tick() { }
        public virtual void Frame() { }
    }
}

// ====================================================================================================
// Abschnitt: Core/Util.cs
// ====================================================================================================
namespace UboatToolkit
{
    using global::System;
    using global::System.Collections.Generic;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Scene.Entities;

    public static class Util
    {
        // 0.3.79: fast type lookup by name. Harmony's TypeByName walks every loaded type on each call (0.2-0.4 s when the
        // type does not exist, e.g. another mod that is not installed); measured 15.3 s of patching at game start, most of
        // it these lookups. Here all types are listed once; the list is rebuilt only when a new assembly was loaded.
        private static Dictionary<string, Type> typeByFull, typeByShort;
        private static int typeAsmCount = -1;
        public static Type FindType(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            try
            {
                var direct = Type.GetType(name, false);
                if (direct != null) return direct;
                int n = 0; foreach (var a in AccessTools.AllAssemblies()) n++;
                if (typeByFull == null || n != typeAsmCount)
                {
                    var full = new Dictionary<string, Type>(); var shortN = new Dictionary<string, Type>();
                    foreach (var t in AccessTools.AllTypes())
                    {
                        if (t == null) continue;
                        if (t.FullName != null && !full.ContainsKey(t.FullName)) full[t.FullName] = t;
                        if (t.Name != null && !shortN.ContainsKey(t.Name)) shortN[t.Name] = t;
                    }
                    typeByFull = full; typeByShort = shortN; typeAsmCount = n;
                }
                if (typeByFull.TryGetValue(name, out var r)) return r;
                return typeByShort.TryGetValue(name, out r) ? r : null;
            }
            catch { return AccessTools.TypeByName(name); }
        }

        public const string ModifierName = "UboatToolkit";

        private static PlayerShip playerShip;

        // The player's boat, or null in menus / port loading.
        public static PlayerShip PlayerShip
        {
            get
            {
                // Searching the whole scene is slow: in menus (no boat) only try once per second.
                if (playerShip == null && Time.unscaledTime >= nextShipSearch)
                {
                    nextShipSearch = Time.unscaledTime + 1f;
                    playerShip = UnityEngine.Object.FindObjectOfType<PlayerShip>();
                    if (playerShip != null) shipFoundAt = Time.unscaledTime;
                }
                return playerShip;
            }
        }

        private static float shipFoundAt;

        // Number of slots of a storage, or 0 while the storage is not set up yet (would crash otherwise).
        public static int Slots(UBOAT.Game.Scene.Items.Storage s)
        {
            if (s == null) return 0;
            try { return s.SlotCount; } catch { return 0; }
        }

        // True once the boat has existed for a few seconds: right after loading many parts are
        // still being built, and touching them then gives "Object reference not set" errors.
        public static bool ShipReady => PlayerShip != null && Time.unscaledTime - shipFoundAt >= 5f;

        private static float nextShipSearch;
        private static PlayerShip cacheShip;
        private static readonly Dictionary<Type, Array> found = new Dictionary<Type, Array>();
        private static readonly Dictionary<Type, float> foundAt = new Dictionary<Type, float>();

        // All parts of type T on the player's boat. Searching the boat is slow, so the result is kept
        // for 10 seconds (or until one of the parts is gone). Saves many small stutters.
        // 0.4.7: how often and how long the boat was searched (logged once per minute with the Toolkit cost line)
        internal static int FindScans; internal static double FindSeconds;
        public static T[] Find<T>(float maxAge = 10f) where T : Component
        {
            var ship = PlayerShip;
            if (ship == null) return new T[0];
            if (cacheShip != ship) { found.Clear(); foundAt.Clear(); cacheShip = ship; }
            var t = typeof(T);
            if (found.TryGetValue(t, out var a) && Time.unscaledTime - foundAt[t] < maxAge)
            {
                var arr = (T[])a;
                bool ok = true;
                for (int i = 0; i < arr.Length; i++) if (arr[i] == null) { ok = false; break; }
                if (ok) return arr;
            }
            double f0 = Time.realtimeSinceStartupAsDouble; FindScans++;
            var res = ship.GetComponentsInChildren<T>(true);
            // Interior devices (pumps, racks, ...) live in a separate "<Name> Interior" scene: add them too.
            try
            {
                var inner = ship.Interior;
                if (inner != null && !inner.transform.IsChildOf(ship.transform))
                {
                    var more = inner.GetComponentsInChildren<T>(true);
                    if (more.Length > 0)
                    {
                        var all = new List<T>(res);
                        var seen = new HashSet<T>(res);
                        foreach (var m in more) if (seen.Add(m)) all.Add(m);
                        res = all.ToArray();
                    }
                }
            }
            catch { }
            found[t] = res; foundAt[t] = Time.unscaledTime;
            FindSeconds += Time.realtimeSinceStartupAsDouble - f0;
            return res;
        }

        // 0.4.6: same as Find<T>, for a type only known by name (boat + its separate interior scene). Not cached.
        public static Component[] FindByType(Type t)
        {
            var ship = PlayerShip;
            if (ship == null || t == null) return new Component[0];
            var res = ship.GetComponentsInChildren(t, true);
            try
            {
                var inner = ship.Interior;
                if (inner != null && !inner.transform.IsChildOf(ship.transform))
                {
                    var more = inner.GetComponentsInChildren(t, true);
                    if (more.Length > 0)
                    {
                        var all = new List<Component>(res);
                        var seen = new HashSet<Component>(res);
                        foreach (var m in more) if (seen.Add(m)) all.Add(m);
                        res = all.ToArray();
                    }
                }
            }
            catch { }
            return res;
        }

        // true while the normal game view (boat, map, first person, manual device) is shown; false in port menus,
        // ship selection, headquarters, dialogues, cutscenes and other fullscreen UIs: our gauges are not drawn there.
        private static UBOAT.Game.UI.GameUI gameUi; private static float gameUiAt = -99f;
        private static UBOAT.Game.UI.ShipSelectorUI shipSel; private static float shipSelAt = -99f;
        public static bool InGameView()
        {
            if (gameUi == null && Time.unscaledTime - gameUiAt > 5f) { gameUiAt = Time.unscaledTime; gameUi = UnityEngine.Object.FindObjectOfType<UBOAT.Game.UI.GameUI>(); }
            if (shipSel == null && Time.unscaledTime - shipSelAt > 5f) { shipSelAt = Time.unscaledTime; shipSel = UnityEngine.Object.FindObjectOfType<UBOAT.Game.UI.ShipSelectorUI>(true); }
            if (shipSel != null && shipSel.isActiveAndEnabled) return false;   // ship selection open
            if (gameUi == null) return true;
            var m = gameUi.CurrentMode;
            return m == UBOAT.Game.UI.GameUI.Mode.Normal || m == UBOAT.Game.UI.GameUI.Mode.Map || m == UBOAT.Game.UI.GameUI.Mode.FPP
                || m == UBOAT.Game.UI.GameUI.Mode.FPPEx || m == UBOAT.Game.UI.GameUI.Mode.DeviceManualMode;
        }

        // "Type XXX" = the Warthog boat type (string check only, no reference to the Warthog mod).
        public static bool IsTypeXXX(PlayerShip ship)
        {
            if (ship == null) return false;
            try { var t = ship.SandboxEntity != null ? ship.SandboxEntity.Type : null; if (t != null && t.Name != null) return t.Name.StartsWith("Type XXX"); } catch { }
            return ship.gameObject.name.StartsWith("Type XXX");
        }

        // True when the Warthog mod brings its own Warthog code (namespace "Warthog", 0.3.9+). Then the Toolkit's
        // built-in Warthog parts stay off, so nothing runs twice; with an older Warthog (or none) the Toolkit's own
        // parts do the work. Both mods run alone and in any version combination (DoMe 2026-11-13).
        private static int warthogMod = -1;
        public static bool WarthogModPresent
        {
            get
            {
                if (warthogMod < 0)
                {
                    warthogMod = 0;
                    try
                    {
                        // No System.AppDomain here (the game forbids it in mod code): Harmony lists the loaded types.
                        var own = typeof(Util).Assembly;
                        foreach (var t in HarmonyLib.AccessTools.AllTypes())
                        {
                            if (t == null || t.Assembly == own) continue;
                            var ns = t.Namespace;
                            if (ns == "Warthog" || ns == "UboatToolkit.Modules.Warthog" || (ns != null && ns.StartsWith("Warthog."))) { warthogMod = 1; break; }
                        }
                    }
                    catch { }
                    Log("Warthog-Mod mit eigenem Code: " + (warthogMod == 1 ? "ja (Toolkit-Warthog-Teile bleiben aus)" : "nein (Toolkit-Warthog-Teile aktiv)"));
                }
                return warthogMod == 1;
            }
        }
        public static bool WarthogModOwns(PlayerShip ship) => WarthogModPresent && IsTypeXXX(ship);

        // true for parts of the player's boat, also for devices in the separate interior scene
        // (the game itself finds the owner the same way: SceneExtensions.GetParentShip).
        private static readonly Dictionary<int, bool> ownerCache = new Dictionary<int, bool>();
        private static PlayerShip ownerShip;
        public static bool IsPlayers(Component c)
        {
            var ship = PlayerShip;
            if (ship == null || c == null) return false;
            if (ownerShip != ship) { ownerCache.Clear(); ownerShip = ship; }
            var tr = c.transform;
            if (tr.IsChildOf(ship.transform)) return true;
            int id = c.GetInstanceID();
            bool r;
            if (ownerCache.TryGetValue(id, out r)) return r;
            r = false;
            try
            {
                var inner = ship.Interior;
                if (inner != null && tr.IsChildOf(inner.transform)) r = true;
                else
                {
                    var pi = c.GetComponentInParent<UBOAT.Game.Scene.Spaces.PlayerShipInterior>();
                    if (pi != null && pi.Ship == ship) r = true;
                }
            }
            catch { }
            if (ownerCache.Count > 20000) ownerCache.Clear();
            ownerCache[id] = r;
            return r;
        }

        // Makes a game parameter end up at "target" (or back to the game value when target <= 0),
        // using a scale modifier that the game itself understands.
        public static void SetParameter(Parameter p, float target)
        {
            if (p == null) return;
            var m = OneScale(p, ModifierName);
            if (target <= 0f) { m.Value = 1f; return; }
            float baseValue = p.GetValueExcludingModifier(ModifierName);
            m.Value = Mathf.Abs(baseValue) > 0.0001f ? target / baseValue : 1f;
        }

        // PlayerCareer is a ScriptableObject, so FindObjectOfType may miss it; FindObjectsOfTypeAll always finds it.
        private static UBOAT.Game.Sandbox.PlayerCareer career;
        private static float careerRetry;
        public static UBOAT.Game.Sandbox.PlayerCareer Career
        {
            get
            {
                // FindObjectsOfTypeAll searches every loaded object (40-100 ms). While no career exists (menu, loading)
                // it ran on every call; now at most every 10 s.
                if (career == null && Time.unscaledTime >= careerRetry)
                {
                    careerRetry = Time.unscaledTime + 10f;
                    career = System.Linq.Enumerable.FirstOrDefault(Resources.FindObjectsOfTypeAll<UBOAT.Game.Sandbox.PlayerCareer>());
                }
                return career;
            }
        }

        public static void ScaleParameter(Parameter p, float factor)
        {
            if (p == null) return;
            OneScale(p, ModifierName).Value = factor <= 0f ? 1f : factor;
        }

        // 0.4.19 (measured 2026-11-18: "xUboatToolkit=5 xUboatToolkit=5 xToolkit Horcher=2 xToolkit Horcher=2" on the
        // hydrophone = 5063 km instead of 506). The game stores a parameter's factors in the savegame. After loading,
        // the stored factor is still there and ours was added a second time. This returns exactly ONE factor with
        // that name (compared by text) and removes every further one.
        internal static int DuplicatesRemoved;
        public static Modifier OneScale(Parameter p, string name)
        {
            var list = p.ScaleModifiers; Modifier keep = null; int extra = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i];
                if (m == null || m.Name != name) continue;
                if (keep == null) keep = m; else extra++;
            }
            if (extra > 0)
            {
                for (int i = list.Count - 1; i >= 0; i--) { var m = list[i]; if (m != null && m != keep && m.Name == name) list.RemoveAt(i); }
                DuplicatesRemoved += extra;
                float v = keep.Value; keep.Value = v == 1f ? 1.0001f : 1f; keep.Value = v;   // makes the game recompute the value
                Log("Doppelter Faktor entfernt: \"" + name + "\" stand " + (extra + 1) + " mal an " + (p.Name ?? "?") + " (aus dem Spielstand), jetzt 1 mal");
            }
            return keep ?? p.AddScaleModifier(name);
        }
        // same for added values ("delta"). create = false: only tidy up and return the one that is there (or null).
        public static Modifier OneDelta(Parameter p, string name, bool create = true)
        {
            var list = p.DeltaModifiers; Modifier keep = null; int extra = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i];
                if (m == null || m.Name != name) continue;
                if (keep == null) keep = m; else extra++;
            }
            if (extra > 0)
            {
                for (int i = list.Count - 1; i >= 0; i--) { var m = list[i]; if (m != null && m != keep && m.Name == name) list.RemoveAt(i); }
                DuplicatesRemoved += extra;
                float v = keep.Value; keep.Value = v + 1f; keep.Value = v;   // makes the game recompute the value
                Log("Doppelter Wert entfernt: \"" + name + "\" stand " + (extra + 1) + " mal an " + (p.Name ?? "?") + " (aus dem Spielstand), jetzt 1 mal");
            }
            return keep ?? (create ? p.AddDeltaModifier(name) : null);
        }
        public static void DropDelta(Parameter p, string name)
        {
            if (p == null) return;
            var m = OneDelta(p, name, false);
            if (m != null) m.Remove();
        }
        public static void DropScale(Parameter p, string name)
        {
            if (p == null) return;
            var list = p.ScaleModifiers; bool any = false;
            for (int i = list.Count - 1; i >= 0; i--) { var m = list[i]; if (m != null && m.Name == name) { if (!any) { any = true; m.Value = 1f; } list.RemoveAt(i); } }
            if (any) { var t = p.AddScaleModifier("Toolkit tmp"); t.Value = 1f; p.RemoveScaleModifiers("Toolkit tmp"); }
        }

        // Remembers the original value of a private field per object, so a feature can be switched off again.
        private static readonly Dictionary<string, object> originals = new Dictionary<string, object>();

        public static T Original<T>(object obj, string field)
        {
            string key = obj.GetHashCode() + "/" + field;
            if (!originals.TryGetValue(key, out var v))
            {
                v = Traverse.Create(obj).Field(field).GetValue();
                if (v is float[] arr) v = arr.Clone();
                originals[key] = v;
            }
            return (T)v;
        }

        public static void SetField(object obj, string field, object value) => Traverse.Create(obj).Field(field).SetValue(value);

        public static void Log(string text)
        {
            Debug.Log(ToolkitLoader.Tag + " " + text);
            DebugLog.Write("INFO", text);
        }

        private static readonly Dictionary<string, int> errorCounts = new Dictionary<string, int>();

        public static T SafeGet<T>(Func<T> f, T fallback) { try { return f(); } catch { return fallback; } }

        public static void Safe(string what, Action a)
        {
            try { a(); }
            catch (Exception e0)
            {
                // errors of calls made through reflection (the whole menu) arrive wrapped: log the real one
                var e = e0;
                while (e is System.Reflection.TargetInvocationException && e.InnerException != null) e = e.InnerException;
                // Same error every second would flood the log: write it 3 times, then every 100th time.
                string key = what + "|" + e.Message;
                errorCounts.TryGetValue(key, out int n);
                errorCounts[key] = ++n;
                if (n <= 3 || n % 100 == 0)
                {
                    Debug.Log(ToolkitLoader.Tag + " error in " + what + " (x" + n + "): " + e.Message);
                    DebugLog.Write("ERROR", what + " (x" + n + "): " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
                }
            }
        }
    }
}

