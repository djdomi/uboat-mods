// Crew-KI (Teil des UBOAT Toolkit): CrewAIAlarmBehaviour. Schalter: F10 > QoL > "Crew-KI".
using System;
using System.Reflection;
using HarmonyLib;
using UBOAT.Game.Core.AI.Behaviours;
using UBOAT.Game.Scene.Entities;

namespace UboatToolkit.Modules.CrewAi
{
    internal static class AlarmToggles
    {
        public static bool NoEatingDuringAlarm => UboatToolkit.Settings.B("CrewNoEatAlarm");

        public const float CriticalHunger = 0.85f;

        public static readonly bool NoSleepingDuringAlarm = false;

        public static readonly bool LogSuppression = true;
    }

    [HarmonyPatch(typeof(EatBehaviour), "OnBehaviourBackgroundUpdate")]
    internal static class Patch_Alarm_NoEating
    {
        static readonly FieldInfo ShipField = AccessTools.Field(typeof(EatBehaviour), "playerShip");
        static readonly FieldInfo HungerField = AccessTools.Field(typeof(EatBehaviour), "hunger");
        static readonly FieldInfo CharField = AccessTools.Field(typeof(EatBehaviour), "character");

        static bool _loggedOnce;

        static void Postfix(EatBehaviour __instance)
        {
            if (!AlarmToggles.NoEatingDuringAlarm) return;
            try
            {
                if (__instance == null || __instance.Profit <= 0f) return;

                bool alarmed = false;
                if (ShipField != null)
                {
                    var ship = ShipField.GetValue(__instance) as PlayerShip;
                    if (ship == null) return;
                    try { alarmed = ship.Alarmed; } catch { return; }
                }
                if (!alarmed) return;

                float hunger = 0f;
                if (HungerField != null)
                {
                    var need = HungerField.GetValue(__instance);
                    if (need != null)
                    {
                        try
                        {
                            var p = AccessTools.Property(need.GetType(), "BiasedStress");
                            if (p != null) hunger = (float)p.GetValue(need, null);
                        }
                        catch { }
                    }
                }
                if (hunger >= AlarmToggles.CriticalHunger) return;

                __instance.Profit = 0f;
                OptCounters.EatSuppressedOnAlarm++;

                if (AlarmToggles.LogSuppression && !_loggedOnce)
                {
                    _loggedOnce = true;
                    string who = "?";
                    try
                    {
                        var c = CharField != null
                            ? CharField.GetValue(__instance) as UBOAT.Game.Scene.Characters.PlayableCharacter
                            : null;
                        if (c != null) who = c.Name ?? "?";
                    }
                    catch { }
                    DiagLog.Write("[Alarm] suppressing EatBehaviour while alarmed (first: " + who +
                                  ", hunger=" + hunger.ToString("F2") +
                                  ", critical=" + AlarmToggles.CriticalHunger +
                                  ") - vanilla EatBehaviour has no alarm gate at all");
                }
            }
            catch (Exception e)
            {
                DiagLog.WriteError("Alarm.NoEating", e);
            }
        }
    }
}
