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

namespace UboatToolkit.Modules
{
    // Disk cache cleanup, once per game start: old cached data sheets (.dat, the game rebuilds them when needed),
    // old crash reports and old mod logs older than X days are deleted. Save games are never touched.
    public class CacheCleaner : ModuleBase
    {
        private bool done;

        public override void Tick()
        {
            if (done || !Settings.B("AutoCleanCache")) return;
            done = true;
            Util.Safe("CacheCleaner", () =>
            {
                var limit = System.DateTime.Now.AddDays(-Mathf.Max(1f, Settings.F("AutoCleanDays")));
                string root = Application.persistentDataPath;
                int n = 0; long bytes = 0;
                n += Clean(System.IO.Path.Combine(root, "Data Sheets"), "*.dat", limit, ref bytes);
                n += Clean(System.IO.Path.Combine(Settings.Folder, "logs"), "*.*", limit, ref bytes, keep: "kriegstagebuch.txt");
                n += Clean(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Deep Water Studio", "UBOAT", "Crashes"), "*.*", limit, ref bytes);
                if (n > 0) Util.Log("Cache aufgeräumt: " + n + " Dateien, " + (bytes / 1048576) + " MB");
            });
        }

        private static int Clean(string dir, string pattern, System.DateTime limit, ref long bytes, string keep = null)
        {
            if (!System.IO.Directory.Exists(dir)) return 0;
            int n = 0;
            foreach (var f in System.IO.Directory.GetFiles(dir, pattern, System.IO.SearchOption.AllDirectories))
            {
                try
                {
                    var fi = new System.IO.FileInfo(f);
                    if (keep != null && fi.Name == keep) continue;
                    if (fi.LastWriteTime > limit) continue;
                    bytes += fi.Length;
                    fi.Delete();
                    n++;
                }
                catch { }
            }
            return n;
        }
    }
}
