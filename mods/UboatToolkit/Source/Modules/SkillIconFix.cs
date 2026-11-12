using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using DWS.Common.Resources;
using HarmonyLib;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // Fix 'Requested asset at path "Icons/Skills/<Name> (Owned)" doesn't exist.': some skills (e.g. "Advanced
    // Equipment", or skills added by other mods such as "RadiomanAuthority1") have no picture for every state.
    // The game then logs an error with a long stack for each button, every time the skill tree opens.
    // Now: try the picture quietly, else the same skill without the state suffix, else the game's "Unknown" icon.
    [HarmonyPatch(typeof(UBOAT.Game.UI.SkillButton), "Start")]
    internal static class SkillIconFix
    {
        private static readonly MethodInfo Ours = AccessTools.Method(typeof(SkillIconFix), nameof(Get));

        public static Resource<Sprite> Get(ResourceManager rm, string path, object owner, bool logErrors)
        {
            if (!Settings.B("FixSkillIcons")) return rm.RetrieveAsset<Sprite>(path, owner, logErrors);
            var s = rm.RetrieveAsset<Sprite>(path, owner, false);
            if (s.Valid) return s;
            int i = path.IndexOf(" (");
            if (i > 0) { s = rm.RetrieveAsset<Sprite>(path.Substring(0, i), owner, false); if (s.Valid) return s; }
            return rm.RetrieveAsset<Sprite>("Icons/Skills/Unknown", owner, false);
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            foreach (var c in code)
            {
                if ((c.opcode == OpCodes.Callvirt || c.opcode == OpCodes.Call) && c.operand is MethodInfo m && m.Name == "RetrieveAsset"
                    && m.IsGenericMethod && m.GetGenericArguments()[0] == typeof(Sprite) && m.GetParameters().Length == 3)
                { c.opcode = OpCodes.Call; c.operand = Ours; }
                yield return c;
            }
        }
    }
}
