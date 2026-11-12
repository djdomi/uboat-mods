// Crew-KI (Teil des UBOAT Toolkit): Verletzte und geteilte Kojen.
// Das Spiel legt Verletzte immer auf den ersten Platz einer Koje (Slots[0]) und prüft dafür nur den
// ERSTEN Mann in der Reservierungsliste der Koje. Bei geteilten Kojen ist das nicht immer der Mann auf
// dem ersten Platz: dann hielt das Spiel eine Koje für frei, auf der schon ein Verletzter liegt, und der
// Träger blieb mit dem nächsten Verletzten hängen.
// Fix: bei geteilten Kojen steht der Mann auf dem ersten Platz immer vorne in der Liste. So sieht das
// Spiel genau das, was es ohne Teilen sehen würde: liegt dort ein Verletzter, ist die Koje belegt;
// schläft dort ein gesunder Mann, wird er wie im Spiel für den Verletzten geweckt.
// Verletzte bekommen nie einen Zusatzplatz, nur den echten ersten Platz.
using System;
using System.Collections.Generic;
using HarmonyLib;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Interactions;
using UBOAT.Game.Scene.Items;

namespace UboatToolkit.Modules.CrewAi
{
    [HarmonyPatch(typeof(Interactable), "OnSlotReservationChanged")]
    internal static class Patch_BedSharingWoundedOrder
    {
        private static int _logLines;

        static void Postfix(Interactable __instance, List<InteractiveCharacter> ___reservingWorkers)
        {
            try
            {
                if (!(__instance is Bed) || ___reservingWorkers == null || ___reservingWorkers.Count < 2) return;
                if (!BedSharing.IsShared(__instance)) return;
                var slots = __instance.Slots;
                if (slots == null || slots.Length == 0 || slots[0] == null) return;
                var first = slots[0].ReservingCharacter;
                if (first == null) return;
                int i = ___reservingWorkers.IndexOf(first);
                if (i <= 0) return;
                ___reservingWorkers.RemoveAt(i);
                ___reservingWorkers.Insert(0, first);
                if (_logLines < 20)
                {
                    _logLines++;
                    DiagLog.Write("[BedSharing] '" + __instance.name + "': " + DiagLog.NameOf(first) +
                                  " (first place" + (first.Injury ? ", injured" : "") + ") moved to the front of the reservation list");
                }
            }
            catch (Exception e) { DiagLog.WriteError("BedSharingWoundedOrder", e); }
        }
    }
}
