using System;
using System.Collections.Generic;
using System.Text;

namespace UboatToolkit
{
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
