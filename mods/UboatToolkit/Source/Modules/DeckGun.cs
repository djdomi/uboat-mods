using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UBOAT.Game.Core;
using UBOAT.Game.Scene.Characters.Actions;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Environment;
using UBOAT.Game.Scene.Items;
using UBOAT.Game.Scene.Utilities;

namespace UboatToolkit.Modules
{
    // Deck guns and the sea: wider spread and slower reload as the sea rises, no firing at all in a
    // high sea, plus calmer enemy gunnery and enemy guns your shells can knock out.
    // Everything is read live from settings, so switching off is vanilla at once. Nothing goes into the save.
    internal static class SeaGunCfg
    {
        public static bool On => Settings.B("DeckGunSeaState");
        public static float Calm => Mathf.Clamp(Settings.F("DgsCalm"), 0f, 12f);
        public static float Rough => Mathf.Clamp(Settings.F("DgsRough"), 0f, 12f);
        public static float Disable => Mathf.Clamp(Settings.F("DgsDisable"), 0f, 12f);
        public static float MaxSpread => Mathf.Clamp(Settings.F("DgsMaxSpread"), 1f, 10f);
        public static float MaxReload => Mathf.Clamp(Settings.F("DgsMaxReload"), 1f, 5f);
        public static bool AllShips => Settings.B("DgsAllShips");
        public static bool AA => Settings.B("DgsAA");
        public static bool ManualAim => Settings.B("DgsManualAim");
        public static float NpcSpread => Mathf.Clamp(Settings.F("DgsNpcSpread"), 100f, 500f);
        public static float NpcFloor => Mathf.Clamp(Settings.F("DgsNpcFloor"), 0f, 20f);
        public static float NpcRanging => Mathf.Clamp(Settings.F("DgsNpcRanging"), 10f, 100f);
        public static bool NpcEvenOut => Settings.B("DgsNpcEvenOut");
        public static bool NpcWarships => Settings.B("DgsNpcWarships");
        public static bool Knockout => Settings.B("DgsKnockout");
        public static float KnockoutPct => Mathf.Clamp(Settings.F("DgsKnockoutPct"), 50f, 500f);
        public static float KnockoutRadius => Mathf.Clamp(Settings.F("DgsKnockoutRadius"), 1f, 20f);
        public static bool Log => Settings.B("DgsLog");
    }

    internal static class SeaGunState
    {
        private static FieldInfo weatherField;
        private static Weather weather;

        // Gun keeps a static injected Weather; reading it is cheap (no scene search per shot).
        public static Weather Weather
        {
            get
            {
                if (weather != null) return weather;
                if (weatherField == null) weatherField = AccessTools.Field(typeof(Gun), "weather");
                if (weatherField == null) return null;
                weather = weatherField.GetValue(null) as Weather;
                return weather;
            }
        }

        public static float Beaufort()
        {
            var w = Weather;
            return w == null ? -1f : w.SeaCondition;
        }

        private static float Severity()
        {
            float sea = Beaufort();
            if (sea < 0f) return 0f;
            float calm = SeaGunCfg.Calm, span = SeaGunCfg.Rough - calm;
            if (span <= 0.001f) return sea >= calm ? 1f : 0f;
            return Mathf.Clamp01((sea - calm) / span);
        }

        public static bool Applies(Gun gun)
        {
            if (gun == null || !SeaGunCfg.On) return false;
            if (gun.IsAntiAircraftWeapon && !SeaGunCfg.AA) return false;
            if (!SeaGunCfg.AllShips && !(gun.ParentEntity is PlayerShip)) return false;
            return true;
        }

        public static float SpreadMul(Gun gun) => Applies(gun) ? Mathf.Lerp(1f, SeaGunCfg.MaxSpread, Severity()) : 1f;
        public static float ReloadMul(Gun gun) => Applies(gun) ? Mathf.Lerp(1f, SeaGunCfg.MaxReload, Severity()) : 1f;

