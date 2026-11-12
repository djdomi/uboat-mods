// Ausguck (Teil des UBOAT Toolkit): LrlSandboxGroups. Schalter: F10 > QoL > "Ausguck".
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UBOAT.Game.Sandbox;
using UnityEngine;

namespace UboatToolkit.Modules.Lrl
{
    internal static class LrlSandboxGroups
    {
        private static bool _resolved;
        private static PropertyInfo _instanceProp;
        private static FieldInfo _instanceField;
        private static MethodInfo _getGroups;
        private static MethodInfo _getPublicGroups;
        private static string _whyNot = "not resolved yet";

        internal static int LastTotal { get; private set; }
        internal static int LastPublic { get; private set; }
        internal static string LastError { get; private set; }

        private static void Resolve()
        {
            if (_resolved)
                return;

            _resolved = true;

            try
            {
                var t = typeof(Sandbox);
                _instanceProp = AccessTools.Property(t, "Instance");
                _instanceField = AccessTools.Field(t, "Instance");
                _getGroups = AccessTools.Method(t, "GetGroupsDirect");
                _getPublicGroups = AccessTools.Method(t, "GetPublicGroupsDirect");

                if (_getGroups == null)
                    _whyNot = "Sandbox.GetGroupsDirect not found";
                else if (_instanceProp == null && _instanceField == null)
                    _whyNot = "Sandbox.Instance not found";
                else
                    _whyNot = null;
            }
            catch (Exception ex)
            {
                _whyNot = "resolve threw: " + ex.GetType().Name;
            }
        }

        private static object Instance()
        {
            if (_instanceProp != null)
            {
                var getter = _instanceProp.GetGetMethod(true);
                if (getter != null && getter.IsStatic)
                    return _instanceProp.GetValue(null, null);
            }

            if (_instanceField != null && _instanceField.IsStatic)
                return _instanceField.GetValue(null);

            return null;
        }

        private static void Harvest(MethodInfo method, object instance, Vector2 origin,
                                    float maxKm, List<SandboxGroup> into, ref int counted)
        {
            if (method == null)
                return;

            var result = method.IsStatic
                ? method.Invoke(null, null)
                : method.Invoke(instance, null);

            var list = result as IEnumerable;
            if (list == null)
                return;

            foreach (var item in list)
            {
                var g = item as SandboxGroup;
                if (g == null)
                    continue;

                counted++;

                if ((g.Position - origin).magnitude <= maxKm)
                    into.Add(g);
            }
        }

        internal static bool GetNearest(Vector2 origin, float maxKm, List<SandboxGroup> results)
        {
            results.Clear();
            LastTotal = 0;
            LastPublic = 0;
            LastError = null;

            Resolve();

            if (_whyNot != null)
            {
                LastError = _whyNot;
                return false;
            }

            try
            {
                var instance = Instance();
                if (instance == null)
                {
                    LastError = "no Sandbox instance";
                    return false;
                }

                var total = 0;
                Harvest(_getGroups, instance, origin, maxKm, results, ref total);
                LastTotal = total;

                var pub = 0;
                Harvest(_getPublicGroups, instance, origin, maxKm, results, ref pub);
                LastPublic = pub;
                LastTotal += pub;

                results.Sort(delegate(SandboxGroup a, SandboxGroup b)
                {
                    var da = (a.Position - origin).sqrMagnitude;
                    var db = (b.Position - origin).sqrMagnitude;
                    return da.CompareTo(db);
                });

                return true;
            }
            catch (Exception ex)
            {
                LastError = "read threw: " + ex.GetType().Name;
                results.Clear();
                return false;
            }
        }
    }
}
