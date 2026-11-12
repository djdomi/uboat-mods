// Crew-KI (Teil des UBOAT Toolkit): SkipperPeriscope. Schalter: F10 > QoL > "Crew-KI".
using System;
using HarmonyLib;
using UBOAT.Game;
using UBOAT.Game.Scene;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Characters.Actions;
using Activator = UBOAT.Game.Scene.Characters.Activators.Activator; // System.Activator also in scope

namespace UboatToolkit.Modules.CrewAi
{
    internal enum PeriscopePolicy
    {
        Off,            // no C# patch; data sheet only
        BlockOfficers,  // default - sailors keep the watch station, officers refused
        SkipperOnly     // v0.5 behaviour
    }

    internal static class PeriscopeToggles
    {
        public static PeriscopePolicy Policy => UboatToolkit.Settings.B("CrewScopeNoOfficers") ? PeriscopePolicy.BlockOfficers : PeriscopePolicy.Off;

        public static bool AttackPeriscopeSkipperOnly => UboatToolkit.Settings.B("CrewSkipperAttackScope");

        public const string PeriscopeActivatorTag = "Periscope";

        public const string AttackPeriscopeObjectName = "Attack Periscope";

        public static readonly bool AllowDirectPlayerOrder = true;

        public static readonly bool LogWhoUsesIt = true;
    }

    internal static class PeriscopeLog
    {
        private static readonly System.Collections.Generic.HashSet<string> _seen =
            new System.Collections.Generic.HashSet<string>();
        private static readonly object _lock = new object();

        public static void NoteOnce(PlayableCharacter c, string what)
        {
            if (!PeriscopeToggles.LogWhoUsesIt || c == null) return;
            try
            {
                string name = c.Name ?? "unknown";
                string key = what + "|" + name;
                lock (_lock) { if (!_seen.Add(key)) return; }

                string role = "?";
                try { role = c.Role != null ? c.Role.ToString() : "null"; } catch { }
                DiagLog.Write("[Periscope] " + what + " | character=" + name +
                              " role=" + role + " officer=" + SafeBool(c, true) +
                              " skipper=" + SafeBool(c, false));
            }
            catch (Exception e) { DiagLog.WriteError("Periscope.NoteOnce", e); }
        }

        private static string SafeBool(PlayableCharacter c, bool officer)
        {
            try { return (officer ? c.IsOfficer : c.IsSkipper).ToString(); }
            catch { return "?"; }
        }

        public static bool IsDenied(PlayableCharacter c)
        {
            if (c == null) return false;
            switch (PeriscopeToggles.Policy)
            {
                case PeriscopePolicy.Off:
                    return false;
                case PeriscopePolicy.SkipperOnly:
                    return !c.IsSkipper;
                case PeriscopePolicy.BlockOfficers:
                    return c.IsOfficer && !c.IsSkipper;
                default:
                    return false;
            }
        }
    }

    [HarmonyPatch(typeof(BasicObservationPeriscopeUseAction), "GetActionProfit",
        new Type[] { typeof(Activator), typeof(PlayableCharacter) })]
    internal static class Patch_BasicObservationPeriscope_GetActionProfit
    {
        static void Postfix(PlayableCharacter character, ref ActionProfit __result)
        {
            try
            {
                if (character == null) return;
                PeriscopeLog.NoteOnce(character, "scored");
                if (PeriscopeToggles.Policy == PeriscopePolicy.Off) return;

                if (PeriscopeLog.IsDenied(character))
                {
                    __result = new ActionProfit(__result.ProfitForPlayer, 0f);
                }
            }
            catch (Exception e)
            {
                DiagLog.WriteError("Periscope.GetActionProfit", e);
            }
        }
    }

    [HarmonyPatch(typeof(BasicObservationPeriscopeUseAction), "StartCharacter",
        new Type[] { typeof(PlayableCharacter) })]
    internal static class Patch_BasicObservationPeriscope_StartCharacter
    {
        static bool Prefix(PlayableCharacter character)
        {
            try
            {
                if (character == null) return true;
                PeriscopeLog.NoteOnce(character, "started");
                if (PeriscopeToggles.Policy == PeriscopePolicy.Off) return true;

                if (PeriscopeLog.IsDenied(character))
                {
                    DiagLog.Write("[Periscope] BLOCKED start | character=" + (character.Name ?? "unknown") +
                                  " policy=" + PeriscopeToggles.Policy);
                    return false; // skip the original StartCharacter entirely
                }
            }
            catch (Exception e)
            {
                DiagLog.WriteError("Periscope.StartCharacter", e);
            }
            return true;
        }
    }
}
