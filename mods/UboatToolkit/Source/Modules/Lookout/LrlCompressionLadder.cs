// Ausguck (Teil des UBOAT Toolkit): LrlCompressionLadder. Schalter: F10 > QoL > "Ausguck".
using System;
using System.Reflection;
using HarmonyLib;

namespace UboatToolkit.Modules.Lrl
{
    internal static class LrlCompressionLadder
    {
        private const int RungCount = 5;

        private static bool _resolved;
        private static string _whyNot = "not resolved yet";
        private static readonly FieldInfo[] RungFields = new FieldInfo[RungCount];
        private static MethodInfo _toFloat;

        private static readonly float[] Rungs = new float[RungCount];

        private static void Resolve()
        {
            if (_resolved)
                return;

            _resolved = true;

            try
            {
                var type = AccessTools.TypeByName("UBOAT.Game.Core.Time.TimeCompressionController");
                if (type == null)
                {
                    _whyNot = "TimeCompressionController type not found";
                    return;
                }

                for (var i = 0; i < RungCount; i++)
                {
                    RungFields[i] = AccessTools.Field(type, "timeCompression" + (i + 1));
                    if (RungFields[i] == null)
                    {
                        _whyNot = "timeCompression" + (i + 1) + " field not found";
                        return;
                    }
                }

                var refType = RungFields[0].FieldType;
                _toFloat = null;
                foreach (var m in refType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name != "op_Implicit" || m.ReturnType != typeof(float))
                        continue;
                    var ps = m.GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType == refType)
                    {
                        _toFloat = m;
                        break;
                    }
                }
                if (_toFloat == null)
                {
                    _whyNot = "GameDataReference.op_Implicit(float) not found";
                    return;
                }

                _whyNot = null;
            }
            catch (Exception ex)
            {
                _whyNot = "resolve threw: " + ex.GetType().Name;
            }
        }

        internal static bool TryRead(out float[] rungs)
        {
            rungs = Rungs;

            Resolve();
            if (_whyNot != null)
                return false;

            try
            {
                for (var i = 0; i < RungCount; i++)
                {
                    var reference = RungFields[i].GetValue(null);
                    if (reference == null)
                    {
                        _whyNot = "timeCompression" + (i + 1) + " is null";
                        return false;
                    }

                    Rungs[i] = (float)_toFloat.Invoke(null, new[] { reference });
                }

                return true;
            }
            catch (Exception ex)
            {
                _whyNot = "read threw: " + ex.GetType().Name;
                return false;
            }
        }

        internal static string WhyNot { get { return _whyNot; } }

        internal static float Top
        {
            get
            {
                float[] rungs;
                if (!TryRead(out rungs))
                    return float.NaN;

                var top = rungs[0];
                for (var i = 1; i < RungCount; i++)
                    if (rungs[i] > top)
                        top = rungs[i];

                return top;
            }
        }

        internal static float EffectiveCompression(float cap)
        {
            float[] rungs;
            if (!TryRead(out rungs))
                return float.NaN;

            var best = 1f;
            for (var i = 0; i < RungCount; i++)
                if (rungs[i] <= cap && rungs[i] > best)
                    best = rungs[i];

            return best;
        }
    }
}
