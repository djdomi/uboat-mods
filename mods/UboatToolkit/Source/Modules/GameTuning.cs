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
