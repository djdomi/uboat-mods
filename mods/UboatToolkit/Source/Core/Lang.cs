using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UboatToolkit
{
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
