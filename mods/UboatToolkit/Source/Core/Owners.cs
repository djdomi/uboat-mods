using System.Collections.Generic;
using UBOAT.Game.Scene.Entities;
using UnityEngine;

namespace UboatToolkit
{
    // 0.4.28 (DoMe 2026-11-18: "ein single global owner"): every game value that several Toolkit parts want to set has
    // exactly ONE place that writes it. The parts only say what they want; this place decides by a fixed order and
    // writes into the log who decides now and who was overruled.
    //
    // First value: the ordered depth of our boat. Order: 3 = your own order (keys, depth gauge),
    // 2 = safety (snorkel head, crash dive under fire), 1 = automatic (ground-clearance autopilot).
    // After an order of a higher rank, lower ranks are refused for a while (your order 20 s, safety 10 s).
    internal static class Owners
    {
        public const int Player = 3, Safety = 2, Auto = 1;

        // Second value: propeller power of OUR boat. Two parts wanted to write it (speed cheat and Warthog speed factor) and
        // overwrote each other every second. Now both only state their factor here; Navigation writes game value x both.
        public static float WarthogSpeed = 1f;
        private static float lastSpeedLog = -1f;
        public static float PropellerFactor(float cheat)
        {
            float w = Mathf.Clamp(WarthogSpeed, 0.5f, 2f), f = Mathf.Max(1f, cheat) * w;
            if (Mathf.Abs(f - lastSpeedLog) > 0.001f)
            {
                lastSpeedLog = f;
                Util.Log("Schraubenkraft (nur eigenes Boot): Spielwert x " + (f * f).ToString("0.##") + " (Tempo-Cheat x" + Mathf.Max(1f, cheat).ToString("0.##") + ", Warthog-Tempo x" + w.ToString("0.##") + ")");
            }
            return f * f;
        }
        private static int rankNow; private static float until; private static string whoNow = "";
        private static readonly Dictionary<string, float> told = new Dictionary<string, float>();

        private static bool Allow(string who, int rank, string what)
        {
            float now = Time.unscaledTime;
            if (now < until && rank < rankNow && who != whoNow)
            {
                float last;
                if (!told.TryGetValue(who, out last) || now - last > 10f)
                {
                    told[who] = now;
                    Util.Log("Tiefe: " + who + " will " + what + ", überstimmt von " + whoNow + " (noch " + (until - now).ToString("0") + " s)");
                }
                return false;
            }
            if (who != whoNow) Util.Log("Tiefe: bestimmt jetzt " + who + " (" + what + ")" + (whoNow != "" && now < until ? ", vorher " + whoNow : ""));
            whoNow = who; rankNow = rank;
            until = now + (rank >= Player ? 20f : rank == Safety ? 10f : 0f);
            return true;
        }

        // Ordered depth in metres (deck line, like the game's depth meter). Returns false when overruled.
        public static bool Depth(PlayerShip ship, string who, int rank, float depth, bool silent, bool allowAlarm)
        {
            if (ship == null) return false;
            float t = Mathf.Max(0f, depth);
            if (!Allow(who, rank, "Zieltiefe " + t.ToString("0.0") + " m")) return false;
            ship.SetDepthPreset(DepthPreset.None, true);
            ship.SetTargetDepth(t, silent, allowAlarm);
            return true;
        }

        public static bool Preset(PlayerShip ship, string who, int rank, DepthPreset preset, bool silent)
        {
            if (ship == null) return false;
            if (!Allow(who, rank, "Tiefe " + preset)) return false;
            ship.SetDepthPreset(preset, silent);
            return true;
        }
    }
}