        public static bool TooRough(Gun gun)
        {
            if (!Applies(gun)) return false;
            float sea = Beaufort();
            return sea >= 0f && sea >= SeaGunCfg.Disable;
        }

        private static LocalizedString reason;
        private static int reasonTenths = int.MinValue;
        public static string ReasonText;

        public static LocalizedString Reason()
        {
            int tenths = Mathf.RoundToInt(Beaufort() * 10f);
            if (tenths != reasonTenths || reason == null)
            {
                reasonTenths = tenths;
                ReasonText = "See zu rau für das Geschütz (Windstärke " + (tenths / 10f).ToString("F1") + ")";
                reason = LocalizedString.CreateUnlocalized(ReasonText);
            }
            return reason;
        }
    }

    internal static class EnemyGunnery
    {
        private static readonly FieldInfo CurrentAccuracyField = AccessTools.Field(typeof(Gun), "currentAccuracy");

        public static bool Applies(Gun gun) => gun != null && SeaGunCfg.On && InScope(gun.ParentEntity);

        public static bool InScope(Entity e)
        {
            if (e == null || e is PlayerShip || !(e is Ship)) return false;
            if (SeaGunCfg.NpcWarships) return true;
            string c = Category(e);
            return c == "Freighter" || c == "Tanker" || c == "Coaster" || c == "Fishing Boat";
        }

        private static EntityType TypeOf(Entity e)
        {
            var s = e == null ? null : e.SandboxEntity;
            return s == null ? null : s.Type;
        }

        private static string Category(Entity e) { var t = TypeOf(e); return t == null ? null : t.CategoryString; }

        // C3 and every tanker row lack the 35 % crew accuracy cut the other freighters have.
        private static bool LacksMerchantCut(Entity e)
        {
            var t = TypeOf(e);
            return t != null && (t.CategoryString == "Tanker" || t.Name == "C3");
        }

        public static float AdjustSpread(float spread, Gun gun)
        {
            if (!Applies(gun)) return spread;
            spread *= SeaGunCfg.NpcSpread / 100f;
            float floor = SeaGunCfg.NpcFloor / 1000f;
            return spread < floor ? floor : spread;
        }

        public static float AdjustRanging(float next, Gun gun)
        {
            if (!Applies(gun) || CurrentAccuracyField == null) return next;
            float k = SeaGunCfg.NpcRanging / 100f;
            if (SeaGunCfg.NpcEvenOut && LacksMerchantCut(gun.ParentEntity)) k *= 0.35f;
            if (k >= 0.999f) return next;
            float prev = (float)CurrentAccuracyField.GetValue(gun);
            return prev + (next - prev) * k;
        }

        private static bool subscribed;
        public static void Subscribe()
        {
            if (subscribed) return;
            DamageUtility.DamageApplied += OnDamage;
            subscribed = true;
        }

        // Your shell bursting near an enemy gun damages that gun (falls off to zero at the radius).
        private static void OnDamage(DamageEvent e)
        {
            try
            {
                if (!SeaGunCfg.On || !SeaGunCfg.Knockout) return;
                if (e == null || e.DamageType != DamageType.Projectile || !(e.AppliedBy is PlayerShip)) return;
                var hit = e.DamagedEntities;
                if (hit == null) return;
                float r = SeaGunCfg.KnockoutRadius, r2 = r * r, scale = SeaGunCfg.KnockoutPct / 100f;
                foreach (var ent in hit)
                {
                    var ship = ent as Ship;
                    if (ship == null || !InScope(ship) || ship.Guns == null) continue;
                    foreach (var gun in ship.Guns)
                    {
                        if (gun == null || gun.Health <= 0f) continue;
                        float d2 = (gun.transform.position - e.Position).sqrMagnitude;
                        if (d2 > r2) continue;
                        float amount = e.Damage * scale * (1f - Mathf.Sqrt(d2) / r);
                        if (amount <= 0f) continue;
                        float before = gun.Health;
                        gun.Health = before - amount;
                        Util.Log("Deckgeschütz: Treffer an " + gun.name + " auf " + ship.name + " " + before.ToString("F2") + " -> " + gun.Health.ToString("F2") + (gun.Health <= 0f ? " AUSGEFALLEN" : ""));
                    }
                }
            }
            catch (Exception ex) { Util.Log("Deckgeschütz: Treffer-Auswertung Fehler " + ex.Message); }
        }
    }

