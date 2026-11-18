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
        private static bool Owns(Opt o, Parsed k)
        {
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
