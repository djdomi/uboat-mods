using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace UboatToolkit
{
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
