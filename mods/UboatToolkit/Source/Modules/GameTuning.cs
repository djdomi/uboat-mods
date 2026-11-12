using UnityEngine;
using UnityEngine.Scripting;

namespace UboatToolkit.Modules
{
    // Game-wide tuning (not only the mod):
    // - Log filter: the game writes masses of info lines into Player.log (reports of up to 6 GB).
    //   Only warnings and errors are kept -> less disk work and less memory for log text.
    // - GC time slice: the incremental garbage collector may use x ms per frame (0 = game value).
    public class GameTuning : ModuleBase
    {
        private LogType? originalFilter;
        private ulong originalSlice;

        private static readonly UBOAT.Game.Core.Data.GameDataReference maxUnits = new UBOAT.Game.Core.Data.GameDataReference("General/Settings/World/Max Spawned Units Per Group");
        private static readonly UBOAT.Game.Core.Data.GameDataReference maxGroups = new UBOAT.Game.Core.Data.GameDataReference("General/Settings/World/Max Groups Per Battle");
        private float origUnits = -1f, origGroups = -1f;

        // Fewer ships at the same time in 3D = big CPU saving in convoy battles (0 = game value 28 / 4).
        public override void Tick()
        {
            Apply(maxUnits, ref origUnits, Settings.F("MaxShipsPerGroup"));
            Apply(maxGroups, ref origGroups, Settings.F("MaxGroupsPerBattle"));

        }

        private static void Apply(UBOAT.Game.Core.Data.GameDataReference r, ref float orig, float want)
        {
            var p = r.SelectedValue;
            if (p == null) return;
            if (orig < 0f) orig = p.Value;
            float v = want > 0f ? want : orig;
            if (p.Value != v) p.Value = v;
        }

        public override void Frame()
        {
            if (Settings.B("GameLogFilter"))
            {
                if (originalFilter == null) originalFilter = Debug.unityLogger.filterLogType;
                Debug.unityLogger.filterLogType = LogType.Warning;
            }
            else if (originalFilter != null)
            {
                Debug.unityLogger.filterLogType = originalFilter.Value;
                originalFilter = null;
            }

            if (!GarbageCollector.isIncremental) return;
            if (originalSlice == 0) originalSlice = GarbageCollector.incrementalTimeSliceNanoseconds;
            float ms = Settings.F("GcSliceMs");
            ulong want = ms > 0f ? (ulong)(ms * 1000000f) : originalSlice;
            if (GarbageCollector.incrementalTimeSliceNanoseconds != want) GarbageCollector.incrementalTimeSliceNanoseconds = want;
        }
    }
}

namespace UboatToolkit.Modules
{
    // Game's own developer console: only its "enabled" setting is answered with true (idea from the Custom Actions mod).
    // With "achievements always allowed" using it costs no achievements.
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Core.Data.GameDataReference), "GetBoolean", new System.Type[0])]
    internal static class GameTuning_DevConsolePatch
    {
        private static object consoleRef;
        private static void Postfix(UBOAT.Game.Core.Data.GameDataReference __instance, ref bool __result)
        {
            if (__result || !Settings.B("DevConsole")) return;
            if (consoleRef == null)
                consoleRef = HarmonyLib.AccessTools.Field(HarmonyLib.AccessTools.TypeByName("UBOAT.Game.UI.DevConsole.DevConsole"), "enableDeveloperConsole")?.GetValue(null);
            if (ReferenceEquals(__instance, consoleRef)) __result = true;
        }
    }
}
