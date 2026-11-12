// Ausguck (Teil des UBOAT Toolkit): LrlSettings. Schalter: F10 > QoL > "Ausguck".
namespace UboatToolkit.Modules.Lrl
{
    internal static class LrlSettings
    {
        private static bool _offEnabled;
        internal static bool Enabled { get { return UboatToolkit.Settings.B("Lookout") && !_offEnabled; } set { _offEnabled = !value; } }

        internal static float GroupSightRangeMultiplier { get { float v = UboatToolkit.Settings.F("LookoutGroupRange"); return v > 0f ? v : 1.6923f; } }

        internal static float LegacyMultiplier = 1.6923f;

        private static bool _offCancelWeatherSquaring;
        internal static bool CancelWeatherSquaring { get { return UboatToolkit.Settings.B("LookoutWeatherOnce") && !_offCancelWeatherSquaring; } set { _offCancelWeatherSquaring = !value; } }

        internal static float MaxWeatherCompensation = 2f;

        internal static float VisibilityFloor = 0.40f;

        internal static float VisibilityFloorSoftness = 0.5f;

        private static bool _offStrengthenFog;
        internal static bool StrengthenFog { get { return UboatToolkit.Settings.B("LookoutFog") && !_offStrengthenFog; } set { _offStrengthenFog = !value; } }

        internal static float FogExtraExponent = 1f;

        private static bool _offTimeCompressionGuard;
        internal static bool TimeCompressionGuard { get { return UboatToolkit.Settings.B("LookoutTimeGuard") && !_offTimeCompressionGuard; } set { _offTimeCompressionGuard = !value; } }

        internal static float TimeCompressionCap = 150f;

        internal static float GuardRadiusFactor = 1.0f;

        internal static bool GuardIgnoresAllies = true;

        private static bool _offReportDarkContacts;
        internal static bool ReportDarkContacts { get { return UboatToolkit.Settings.B("LookoutDarkReports") && !_offReportDarkContacts; } set { _offReportDarkContacts = !value; } }

        private static bool _offFixInvertedAcceptance;
        internal static bool FixInvertedAcceptance { get { return UboatToolkit.Settings.B("LookoutFixRoll") && !_offFixInvertedAcceptance; } set { _offFixInvertedAcceptance = !value; } }

        private static bool _offCalloutKnownContacts;
        internal static bool CalloutKnownContacts { get { return UboatToolkit.Settings.B("LookoutKnownAgain") && !_offCalloutKnownContacts; } set { _offCalloutKnownContacts = !value; } }

        private static bool _offFloorHiddenForGroupDetection;
        internal static bool FloorHiddenForGroupDetection { get { return UboatToolkit.Settings.B("LookoutAwashView") && !_offFloorHiddenForGroupDetection; } set { _offFloorHiddenForGroupDetection = !value; } }

        internal static float HiddenFloor = 0.85f;

        private static bool _offRetryDroppedCallouts;
        internal static bool RetryDroppedCallouts { get { return UboatToolkit.Settings.B("LookoutRetry") && !_offRetryDroppedCallouts; } set { _offRetryDroppedCallouts = !value; } }

        internal static float RetryIntervalSeconds = 4f;

        internal static float RetryWindowSeconds = 90f;

        private static bool _offSmokeHeightEnabled;
        internal static bool SmokeHeightEnabled { get { return UboatToolkit.Settings.B("LookoutSmoke") && !_offSmokeHeightEnabled; } set { _offSmokeHeightEnabled = !value; } }

        internal static float MinimumSmokingSpeed = 2.0f;

        internal static float SpeedSlow = 6.0f;
        internal static float SpeedNormal = 12.0f;
        internal static float SpeedFast = 18.0f;

        internal static float SmokeBarelyMoving = 6f;
        internal static float SmokeSlow = 10f;
        internal static float SmokeNormal = 18f;
        internal static float SmokeFast = 28f;

        internal static float SmokePerExtraShip = 2f;

        internal static float SmokeCrippledBonus = 8f;

        internal static float SmokeGroupCap = 40f;

        internal static float SmokeCacheSeconds = 2f;

        private static bool _offDaylightTrimEnabled;
        internal static bool DaylightTrimEnabled { get { return UboatToolkit.Settings.B("LookoutDaylightTrim") && !_offDaylightTrimEnabled; } set { _offDaylightTrimEnabled = !value; } }

        internal static float DaylightTrimStartVisibility = 0.70f;

        internal static float DaylightTrimFullVisibility = 0.90f;

        internal static float DaylightRangeScale = 0.93f;

        internal static float DaylightSmokeScale = 0.60f;

        private static bool _offConvoyNightBonusEnabled;
        internal static bool ConvoyNightBonusEnabled { get { return UboatToolkit.Settings.B("LookoutConvoyNight") && !_offConvoyNightBonusEnabled; } set { _offConvoyNightBonusEnabled = !value; } }

        internal static float ConvoyNightBonusPerExtraShip = 0.03f;

        internal static float ConvoyNightBonusMax = 1.5f;

        internal static float ConvoyNightBonusFullVisibility = 0.20f;

        internal static float ConvoyNightBonusEndVisibility = 0.40f;

        internal static bool DebugLogging { get { return UboatToolkit.Settings.B("LookoutLog"); } }

        internal static float DebugInterval = 15f;

        private static bool _offWeatherFloorEnabled;
        internal static bool WeatherFloorEnabled { get { return UboatToolkit.Settings.B("LookoutWeatherFloor") && !_offWeatherFloorEnabled; } set { _offWeatherFloorEnabled = !value; } }

        internal static void Load()
        {
            _offTimeCompressionGuard = false;
        }

        internal static string Key(string field) { return LrlInstall.ModuleId + "." + field; }
    }
}
