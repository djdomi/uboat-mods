// Crew-KI (Teil des UBOAT Toolkit): CrewAIPerf. Schalter: F10 > QoL > "Crew-KI".
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace UboatToolkit.Modules.CrewAi
{
    internal static class PerfToggles
    {
        public static readonly bool Enabled = true;

        public const double SlowCallMs = 15.0;

        public const int MaxSlowLines = 60;

        public const float ReportIntervalSeconds = 60f;
    }

    internal static class Perf
    {
        private sealed class Site
        {
            public long Calls;
            public double TotalMs;
            public double MaxMs;
            public float MaxAtUnscaled;
            public long SlowCalls;
        }

        private static readonly Dictionary<string, Site> _sites = new Dictionary<string, Site>();
        private static readonly object _lock = new object();
        private const double MsPerTick = 0.001;
        private static long Now() { return (long)(Time.realtimeSinceStartupAsDouble * 1000000.0); }

        private static int _slowLines;
        private static float _lastReport;
        private static bool _reportedFirst;

        public static long Begin()
        {
            if (!PerfToggles.Enabled) return 0L;
            return Now();
        }

        public static void End(string site, long startTicks)
        {
            if (!PerfToggles.Enabled || startTicks == 0L) return;
            try
            {
                double ms = (Now() - startTicks) * MsPerTick;
                float now = Time.unscaledTime;

                bool slow;
                lock (_lock)
                {
                    Site s;
                    if (!_sites.TryGetValue(site, out s)) { s = new Site(); _sites[site] = s; }
                    s.Calls++;
                    s.TotalMs += ms;
                    if (ms > s.MaxMs) { s.MaxMs = ms; s.MaxAtUnscaled = now; }
                    slow = ms >= PerfToggles.SlowCallMs;
                    if (slow) s.SlowCalls++;
                }

                if (slow && _slowLines < PerfToggles.MaxSlowLines)
                {
                    _slowLines++;
                    DiagLog.Write("[Perf] SLOW " + site + " took " + ms.ToString("F1") +
                                  " ms in one call (threshold " + PerfToggles.SlowCallMs + " ms)");
                }
            }
            catch { }
        }

        public static void MaybeReport()
        {
            if (!PerfToggles.Enabled) return;
            try
            {
                float now = Time.unscaledTime;
                if (_reportedFirst && now - _lastReport < PerfToggles.ReportIntervalSeconds) return;
                _lastReport = now;
                _reportedFirst = true;

                var sb = new StringBuilder("[Perf] ");
                bool any = false;
                lock (_lock)
                {
                    foreach (var kv in _sites)
                    {
                        var s = kv.Value;
                        if (s.Calls == 0) continue;
                        any = true;
                        sb.Append(kv.Key).Append(' ')
                          .Append("n=").Append(s.Calls).Append(' ')
                          .Append("total=").Append(s.TotalMs.ToString("F1")).Append("ms ")
                          .Append("mean=").Append((s.TotalMs / s.Calls).ToString("F3")).Append("ms ")
                          .Append("max=").Append(s.MaxMs.ToString("F1")).Append("ms@rt")
                          .Append(s.MaxAtUnscaled.ToString("F1"));
                        if (s.SlowCalls > 0) sb.Append(" slow=").Append(s.SlowCalls);
                        sb.Append(" | ");
                    }
                }
                if (any) DiagLog.Write(sb.ToString());
            }
            catch { }
        }
    }
}
