using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Sandbox.HQ;
using UBOAT.Game.Sandbox.Missions;

namespace UboatToolkit.Modules
{
    // Wolfpack settings: from which date packs form, chance, max boats, first target tonnage.
    // Done by swapping the game's own values in its code (transpiler); untouched if the game changed.
    internal static class WolfpackTuning
    {
        public static readonly string[] FromNames = { "September 1940 (erster Rudel-Einsatz)", "Juli 1940", "November 1940", "Januar 1941", "8. Mai 1941 (Spiel)" };
        private static readonly DateTime[] FromDates = { new DateTime(1940, 9, 1), new DateTime(1940, 7, 1), new DateTime(1940, 11, 1), new DateTime(1941, 1, 1), new DateTime(1941, 5, 8) };
        private static bool On => Settings.B("Wolfpack");

        internal static readonly MethodInfo ToFloat = Conversion("op_Implicit", typeof(float));
        internal static readonly MethodInfo ToInt = Conversion("op_Explicit", typeof(int));
        private static MethodInfo Conversion(string name, Type returns)
        {
            try
            {
                foreach (var m in typeof(GameDataReference).GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name != name || m.ReturnType != returns) continue;
                    var ps = m.GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType == typeof(GameDataReference)) return m;
                }
            }
            catch { }
            return null;
        }

        public static DateTime GateDate(int year, int month, int day)
        {
            if (!On) return new DateTime(year, month, day);
            int i = (int)Math.Round(Settings.F("WolfpackFrom"));
            return FromDates[i < 0 || i >= FromDates.Length ? 0 : i];
        }
        public static float Probability(GameDataReference r) { float v = (float)r; return On ? Math.Max(5f, Math.Min(100f, Settings.F("WolfpackChance"))) / 100f : v; }
        public static float MaxUnits(GameDataReference r) { float v = (float)r; return On ? Math.Max(2f, Math.Min(8f, (float)Math.Round(Settings.F("WolfpackBoats")))) : v; }
        public static int FirstObjectiveGrt(GameDataReference r) { int v = (int)r; return On ? (int)Math.Max(1000f, Math.Min(50000f, Settings.F("WolfpackGrt"))) : v; }

        internal static bool IsCall(CodeInstruction c, MethodInfo m) => m != null && (c.opcode == OpCodes.Call || c.opcode == OpCodes.Callvirt) && c.operand as MethodInfo == m;
        internal static bool IsStaticLoad(CodeInstruction c, string name) => c.opcode == OpCodes.Ldsfld && (c.operand as FieldInfo)?.Name == name;
        internal static bool IsDateCtor(CodeInstruction c)
        {
            var ctor = c.operand as ConstructorInfo;
            if (c.opcode != OpCodes.Newobj || ctor == null || ctor.DeclaringType != typeof(DateTime)) return false;
            var ps = ctor.GetParameters();
            return ps.Length == 3 && ps[0].ParameterType == typeof(int) && ps[1].ParameterType == typeof(int) && ps[2].ParameterType == typeof(int);
        }

        // Replaces the call right after "ldsfld name" with our method (exactly one site, else untouched).
        internal static List<CodeInstruction> SwapAfterField(IEnumerable<CodeInstruction> code, string field, MethodInfo conv, string ours, string where)
        {
            var list = new List<CodeInstruction>(code);
            int site = -1, found = 0;
            for (int i = 0; i + 1 < list.Count; i++)
                if (IsStaticLoad(list[i], field) && IsCall(list[i + 1], conv)) { site = i + 1; found++; }
            if (found != 1) { Util.Log("Rudel: " + where + " Stelle " + field + " " + found + "/1 - nicht geändert (Spiel-Update?)"); return list; }
            list[site].operand = AccessTools.Method(typeof(WolfpackTuning), ours);
            return list;
        }
    }

    [HarmonyPatch(typeof(Headquarters), "ParseReport")]
    internal static class Wolfpack_ParseReportPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            var list = new List<CodeInstruction>(code);
            int dates = 0, probs = 0;
            foreach (var c in list) { if (WolfpackTuning.IsDateCtor(c)) dates++; else if (WolfpackTuning.IsCall(c, WolfpackTuning.ToFloat)) probs++; }
            if (dates != 1 || probs != 1) { Util.Log("Rudel: ParseReport Datum " + dates + "/1, Chance " + probs + "/1 - nicht geändert (Spiel-Update?)"); return list; }
            foreach (var c in list)
            {
                if (WolfpackTuning.IsDateCtor(c)) { c.opcode = OpCodes.Call; c.operand = AccessTools.Method(typeof(WolfpackTuning), "GateDate"); }
                else if (WolfpackTuning.IsCall(c, WolfpackTuning.ToFloat)) c.operand = AccessTools.Method(typeof(WolfpackTuning), "Probability");
            }
            return list;
        }
    }

    [HarmonyPatch(typeof(Headquarters), "StartWolfpack")]
    internal static class Wolfpack_StartPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code) =>
            WolfpackTuning.SwapAfterField(code, "maxUnitsPerWolfpack", WolfpackTuning.ToFloat, "MaxUnits", "StartWolfpack");
    }

    [HarmonyPatch(typeof(WolfpackAttackMission), "OnMissionStarted")]
    internal static class Wolfpack_MissionPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code) =>
            WolfpackTuning.SwapAfterField(code, "wolfpackFirstObjectiveGRT", WolfpackTuning.ToInt, "FirstObjectiveGrt", "OnMissionStarted");
    }
}