    public static class SeaGunHooks
    {
        public static float Spread(float spread, Gun gun) => EnemyGunnery.AdjustSpread(spread * SeaGunState.SpreadMul(gun), gun);
        public static float Ranging(float next, Gun gun) => EnemyGunnery.AdjustRanging(next, gun);
        public static float Reload(Gun gun) => SeaGunState.ReloadMul(gun);

        // false = treat as crew aim (spread + ranging apply), true = vanilla manual aim.
        public static bool ManualAimGate(Gun gun)
        {
            if (!gun.ManualAim) return false;
            return !(SeaGunState.Applies(gun) && SeaGunCfg.ManualAim);
        }

        public static readonly MethodInfo SpreadM = AccessTools.Method(typeof(SeaGunHooks), "Spread");
        public static readonly MethodInfo RangingM = AccessTools.Method(typeof(SeaGunHooks), "Ranging");
        public static readonly MethodInfo ReloadM = AccessTools.Method(typeof(SeaGunHooks), "Reload");
        public static readonly MethodInfo GateM = AccessTools.Method(typeof(SeaGunHooks), "ManualAimGate");

        public static bool IsLoad(CodeInstruction c, string name)
        {
            var f = c.operand as FieldInfo;
            return c.opcode == OpCodes.Ldfld && f != null && f.Name == name;
        }
    }

