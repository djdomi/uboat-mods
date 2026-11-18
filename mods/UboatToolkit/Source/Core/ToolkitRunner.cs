using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace UboatToolkit
{
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
            if (mk != KeyCode.None && Input.GetKeyDown(mk)) MenuKey("Update");
            if (mk == KeyCode.None || !Input.GetKey(mk)) guiKeyHeld = false;
            if (names == null) { names = new string[Modules.Count]; frameNames = new string[Modules.Count]; for (int i = 0; i < Modules.Count; i++) { names[i] = Modules[i].GetType().Name; frameNames[i] = names[i] + ".Frame"; } }
            for (int i = 0; i < Modules.Count; i++) Timed(frameNames[i], Modules[i].Frame, 20.0);

            if (Time.unscaledTime >= nextState) { nextState = Time.unscaledTime + 60f; DebugLog.DumpState(); }
            if (Util.PlayerShip == null) return;
            // Each module runs once per second, but spread over the frames (one module per frame),
            // so there is no single heavy frame = no stutter.
            if (Time.unscaledTime < nextTick) return;
            Timed(names[tickIndex] + ".Tick", Modules[tickIndex].Tick, 40.0);
            tickIndex++;
            if (tickIndex >= Modules.Count) { tickIndex = 0; nextTick = Time.unscaledTime + 1f; }
        }

        private string[] names, frameNames;
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
                if (k == 1 && !guiKeyHeld) { guiKeyHeld = true; MenuKey("Fenster"); }
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
