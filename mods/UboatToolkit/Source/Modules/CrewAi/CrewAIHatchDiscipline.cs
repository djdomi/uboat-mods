// Crew-KI (Teil des UBOAT Toolkit): CrewAIHatchDiscipline. Schalter: F10 > QoL > "Crew-KI".
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UBOAT.Game.Scene.Characters.Actions;
using UBOAT.Game.Scene.Items;

namespace UboatToolkit.Modules.CrewAi
{
    internal static class HatchToggles
    {
        public static bool KeepCommandersHatchOrder => UboatToolkit.Settings.B("CrewHatchOrders");

        public static readonly bool LogPreservation = true;

        public static readonly bool ExcludeExteriorHatches = false;
    }

    [HarmonyPatch(typeof(HatchOpenCloseAction), "OnDeviceUsed")]
    internal static class Patch_Hatch_OnDeviceUsed
    {
        private static readonly HashSet<int> _logged = new HashSet<int>();
        private static readonly object _lock = new object();

        private static Hatch Resolve(HatchOpenCloseAction action)
        {
            try
            {
                var act = action != null ? action.Activator : null;
                return act != null ? act.GetComponent<Hatch>() : null;
            }
            catch { return null; }
        }

        static void Prefix(HatchOpenCloseAction __instance, out bool[] __state)
        {
            __state = null;
            if (!HatchToggles.KeepCommandersHatchOrder) return;
            try
            {
                var h = Resolve(__instance);
                if (h == null) return;
                __state = new[] { h.Locked, h.IsPreferredToBeLocked };
            }
            catch (Exception e) { DiagLog.WriteError("Hatch.Prefix", e); }
        }

        static void Postfix(HatchOpenCloseAction __instance, bool[] __state)
        {
            if (!HatchToggles.KeepCommandersHatchOrder || __state == null) return;
            try
            {
                var h = Resolve(__instance);
                if (h == null) return;

                bool wasLocked = __state[0];
                bool orderWasKeepShut = __state[1];

                if (!wasLocked || h.Locked || !orderWasKeepShut) return;

                h.IsPreferredToBeLocked = true;
                OptCounters.HatchOrdersPreserved++;

                if (HatchToggles.LogPreservation)
                {
                    int id = h.GetInstanceID();
                    bool first;
                    lock (_lock) { first = _logged.Add(id); }
                    if (first)
                    {
                        DiagLog.Write("[Hatch] preserved commander's keep-shut order on '" +
                                      SafeName(h) + "' after crew opened it - " +
                                      "HatchCloseController will shut it once clear");
                    }
                }
            }
            catch (Exception e) { DiagLog.WriteError("Hatch.Postfix", e); }
        }

        private static string SafeName(UnityEngine.Object o)
        {
            try { return o != null ? o.name : "null"; } catch { return "?"; }
        }
    }

    [HarmonyPatch]
    internal static class Patch_Hatch_CloseAllOrder
    {
        static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("UBOAT.Game.UI.CharacterSwitchUI.CharacterSwitchCloseHatchesUI");
            return t == null ? null : AccessTools.Method(t, "OnClick");
        }

        static bool Prepare() { return TargetMethod() != null; }

        static void Postfix(object __instance)
        {
            if (!HatchToggles.KeepCommandersHatchOrder) return;
            try
            {
                var t = __instance.GetType();
                var closedField = AccessTools.Field(t, "areHatchesClosed");
                var hatchesField = AccessTools.Field(t, "hatches");
                if (closedField == null || hatchesField == null) return;

                if (!(bool)closedField.GetValue(__instance)) return;

                var list = hatchesField.GetValue(__instance) as IEnumerable;
                if (list == null) return;

                int applied = 0;
                foreach (var o in list)
                {
                    var h = o as Hatch;
                    if (h == null) continue;
                    if (HatchToggles.ExcludeExteriorHatches)
                    {
                        bool ext = false;
                        try { ext = h.IsExteriorHatch; } catch { }
                        if (ext) continue;
                    }
                    if (h.IsPreferredToBeLocked) continue;   // setter early-outs anyway
                    h.IsPreferredToBeLocked = true;
                    applied++;
                }

                if (applied > 0)
                {
                    OptCounters.HatchOrdersApplied += applied;
                    DiagLog.Write("[Hatch] close-all order: set keep-shut on " + applied +
                                  " hatch(es) that vanilla left without one " +
                                  "(it only marks already-locked hatches)");
                }
            }
            catch (Exception e) { DiagLog.WriteError("Hatch.CloseAllOrder", e); }
        }
    }
}
