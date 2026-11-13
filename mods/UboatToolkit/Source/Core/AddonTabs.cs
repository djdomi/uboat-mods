using System;
using System.Collections.Generic;

namespace UboatToolkit
{
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
