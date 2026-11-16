using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UboatToolkit
{
    // 0.3.69 (DoMe: "Toolkit auf Multithreading umstellen"): all log lines are written by ONE background thread.
    // The game's main thread only puts the text into a list (no disk access, no file open/close per line).
    // Order per file is kept. Written every 0.3 s, and at once when the game quits.
    public static class BgFile
    {
        private static readonly object gate = new object();
        private static List<KeyValuePair<string, string>> queue = new List<KeyValuePair<string, string>>();
        private static System.Threading.Thread thread;
        private static readonly System.Threading.AutoResetEvent wake = new System.Threading.AutoResetEvent(false);
        private static bool hooked;

        public static void Append(string path, string text)
        {
            if (path == null || text == null) return;
            lock (gate)
            {
                queue.Add(new KeyValuePair<string, string>(path, text));
                if (thread == null)
                {
                    thread = new System.Threading.Thread(Loop) { IsBackground = true, Name = "UboatToolkit log" };
                    thread.Start();
                }
            }
            if (!hooked) { hooked = true; try { UnityEngine.Application.quitting += Flush; } catch { } }
        }

        private static void Loop()
        {
            while (true)
            {
                wake.WaitOne(300);
                Flush();
            }
        }

        private static readonly object writeGate = new object();
        public static void Flush()
        {
            List<KeyValuePair<string, string>> batch;
            lock (gate)
            {
                if (queue.Count == 0) return;
                batch = queue;
                queue = new List<KeyValuePair<string, string>>();
            }
            lock (writeGate)
            {
                // group by file, keep order
                var parts = new Dictionary<string, StringBuilder>();
                var order = new List<string>();
                foreach (var kv in batch)
                {
                    if (!parts.TryGetValue(kv.Key, out var sb)) { sb = new StringBuilder(); parts[kv.Key] = sb; order.Add(kv.Key); }
                    sb.Append(kv.Value);
                }
                foreach (var p in order)
                {
                    try { File.AppendAllText(p, parts[p].ToString(), new UTF8Encoding(false)); }
                    catch { /* logging must never break the game */ }
                }
            }
        }
    }
}
