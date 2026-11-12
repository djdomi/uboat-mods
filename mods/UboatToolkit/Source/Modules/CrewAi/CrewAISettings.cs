// Crew-KI (Teil des UBOAT Toolkit): Schalter-Tabelle.
// Alle Schalter stehen in F10 > QoL (Crew-KI) und in der UboatToolkit.ini.
// Jeder Schalter wird in dem Moment gelesen, in dem er gebraucht wird (wirkt sofort).
// Ausnahmen: der Hauptschalter "CrewAIOverhaul" und "CrewAILog" wirken erst beim nächsten Spielstart.
using System;
using System.Text;

namespace UboatToolkit.Modules.CrewAi
{
    internal sealed class SettingRow
    {
        public string Key, ToolkitKey;
        public bool IsInt;
        public int Min, Max;
    }

    internal static class CrewAISettings
    {
        private static SettingRow B(string key, string tk) { return new SettingRow { Key = key, ToolkitKey = tk }; }
        private static SettingRow I(string key, string tk, int min, int max) { return new SettingRow { Key = key, ToolkitKey = tk, IsInt = true, Min = min, Max = max }; }

        // Key = the original key (used for the one-time import), ToolkitKey = our INI key.
        public static readonly SettingRow[] Rows =
        {
            B("RestInBunks", "CrewRestInBunks"),
            B("NearestBunk", "CrewNearestBed"),
            B("BedSharing", "CrewBedSharing"),
            B("BedYield", "CrewBedYield"),
            B("BowDrillLimit", "CrewBowRunCap"),
            I("BowDrillMen", "CrewBowRunMen", 0, 30),
            B("SkipperAttackPeriscope", "CrewSkipperAttackScope"),
            B("NoEatingInAlarm", "CrewNoEatAlarm"),
            B("ControlRoomDiscipline", "CrewControlRoom"),
            B("NearbyJobs", "CrewNearbyJobs"),
            B("WatchClaims", "CrewWatchClaims"),
            B("KeepRunningJobs", "CrewKeepJobs"),
            B("OrderRoom", "CrewOrderRoom"),
            B("HatchOrders", "CrewHatchOrders"),
            B("StuckCooldown", "CrewStuckCooldown"),
            B("VerboseLog", "CrewAILog"),
        };

        public static string Status { get; private set; }

        public static void Load()
        {
            var notes = new StringBuilder();
            try
            {
                notes.Append(CrewAIMigration.RunOnce(Rows));
                notes.Append("Werte aus UboatToolkit.ini");
            }
            catch (Exception e) { notes.Append("Einstellungen lesen fehlgeschlagen: " + e.GetType().Name + ": " + e.Message); }
            Status = notes.ToString();
        }

        public static string Summary()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Rows.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                var o = UboatToolkit.Settings.Get(Rows[i].ToolkitKey);
                sb.Append(Rows[i].ToolkitKey).Append('=');
                if (o == null) sb.Append('?');
                else if (Rows[i].IsInt) sb.Append(Convert.ToInt32(o.Value));
                else sb.Append(o.On);
            }
            return sb.ToString();
        }
    }
}
