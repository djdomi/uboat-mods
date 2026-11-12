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
        private bool menuOpen;
        private string tab = Settings.QoL;
        private Vector2 scroll;

        private void Awake()
        {
            Modules.Add(new Modules.Radio());
            Modules.Add(new Modules.Lighting());
            Modules.Add(new Modules.Hatches());
            Modules.Add(new Modules.Optics());
            Modules.Add(new Modules.Crew());
            Modules.Add(new Modules.Weapons());
            Modules.Add(new Modules.MilkCows());
            Modules.Add(new Modules.Ballast());
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
            Keys.CaptureFrame();
            if (Keys.Down("KeyMenu")) menuOpen = !menuOpen;
            if (names == null) { names = new string[Modules.Count]; frameNames = new string[Modules.Count]; for (int i = 0; i < Modules.Count; i++) { names[i] = Modules[i].GetType().Name; frameNames[i] = names[i] + ".Frame"; } }
            for (int i = 0; i < Modules.Count; i++) Util.Safe(frameNames[i], Modules[i].Frame);

            if (Time.unscaledTime >= nextState) { nextState = Time.unscaledTime + 60f; DebugLog.DumpState(); }
            if (Util.PlayerShip == null) return;
            // Each module runs once per second, but spread over the frames (one module per frame),
            // so there is no single heavy frame = no stutter.
            if (Time.unscaledTime < nextTick) return;
            Util.Safe(names[tickIndex], Modules[tickIndex].Tick);
            tickIndex++;
            if (tickIndex >= Modules.Count) { tickIndex = 0; nextTick = Time.unscaledTime + 1f; }
        }

        private string[] names, frameNames;
        private int tickIndex;

        private float nextState;

        private void OnGUI()
        {
            Imgui.Init();
            if (!Imgui.Ready) return;
            Util.Safe("menu", DrawGui);
        }

        // Short on-screen message from a module (e.g. "Land voraus").
        public static string Notice;
        public static string InfoLine;
        public static float NoticeUntil;
        public static void ShowNotice(string text, float seconds = 6f) { Notice = text; NoticeUntil = Time.unscaledTime + seconds; Util.Log(text); }

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

        private void DrawGui()
        {
            if (Notice != null && Time.unscaledTime < NoticeUntil)
                Imgui.TextAt(new Rect(Screen.width / 2f - 300, 60, 600, 24), ">>> " + Notice + " <<<");
            if (!string.IsNullOrEmpty(InfoLine)) Imgui.TextAt(new Rect(8, 24, 1100, 22), InfoLine);
            if (!string.IsNullOrEmpty(UboatToolkit.Modules.ExtraFeatures.TubeLine)) Imgui.TextAt(new Rect(8, 44, 1100, 22), UboatToolkit.Modules.ExtraFeatures.TubeLine);
            if (Settings.B("CheatShowActive"))
            {
                var active = Settings.All.Where(o => o.Tab == Settings.Cheat && o.Key != "CheatShowActive" && (o.IsBool ? o.Value > 0f : o.Value != o.Default)).Select(o => o.Key.Replace("Cheat", "")).ToArray();
                if (active.Length > 0) Imgui.TextAt(new Rect(8, 4, 900, 22), "Cheats: " + string.Join(", ", active));
            }
            UboatToolkit.Modules.UnderFire.Draw();
            if (!menuOpen) return;
            DragMenu();

            var r = MenuRect();
            Imgui.Backdrop(r, Settings.F("MenuOpacity"));
            Imgui.Box(r, "UBOAT Toolkit  (F10 schließt, Titel ziehen = verschieben, Ecke unten rechts = Größe)");
            Imgui.TextAt(new Rect(r.xMax - 18, r.yMax - 20, 16, 18), "◢");
            Imgui.BeginArea(new Rect(r.x + 10, r.y + 28, r.width - 20, r.height - 38));
            // tabs: as wide as fits, second row when the window is narrow
            var tabs = new[] { Settings.QoL, Settings.Cheat, "Crew", "Torpedos", "Karte", "Licht", "Schaden", Settings.KeysTab, Settings.Tools };
            int perRow = Mathf.Clamp(Mathf.FloorToInt((r.width - 20) / 90f), 1, tabs.Length);
            float tw = (r.width - 30) / perRow - 4;
            for (int i = 0; i < tabs.Length; i += perRow)
            {
                Imgui.BeginH();
                for (int j = i; j < Mathf.Min(i + perRow, tabs.Length); j++)
                    if (Imgui.Button((tabs[j] == tab ? "> " : "") + tabs[j], tw)) tab = tabs[j];
                Imgui.EndH();
            }
            Imgui.Space(6);
            scroll = Imgui.BeginScroll(scroll);
            if (tab == "Crew") UboatToolkit.Modules.ExtraFeatures.DrawCrewTab();
            if (tab == "Torpedos") UboatToolkit.Modules.TorpedoTab.Draw();
            if (tab == "Karte") UboatToolkit.Modules.MapTab.Draw();
            if (tab == "Licht") UboatToolkit.Modules.LightTab.Draw();
            if (tab == "Schaden") UboatToolkit.Modules.DamageReport.Draw();
            foreach (var o in Settings.All.Where(x => x.Tab == tab))
            {
                if (o.IsKey)
                {
                    Imgui.BeginH();
                    Imgui.Label(o.Label + ":  " + (string.IsNullOrEmpty(o.Text) ? "(keine)" : o.Text), 450);
                    if (Imgui.Button(Keys.Capturing == o ? "Taste drücken ... (Esc = Abbruch, Rück = keine)" : "Ändern", 200)) Keys.Capturing = o;
                    Imgui.EndH();
                }
                else if (o.IsBool)
                {
                    o.Value = Imgui.Toggle(o.On, " " + o.Label) ? 1 : 0;
                }
                else
                {
                    Imgui.Label(o.Label + ":  " + o.Value.ToString("0.##", CultureInfo.InvariantCulture));
                    float v = Imgui.Slider(Mathf.Clamp(o.Value, o.Min, o.Max), o.Min, o.Max, Mathf.Max(200f, MenuRect().width - 120));
                    if (o.Step > 0) v = Mathf.Round(v / o.Step) * o.Step;
                    if (Mathf.Abs(v - Mathf.Clamp(o.Value, o.Min, o.Max)) > 0.0001f) o.Value = v;
                }
            }
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
