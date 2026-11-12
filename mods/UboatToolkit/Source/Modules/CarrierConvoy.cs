using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene.Entities;

namespace UboatToolkit.Modules
{
    // Convoys with a carrier: aircraft parked on deck read speed 0 and held the whole convoy back.
    // While the group has ships, the aircraft are left out of the group's top speed.
    [HarmonyPatch(typeof(SandboxMobileGroup), "UpdateParametersFromMembers", new[] { typeof(bool) })]
    internal static class CarrierConvoy_Patch
    {
        private const EntityTypeCategory VanillaSkipped = EntityTypeCategory.NotDetectableDirectly | EntityTypeCategory.SonarDecoy | EntityTypeCategory.Pontoon | EntityTypeCategory.Iceberg | EntityTypeCategory.Wreckage;
        private static SandboxMobileGroup cachedGroup, skippedFor;
        private static bool cachedHasShips, seen;
        private static int errorLogged;
        private static readonly HashSet<int> loggedGroups = new HashSet<int>();
        private static readonly HashSet<string> loggedTypes = new HashSet<string>(StringComparer.Ordinal);

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions);
            var getVelocity = AccessTools.PropertyGetter(typeof(SandboxEntity), "Velocity");
            var toFloat = AccessTools.Method(typeof(Parameter), "op_Implicit", new[] { typeof(Parameter) });
            int at = -1, n = 0;
            for (int i = 1; i + 1 < list.Count; i++)
            {
                if (list[i].operand as MethodInfo != getVelocity || list[i + 1].operand as MethodInfo != toFloat) continue;
                var op = list[i - 1].opcode;
                if (op != OpCodes.Ldloc && op != OpCodes.Ldloc_S && op != OpCodes.Ldloc_0 && op != OpCodes.Ldloc_1 && op != OpCodes.Ldloc_2 && op != OpCodes.Ldloc_3) continue;
                at = i; n++;
            }
            if (n != 1 || getVelocity == null || toFloat == null) { Util.Log("Träger-Konvoi: Geschwindigkeits-Stelle passt nicht (" + n + ") - bleibt Spielwert."); return list; }
            list.InsertRange(at + 2, new[]
            {
                new CodeInstruction(list[at - 1].opcode, list[at - 1].operand),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CarrierConvoy_Patch), "MemberSpeed"))
            });
            return list;
        }

        private static void Prefix() { cachedGroup = null; seen = true; }

        public static float MemberSpeed(float speed, SandboxEntity entity, SandboxMobileGroup group)
        {
            if (!Settings.B("FixCarrierConvoy")) return speed;
            try
            {
                if ((entity.Blueprint.Type.Category & EntityTypeCategory.CarrierBorneAircraft) == 0) return speed;
                if (!ReferenceEquals(cachedGroup, group)) { cachedGroup = group; cachedHasShips = HasShips(group); }
                if (!cachedHasShips) return speed;
                skippedFor = group;
                return float.PositiveInfinity;
            }
            catch (Exception e)
            {
                if (System.Threading.Interlocked.Exchange(ref errorLogged, 1) == 0) Util.Log("Träger-Konvoi: Prüfung fehlgeschlagen, Spielwert (einmalig): " + e);
                return speed;
            }
        }

        private static bool HasShips(SandboxMobileGroup g)
        {
            var m = g.Entities;
            for (int i = m.Count - 1; i >= 0; i--)
            {
                var c = m[i].Blueprint.Type.Category;
                if ((c & VanillaSkipped) == 0 && (c & EntityTypeCategory.CarrierBorneAircraft) == 0) return true;
            }
            return false;
        }

        private static void Postfix(SandboxMobileGroup __instance)
        {
            if (!ReferenceEquals(skippedFor, __instance)) return;
            skippedFor = null;
            try
            {
                int id = __instance.GetInstanceID();
                if (loggedGroups.Contains(id)) return;
                float ships = float.PositiveInfinity, air = float.PositiveInfinity;
                int airCount = 0, zero = 0;
                string zeroTypes = null;
                var m = __instance.Entities;
                for (int i = m.Count - 1; i >= 0; i--)
                {
                    var e = m[i];
                    var c = e.Blueprint.Type.Category;
                    if ((c & VanillaSkipped) != 0) continue;
                    float v = e.Velocity;
                    if ((c & EntityTypeCategory.CarrierBorneAircraft) == 0) { if (v < ships) ships = v; continue; }
                    airCount++;
                    if (v < air) air = v;
                    if (v <= 0f)
                    {
                        zero++;
                        string type = e.Blueprint.Type.Name;
                        if (zeroTypes == null || zeroTypes.IndexOf(type, StringComparison.Ordinal) < 0) zeroTypes = zeroTypes == null ? type : zeroTypes + ", " + type;
                        if (loggedTypes.Add(type))
                        {
                            int slots = e.Blueprint.Slots != null ? e.Blueprint.Slots.Length : 0;
                            Util.Log("Träger-Konvoi: Flugzeugtyp '" + type + "' hat Tempo 0: " + slots + " Ausrüstungs-Plätze" + (slots == 0 ? " (kein Propeller im Datenblatt)" : "") + ". Datenlücke im Mod, der den Typ hinzufügt.");
                        }
                    }
                }
                if (!(air < ships)) return;
                loggedGroups.Add(id);
                Util.Log("Träger-Konvoi: '" + __instance.Name + "': " + zero + " von " + airCount + " Flugzeugen mit Tempo 0 (" + (zeroTypes ?? "-") + "); langsamstes Flugzeug " + Kmh(air) + ", langsamstes Schiff " + Kmh(ships) + " km/h - Gruppe fährt jetzt nach den Schiffen.");
            }
            catch (Exception e)
            {
                if (System.Threading.Interlocked.Exchange(ref errorLogged, 1) == 0) Util.Log("Träger-Konvoi: Log fehlgeschlagen (einmalig): " + e);
            }
        }

        private static string Kmh(float v) => float.IsPositiveInfinity(v) ? "keins" : v.ToString("0.#", CultureInfo.InvariantCulture);

        // After switching the fix on or off, every group recomputes its top speed.
        public static void RefreshGroups()
        {
            if (!seen) return;
            var f = AccessTools.Field(typeof(SandboxGroup), "sandbox");
            var sb = f != null ? f.GetValue(null) as UBOAT.Game.Sandbox.Sandbox : null;
            var groups = sb?.GetGroupsDirect();
            if (groups == null) return;
            for (int i = groups.Count - 1; i >= 0; i--) (groups[i] as SandboxMobileGroup)?.SetParametersDirty();
        }
    }

    public class CarrierConvoyHost : ModuleBase
    {
        private bool last, init;
        public override void Tick()
        {
            bool on = Settings.B("FixCarrierConvoy");
            if (init && on != last) Util.Safe("CarrierConvoy.Refresh", CarrierConvoy_Patch.RefreshGroups);
            last = on; init = true;
        }
    }
}
