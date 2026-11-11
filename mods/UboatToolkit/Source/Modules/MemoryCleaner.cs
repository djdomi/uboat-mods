using UnityEngine;

namespace UboatToolkit.Modules
{
    // Memory cleanup against the game's slowly growing RAM use: every X minutes the mod frees
    // unused graphics/sound data and runs the .NET garbage collector. This causes one short hitch,
    // so it waits for a calm moment (game paused or time compression on).
    public class MemoryCleaner : ModuleBase
    {
        private float due = -1f;

        public override void Tick()
        {
            float min = Settings.F("MemoryCleanMinutes");
            if (min <= 0f) { due = -1f; return; }
            if (due < 0f) { due = Time.unscaledTime + min * 60f; return; }
            if (Time.unscaledTime < due) return;
            bool calm = Time.timeScale == 0f || (Game.TimeCompression?.TimeCompression ?? false);
            if (!calm && Time.unscaledTime < due + 300f) return; // wait max. 5 min for a calm moment
            due = Time.unscaledTime + min * 60f;
            long before = System.GC.GetTotalMemory(false);
            Resources.UnloadUnusedAssets();
            System.GC.Collect();
            long after = System.GC.GetTotalMemory(false);
            Util.Log("Speicher aufgeräumt: " + ((before - after) / 1048576) + " MB frei");
        }
    }
}
