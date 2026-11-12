// Ausguck (Teil des UBOAT Toolkit): LrlAcceptancePatch. Schalter: F10 > QoL > "Ausguck".
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace UboatToolkit.Modules.Lrl
{
    [HarmonyPatch(typeof(UBOAT.Game.Sandbox.Sandbox), "GetRandomGroupInRangeWithHorizonOcclusion")]
    internal static class LrlAcceptancePatch
    {
        private static int _rolls;
        private static int _vanillaAccepts;
        private static int _fixedAccepts;

        internal static bool Applied { get; private set; }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions);
            var getValue = AccessTools.PropertyGetter(typeof(UnityEngine.Random), "value");
            var decide = AccessTools.Method(typeof(LrlAcceptancePatch), "Decide");

            var found = -1;
            var matches = 0;
            for (var i = 0; i + 2 < list.Count; i++)
            {
                if (list[i].opcode == OpCodes.Call && Equals(list[i].operand, getValue) &&
                    list[i + 1].opcode == OpCodes.Ldloc_0 &&
                    (list[i + 2].opcode == OpCodes.Bge_Un_S || list[i + 2].opcode == OpCodes.Bge_Un))
                {
                    found = i + 2;
                    matches++;
                }
            }

            if (matches != 1 || decide == null)
            {
                LrlLog.Warn("acceptance fix NOT applied - expected one 'Random.value >= summed' branch, found " +
                            matches + "; the search is left exactly as vanilla");
                return list;
            }

            var target = list[found].operand;
            var labels = list[found].labels;

            var loadFlag = new CodeInstruction(OpCodes.Ldarg_S, (byte)6);   // updateCandidates
            loadFlag.labels.AddRange(labels);

            list[found] = loadFlag;
            list.Insert(found + 1, new CodeInstruction(OpCodes.Call, decide));
            list.Insert(found + 2, new CodeInstruction(OpCodes.Brtrue, target));

            Applied = true;
            return list;
        }

        private static bool Decide(float random, float summed, bool isPlayer)
        {
            var vanilla = !(random < summed);   // == bge.un, unordered included

            if (!isPlayer || !LrlSettings.Enabled || !LrlSettings.FixInvertedAcceptance)
                return vanilla;

            var pick = random < summed;   // accept with probability = total visibility weight

            _rolls++;
            if (vanilla) _vanillaAccepts++;
            if (pick) _fixedAccepts++;

            return pick;
        }

        internal static string Report()
        {
            var s = string.Format(
                "acceptance fix {0}: player rolls={1} vanilla would accept={2} accepted={3}",
                Applied ? (LrlSettings.FixInvertedAcceptance ? "ON" : "off") : "NOT APPLIED",
                _rolls, _vanillaAccepts, _fixedAccepts);
            _rolls = 0;
            _vanillaAccepts = 0;
            _fixedAccepts = 0;
            return s;
        }
    }
}
