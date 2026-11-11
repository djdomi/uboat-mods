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
            Modules.Add(new Modules.IronCoffins());
            Modules.Add(new Modules.Medical());
            Modules.Add(new Modules.Repairs());
            Modules.Add(new Modules.Navigation());
            Modules.Add(new Modules.Stations());
            Modules.Add(new Modules.Air());
            Modules.Add(new Modules.Pump());
            Modules.Add(new Modules.CrewFill());
            Modules.Add(new Modules.Torpedoes());
            Modules.Add(new Modules.Cheats());
            Modules.Add(new Modules.PerfLogger());
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F10)) menuOpen = !menuOpen;
            foreach (var m in Modules) Util.Safe(m.GetType().Name + ".Frame", m.Frame);

            if (Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + 1f;
            if (Util.PlayerShip == null) return;
            foreach (var m in Modules) Util.Safe(m.GetType().Name, m.Tick);
        }

        private void OnGUI()
        {
            Imgui.Init();
            if (!Imgui.Ready) return;
            Util.Safe("menu", DrawGui);
        }

        // Short on-screen message from a module (e.g. "Land voraus").
        public static string Notice;
        public static float NoticeUntil;
        public static void ShowNotice(string text, float seconds = 6f) { Notice = text; NoticeUntil = Time.unscaledTime + seconds; Util.Log(text); }

        private void DrawGui()
        {
            if (Notice != null && Time.unscaledTime < NoticeUntil)
                Imgui.TextAt(new Rect(Screen.width / 2f - 300, 60, 600, 24), ">>> " + Notice + " <<<");
            if (Settings.B("CheatShowActive"))
            {
                var active = Settings.All.Where(o => o.Tab == Settings.Cheat && o.Key != "CheatShowActive" && (o.IsBool ? o.Value > 0f : o.Value != o.Default)).Select(o => o.Key.Replace("Cheat", "")).ToArray();
                if (active.Length > 0) Imgui.TextAt(new Rect(8, 4, 900, 22), "Cheats: " + string.Join(", ", active));
            }
            if (!menuOpen) return;

            var r = new Rect(40, 40, 720, Mathf.Min(760, Screen.height - 80));
            Imgui.Box(r, "UBOAT Toolkit  (F10 schließt)");
            Imgui.BeginArea(new Rect(r.x + 10, r.y + 28, r.width - 20, r.height - 38));
            Imgui.BeginH();
            foreach (var t in new[] { Settings.QoL, Settings.Cheat, Settings.Tools })
                if (Imgui.Button((t == tab ? "> " : "") + t, 120)) tab = t;
            Imgui.EndH();
            Imgui.Space(6);
            scroll = Imgui.BeginScroll(scroll);
            foreach (var o in Settings.All.Where(x => x.Tab == tab))
            {
                if (o.IsBool)
                {
                    o.Value = Imgui.Toggle(o.On, " " + o.Label) ? 1 : 0;
                }
                else
                {
                    Imgui.Label(o.Label + ":  " + o.Value.ToString("0.##", CultureInfo.InvariantCulture));
                    float v = Imgui.Slider(Mathf.Clamp(o.Value, o.Min, o.Max), o.Min, o.Max, 600);
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
