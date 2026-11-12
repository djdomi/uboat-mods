using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UBOAT.Game.Core.Serialization;
using UBOAT.Game.UI;

namespace UboatToolkit.Modules
{
    // Faster loading: the loader may work longer per frame (fewer drawn frames, more loading).
    [HarmonyPatch]
    internal static class SaveTools_LoadSpeedPatch
    {
        private static System.Reflection.MethodBase TargetMethod() =>
            AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(SavesManager), "LoadCoroutine"));

        public static long Budget() => Settings.B("FastLoad") ? (long)(100.0 * Stopwatch.Frequency / 1000.0) : 160000L;
        public static int MaxActions() => Settings.B("FastLoad") ? 5000 : 500;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            var list = code.ToList();
            int b = -1, m = -1, nb = 0, nm = 0;
            for (int i = 1; i < list.Count - 1; i++)
            {
                var c = list[i];
                if (c.opcode != OpCodes.Ldc_I4 && c.opcode != OpCodes.Ldc_I4_S) continue;
                int v = c.operand is int x ? x : c.operand is sbyte s ? s : -1;
                if (v == 160000 && list[i + 1].opcode == OpCodes.Conv_I8) { b = i; nb++; }
                if (v == 500) { m = i; nm++; }
            }
            if (nb != 1 || nm != 1) return list;   // game changed: leave untouched
            list[b + 1].opcode = OpCodes.Nop; list[b + 1].operand = null;
            list[b].opcode = OpCodes.Call; list[b].operand = AccessTools.Method(typeof(SaveTools_LoadSpeedPatch), "Budget");
            list[m].opcode = OpCodes.Call; list[m].operand = AccessTools.Method(typeof(SaveTools_LoadSpeedPatch), "MaxActions");
            return list;
        }
    }

    // Load/save list: saves of the same commander (career) stand together, newest career first.
    [HarmonyPatch(typeof(GameStateBaseUI), "FetchSaves")]
    internal static class SaveTools_GroupPatch
    {
        private static readonly Dictionary<string, KeyValuePair<long, string>> cache = new Dictionary<string, KeyValuePair<long, string>>();

        private static string Career(SavesManager.GameStateData d)
        {
            long t = d.WriteDate.Ticks;
            if (cache.TryGetValue(d.Path, out var kv) && kv.Key == t) return kv.Value;
            string who = "?";
            try { var x = d.ReadExtendedData(); who = (x?.SkipperName ?? "?") + "|" + (x?.ShipName ?? ""); } catch { }
            cache[d.Path] = new KeyValuePair<long, string>(t, who);
            return who;
        }

        private static void Postfix(ref SavesManager.GameStateData[] ___saves, ref GameStateItemUI[] ___items)
        {
            if (!Settings.B("SaveGroupCareer") || ___saves == null || ___items == null || ___saves.Length != ___items.Length || ___saves.Length < 2) return;
            var saves = ___saves; var items = ___items;
            Util.Safe("SaveTools.Group", () =>
            {
                var idx = Enumerable.Range(0, saves.Length).ToList();
                var key = idx.ToDictionary(i => i, i => Career(saves[i]));
                var first = new Dictionary<string, int>();
                foreach (var i in idx) if (!first.ContainsKey(key[i])) first[key[i]] = i;   // list is newest first
                var order = idx.OrderBy(i => first[key[i]]).ThenBy(i => i).ToList();
                var s2 = order.Select(i => saves[i]).ToArray();
                var it2 = order.Select(i => items[i]).ToArray();
                for (int i = 0; i < it2.Length; i++) if (it2[i] != null) it2[i].transform.SetSiblingIndex(i);
                Array.Copy(s2, saves, s2.Length);
                Array.Copy(it2, items, it2.Length);
            });
        }
    }
}
