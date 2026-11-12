// Crew-KI (Teil des UBOAT Toolkit): Verletzte und geteilte Kojen.
// Das Spiel legt Verletzte immer auf den ersten Platz einer Koje (Slots[0]) und prüft dafür nur den
// ERSTEN Mann in der Reservierungsliste der Koje. Bei geteilten Kojen ist das nicht immer der Mann auf
// dem ersten Platz: dann hielt das Spiel eine Koje für frei, auf der schon ein Verletzter liegt, und der
// Träger blieb mit dem nächsten Verletzten hängen.
// Fix: bei geteilten Kojen steht der Mann auf dem ersten Platz immer vorne in der Liste. So sieht das
// Spiel genau das, was es ohne Teilen sehen würde: liegt dort ein Verletzter, ist die Koje belegt;
// schläft dort ein gesunder Mann, wird er wie im Spiel für den Verletzten geweckt.
// Verletzte bekommen nie einen Zusatzplatz, nur den echten ersten Platz.
// Platz 1 ist für Verletzte da: bei geteilten Kojen legen sich Gesunde auf Platz 2 bis x, solange dort
// etwas frei ist. Ein Verletzter, der selbst zur Koje geht, nimmt Platz 1, wenn der frei ist.
using System;
using System.Collections.Generic;
using HarmonyLib;
using UBOAT.Game.Core.AI;
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

    [HarmonyPatch(typeof(Interactable), "AppendUseBehaviours",
        new Type[] { typeof(InteractiveCharacter), typeof(IBehavioursContext), typeof(float), typeof(bool),
                     typeof(InteractableSlot), typeof(bool), typeof(SlotUsageFlags) })]
    internal static class Patch_BedSharingFirstPlaceForWounded
    {
        private static int _logLines;

        private static bool Usable(InteractableSlot s, InteractiveCharacter c, SlotUsageFlags flags)
        {
            return s != null && (s.ReservingCharacter == null || s.ReservingCharacter == c) && s.Character == null &&
                   s.isActiveAndEnabled && (!s.IsBelowWater || (c.HasRebreather && (flags & SlotUsageFlags.AllowUnderWaterWithRebreather) != 0) ||
                                            (flags & SlotUsageFlags.AllowUnderWaterAlways) != 0);
        }

        static bool Prefix(Interactable __instance, InteractableSlot[] ___crewWorkSlots, InteractiveCharacter character,
                           IBehavioursContext context, float profit, bool isPlayerOrder, ref InteractableSlot slotToIgnore,
                           bool isSwapping, SlotUsageFlags slotUsageFlags, ref bool __result)
        {
            try
            {
                if (!(__instance is Bed) || character == null || ___crewWorkSlots == null || !BedSharing.IsShared(__instance)) return true;
                var slots = __instance.Slots;
                if (slots == null || slots.Length == 0) return true;
                var first = slots[0];
                if (first == null || Array.IndexOf(___crewWorkSlots, first) < 0 || first == slotToIgnore) return true;

                if (character.Injury)
                {
                    // Wounded: take place 1 when it is free (same checks the game makes for any place).
                    if (!Usable(first, character, slotUsageFlags)) return true;
                    if (__instance.ReservingCharacters.Count >= __instance.MaxCharacters && !__instance.ReservingCharacters.Contains(character)) return true;
                    __result = first.AppendUseBehaviours(character, context, profit, isPlayerOrder, isSwapping, slotUsageFlags);
                    Note(__instance, character, "injured -> place 1");
                    return false;
                }

                // Healthy: leave place 1 free while one of places 2..x is free.
                if (slotToIgnore != null) return true;
                for (int i = 0; i < ___crewWorkSlots.Length; i++)
                {
                    var s = ___crewWorkSlots[i];
                    if (s != first && Usable(s, character, slotUsageFlags))
                    {
                        slotToIgnore = first;
                        Note(__instance, character, "healthy -> place 2..x");
                        return true;
                    }
                }
            }
            catch (Exception e) { DiagLog.WriteError("BedSharingFirstPlace", e); }
            return true;
        }

        private static void Note(Interactable bed, InteractiveCharacter c, string what)
        {
            if (_logLines >= 30) return;
            _logLines++;
            DiagLog.Write("[BedSharing] '" + bed.name + "': " + DiagLog.NameOf(c) + " " + what);
        }
    }
}