    // Gun.Fire: hook spread, ranging-in and the two manual-aim gates.
    [HarmonyPatch(typeof(Gun), "Fire")]
    internal static class DeckGun_FireTranspiler
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var c = new List<CodeInstruction>(instructions);
            int spread = -1, nSpread = 0, gate = -1, nGate = 0, learn = -1, nLearn = 0, store = -1;
            for (int i = 0; i + 1 < c.Count; i++)
                if (SeaGunHooks.IsLoad(c[i], "inaccuracyMultiplier") && c[i + 1].opcode == OpCodes.Mul) { spread = i + 1; nSpread++; }
            for (int i = 1; i + 2 < c.Count; i++)
                if (SeaGunHooks.IsLoad(c[i], "manualAim") && c[i - 1].opcode == OpCodes.Ldarg_0 && c[i + 1].opcode == OpCodes.Ldc_I4_0 && c[i + 2].opcode == OpCodes.Ceq) { gate = i; nGate++; }
            for (int i = 1; i + 4 < c.Count; i++)
                if (SeaGunHooks.IsLoad(c[i], "manualAim") && c[i - 1].opcode == OpCodes.Ldarg_0
                    && (c[i + 1].opcode == OpCodes.Brtrue || c[i + 1].opcode == OpCodes.Brtrue_S)
                    && c[i + 2].opcode == OpCodes.Ldarg_0 && c[i + 3].opcode == OpCodes.Ldarg_0 && SeaGunHooks.IsLoad(c[i + 4], "currentAccuracy")) { learn = i; nLearn++; }
            if (nLearn == 1)
                for (int i = learn + 1; i < c.Count && i < learn + 40; i++)
                {
                    var f = c[i].operand as FieldInfo;
                    if (c[i].opcode == OpCodes.Stfld && f != null && f.Name == "currentAccuracy") { store = i; break; }
                }
            if (nSpread != 1 || nGate != 1 || nLearn != 1 || store < 0 || SeaGunHooks.SpreadM == null || SeaGunHooks.RangingM == null || SeaGunHooks.GateM == null)
            {
                Util.Log("Deckgeschütz: Gun.Fire passt nicht (" + nSpread + "/" + nGate + "/" + nLearn + "/" + store + ") - Streuung bleibt Spielwert.");
                return c;
            }
            c.InsertRange(store, new[] { new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Call, SeaGunHooks.RangingM) });
            c[learn].opcode = OpCodes.Call; c[learn].operand = SeaGunHooks.GateM;
            c.InsertRange(spread + 1, new[] { new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Call, SeaGunHooks.SpreadM) });
            if (gate > spread) gate += 2;
            c[gate].opcode = OpCodes.Call; c[gate].operand = SeaGunHooks.GateM;
            return c;
        }
    }

    // Gun.Reload and Gun.CanFire: multiply the reload seconds before they become ticks.
    [HarmonyPatch]
    internal static class DeckGun_ReloadTranspiler
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(Gun), "Reload");
            yield return AccessTools.DeclaredMethod(typeof(Gun), "CanFire");
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var c = new List<CodeInstruction>(instructions);
            var sites = new List<int>();
            for (int i = 0; i < c.Count; i++)
            {
                var f = c[i].operand as FieldInfo;
                if (c[i].opcode != OpCodes.Stfld || f == null || f.Name != "nextFireTime") continue;
                for (int j = i - 1; j >= 0 && j > i - 8; j--)
                    if (c[j].opcode == OpCodes.Ldc_R4 && c[j].operand is float v && Math.Abs(v - 1e7f) <= 1f) { if (j > 0) sites.Add(j); break; }
            }
            int expected = original != null && original.Name == "CanFire" ? 2 : 1;
            if (sites.Count != expected || SeaGunHooks.ReloadM == null)
            {
                Util.Log("Deckgeschütz: " + (original == null ? "?" : original.Name) + " Ladezeit-Stellen " + sites.Count + " statt " + expected + " - Ladezeit bleibt Spielwert.");
                return c;
            }
            for (int k = sites.Count - 1; k >= 0; k--)
                c.InsertRange(sites[k], new[] { new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Call, SeaGunHooks.ReloadM), new CodeInstruction(OpCodes.Mul) });
            return c;
        }
    }

    // In a high sea the gun cannot fire at all (star shells excepted).
    [HarmonyPatch(typeof(Gun), "Fire")]
    internal static class DeckGun_FireGate
    {
        private static readonly FieldInfo Flares = AccessTools.Field(typeof(Gun), "flares");

        private static bool Prefix(Gun __instance, Ammunition ammunition, ref Shell __result)
        {
            try
            {
                if (!SeaGunState.TooRough(__instance)) return true;
                if (Flares != null && ammunition != null && ReferenceEquals(ammunition, Flares.GetValue(__instance))) return true;
                __result = null;
                return false;
            }
            catch { return true; }
        }
    }

    [HarmonyPatch(typeof(Gun), "CanFire")]
    internal static class DeckGun_CanFire
    {
        private static float nextLog;

        private static void Postfix(Gun __instance, ref bool __result)
        {
            if (!__result || !SeaGunState.TooRough(__instance)) return;
            __result = false;
            if (!SeaGunCfg.Log || Time.unscaledTime < nextLog) return;
            nextLog = Time.unscaledTime + 10f;
            SeaGunState.Reason();
            Util.Log("Deckgeschütz: " + SeaGunState.ReasonText + " - kein Schuss.");
        }
    }

    // The gun order shows why it is greyed out.
    [HarmonyPatch(typeof(UseGun), "GetState")]
    internal static class DeckGun_UseGunState
    {
        private static void Postfix(UseGun __instance, bool fillDetails, ref ActionStateData __result)
        {
            if (__instance == null || __result.ActionState <= ActionState.Disabled) return;
            var gun = __instance.Gun;
            if (gun == null || !SeaGunState.TooRough(gun)) return;
            __result = fillDetails ? new ActionStateData(ActionState.Disabled, SeaGunState.Reason()) : new ActionStateData(ActionState.Disabled);
        }
    }

    // Subscribes the knockout handler once.
    public class SeaGun : ModuleBase
    {
        public override void Tick() { EnemyGunnery.Subscribe(); }
    }
}
