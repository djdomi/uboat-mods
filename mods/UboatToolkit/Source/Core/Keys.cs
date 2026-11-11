using System;
using UnityEngine;

namespace UboatToolkit
{
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
            return k.C == Ctrl && k.S == Shift && k.A == Alt;
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
