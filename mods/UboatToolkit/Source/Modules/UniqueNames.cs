using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UBOAT.Game.Scene.Characters;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // More unique crew names: the game rolls first + last name freely, so the same last name
    // (or even the same full name) shows up again and again. A new man now gets a last name that
    // nobody on the crew has yet. The game's name lists are mixed with a bigger list of our own.
    // The button "Doppelte Namen jetzt ändern" renames men who share a name with someone else.
    public class UniqueNames : ModuleBase
    {
        internal static readonly string[] First = {
            "Adolf","Albert","Albrecht","Alfons","Alfred","Alois","Anton","Arnold","Arthur","August","Bernhard","Berthold","Bruno","Carl","Christian","Clemens",
            "Conrad","Dietrich","Eberhard","Eduard","Egon","Emil","Engelbert","Erich","Ernst","Erwin","Eugen","Ewald","Felix","Ferdinand","Franz","Friedrich",
            "Fritz","Georg","Gerhard","Gottfried","Gottlieb","Gustav","Hans","Harald","Hartmut","Heinrich","Heinz","Helmut","Herbert","Hermann","Horst","Hubert",
            "Hugo","Ignaz","Jakob","Joachim","Johann","Josef","Julius","Jürgen","Karl","Kaspar","Klaus","Konrad","Kurt","Leo","Leonhard","Leopold","Lothar",
            "Ludwig","Lukas","Manfred","Martin","Matthias","Max","Michael","Nikolaus","Oskar","Oswald","Otto","Paul","Peter","Philipp","Rainer","Reinhard",
            "Richard","Robert","Rolf","Rudolf","Rupert","Siegfried","Stefan","Theodor","Thomas","Ulrich","Valentin","Viktor","Volker","Walter","Werner","Wilhelm",
            "Willi","Wolfgang","Xaver","Anselm","Balthasar","Benedikt","Eckhard","Edmund","Gebhard","Gregor","Gunther","Hagen","Heribert","Ingo","Kilian",
            "Lambert","Meinrad","Norbert","Ottmar","Quirin","Raimund","Roland","Sebastian","Severin","Sigmund","Tobias","Ulf","Veit","Waldemar","Wendelin"
        };
        internal static readonly string[] Last = {
            "Ackermann","Adler","Albrecht","Arnold","Bachmann","Bauer","Baumann","Beck","Becker","Behrens","Berger","Bergmann","Beyer","Bischoff","Blum","Böhm",
            "Brandt","Braun","Brückner","Busch","Dietrich","Dittrich","Döring","Eckert","Engel","Ernst","Falk","Fiedler","Fischer","Fleischer","Frank","Franke",
            "Friedrich","Fuchs","Gärtner","Graf","Groß","Günther","Haas","Hahn","Hartmann","Hauser","Heinrich","Heller","Herrmann","Hesse","Hoffmann","Hofmann",
            "Horn","Huber","Jäger","Jung","Kaiser","Keller","Kirchner","Klein","Koch","Köhler","König","Krämer","Kraus","Krause","Krüger","Kuhn","Kühn","Kunz",
            "Lang","Lange","Lehmann","Lorenz","Ludwig","Maier","Marx","Mayer","Meier","Meyer","Möller","Morgenstern","Müller","Nagel","Neubauer","Neumann",
            "Nowak","Oswald","Otto","Pfeiffer","Pohl","Popp","Puschmann","Rau","Reuter","Richter","Ritter","Roth","Sauer","Schäfer","Scharf","Schilling",
            "Schmid","Schmidt","Schmitt","Schneider","Scholz","Schreiber","Schröder","Schubert","Schulte","Schulz","Schumacher","Schuster","Schwarz","Seidel",
            "Seifert","Sommer","Stahl","Stein","Steiner","Thiel","Unger","Vogel","Vogt","Voigt","Wagner","Walter","Weber","Wegener","Weidner","Weiß","Wendt",
            "Werner","Wilke","Winkler","Winter","Wirth","Witt","Wolf","Wolff","Zimmermann","Ziegler","Zander","Brenner","Drechsler","Eberle","Fendt","Gerlach",
            "Haberland","Hölzl","Jansen","Kessler","Lindner","Mahler","Niemann","Ostermann","Pichler","Quast","Riedel","Sattler","Tischler","Uhlig","Vollmer",
            "Waldner","Zeller","Achterberg","Behnke","Claasen","Dorn","Eilers","Feddersen","Gosch","Harms","Iwersen","Jessen","Kröger","Lüders","Mommsen",
            "Nissen","Ohlsen","Petersen","Rohwer","Sievers","Thomsen","Ulrichs","Vosgerau","Wichmann","Asmussen","Brodersen","Carstens","Detlefsen","Ehlers"
        };

        internal static bool Busy;
        private static readonly System.Random rng = new System.Random();

        public static IEnumerable<string> UsedNames()
        {
            var c = Util.Career;
            if (c?.Characters == null) return Enumerable.Empty<string>();
            return c.Characters.Where(d => d != null && !string.IsNullOrEmpty(d.Name)).Select(d => d.Name);
        }

        public static string LastName(string full) { var i = full == null ? -1 : full.LastIndexOf(' '); return i < 0 ? full : full.Substring(i + 1); }

        // Picks a name whose last name is not used yet; if every last name is taken, a full name nobody has.
        public static string Pick(System.Func<string> gameRoll, string current, string skip = null)
        {
            var used = UsedNames().Where(n => !ReferenceEquals(n, skip)).ToList();
            var lastUsed = new HashSet<string>(used.Select(LastName));
            var fullUsed = new HashSet<string>(used);
            if (current != null && !lastUsed.Contains(LastName(current))) return current;
            string fallback = null;
            for (int i = 0; i < 200; i++)
            {
                string n = (i % 2 == 0 && gameRoll != null) ? gameRoll() : First[rng.Next(First.Length)] + " " + Last[rng.Next(Last.Length)];
                if (string.IsNullOrEmpty(n)) continue;
                if (!lastUsed.Contains(LastName(n))) return n;
                if (fallback == null && !fullUsed.Contains(n)) fallback = n;
            }
            return fallback ?? current;
        }

        public override void Tick()
        {
            var o = Settings.Get("RenameDuplicateNames");
            if (o == null || !o.On) return;
            o.Value = 0;
            Util.Safe("RenameDuplicates", () =>
            {
                var c = Util.Career;
                if (c?.Characters == null) return;
                var seen = new HashSet<string>();
                int n = 0;
                foreach (var d in c.Characters)
                {
                    if (d == null || string.IsNullOrEmpty(d.Name)) continue;
                    if (seen.Add(LastName(d.Name))) continue;
                    var name = Pick(null, null, d.Name);
                    if (name == null) continue;
                    d.Name = name;
                    seen.Add(LastName(name));
                    n++;
                }
                ToolkitRunner.ShowNotice(n > 0 ? n + " Männer umbenannt, jetzt hat jeder einen eigenen Nachnamen" : "Keine doppelten Namen gefunden", 5f);
            });
        }
    }

    [HarmonyPatch]
    internal static class UniqueNames_Patch
    {
        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(CharacterGenerator), "GenerateCharacterNameByCountry", new[] { typeof(string) });
            yield return AccessTools.Method(typeof(CharacterGenerator), "GenerateCharacterNameByType", new[] { typeof(string) });
        }

        private static void Postfix(CharacterGenerator __instance, System.Reflection.MethodBase __originalMethod, string __0, ref string __result)
        {
            if (UniqueNames.Busy || __result == null || !Settings.B("UniqueCrewNames")) return;
            try
            {
                UniqueNames.Busy = true;
                var inst = __instance; var arg = __0; var m = __originalMethod;
                __result = UniqueNames.Pick(() => m.Invoke(inst, new object[] { arg }) as string, __result);
            }
            catch (System.Exception e) { Util.Log("Namen: " + e.Message); }
            finally { UniqueNames.Busy = false; }
        }
    }
}
