// Crew-KI (Teil des UBOAT Toolkit): CrewAIPatching. Schalter: F10 > QoL > "Crew-KI".
using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace UboatToolkit.Modules.CrewAi
{
    internal static class CrewAIPatching
    {
        internal static int PatchOwnClasses(Harmony harmony, out int failed)
        {
            failed = 0;
            int classes = 0;
            string ns = typeof(CrewAIPatching).Namespace;
            Type[] types;
            try { types = typeof(CrewAIPatching).Assembly.GetTypes(); }
            catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types; }
            var mine = new List<Type>();
            foreach (Type t in types)
            {
                if (t == null || t.Namespace != ns) continue;
                if (t.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
                mine.Add(t);
            }
            mine.Sort(delegate (Type a, Type b) { return string.CompareOrdinal(a.FullName, b.FullName); });
            foreach (Type t in mine)
            {
                try
                {
                    harmony.CreateClassProcessor(t).Patch();
                    classes++;
                }
                catch (Exception e)
                {
                    failed++;
                    Debug.LogWarning("[UboatToolkit Crew-KI] konnte " + t.Name + " nicht patchen, dieser Teil bleibt wie im Spiel: " + e.Message);
                    try { DiagLog.Write("ERROR patching " + t.Name + ": " + e); } catch { }
                }
            }
            return classes;
        }
    }
}
