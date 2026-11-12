// Ausguck (Teil des UBOAT Toolkit): LrlLog. Schalter: F10 > QoL > "Ausguck".
using System;
using System.Collections.Generic;
using UnityEngine;

namespace UboatToolkit.Modules.Lrl
{
    internal static class LrlLog
    {
        private const string Prefix = "[UboatToolkit Ausguck] ";

        private static readonly Dictionary<string, float> LastLogged = new Dictionary<string, float>();

        internal static void Info(string message)
        {
            Debug.Log(Prefix + message);
        }

        internal static void Warn(string message)
        {
            Debug.LogWarning(Prefix + message);
        }

        internal static void Error(string message, Exception ex)
        {
            Debug.LogError(Prefix + message);
            if (ex != null)
                Debug.LogException(ex);
        }

        internal static void Throttled(string key, float seconds, string message)
        {
            if (!ShouldLog(key, seconds))
                return;

            Debug.Log(Prefix + message);
        }

        internal static bool ShouldLog(string key, float seconds)
        {
            var now = Time.unscaledTime;

            float last;
            if (LastLogged.TryGetValue(key, out last) && now - last < seconds)
                return false;

            LastLogged[key] = now;
            return true;
        }
    }
}
