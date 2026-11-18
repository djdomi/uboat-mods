// UBOAT Toolkit - Waffen: Deckgeschütz, Flak, Torpedos, Minen
// Diese Datei fasst 11 frühere Einzeldateien zusammen (0.4.30). Jeder Abschnitt nennt seine alte Datei.

// ====================================================================================================
// Abschnitt: Modules/DeckGun.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::System.Reflection.Emit;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Core;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Environment;
    using global::UBOAT.Game.Scene.Items;
    using global::UBOAT.Game.Scene.Utilities;

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
            if (gun == null) return false;
            // 0.4.31: own guns = "DeckGunSeaState" (+ "DgsAA" for flak); every other gun = "DgsAllShips" alone (weakens them)
            if (gun.ParentEntity is PlayerShip) return SeaGunCfg.On && (!gun.IsAntiAircraftWeapon || SeaGunCfg.AA);
            return SeaGunCfg.AllShips && (!gun.IsAntiAircraftWeapon || SeaGunCfg.AA);
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

        public static bool Applies(Gun gun) => gun != null && InScope(gun.ParentEntity);

        public static bool InScope(Entity e)
        {
            // 0.3.60 (DoMe: "Gegner sind alles, was nicht der Player ist und nicht grün"): every non-player, non-ally
            // shooter (ships of all kinds, port guns, aircraft) gets the enemy spread.
            if (e == null || e is PlayerShip) return false;
            try
            {
                var me = Util.PlayerShip != null && Util.PlayerShip.SandboxEntity != null ? Util.PlayerShip.SandboxEntity.Country : null;
                var c = e.SandboxEntity != null ? e.SandboxEntity.PerceivedCountry : null;
                if (me != null && c != null && c.GetRelationWith(me) == UBOAT.Game.Sandbox.Country.Relation.Ally) return false;
            }
            catch { }
            return true;
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
            if (!Applies(gun) || !Settings.B("DgsNpcSpreadOn")) return spread;
            spread *= SeaGunCfg.NpcSpread / 100f;
            // 0.4.22 (option audit): a gun that has ranged in has spread 0 in the game, and x % of 0 stays 0. So the
            // percentage also raises the smallest miss; else the option did nothing against a ranged-in port gun.
            float floor = SeaGunCfg.NpcFloor / 1000f * (SeaGunCfg.NpcSpread / 100f);
            return spread < floor ? floor : spread;
        }

        public static float AdjustRanging(float next, Gun gun)
        {
            if (!Applies(gun) || CurrentAccuracyField == null) return next;
            // 0.4.32 (DoMe 2026-11-18: cheat "können sich nicht einschießen"): the accuracy of a non-player gun never rises
            if (Settings.B("CheatEnemyNoRanging"))
            {
                float before = (float)CurrentAccuracyField.GetValue(gun);
                EnemyPacifist.Count("Einschießen verhindert");
                return next > before ? before : next;
            }
            float k = Settings.B("DgsNpcRangingOn") ? SeaGunCfg.NpcRanging / 100f : 1f;
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
                if (!SeaGunCfg.Knockout) return;
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

// ====================================================================================================
// Abschnitt: Modules/EnemyHitLog.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Entities;
    using global::UnityEngine;

    // 0.3.95 proof (DoMe: "up to 250 hits from one ship at over 5 km"; "the toolkit must provably only improve the
    // player, never the enemy"). Every enemy shell that bursts near our boat is logged: shooter, shell, distance,
    // damage, and whether any toolkit value is inside (damage/crew-damage modifier, changed body mass, changed speed).
    // Only reads, changes nothing. If a toolkit value is found it is also removed, so the shell flies with game values.
    [HarmonyPatch(typeof(Shell), nameof(Shell.Explode))]
    internal static class EnemyHitLog
    {
        private const string Mod = "UboatToolkit Shell";
        private class Stat { public int Hits, Toolkit; public float MinKm = 999f, MaxKm, Dmg; public string Sample; }
        private static readonly Dictionary<string, Stat> stats = new Dictionary<string, Stat>();
        private static float nextLog; private static int single;

        private static void Prefix(Shell __instance)
        {
            try
            {
                var ship = Util.PlayerShip;
                var by = __instance.LaunchedBy;
                if (ship == null || by == null || by == ship) return;
                Vector3 pos = __instance.transform.position;
                if ((pos - ship.transform.position).sqrMagnitude > 70f * 70f) return;   // not near our boat
                float km = (by.transform.position - ship.transform.position).magnitude / 1000f;
                var t = Traverse.Create(__instance);
                var dmg = t.Field("damage").GetValue<UBOAT.Game.Core.Data.Parameter>();
                var crew = t.Field("crewDamage").GetValue<UBOAT.Game.Core.Data.Parameter>();
                var mass = t.Field("mass").GetValue<UBOAT.Game.Core.Data.Parameter>();
                var rb = __instance.GetComponent<Rigidbody>();
                bool tk = false; string why = "";
                if (dmg != null && Mathf.Abs(dmg.Value - dmg.GetValueExcludingModifier(Mod)) > 0.001f) { tk = true; why += " Schaden"; dmg.RemoveScaleModifiers(Mod); }
                if (crew != null && Mathf.Abs(crew.Value - crew.GetValueExcludingModifier(Mod)) > 0.001f) { tk = true; why += " Crew-Schaden"; crew.RemoveScaleModifiers(Mod); }
                if (rb != null && mass != null && mass.Value > 0f && Mathf.Abs(rb.mass - mass.Value) > 0.001f) { tk = true; why += " Masse " + rb.mass.ToString("0.###") + "/" + mass.Value.ToString("0.###"); }
                string key = by.name + " / " + __instance.name.Replace("(Clone)", "").Trim();
                if (!stats.TryGetValue(key, out var s)) stats[key] = s = new Stat();
                s.Hits++; if (tk) s.Toolkit++;
                s.MinKm = Mathf.Min(s.MinKm, km); s.MaxKm = Mathf.Max(s.MaxKm, km);
                float d = dmg != null ? dmg.Value : 0f; s.Dmg += d;
                if (single < 40)
                {
                    single++;
                    Util.Log("Treffer am Boot: " + key + ", Schütze " + km.ToString("0.0") + " km entfernt, Schaden " + d.ToString("0.##")
                        + ", Crew-Schaden " + (crew != null ? crew.Value.ToString("0.##") : "?") + ", Toolkit-Werte: " + (tk ? "JA (" + why.Trim() + ", entfernt)" : "nein"));
                }
                if (Time.unscaledTime >= nextLog) Flush();
            }
            catch { }
        }

        // Once a minute: every enemy gun in the scene is compared with the game values. Any toolkit value on an
        // enemy (a "UboatToolkit" modifier on one of its parameters, or a changed shell speed) is logged and removed.
        private static List<System.Reflection.FieldInfo> paramFields;
        internal static void CheckEnemies()
        {
            try
            {
                if (paramFields == null)
                {
                    paramFields = new List<System.Reflection.FieldInfo>();
                    for (var ty = typeof(UBOAT.Game.Scene.Items.Gun); ty != null && ty != typeof(object); ty = ty.BaseType)
                        foreach (var f in ty.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                            if (f.FieldType == typeof(UBOAT.Game.Core.Data.Parameter)) paramFields.Add(f);
                }
                int guns = 0, bad = 0; float pct = Settings.B("EnemyShellSpeedOn") ? Mathf.Clamp(Settings.F("EnemyShellSpeedPct"), 20f, 100f) : 100f;
                foreach (var g in PortDefense.AllGuns())
                {
                    if (g == null || Util.IsPlayers(g) || g.ParentEntity is PlayerShip) continue;
                    guns++;
                    foreach (var f in paramFields)
                    {
                        var par = f.GetValue(g) as UBOAT.Game.Core.Data.Parameter;
                        if (par == null) continue;
                        float ex = par.GetValueExcludingModifier(Util.ModifierName);
                        if (Mathf.Abs(par.Value - ex) <= 0.0001f) continue;
                        bad++;
                        if (bad <= 20) Util.Log("Gegner-Prüfung: ABWEICHUNG " + g.name + " (" + (g.ParentEntity != null ? g.ParentEntity.name : "?") + ") " + f.Name + " " + par.Value.ToString("0.###") + " statt " + ex.ToString("0.###") + " - zurückgesetzt");
                        Util.SetParameter(par, 0f);
                    }
                    float game = Util.Original<float>(g, "shellSpeed");
                    float now = Traverse.Create(g).Field("shellSpeed").GetValue<float>();
                    if (now > game + 0.01f || (pct >= 100f && Mathf.Abs(now - game) > 0.01f))
                    {
                        bad++;
                        if (bad <= 20) Util.Log("Gegner-Prüfung: ABWEICHUNG " + g.name + " Mündungsgeschwindigkeit " + now.ToString("0") + " statt " + game.ToString("0") + " - zurückgesetzt");
                        Util.SetField(g, "shellSpeed", game);
                    }
                }
                Util.Log("Gegner-Prüfung: " + guns + " gegnerische Waffen verglichen, " + (bad == 0 ? "alle mit Spielwerten (Toolkit verbessert keinen Gegner)" : bad + " Abweichungen gefunden und zurückgesetzt"));
            }
            catch (System.Exception e) { Util.Log("Gegner-Prüfung Fehler: " + e.Message); }
        }

        internal static void Flush()
        {
            nextLog = Time.unscaledTime + 60f;
            if (stats.Count == 0) return;
            foreach (var kv in stats)
                Util.Log("Treffer-Summe letzte Minute: " + kv.Key + " " + kv.Value.Hits + "x, Entfernung " + kv.Value.MinKm.ToString("0.0") + "-" + kv.Value.MaxKm.ToString("0.0")
                    + " km, Schaden zusammen " + kv.Value.Dmg.ToString("0.#") + ", davon mit Toolkit-Werten: " + kv.Value.Toolkit);
            stats.Clear();
        }
    
    }

    public class EnemyCheck : ModuleBase
    {
        private float next = 60f;
        public override void Tick()
        {
            if (Time.unscaledTime < next || Util.PlayerShip == null) return;
            next = Time.unscaledTime + 60f;
            EnemyHitLog.CheckEnemies();
            EnemyHitLog.Flush();
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/GunAim.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Items;
    using global::UnityEngine;

    // Shell weight and damage, aim assist (QoL) and perfect aim (cheat). Only your own boat's guns.
    //  * Shell weight: the shell's physics mass (own value in kg, 0 = game) times a multiplier.
    //    Note: in this game version the damage does NOT come from weight or speed (Shell.Explode uses only the
    //    Damage / CrewDamage values x a random 1..2). So there are own multipliers for damage and crew damage,
    //    and an optional "weight x speed" mode that scales both like the first release did.
    //    ArmorPiercing exists as a value but the game never reads it, so a setting for it would do nothing.
    //  * Aim assist (GunAimAssist, degrees): when YOU fire and an enemy ship you can see is within this
    //    angle of your barrel, the shot flies with the game's own lead calculation (the same one the crew
    //    uses: target speed and shell flight time). 0 = off.
    //  * Cheat perfect aim: crew always at 100 % ranging, no spread at all (also not at night), and your own
    //    shots use the lead calculation for any enemy within 30 degrees.
    // Collects every shell a gun creates during one Gun.Fire call.
    [HarmonyPatch(typeof(Gun), "Fire")]
    internal static class GunShells
    {
        internal static readonly List<Shell> Fired = new List<Shell>();
        internal static bool Firing;
        [HarmonyPriority(Priority.First)]
        private static void Prefix() { Fired.Clear(); Firing = true; }
        private static System.Exception Finalizer(System.Exception __exception) { Firing = false; Fired.Clear(); return __exception; }
    }
    // The pool hands out the shell objects (the tiny LaunchedBy setter could be inlined, so it is not patched).
    [HarmonyPatch(typeof(DWS.Common.Pooling.GlobalPool), nameof(DWS.Common.Pooling.GlobalPool.GetPrefabInstance), new[] { typeof(string), typeof(DWS.Common.Resources.InstantiationFlags) })]
    internal static class GunShells_Collect
    {
        private static void Postfix(GameObject __result)
        {
            if (!GunShells.Firing || __result == null) return;
            var s = __result.GetComponent<Shell>();
            if (s != null && !GunShells.Fired.Contains(s)) GunShells.Fired.Add(s);
        }
    }

    [HarmonyPatch(typeof(Gun), "Fire")]
    internal static class GunAim_FirePatch
    {
        private const string ModName = "UboatToolkit Shell";
        private static readonly List<Entity> targets = new List<Entity>();
        private static readonly System.Func<Entity, Vector3> ShootPoint =
            AccessTools.MethodDelegate<System.Func<Entity, Vector3>>(AccessTools.Method(typeof(Gun), "GetEntityShootingTarget"));

        [HarmonyPriority(Priority.High)]
        private static void Prefix(Gun __instance, bool ___manualAim, ref float ___currentAccuracy, ref Entity target)
        {
            if (!Util.IsPlayers(__instance)) return;
            bool cheat = Settings.B("CheatPerfectAim");
            if (cheat) ___currentAccuracy = 1f;
            if (!___manualAim || target != null) return;

            float cone = cheat ? Mathf.Max(30f, Settings.F("GunAimAssist")) : Settings.F("GunAimAssist");
            if (cone <= 0f) return;
            Transform barrel = __instance.VerticalAngleTransform != null ? __instance.VerticalAngleTransform : __instance.HorizontalAngleTransform;
            if (barrel == null) return;
            targets.Clear();
            __instance.GetPotentialTargets(targets);
            Entity best = null; float bestAngle = cone;
            foreach (var e in targets)
            {
                if (e == null || !e.isActiveAndEnabled) continue;
                Vector3 to = ShootPoint(e) - barrel.position;
                float a = Vector3.Angle(barrel.forward, to);
                if (a < bestAngle) { bestAngle = a; best = e; }
            }
            if (best == null) return;
            target = best; // the game now aims this shot with its own lead calculation
            if (Settings.B("CrewSkillLog")) Util.Log("Zielhilfe: Schuss auf " + best.name + " (" + bestAngle.ToString("0.0") + " Grad daneben gezielt)");
        }

        // 0.4.22 (option audit): a gun with several barrels fires one shell per barrel, but the game only hands back the
        // last one. All shells of this shot are collected (GunShells) and treated, so no pooled shell keeps our values.
        private static void Postfix(Gun __instance, Shell __result)
        {
            var all = GunShells.Fired;
            if (__result != null && !all.Contains(__result)) all.Add(__result);
            for (int i = 0; i < all.Count; i++) Apply(__instance, all[i]);
            if (all.Count > 1 && !multiLogged) { multiLogged = true; Util.Log("Granaten: Geschütz mit mehreren Rohren, alle " + all.Count + " Granaten des Schusses behandelt (" + __instance.name + ", " + (Util.IsPlayers(__instance) ? "eigenes" : "fremdes, Spielwerte") + ")"); }
            all.Clear();
        }
        private static bool multiLogged;
        private static void Apply(Gun __instance, Shell __result)
        {
            if (__result == null) return;
            if (!Util.IsPlayers(__instance))
            {
                // Shells come from a shared pool: one we fired earlier (x100 damage, heavier body) could be re-used by an
                // ENEMY gun and hit us with our own multipliers. Enemy shells always fly with the game's own values.
                Scale(__result, "damage", 1f); Scale(__result, "crewDamage", 1f);
                var erb = __result.GetComponent<Rigidbody>();
                var emp = Traverse.Create(__result).Field("mass").GetValue<UBOAT.Game.Core.Data.Parameter>();
                if (erb != null && emp != null && emp.Value > 0f && Mathf.Abs(erb.mass - emp.Value) > 0.0001f) erb.mass = emp.Value;
                return;
            }
            string p = Weapons.Prefix(__instance);
            float kg = Settings.F(p + "ShellMass"), massMul = Settings.F(p + "ShellMassMul");
            float dmgMul = Settings.F(p + "ShellDamageMul"), crewMul = Settings.F(p + "ShellCrewDamageMul");
            if (dmgMul <= 0f) dmgMul = 1f;
            if (crewMul <= 0f) crewMul = 1f;
            var rb = __result.GetComponent<Rigidbody>();
            var massParam = Traverse.Create(__result).Field("mass").GetValue<UBOAT.Game.Core.Data.Parameter>();
            float gameMass = massParam != null ? massParam.Value : 0f, mass = gameMass;
            if (rb != null && massParam != null)
            {
                mass = Mathf.Max(0.0001f, (kg > 0f ? kg : gameMass) * (massMul > 0f ? massMul : 1f));
                rb.mass = mass;
            }
            // Optional (like the first release): damage grows with weight x speed compared to the game's shell.
            float kinetic = 1f;
            if (Settings.B(p + "KineticDamage") && gameMass > 0f)
            {
                float gameSpeed = Util.Original<float>(__instance, "shellSpeed");
                float speed = Traverse.Create(__instance).Field("shellSpeed").GetValue<float>();
                if (gameSpeed > 0f) kinetic = Mathf.Clamp(mass * speed / (gameMass * gameSpeed), 0.01f, 10000f);
            }
            // The shell is a real physics body: heavier/faster shells shoved the hit ship away (one was knocked
            // off the map). Damage comes from the values above, so the body only keeps the game's own push
            // (mass x speed). Trajectory is unchanged: gravity and drag do not depend on mass.
            if (rb != null && gameMass > 0f && Settings.B("NoShellPush"))
            {
                float gs = Util.Original<float>(__instance, "shellSpeed");
                float sp = Traverse.Create(__instance).Field("shellSpeed").GetValue<float>();
                float maxMass = sp > 0f && gs > 0f ? gameMass * gs / sp : gameMass;
                rb.mass = Mathf.Max(0.0001f, Mathf.Min(rb.mass, maxMass));
            }
            Scale(__result, "damage", dmgMul * kinetic);
            Scale(__result, "crewDamage", crewMul * kinetic);
        }

        private static void Scale(Shell shell, string field, float f)
        {
            var par = Traverse.Create(shell).Field(field).GetValue<UBOAT.Game.Core.Data.Parameter>();
            if (par == null) return;
            par.RemoveScaleModifiers(ModName); // shells are re-used from a pool
            if (Mathf.Abs(f - 1f) > 0.001f) par.AddScaleModifier(ModName).Value = f;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/GunGyro.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Items;
    using global::UnityEngine;

    // Gun gyro (only your own boat's guns).
    //  * Crew aims (GunGyroCrew): the game turns a gun very slowly (5 % speed) for the last 8 degrees
    //    to the target. In waves the boat rolls faster than that, so the barrel never settles.
    //    With the gyro the gun follows those small corrections at once; big turns to a new target
    //    keep the crew's normal turning speed.
    //  * You aim yourself (GunGyroManual): the game keeps the gun fixed to the boat, so the boat's
    //    roll moves your aim point. With the gyro the gun keeps pointing at the same spot in the world
    //    (the boat's own movement is taken out), and only your mouse moves it.
    [HarmonyPatch(typeof(Gun), "UpdateAnglesGradually")]
    internal static class GunGyro_Patch
    {
        private const float FineZone = 8f; // the game's own slow fine-tracking zone
        private static readonly AccessTools.FieldRef<Gun, Transform> Origin = AccessTools.FieldRefAccess<Gun, Transform>("originTransform");

        private class Hold { public Vector3 World; public Vector2 Set; }
        private static readonly Dictionary<int, Hold> holds = new Dictionary<int, Hold>();

        private static Vector3 Dir(Vector2 a) => Quaternion.Euler(a.x, a.y, 0f) * Vector3.forward;

        private static Vector2 Angles(Vector3 d)
        {
            float flat = Mathf.Sqrt(d.x * d.x + d.z * d.z);
            return new Vector2(-Mathf.Atan2(d.y, flat) * Mathf.Rad2Deg, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
        }

        private static void Prefix(Gun __instance, bool ___manualAim, ref Vector2 ___angles, ref Vector2 ___targetAngles)
        {
            if (!Util.IsPlayers(__instance)) return;
            int id = __instance.GetInstanceID();

            if (!___manualAim)
            {
                holds.Remove(id);
                if (!Settings.B("GunGyroCrew")) return;
                float dy = Mathf.DeltaAngle(___angles.y, ___targetAngles.y), dx = ___targetAngles.x - ___angles.x;
                if (dx * dx + dy * dy < FineZone * FineZone) ___angles = ___targetAngles;
                return;
            }

            if (!Settings.B("GunGyroManual")) { holds.Remove(id); return; }
            var origin = Origin(__instance);
            if (origin == null) return;
            Hold h;
            if (!holds.TryGetValue(id, out h) || (___targetAngles - h.Set).sqrMagnitude > 1e-6f)
            {
                // First frame or you moved the mouse: remember where the gun now points in the world.
                if (h == null) { h = new Hold(); holds[id] = h; }
                h.World = origin.TransformDirection(Dir(___targetAngles));
                h.Set = ___targetAngles;
                return;
            }
            // Boat moved, mouse did not: turn the gun so it still points at the same world spot.
            Vector2 want = Angles(origin.InverseTransformDirection(h.World));
            __instance.SetAngles(want);          // keeps the gun's own turning/elevation limits
            h.Set = ___targetAngles;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/GunSmoke.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Items;
    using global::UnityEngine;

    // 0.3.73 (DoMe: suppress the smoke of the 88). The muzzle flash + smoke of a gun is one particle effect per barrel
    // (Gun.Barrel.FireEffect). With "GunSmokeOff" the emission of that effect (and its child effects) is switched off on
    // the PLAYER's guns, so the game's Play() shows nothing. No object is deactivated. Switched off again = effect back. Enemy guns are never touched.
    // Measured 2026-11-16: all particle effects together cost ~0.1 ms per frame, so this is mainly for the look.
    public class GunSmoke : ModuleBase
    {
        private static readonly System.Reflection.FieldInfo BarrelsField = AccessTools.Field(typeof(Gun), "barrels");
        private static readonly System.Reflection.FieldInfo EffectField =
            AccessTools.Inner(typeof(Gun), "Barrel") != null ? AccessTools.Field(AccessTools.Inner(typeof(Gun), "Barrel"), "FireEffect") : null;
        private static readonly List<ParticleSystem> off = new List<ParticleSystem>();
        private bool logged;

        public override void Tick()
        {
            bool want = Settings.B("GunSmokeOff");
            if (!want)
            {
                if (off.Count == 0) return;
                foreach (var p in off) if (p != null) { var em = p.emission; em.enabled = true; }
                off.Clear(); logged = false;
                return;
            }
            if (BarrelsField == null || EffectField == null) return;
            int n = 0;
            foreach (var g in Util.Find<Gun>())
            {
                if (g == null) continue;
                var arr = BarrelsField.GetValue(g) as System.Array;
                if (arr == null) continue;
                foreach (var b in arr)
                {
                    var ps = b == null ? null : EffectField.GetValue(b) as ParticleSystem;
                    if (ps == null) continue;
                    foreach (var p in ps.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        var em = p.emission;
                        if (!em.enabled) continue;
                        em.enabled = false;
                        p.Clear(false);
                        off.Add(p); n++;
                    }
                }
            }
            if (n > 0 && !logged) { logged = true; Util.Log("Mündungsrauch: " + n + " Effekte der eigenen Geschütze abgeschaltet"); }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/PortDefense.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Items;
    using global::UBOAT.Game.Scene.Utilities;
    using global::UnityEngine;

    // DoMe 2026-11-14: port defence can be destroyed for a while. Port guns are equipment of the port entity (not a ship).
    // When OUR shot or torpedo hits near such a gun, the gun gets Health 0 (game then disables it) and is repaired after
    // PortDefenseMinutes (game time).
    public class PortDefense : ModuleBase
    {
        internal static readonly Dictionary<Gun, float> Down = new Dictionary<Gun, float>();
        private static readonly List<Gun> done = new List<Gun>();

        internal static bool IsPortGun(Gun g)
        {
            if (g == null || Util.IsPlayers(g)) return false;
            var e = g.ParentEntity;
            return e != null && !(e is Ship) && !(e is Aircraft);
        }

        // 0.3.59: Util.Find only searches the player's boat, so port guns were never found. Scene-wide search, cached 10 s.
        private static Gun[] all = new Gun[0];
        private static float allAt = -100f;
        internal static Gun[] AllGuns()
        {
            if (Time.unscaledTime - allAt > 30f) { all = Object.FindObjectsOfType<Gun>(); allAt = Time.unscaledTime; }
            return all;
        }

        internal static void Hit(Vector3 pos, float radius)
        {
            float r = Mathf.Max(radius, Settings.F("PortDefenseHitM"));
            foreach (var g in AllGuns())
            {
                if (!IsPortGun(g) || g.Health <= 0f) continue;
                if ((g.transform.position - pos).sqrMagnitude > r * r) continue;
                g.Health = 0f;
                Down[g] = Time.time + Settings.F("PortDefenseMinutes") * 60f;
                Util.Log("Hafenverteidigung: " + g.name + " zerstört (" + g.ParentEntity.name + ")");
                ToolkitRunner.ShowNotice("Hafengeschütz zerstört", 4f);
            }
        }

        private float scanAt;
        public override void Tick()
        {
            // 0.3.38: Health 0 is saved with the game, the timer is not. After load / port scene reload, broken port
            // guns without timer get a new one, so they never stay broken forever.
            if (Settings.B("PortDefenseDestroyable") && Time.unscaledTime >= scanAt)
            {
                scanAt = Time.unscaledTime + 60f;   // 0.3.69: scene-wide search cost ~45 ms every 10 s ("Langsam: PortDefense.Tick")
                foreach (var g in AllGuns())
                    if (IsPortGun(g) && g.Health <= 0f && !Down.ContainsKey(g))
                        Down[g] = Time.time + Settings.F("PortDefenseMinutes") * 60f;
            }
            if (Down.Count == 0) return;
            done.Clear();
            foreach (var kv in Down) if (kv.Key == null || Time.time >= kv.Value) done.Add(kv.Key);
            foreach (var g in done)
            {
                Down.Remove(g);
                if (g == null) continue;
                g.Health = g.MaxHealth.Value;
                Util.Log("Hafenverteidigung: " + g.name + " wieder einsatzbereit");
            }
        }
    }

    [HarmonyPatch(typeof(DamageUtility), nameof(DamageUtility.ApplyDamage))]
    internal static class PortDefense_HitPatch
    {
        private static void Postfix(Entity appliedBy, Vector3 position, float damageRadius)
        {
            if (!Settings.B("PortDefenseDestroyable") || appliedBy == null || appliedBy != Util.PlayerShip) return;
            Util.Safe("Hafenverteidigung", () => PortDefense.Hit(position, damageRadius));
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/PortMines.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Linq;
    using global::UBOAT.Game.Sandbox;
    using global::UBOAT.Game.Scene.Entities;
    using global::UnityEngine;

    // Key: in port, put mines into the first torpedo slot of the shop right now (no waiting for restock).
    public class PortMines : ModuleBase
    {
        public override void Frame()
        {
            if (!Keys.Down("KeyPortMines")) return;
            Util.Log("Minen-Taste gedrückt");   // 0.3.47: every branch logs (DoMe: key had no effect)
            var ship = Util.PlayerShip;
            var se = ship?.CurrentDock as SandboxEntity;
            if (se == null || se.Country == null) { UboatToolkit.ToolkitRunner.ShowNotice("Minen: nur im Hafen möglich", 4f); Util.Log("Minen: kein Hafen (Docked=" + (ship != null && ship.Docked) + ", CurrentDock=" + (ship?.CurrentDock != null ? ship.CurrentDock.GetType().Name : "null") + ")"); return; }
            // 0.3.48: only a real port (DoMe: mines ended up in his own tubes)
            if (se.Blueprint?.Type == null || se.Blueprint.Type.Category != EntityTypeCategory.Port)
            {
                UboatToolkit.ToolkitRunner.ShowNotice("Minen: das ist kein Hafen-Laden", 4f);
                Util.Log("Minen: CurrentDock ist kein Hafen (" + se.Name + ", " + se.Blueprint?.Type?.Category + ")");
                return;
            }
            var eqs = se.Equipment.Where(e => e?.Slot != null && e.Slot.EquipmentTag == "Torpedo").ToList();
            var first = eqs.FirstOrDefault();
            if (first == null) { UboatToolkit.ToolkitRunner.ShowNotice("Minen: Hafen hat keine Torpedo-Plätze", 4f); Util.Log("Minen: " + se.Name + " ohne Torpedo-Platz, Plätze: " + string.Join(", ", se.Equipment.Where(e => e?.Slot != null).Select(e => e.Slot.EquipmentTag).Distinct())); return; }
            var pool = se.Country.GetReservesByTagID(first.Slot.EquipmentTagID);
            var mine = pool?.FirstOrDefault(x => x?.ItemData != null && Controls_TorpedoMixPatch.IsMine(x.ItemData.Name));
            if (mine == null) { UboatToolkit.ToolkitRunner.ShowNotice("Minen: dieses Land hat keine Minen", 4f); if (pool != null) Util.Log("Minen: keine im Vorrat, Typen: " + string.Join(", ", pool.Select(x => x?.ItemData?.Name))); return; }
            se.SetEquipment(first.Slot, mine.ItemData, true, true);
            UboatToolkit.ToolkitRunner.ShowNotice("Minen im Hafen-Laden: " + mine.ItemData.Name, 5f);
            Util.Log("Minen in Hafen gelegt: " + mine.ItemData.Name);
        }
    }
}

namespace UboatToolkit.Modules
{
    using global::System.Linq;
    using global::UBOAT.Game.Sandbox;
    using global::UBOAT.Game.Scene.Entities;
    using global::UnityEngine;

    // 0.3.48: log what goes into our tubes (DoMe found mines in the tubes, source unknown)
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Scene.Items.TorpedoLauncher), nameof(UBOAT.Game.Scene.Items.TorpedoLauncher.SetTorpedo))]
    internal static class PortMines_TubeLog
    {
        private static void Postfix(UBOAT.Game.Scene.Items.TorpedoLauncher __instance)
        {
            var t = __instance.Torpedo;
            if (t == null || !Util.IsPlayers(__instance) || !Controls_TorpedoMixPatch.IsMine(t.name)) return;
            Util.Log("Rohr " + __instance.name + ": Mine geladen (" + t.name.Replace("(Clone)", "").Trim() + "), Hafen=" + (Util.PlayerShip != null && Util.PlayerShip.Docked));
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Strike.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Linq;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UnityEngine.UI;
    using global::UBOAT.Game.Sandbox;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Items;

    // Cheats: "Sink the convoy" (every enemy ship nearby gets torpedoes) and
    // "remote torpedo" (fire at the nearest enemy from anywhere, e.g. from your bunk).
    // Torpedoes are spawned close to the target, the same way the game's own developer console does it,
    // and count as fired by your boat.
    public class Strike : ModuleBase
    {
        public override void Frame()
        {
            if (Util.PlayerShip == null) return;
            if (Keys.Down("KeyRemoteTorpedo") && Settings.B("CheatRemoteTorpedo")) Util.Safe("RemoteTorpedo", RemoteTorpedo);
            if (Keys.Down("KeySinkConvoy")) { var o = Settings.Get("CheatSinkConvoy"); if (o != null) o.Value = 1; }
        }

        public override void Tick()
        {
            var o = Settings.Get("CheatSinkConvoy");
            if (o != null && o.On) { o.Value = 0; Util.Safe("SinkConvoy", SinkConvoy); }
        }

        private static bool IsEnemyShip(Entity e, Country me)
        {
            if (!(e is Ship) || e is PlayerShip || e is Torpedo || e.Health <= 0f) return false;
            var se = e.SandboxEntity;
            if (se == null || se.IsPacified || se.Type == null || se.Type.IsAircraft) return false;
            var c = se.PerceivedCountry;
            return c != null && me != null && c.GetRelationWith(me) == Country.Relation.Enemy;
        }

        private static void SinkConvoy()
        {
            var ship = Util.PlayerShip;
            var me = ship.SandboxEntity?.Country;
            float range = Settings.F("SinkConvoyRange") * 1000f;
            int per = Mathf.Max(1, (int)Settings.F("SinkConvoyTorpedoes"));
            int ships = 0;
            foreach (var e in Entity.List.ToArray())
            {
                if (!IsEnemyShip(e, me)) continue;
                if ((e.transform.position - ship.transform.position).magnitude > range) continue;
                for (int i = 0; i < per; i++) Spawn(e, "G7a Torpedo T1 - Pi1.1");
                ships++;
            }
            ToolkitRunner.ShowNotice(ships > 0 ? "Sink the convoy: " + ships + " Schiffe im Visier" : "Kein Gegner in Reichweite", 6f);
        }

        private static void RemoteTorpedo()
        {
            var ship = Util.PlayerShip;
            var me = ship.SandboxEntity?.Country;
            Entity target = null; float best = float.MaxValue;
            foreach (var e in Entity.List)
            {
                if (!IsEnemyShip(e, me)) continue;
                float d = (e.transform.position - ship.transform.position).sqrMagnitude;
                if (d < best) { best = d; target = e; }
            }
            if (target == null) { ToolkitRunner.ShowNotice("Kein Gegner in Reichweite", 3f); return; }

            // A loaded tube gives its torpedo type (and loses it, unless unlimited torpedoes is on).
            string type = "G7a Torpedo T1 - Pi1.1";
            var tube = Util.Find<TorpedoLauncher>().FirstOrDefault(l => l.Torpedo != null && l.IsLoaded && !(l.Torpedo is StoredMinePacket));
            if (tube == null && !Settings.B("CheatInfiniteTorpedoes")) { ToolkitRunner.ShowNotice("Kein Rohr geladen", 3f); return; }
            if (tube != null)
            {
                type = tube.Torpedo.name.Replace("(Clone)", "").Trim();
                if (!Settings.B("CheatInfiniteTorpedoes"))
                {
                    var t = tube.Torpedo;
                    tube.SetTorpedo(null);
                    Object.Destroy(t.gameObject);
                }
            }
            Spawn(target, type);
            ToolkitRunner.ShowNotice("Torpedo los auf " + target.name.Replace("(Clone)", "").Trim(), 3f);
        }

        private static void Spawn(Entity target, string type)
        {
            var sandbox = Object.FindObjectOfType<UBOAT.Game.Sandbox.Sandbox>();
            var w = Game.Wolfpack;
            if (sandbox == null || w == null) return;
            var se = SandboxEntity.Create(type, sandbox.NeutralCountry);
            se.SpawnsInstantly = true;
            w.AddEntity(se);
            se.UpdatePosition();
            if (!(se.SpawnedEntity is Torpedo torp)) return;
            Vector3 aim = target.transform.position;
            if (target is Ship s) { var f = target.transform.forward; f.y = 0f; aim += f.normalized * (s.AverageVelocity * 15f); }
            torp.WasSpawnedInsteadLaunched = true;
            var st = torp.GetComponent<StoredTorpedo>();
            st.Start();
            st.transform.position = aim + Quaternion.AngleAxis(Random.value * 360f, Vector3.up) * new Vector3(0f, 0f, 270f);
            var dir = aim - st.transform.position; dir.y = 0f; dir.Normalize();
            st.transform.rotation = Quaternion.AngleAxis(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, Vector3.up);
            st.DudSeed = 1f;
            st.Launch(Util.PlayerShip, target.transform, aim, aim, 0, st.Speed1, new Collider[0]);
        }
    }

    // Tube icons: hovering over the torpedo symbol of a tube shows its exact type.
    [HarmonyPatch(typeof(UBOAT.Game.UI.TorpedoLauncherUI), "ValidateTorpedoIcon")]
    internal static class Strike_TubeTooltipPatch
    {
        private static void Postfix(UBOAT.Game.UI.TorpedoLauncherUI __instance)
        {
            if (!Settings.B("TubeDisplay")) return;
            var img = Traverse.Create(__instance).Field("torpedoImage").GetValue<Image>();
            if (img == null) return;
            var ui = __instance;
            UBOAT.Game.UI.Tooltip.SetTooltip(img.rectTransform, () =>
            {
                var t = ui.Launcher != null ? ui.Launcher.Torpedo : null;
                return t == null ? "leer" : t.name.Replace("(Clone)", "").Trim();
            });
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Torpedoes.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Linq;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::DWS.Common.Resources;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Core.Time;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Items;

    // #26 / cheats: unlimited torpedoes (the tube is reloaded right after the shot, the same way the game
    // itself loads tubes at campaign start), optional T5 "Zaunkönig" homing torpedoes, and no duds.
    public class Torpedoes : ModuleBase
    {
        public const string T5 = "G7es Zaunkönig Torpedo T5 - Pi4";
        private static ResourceManager resources;

        public static ResourceManager Resources
        {
            get
            {
                if (resources == null) resources = UnityEngine.Resources.FindObjectsOfTypeAll<ResourceManager>().FirstOrDefault();
                return resources;
            }
        }

        private static UserSettings userSettings;
        private static bool tdcKept, tdcAuto, tdcPrev;

        // 0.3.43 (DoMe: "es werden keine Torpedos nachgeladen"): the reload right after the shot (OnLaunchingComplete)
        // did not fire in every case. Backup: an own tube that stays empty for 3 s is filled again here.
        private static readonly System.Collections.Generic.Dictionary<TorpedoLauncher, string> lastName = new System.Collections.Generic.Dictionary<TorpedoLauncher, string>();
        private static readonly System.Collections.Generic.Dictionary<TorpedoLauncher, float> emptySince = new System.Collections.Generic.Dictionary<TorpedoLauncher, float>();
        // 0.3.52 (DoMe: "zusätzliche option T1, t2, t,3, 4, t5 oder mine"): ReloadType 0 = like last, 1-5 = T1-T5, 6 = mine.
        // Picks the item with the newest pistol from the equipment database.
        private static readonly System.Collections.Generic.Dictionary<int, string> pickCache = new System.Collections.Generic.Dictionary<int, string>();
        internal static string Pick(string last)
        {
            int t = Mathf.RoundToInt(Settings.F("ReloadType"));
            if (t <= 0 && Settings.B("CheatAllT5")) t = 5;
            if (t <= 0 || t > 6) return string.IsNullOrEmpty(last) ? T5 : last;
            if (pickCache.TryGetValue(t, out var c)) return c;
            string best = null; float bp = -1f;
            var db = UnityEngine.Resources.FindObjectsOfTypeAll<UBOAT.Game.Sandbox.Devices.EquipmentDatabase>().FirstOrDefault();
            if (db != null)
                foreach (var e in db.GetEquipmentDirect())
                {
                    string n = e != null ? e.Name : null;
                    if (string.IsNullOrEmpty(n)) continue;
                    bool mine = Controls_TorpedoMixPatch.IsMine(n);
                    bool ok = t == 6 ? mine : !mine && n.IndexOf("Torpedo T" + t, System.StringComparison.Ordinal) >= 0 && n.IndexOf("LUT", System.StringComparison.Ordinal) < 0;
                    if (!ok) continue;
                    float p = Controls_TorpedoMixPatch.Pistol(n);
                    if (p > bp) { bp = p; best = n; }
                }
            if (best == null) { Util.Log("Nachladen: kein Typ T" + t + " gefunden, nehme " + (string.IsNullOrEmpty(last) ? T5 : last)); return string.IsNullOrEmpty(last) ? T5 : last; }
            Util.Log("Nachladen: Typ " + t + " = " + best);
            pickCache[t] = best; return best;
        }

        private static void Refill()
        {
            if (!Settings.B("CheatInfiniteTorpedoes")) return;
            foreach (var l in Util.Find<TorpedoLauncher>())
            {
                if (l == null || !(Util.IsPlayers(l) || (l.Group != null && l.Group.ParentEntity is PlayerShip))) continue;
                if (l.Torpedo != null)
                {
                    emptySince.Remove(l);
                    lastName[l] = l.Torpedo.name.Replace("(Clone)", "").Trim();
                    continue;
                }
                if (!emptySince.TryGetValue(l, out var t0)) { emptySince[l] = Time.time; continue; }
                if (Time.time - t0 < 3f) continue;
                emptySince.Remove(l);
                lastName.TryGetValue(l, out var last);
                Util.Safe("torpedo refill", () =>
                {
                    var rm = Resources; if (rm == null) return;
                    string name = Pick(last);
                    var go = rm.InstantiatePrefab("Equipment/" + name);
                    if (go == null && !string.IsNullOrEmpty(last) && name != last) go = rm.InstantiatePrefab("Equipment/" + last);
                    var stored = go != null ? go.GetComponent<StoredTorpedo>() : null;
                    if (stored == null) { if (go != null) Object.Destroy(go); Util.Log("Torpedo nachladen: kein Torpedo-Typ gefunden (" + name + ")"); return; }
                    l.SetTorpedo(stored);
                    l.CompleteLoading(quiet: true);
                    Util.Log("Torpedo nachgeladen (Cheat): " + l.name + " <- " + name);
                });
            }
        }

        // 0.3.86 (DoMe 2026-11-16 BUG: "Torpedos an Bord, aber es wird nichts mehr nachgeladen"): an own tube that stays
        // empty for AutoReloadSeconds gets the nearest torpedo from the boat's own storage (nothing is created).
        // Once a minute the tube states and the stock are logged while a tube is empty.
        private static readonly System.Collections.Generic.Dictionary<TorpedoLauncher, float> autoEmpty = new System.Collections.Generic.Dictionary<TorpedoLauncher, float>();
        private static float nextTubeLog;
        // The crew has an open carrying order for this tube.
        internal static bool TubeBusy(TorpedoLauncher l)
        {
            try { var s = l != null ? l.Slot : null; return s != null && s.TransportOrders != null && s.TransportOrders.Count > 0; }
            catch { return false; }
        }
        private static void AutoReload()
        {
            if (!Settings.B("AutoReloadTubes") || Settings.B("CheatInfiniteTorpedoes")) { autoEmpty.Clear(); return; }
            float wait = Settings.F("AutoReloadSeconds");
            var ship = Util.PlayerShip;
            if (ship == null || ship.Docked) { autoEmpty.Clear(); return; }   // in port the port's own stock is not touched
            Storage[] storages = null;
            int empty = 0, loaded = 0, busy = 0;
            string states = "";
            foreach (var l in Util.Find<TorpedoLauncher>())
            {
                if (l == null || !(Util.IsPlayers(l) || (l.Group != null && l.Group.ParentEntity is PlayerShip))) continue;
                states += (states.Length > 0 ? ", " : "") + l.CurrentState;
                if (l.Torpedo != null) { autoEmpty.Remove(l); continue; }
                // 0.4.22 (option audit): the crew is already carrying a torpedo to this tube. Filling it now made the
                // game's own loading stop half-way and left an order behind; after that the game never loaded this tube
                // by itself again.
                if (TubeBusy(l)) { autoEmpty.Remove(l); busy++; continue; }
                empty++;
                if (!autoEmpty.TryGetValue(l, out var t0)) { autoEmpty[l] = Time.time; continue; }
                if (Time.time - t0 < wait) continue;
                if (storages == null) storages = Util.Find<Storage>().Where(x => x != null && !(x is TorpedoLauncherGroup) && x.GetComponentInParent<UBOAT.Game.Scene.Characters.PlayableCharacter>() == null).ToArray();
                Storage best = null; int bestSlot = -1; float bestDist = float.MaxValue;
                foreach (var st in storages)
                    for (int i = 0; i < Util.Slots(st); i++)
                    {
                        if (!(st.GetItemAt(i) is StoredTorpedo t) || t is StoredMinePacket || st.HasTransportOrders(i)) continue;
                        float dist = (st.transform.position - l.transform.position).sqrMagnitude;
                        if (dist < bestDist) { best = st; bestSlot = i; bestDist = dist; }
                    }
                if (best == null) continue;
                var tube = l; var from = best; int slot = bestSlot;
                Util.Safe("auto reload", () =>
                {
                    var torp = (StoredTorpedo)from.GetItemAt(slot);
                    from.SetItemAt(slot, null);
                    tube.SetTorpedo(torp);
                    tube.CompleteLoading(quiet: true);
                    Util.Log("Rohr automatisch aus dem Lager geladen: " + tube.name + " <- " + torp.name);
                });
                autoEmpty.Remove(l); loaded++;
            }
            if (empty > loaded && Time.unscaledTime > nextTubeLog)
            {
                nextTubeLog = Time.unscaledTime + 60f;
                int stock = 0;
                if (storages == null) storages = Util.Find<Storage>().Where(x => x != null && !(x is TorpedoLauncherGroup) && x.GetComponentInParent<UBOAT.Game.Scene.Characters.PlayableCharacter>() == null).ToArray();
                foreach (var st in storages)
                    for (int i = 0; i < Util.Slots(st); i++) if (st.GetItemAt(i) is StoredTorpedo t && !(t is StoredMinePacket)) stock++;
                Util.Log("Rohre: " + empty + " leer, " + busy + " lädt die Crew gerade (nicht angefasst), Zustände [" + states + "], Torpedos im Lager: " + stock + " (Lager gefunden: " + storages.Length + ")");
                Util.Safe("tube diag", CrewLoadDiag);
            }
        }

        // Why does the crew not load by itself (DoMe 2026-11-16)? The game's own loading needs: a free tube that is
        // accessible and open for the crew, a torpedo in the tube room's own rack, and an officer whose role has the
        // loading job. Each of these is written to the log.
        // 0.4.2 (DoMe 2026-11-17: "finde heraus, warum die Mannschaft nicht nachlädt"): asks the game's own decision
        // code for every officer with the loading job: silent running (the game forbids loading then), may the officer
        // start work at all, what does the loading action answer for each tube room (state + reason + profit).
        private static void CrewLoadDiag2()
        {
            var sb = new System.Text.StringBuilder("Lade-Entscheidung: ");
            try
            {
                var src = DWS.Common.InjectionFramework.InjectionFramework.Instance.GetInstance<UBOAT.Game.Scene.Tasks.ISilentRunningController>();
                sb.Append("Schleichfahrt=").Append(src != null ? (src.IsSilentRunningEnabled ? "AN (Spiel lädt dann nie)" : "aus") : "?").Append("; ");
            }
            catch { sb.Append("Schleichfahrt=?; "); }
            var groups = Util.Find<TorpedoLauncherGroup>();
            var crew = Object.FindObjectOfType<UBOAT.Game.Scene.Characters.PlayerCrew>();
            if (crew == null || crew.Officers == null) { Util.Log(sb.Append("keine Crew").ToString()); return; }
            foreach (var o in crew.Officers)
            {
                if (o == null || o.Role == null || !o.Role.HasAction<UBOAT.Game.Scene.Characters.Actions.LoadTorpedoesAction>()) continue;
                sb.Append("{").Append(o.name).Append(" darfArbeiten=").Append(o.CanStartNormalActions())
                  .Append(" Dienstplan=").Append(o.Schedule != null ? o.Schedule.CurrentBehaviour.ToString() : "?");
                var jobs = Traverse.Create(o.Role).Field("jobs").GetValue() as System.Collections.IEnumerable;
                if (jobs != null)
                    foreach (var job in jobs)
                    {
                        if (job == null) continue;
                        var info = Traverse.Create(job).Field("actionInfo").GetValue();
                        if (info == null) continue;
                        var act = Traverse.Create(info).Field("Action").GetValue() as UBOAT.Game.Scene.Characters.Actions.LoadTorpedoesAction;
                        if (act == null) continue;
                        var tags = Traverse.Create(info).Field("ActivatorTags").GetValue() as string[];
                        sb.Append(" Auftrag[Marken=").Append(tags != null ? string.Join("/", tags) : "?")
                          .Append(" Prio=").Append(Traverse.Create(job).Field("priority").GetValue()).Append("]");
                        foreach (var g in groups)
                        {
                            if (g == null) continue;
                            var activator = g.GetComponentInParent<UBOAT.Game.Scene.Characters.Activators.Activator>();
                            if (activator == null) { sb.Append(" ").Append(g.name).Append(":kein Bedienplatz"); continue; }
                            string st, why = "", profit;
                            try
                            {
                                var r = act.GetState(activator, o, true, false, UBOAT.Game.Scene.Characters.Activators.ActionContext.Normal);
                                st = r.ActionState.ToString();
                                if (r.Warning != null) why = " (" + r.Warning.ToString() + ")";
                            }
                            catch (System.Exception e) { st = "Fehler " + e.GetType().Name; }
                            try { profit = act.GetActionProfit(activator, o, 1f).ToString("0.##"); } catch (System.Exception e) { profit = "Fehler " + e.GetType().Name; }
                            sb.Append(" ").Append(g.name).Append(":Zustand=").Append(st).Append(why).Append(",Nutzen=").Append(profit);
                        }
                    }
                sb.Append("} ");
            }
            Util.Log(sb.ToString());
        }

        private static void CrewLoadDiag()
        {
            var sb = new System.Text.StringBuilder("Lade-Diagnose: ");
            foreach (var l in Util.Find<TorpedoLauncher>())
            {
                var x = l as ExtendedTorpedoLauncher;
                if (x == null || x.Torpedo != null) continue;
                sb.Append("[").Append(x.name).Append(" Zugang=").Append(x.GetAccessibilityStatus())
                  .Append(" Aufträge=").Append(x.Slot != null ? x.Slot.TransportOrders.Count : -1)
                  .Append(" KI-offen=").Append(x.LoadingInteractable ? x.LoadingInteractable.IsOpenForAI.ToString() : "kein Ladeplatz");
                var rack = x.Group != null ? x.Group.TorpedoStorage : null;
                int n = 0;
                if (rack != null) for (int i = 0; i < Util.Slots(rack); i++) if (rack.GetItemAt(i) is StoredTorpedo t && !(t is StoredMinePacket)) n++;
                sb.Append(" Gestell=").Append(rack != null ? n.ToString() : "keins").Append("] ");
            }
            var crew = Object.FindObjectOfType<UBOAT.Game.Scene.Characters.PlayerCrew>();
            if (crew != null && crew.Officers != null)
                foreach (var o in crew.Officers)
                {
                    if (o == null) continue;
                    bool job = o.Role != null && o.Role.HasAction<UBOAT.Game.Scene.Characters.Actions.LoadTorpedoesAction>();
                    sb.Append("{").Append(o.name).Append(job ? " LADE-ROLLE" : "").Append(" tut=").Append(o.Action != null ? o.Action.GetType().Name : "nichts").Append("} ");
                }
            Util.Log(sb.ToString());
            Util.Safe("tube diag 2", CrewLoadDiag2);
            // where do the torpedoes on board lie? (the tube's selection window only shows the tube room's rack)
            var where = new System.Text.StringBuilder("Torpedo-Lagerorte: ");
            foreach (var st in Util.Find<Storage>())
            {
                if (st == null || st.GetComponentInParent<UBOAT.Game.Scene.Characters.PlayableCharacter>() != null) continue;
                int n = 0, slots = Util.Slots(st);
                for (int i = 0; i < slots; i++) if (st.GetItemAt(i) is StoredTorpedo) n++;
                if (n > 0 || st is TorpedoStorage) where.Append(st.name).Append(" (").Append(st.GetType().Name).Append(", Typ ").Append(st.StorageType).Append("): ").Append(n).Append(" von ").Append(slots).Append(" Plätzen; ");
            }
            Util.Log(where.ToString());
        }

        public override void Tick()
        {
            Util.Safe("AutoReload", AutoReload);
            // #17: use the game's own automatic torpedo solution (click target -> values are filled in).
            if (Settings.B("EasyTDC"))
            {
                if (userSettings == null) userSettings = UnityEngine.Resources.FindObjectsOfTypeAll<UserSettings>().FirstOrDefault();
                var gs = userSettings?.GameplaySettings;
                if (gs != null)
                {
                    if (!tdcKept) { tdcKept = true; tdcAuto = gs.disableAutomaticTorpedoSolutions; tdcPrev = gs.disableTorpedoSolutionPreviews; }
                    gs.disableAutomaticTorpedoSolutions = false;
                    gs.disableTorpedoSolutionPreviews = false;
                }
            }
            else if (tdcKept)
            {
                // 0.4.22 (option audit): switched off = the game's own two settings are back as they were before.
                tdcKept = false;
                var gs = userSettings?.GameplaySettings;
                if (gs != null) { gs.disableAutomaticTorpedoSolutions = tdcAuto; gs.disableTorpedoSolutionPreviews = tdcPrev; Util.Log("Einfacher Torpedorechner aus: Spiel-Einstellungen wieder wie vorher"); }
            }

            Refill();
            // 0.4.7: the once-a-second search for launched torpedoes on the boat is gone (it never found one and cost a
            // full search of the boat every second); Torpedoes_NoDudPatch does the job at the moment of impact.
        }
    }

    // 0.4.2 (DoMe 2026-11-17: "Torpedoversager, obwohl es laut Cheat keine geben kann"): a launched torpedo is its own
    // object in the water, not a part of the boat, so the once-a-second search on the boat never found it. Now the
    // torpedo is marked right before the game decides about a dud (impact, magnetic trigger, dud roll). Own torpedoes only.
    [HarmonyPatch]
    internal static class Torpedoes_NoDudPatch
    {
        private static int logged;
        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var n in new[] { "OnCollisionEnter", "OnTriggerEnter", "TryDetonateFlatChance" })
            {
                var m = AccessTools.DeclaredMethod(typeof(Torpedo), n);
                if (m != null) yield return m;
            }
        }
        private static void Prefix(Torpedo __instance)
        {
            try
            {
                if (__instance == null || __instance.DudsDisabled || !Settings.B("CheatNoDuds")) return;
                var ship = Util.PlayerShip;
                if (ship == null || __instance.Owner != ship) return;
                __instance.DudsDisabled = true;
                if (logged++ < 30) Util.Log("Versager verhindert: " + __instance.name);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(TorpedoLauncher), nameof(TorpedoLauncher.OnLaunchingComplete))]
    internal static class Torpedoes_ReloadPatch
    {
        private static void Prefix(TorpedoLauncher __instance, out string __state)
        {
            __state = null;
            if (!Settings.B("CheatInfiniteTorpedoes") || !Util.IsPlayers(__instance)) return;
            var fired = __instance.Torpedo;
            if (fired != null) __state = fired.name.Replace("(Clone)", "").Trim();
        }

        private static void Postfix(TorpedoLauncher __instance, string __state)
        {
            if (string.IsNullOrEmpty(__state) || __instance.Torpedo != null) return;
            Util.Safe("torpedo reload", () =>
            {
                var rm = Torpedoes.Resources;
                if (rm == null) return;
                string name = Torpedoes.Pick(__state);
                var go = rm.InstantiatePrefab("Equipment/" + name);
                if (go == null && name != __state) go = rm.InstantiatePrefab("Equipment/" + __state);
                var stored = go != null ? go.GetComponent<StoredTorpedo>() : null;
                if (stored == null) { if (go != null) Object.Destroy(go); return; }
                __instance.SetTorpedo(stored);
                __instance.CompleteLoading(quiet: true);
            });
        }
    }
    // QoL: a torpedo the crew has maintained stays maintained (player's boat only). The game normally drops the
    // "WarmedUp" dud bonus when the maintenance cooldown runs out and the crew then maintains the tube again.
    internal static class TorpedoKeep
    {
        private static readonly System.Reflection.FieldInfo GameTimeF = AccessTools.Field(typeof(StoredTorpedo), "gameTime");
        internal static readonly AccessTools.FieldRef<StoredTorpedo, long> WarmUpTicks = AccessTools.FieldRefAccess<StoredTorpedo, long>("warmUpTicks");
        private static bool logged;

        internal static bool Wanted(StoredTorpedo t)
        {
            if (t == null || !Settings.B("TorpedoStayMaintained") || t.IsLaunched || !t.IsMaintained) return false;
            var ship = Util.PlayerShip;
            if (ship == null) return false;
            return (object)t.ParentEntity == ship || Util.IsPlayers(t);
        }

        internal static bool Refresh(StoredTorpedo t)
        {
            var gt = GameTimeF != null ? GameTimeF.GetValue(null) as GameTime : null;
            if (gt == null) return false;
            WarmUpTicks(t) = gt.ContinuousTicks;
            if (!logged) { logged = true; Util.Log("Torpedos bleiben gewartet: " + t.name + " bleibt gewartet"); }
            return true;
        }
    }

    [HarmonyPatch(typeof(StoredTorpedo), "RemoveWarmUpModifier")]
    internal static class Torpedoes_KeepMaintainedPatch
    {
        private static bool Prefix(StoredTorpedo __instance, ref float __result)
        {
            try
            {
                if (!TorpedoKeep.Wanted(__instance) || !TorpedoKeep.Refresh(__instance)) return true;
                __result = Mathf.Max(60f, (float)__instance.MaintenanceCooldown);
                return false;
            }
            catch { return true; }
        }
    }

    [HarmonyPatch(typeof(StoredTorpedo), nameof(StoredTorpedo.MaintenanceCooldownProgress), MethodType.Getter)]
    internal static class Torpedoes_KeepProgressPatch
    {
        private static void Postfix(StoredTorpedo __instance, ref float __result)
        {
            try
            {
                if (__result > 0.05f && __result < 1f && TorpedoKeep.Wanted(__instance) && TorpedoKeep.Refresh(__instance)) __result = 0f;
            }
            catch { }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/TorpedoTab.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::System.IO;
    using global::System.Linq;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Sandbox;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Items;

    // Tab "Torpedos" in the F10 menu:
    // - pick which torpedo types ports stock (empty = game / mix)
    // - cheat: create a chosen torpedo directly in a free torpedo slot of the boat
    internal static class TorpedoTab
    {
        internal static readonly HashSet<string> PortPick = new HashSet<string>();
        private static bool loaded;
        private static List<string> names;
        private static float namesAt = -100f;
        private static int count = 1;
        private static string File => Path.Combine(Settings.Folder, "toolkit-torpedos.txt");

        internal static void Load()
        {
            if (loaded) return;
            loaded = true;
            try { if (System.IO.File.Exists(File)) foreach (var l in System.IO.File.ReadAllLines(File)) if (l.Trim().Length > 0) PortPick.Add(l.Trim()); } catch { }
        }

        private static void Save()
        {
            try { Directory.CreateDirectory(Settings.Folder); System.IO.File.WriteAllLines(File, PortPick.ToArray()); } catch { }
        }

        // All torpedo types the German side knows (from the reserves) plus the ones on board.
        private static List<string> Names()
        {
            if (names != null && Time.unscaledTime - namesAt < 10f) return names;
            namesAt = Time.unscaledTime;
            var set = new HashSet<string>();
            var sb = Object.FindObjectOfType<UBOAT.Game.Sandbox.Sandbox>();
            var de = sb != null ? sb.GetCountryByCode("DE", false) : null;
            if (de != null)
                foreach (var r in de.Reserves)
                    if (r?.ItemData != null && r.ItemData.HasTag("Torpedo")) set.Add(r.ItemData.Name);
            foreach (var t in Util.Find<StoredTorpedo>(5f))
                if (t != null && !(t is StoredMinePacket) && !string.IsNullOrEmpty(t.TypeName)) set.Add(t.TypeName);
            names = set.OrderBy(n => n).ToList();
            return names;
        }

        public static void Draw()
        {
            Load();
            if (Util.PlayerShip == null) { Imgui.Label("Nur im Spiel verfügbar."); return; }
            var list = Names();
            if (list.Count == 0) { Imgui.Label("Keine Torpedo-Typen gefunden."); DrawAmmo(); return; }

            Imgui.Label("Hafen-Nachschub: Haken = dieser Typ kommt ins Hafenlager. Nichts angehakt = normal (oder Mischung, wenn eingeschaltet).");
            Imgui.Label("Gilt für Lager, die sich neu auffüllen.");
            foreach (var n in list)
            {
                bool on = PortPick.Contains(n);
                bool now = Imgui.Toggle(on, " " + n);
                if (now != on) { if (now) PortPick.Add(n); else PortPick.Remove(n); Save(); }
            }
            if (PortPick.Count > 0 && Imgui.Button("Auswahl löschen", 200)) { PortPick.Clear(); Save(); }

            Imgui.Space(10);
            Imgui.Label("Cheat: Torpedo direkt ins Boot legen (in freie Torpedo-Plätze).");
            Imgui.BeginH();
            Imgui.Label("Anzahl: " + count, 100);
            if (Imgui.Button("-", 40) && count > 1) count--;
            if (Imgui.Button("+", 40) && count < 30) count++;
            Imgui.EndH();
            foreach (var n in list)
                if (Imgui.Button("Erzeugen: " + n, 600))
                {
                    int made = Create(n, count);
                    ToolkitRunner.ShowNotice(made > 0 ? made + "x " + n + " ins Boot gelegt" : "Kein freier Torpedo-Platz", 4f);
                }
            DrawAmmo();
        }

        private static Storage[] BoatStorages() =>
            Util.Find<Storage>(1f).Where(s => s != null && Util.IsPlayers(s) && s.GetComponentInParent<PlayableCharacter>() == null).ToArray();

        // Torpedo: one per free slot that the game allows for it.
        private static int Create(string name, int want)
        {
            int made = 0;
            Util.Safe("TorpedoTab.Create", () =>
            {
                var rm = Torpedoes.Resources;
                if (rm == null) return;
                StoredTorpedo t = null;
                foreach (var s in BoatStorages())
                    for (int i = 0; i < Util.Slots(s) && made < want; i++)
                    {
                        if ((s.GetItemAt(i) as Object) != null || s.HasTransportOrders(i)) continue;
                        if (t == null)
                        {
                            var go = rm.InstantiatePrefab("Equipment/" + name);
                            t = go != null ? go.GetComponent<StoredTorpedo>() : null;
                            if (t == null) { if (go != null) Object.Destroy(go); return; }
                        }
                        if (!s.IsItemAllowed(i, t)) continue;
                        s.SetItemAt(i, t);
                        t = null;
                        made++;
                    }
                if (t != null) Object.Destroy(t.gameObject);
            });
            return made;
        }

        // Ammunition: stacks into the boat storages like loading in port.
        private static float CreateAmmo(string name, float amount)
        {
            float stored = 0f;
            Util.Safe("TorpedoTab.Ammo", () =>
            {
                var rm = Torpedoes.Resources;
                var go = rm != null ? rm.InstantiatePrefab("Equipment/" + name) : null;
                var c = go != null ? go.GetComponent<Cargo>() : null;
                if (c == null) { if (go != null) Object.Destroy(go); return; }
                Inventory.ApplyStack(c);
                c.Count = amount;
                foreach (var s in BoatStorages())
                {
                    // Stacks already lying there get the big stack size now, not up to 15 s later.
                    for (int i = 0; i < Util.Slots(s); i++) Inventory.ApplyStack(s.GetItemAt(i) as Cargo);
                    for (int k = 0; k < 20 && c != null; k++)
                    {
                        float before = c.Count;
                        if (s.StoreItem(c)) c = null;
                        else if (c.Count >= before - 0.0001f) break;
                    }
                    if (c == null) break;
                }
                if (c == null) { stored = amount; return; }
                if (c != null) { stored = amount - c.Count; Object.Destroy(c.gameObject); }
            });
            return stored;
        }

        private static List<string> ammoNames;
        private static float ammoAt = -100f;
        private static int ammoAmount = 100;
        private static List<string> AmmoNames()
        {
            if (ammoNames != null && Time.unscaledTime - ammoAt < 10f) return ammoNames;
            ammoAt = Time.unscaledTime;
            var set = new HashSet<string>();
            var sb = Object.FindObjectOfType<UBOAT.Game.Sandbox.Sandbox>();
            var de = sb != null ? sb.GetCountryByCode("DE", false) : null;
            if (de != null)
                foreach (var r in de.Reserves)
                    if (r?.ItemData?.Type != null && typeof(Ammunition).IsAssignableFrom(r.ItemData.Type)) set.Add(r.ItemData.Name);
            foreach (var a in Util.Find<Ammunition>(5f)) if (a != null && !string.IsNullOrEmpty(a.TypeName)) set.Add(a.TypeName);
            ammoNames = set.OrderBy(n => n).ToList();
            return ammoNames;
        }

        private static void DrawAmmo()
        {
            Imgui.Space(10);
            Imgui.Label("Cheat: Munition direkt ins Boot legen (stapelt sich ins Lager wie beim Beladen im Hafen).");
            var list = AmmoNames();
            if (list.Count == 0) { Imgui.Label("Keine Munitions-Typen gefunden."); return; }
            Imgui.BeginH();
            Imgui.Label("Menge: " + ammoAmount, 120);
            foreach (var n in new[] { 10, 50, 100, 500, 1000 })
                if (Imgui.Button(n.ToString(), 60)) ammoAmount = n;
            Imgui.EndH();
            foreach (var n in list)
                if (Imgui.Button("Erzeugen: " + n, 600))
                {
                    float got = CreateAmmo(n, ammoAmount);
                    ToolkitRunner.ShowNotice(got > 0.5f ? Mathf.RoundToInt(got) + "x " + n + " ins Lager gelegt" : "Kein Platz im Lager (oder zu schwer)", 4f);
                }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Weapons.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::System.Linq;
    using global::System.Reflection;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Scene.Audio;
    using global::UBOAT.Game.Scene.Items;

    // QoL #27: turn/elevation speed and rounds per minute for the 88 mm deck gun and the AA guns.
    // QoL #28: magazine size (a big magazine plus RPM gives continuous fire instead of shoot-and-reload).
    // Shell speed, gunner accuracy (no spread, also hides the boat's rolling), AA guns may engage ships,
    // and a limit for very fast firing sounds (the engine struggles with many overlapping sounds).
    // Cheat: unlimited ammunition (uses the game's own "infinite ammunition" switch of the boat).
    public class Weapons : ModuleBase
    {
        private bool lastInfinite;
        private readonly HashSet<int> aaSwitched = new HashSet<int>();

        private Gun deck88;

        // Seconds between shots: from RPM (or the game's own value) divided by the rate multiplier. 0 = game value.
        private static float Gap(Parameter p, float rpm, float rateMul)
        {
            if (p == null) return 0f;
            if (rpm <= 0f && Mathf.Abs(rateMul - 1f) < 0.001f) return 0f;
            float baseGap = rpm > 0f ? 60f / rpm : p.GetValueExcludingModifier(Util.ModifierName);
            return Mathf.Max(0.001f, baseGap / rateMul);
        }

        public static string Prefix(Gun gun) => gun.IsAntiAircraftWeapon ? "AAGun" : "DeckGun";

        private static bool bothLogged, enemyLogged;
        private static readonly HashSet<int> bothSwitched = new HashSet<int>();
        public override void Tick()
        {
            var ship = Util.PlayerShip;
            deck88 = null;
            foreach (var g in Util.Find<Gun>())
                if (g != null && !g.IsAntiAircraftWeapon && Util.IsPlayers(g)) { deck88 = g; break; }
            foreach (var gun in Util.Find<Gun>())
            {
                // QoL: player guns attack ships AND aircraft (the game's menu only offers one or the other; enemy flak
                // already uses both). Preference per gun type in Weapons_PreferPatch. "Clear" (hold fire) stays.
                if (Settings.B("AAGunBothTargets") && Util.IsPlayers(gun) && !Util.WarthogModOwns(ship)
                    && (gun.TargetingMode == ArtilleryTargetingMode.Ships || gun.TargetingMode == ArtilleryTargetingMode.Aircrafts))
                {
                    gun.TargetingMode = ArtilleryTargetingMode.Ships | ArtilleryTargetingMode.Aircrafts;
                    bothSwitched.Add(gun.GetInstanceID());
                    if (!bothLogged) { bothLogged = true; Util.Log("Geschütze: Schiffe und Flugzeuge zugleich (Deckgeschütz bevorzugt Schiffe, Flak Flugzeuge), z. B. " + gun.name); }
                }
                // 0.4.22 (option audit): switched off = guns we changed go back to their own kind of target.
                if (!Settings.B("AAGunBothTargets") && bothSwitched.Remove(gun.GetInstanceID()) && !Settings.B("AAvsShips")
                    && gun.TargetingMode == (ArtilleryTargetingMode.Ships | ArtilleryTargetingMode.Aircrafts))
                    gun.TargetingMode = gun.IsAntiAircraftWeapon ? ArtilleryTargetingMode.Aircrafts : ArtilleryTargetingMode.Ships;
                // 0.3.38: enemy guns (port defence, escorts) always keep the game's own values. Up to 0.3.38 turn speed,
                // fire rate, magazine, shell speed and "flak like 88" were applied to EVERY gun in the scene.
                if (!Util.IsPlayers(gun))
                {
                    Util.SetField(gun, "rotationSpeed", Util.Original<float>(gun, "rotationSpeed"));
                    Util.SetField(gun, "minAngle", Util.Original<float>(gun, "minAngle"));
                    Util.SetField(gun, "maxAngle", Util.Original<float>(gun, "maxAngle"));
                    Util.SetField(gun, "shellSpeed", Util.Original<float>(gun, "shellSpeed"));
                    Util.SetParameter(Traverse.Create(gun).Field("range").GetValue<Parameter>(), 0f);
                    Util.SetParameter(gun.MagazineSize, 0f);
                    Util.SetParameter(Traverse.Create(gun).Field("seriesTimeOffset").GetValue<Parameter>(), 0f);
                    Util.SetParameter(gun.ReloadTime, 0f);
                    if (!enemyLogged) { enemyLogged = true; Util.Log("Geschütze: Gegner-Geschütze behalten Spielwerte (z. B. " + gun.name + ")"); }
                    continue;
                }
                string p = Prefix(gun);
                float turn = Settings.F(p + "TurnSpeed");
                float rpm = Settings.F(p + "RPM");
                float mag = Settings.F(p + "Magazine");
                float speed = Settings.F(p + "ShellSpeed");

                // Turn speed: the AA may take the 88's speed if that is faster ("Flak wie 88").
                float rot = Util.Original<float>(gun, "rotationSpeed");
                bool like88 = gun.IsAntiAircraftWeapon && Settings.B("AAGunLike88") && deck88 != null;
                if (like88) rot = Mathf.Max(rot, Util.Original<float>(deck88, "rotationSpeed"));
                Util.SetField(gun, "rotationSpeed", rot * (turn > 0f ? turn : 1f));

                // Lowest elevation (negative = barrel points down). 0 = game value.
                float minA = Util.Original<float>(gun, "minAngle");
                Util.SetField(gun, "minAngle", minA);   // UP limit: always the gun's own (0.4.22: "wie 88" changed this one by mistake)
                // Lowest aim (game/Unity: x angle positive = barrel DOWN, so the DOWN limit is maxAngle; minAngle is the UP
                // limit). Up to 0.2.63 this wrongly changed minAngle, so "Flak tiefer neigen" had no effect.
                float depress = Util.IsPlayers(gun) ? Settings.F(p + "Depress") : 0f;
                if (gun.IsAntiAircraftWeapon && UboatToolkit.Modules.Warthog.WarthogWeapons.On) depress = Mathf.Max(depress, Settings.F("WhFlakDepression"));
                float maxA = Util.Original<float>(gun, "maxAngle");
                if (like88) depress = Mathf.Max(depress, Util.Original<float>(deck88, "maxAngle"));   // 0.4.22: "neigt so tief wie die 88" = DOWN limit
                if (!Util.WarthogModOwns(ship)) Util.SetField(gun, "maxAngle", depress > 0f ? Mathf.Max(maxA, depress) : maxA);

                // Recoil (barrel jumps up after each shot, annoying when aiming yourself). 0 = none, 1 = game.
                if (Util.IsPlayers(gun))
                {
                    float rec = Mathf.Clamp(Settings.F(p + "Recoil"), 0f, 1f);
                    var tg = Traverse.Create(gun);
                    foreach (var f in new[] { "minVerticalRecoil", "maxVerticalRecoil", "horizontalRecoil" })
                    {
                        var par = tg.Field(f).GetValue<Parameter>();
                        if (par != null) Util.OneScale(par, Util.ModifierName).Value = rec;
                    }
                }

                // Engagement range in km (0 = game, ~12 km for the 88). The crew only picks targets inside it.
                float km = Util.IsPlayers(gun) ? Settings.F(p + "Range") : 0f;
                var rangeP = Traverse.Create(gun).Field("range").GetValue<Parameter>();
                if (!(gun.IsAntiAircraftWeapon && (UboatToolkit.Modules.Warthog.WarthogWeapons.On || Util.WarthogModOwns(ship)))) Util.SetParameter(rangeP, km > 0f ? km * 1000f : 0f);

                // Muzzle velocity: own value in m/s (0 = game) times the multiplier.
                float mps = Settings.F(p + "ShellMps");
                float v = (mps > 0f ? mps : Util.Original<float>(gun, "shellSpeed")) * (speed > 0f ? speed : 1f);
                // So the shells really reach that far: at least the speed that throws a shell km*1000 m at the
                // gun's highest elevation (max 45 deg), plus 40 % for air drag.
                if (km > 0f)
                {
                    // the sign of "up" differs between guns, so take the smaller limit (safe side: shells rather too fast)
                    float elev = Mathf.Clamp(Mathf.Min(Mathf.Abs(Util.Original<float>(gun, "maxAngle")), Mathf.Abs(Util.Original<float>(gun, "minAngle"))), 15f, 45f);
                    float need = Mathf.Sqrt(km * 1000f * 9.81f / Mathf.Sin(2f * elev * Mathf.Deg2Rad)) * 1.4f;
                    v = Mathf.Max(v, need);
                }
                Util.SetField(gun, "shellSpeed", v);

                // Fire rate: own rounds per minute (0 = game) and then times the rate multiplier.
                float rateMul = Mathf.Max(0.01f, Settings.F(p + "RateMul") > 0f ? Settings.F(p + "RateMul") : 1f);
                var series = Traverse.Create(gun).Field("seriesTimeOffset").GetValue<Parameter>();
                Util.SetParameter(gun.MagazineSize, mag >= 1f ? Mathf.Round(mag) : 0f);
                Util.SetParameter(series, Gap(series, rpm, rateMul));
                // 0.4.22 (option audit): "Schuss pro Minute" sets the pause between two shots. For a gun that loads every
                // shot singly (88: magazine 1) that pause IS the reload time. For a magazine gun (flak) the reload time is
                // the magazine change: it only follows the rate multiplier, not the rounds per minute (was near instant).
                bool single = gun.MagazineSize.GetValueExcludingModifier(Util.ModifierName) <= 1.5f;
                Util.SetParameter(gun.ReloadTime, Gap(gun.ReloadTime, single ? rpm : 0f, rateMul));

                // AA guns of the player normally only pick aircraft as automatic targets.
                if (gun.IsAntiAircraftWeapon)
                {
                    int id = gun.GetInstanceID();
                    bool both = Settings.B("AAvsShips");
                    if (both && gun.TargetingMode == ArtilleryTargetingMode.Aircrafts)
                    {
                        gun.TargetingMode = ArtilleryTargetingMode.Ships | ArtilleryTargetingMode.Aircrafts;
                        aaSwitched.Add(id);
                    }
                    else if (!both && aaSwitched.Remove(id) && gun.TargetingMode == (ArtilleryTargetingMode.Ships | ArtilleryTargetingMode.Aircrafts))
                    {
                        gun.TargetingMode = ArtilleryTargetingMode.Aircrafts;
                    }
                }
            }

            // Was only applied when the switch changed, so after a boat change or loading a save the new boat
            // never got it ("Flak hat keine unendliche Munition"). Now kept on the current boat every tick.
            bool infinite = Settings.B("CheatInfiniteGunAmmo") && Settings.B("CheatInfiniteFlakAmmo");   // ship-wide flag only when both; one alone works per gun (Fire postfix)
            if (infinite || lastInfinite)
            {
                if (ship.InfiniteAmmunition != infinite) ship.InfiniteAmmunition = infinite;
                lastInfinite = infinite;
            }
        }
    }

    // 0.3.89 (DoMe 2026-11-16: "Hafenverteidigung hat immer noch hohe Mündungsgeschwindigkeit, Geschosse pfeilgerade").
    // The code check found no place where the Toolkit changes enemy guns, so every enemy gun is now measured at its
    // first shot (game value and value in use go to the log), and its muzzle velocity can be lowered in % of the game
    // value. The gun aims with the same value, so a slower shell flies a higher arc and takes longer.
    [HarmonyPatch(typeof(Gun), "Fire")]
    internal static class Weapons_EnemyFirePatch
    {
        private static readonly HashSet<string> seen = new HashSet<string>();
        private static void Prefix(Gun __instance)
        {
            if (__instance == null || Util.IsPlayers(__instance) || !EnemyGunnery.InScope(__instance.ParentEntity)) return;
            float game = Util.Original<float>(__instance, "shellSpeed");
            float now = Traverse.Create(__instance).Field("shellSpeed").GetValue<float>();
            float pct = Settings.B("EnemyShellSpeedOn") ? Mathf.Clamp(Settings.F("EnemyShellSpeedPct"), 20f, 100f) : 100f;
            float want = game * pct / 100f;
            if (Mathf.Abs(now - want) > 0.01f) Util.SetField(__instance, "shellSpeed", want);
            string key = __instance.name + "/" + (__instance.ParentEntity != null ? __instance.ParentEntity.name : "?");
            if (seen.Count < 200 && seen.Add(key))
                Util.Log("Gegner-Geschütz: " + key + " Spielwert " + game.ToString("0") + " m/s, vor dem Schuss " + now.ToString("0") + " m/s, jetzt " + want.ToString("0")
                    + " m/s, Treffsicherheit " + __instance.CurrentAccuracy.ToString("0.00"));
        }
    }

    // Runs around every shot of a player gun: better accuracy, and marks the shot for the sound limit.
    [HarmonyPatch(typeof(Gun), "Fire")]
    internal static class Weapons_FirePatch
    {
        public static Gun Current;
        public static readonly Dictionary<int, float> LastSound = new Dictionary<int, float>();

        private static void Prefix(Gun __instance)
        {
            if (!Util.IsPlayers(__instance)) return;
            Current = __instance;
            // 0.4.22 (option audit): the value is a lower limit now (0.3 = never worse than 30 %). Before it was added on
            // every shot on top of the game's own ranging, so even 0.1 was near perfect after a few shots.
            float acc = Mathf.Clamp01(Settings.F("GunnerAccuracy"));
            if (acc > 0f && __instance.CurrentAccuracy < acc) __instance.CurrentAccuracy = acc;
        }

        private static void Postfix(Gun __instance, bool ___manualAim)
        {
            // 0.4.22 (option audit): the game divides the crew's ranging step by the magazine size. Our bigger magazine
            // made the own crew range in that much slower (magazine 200 = practically never). The missing part of the
            // step is added here, so ranging is as fast as with the game's own magazine.
            if (___manualAim || !Util.IsPlayers(__instance)) return;
            try
            {
                float mag = (float)__instance.MagazineSize, orig = __instance.MagazineSize.GetValueExcludingModifier(Util.ModifierName);
                if (orig < 1f || mag <= orig + 0.5f) return;
                var obs = Traverse.Create(__instance).Field("parentObservator").GetValue<UBOAT.Game.Scene.Entities.Observator>();
                if (obs == null) return;
                float a = __instance.CurrentAccuracy;
                __instance.CurrentAccuracy = Mathf.Clamp01(a + (1f - a) * (float)obs.CrewAccuracy * (1f / orig - 1f / mag));
            }
            catch { }
        }

        // Finalizer, not Postfix: also when the game's Fire throws ("no ammunition left"), so no enemy shot is ever
        // taken for the player's (night penalty / aimbot values).
        private static System.Exception Finalizer(System.Exception __exception) { Current = null; return __exception; }
    }

    // Skips the firing sound when the same gun already played one a moment ago.
    [HarmonyPatch]
    internal static class Weapons_SoundPatch
    {
        private static IEnumerable<MethodBase> TargetMethods() =>
            typeof(SoundEffectsDatabase).GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => m.Name == "GetClip");

        private static bool Prefix(ref ISoundEffect __result)
        {
            var gun = Weapons_FirePatch.Current;
            float gap = Settings.F("GunSoundGap");
            if (gun == null || gap <= 0f) return true;
            int id = gun.GetInstanceID();
            float now = Time.time;
            if (Weapons_FirePatch.LastSound.TryGetValue(id, out var last) && now - last < gap)
            {
                __result = null;
                return false;
            }
            Weapons_FirePatch.LastSound[id] = now;
            return true;
        }
    }

    // With infinite ammo the game still fills a magazine only with what is in the ammo box (Reload: min(box, magazine)).
    // An empty box therefore meant an empty gun even with the cheat on. Now the magazine is always full.
    [HarmonyPatch]
    internal static class Weapons_InfiniteReload
    {
        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Gun), nameof(Gun.Reload));
            yield return AccessTools.Method(typeof(Gun), nameof(Gun.ReloadInstantly));
        }
        private static readonly AccessTools.FieldRef<Gun, int> AmmoCount = AccessTools.FieldRefAccess<Gun, int>("ammoCount");
        private static void Postfix(Gun __instance)
        {
            if (!Settings.B(__instance.IsAntiAircraftWeapon ? "CheatInfiniteFlakAmmo" : "CheatInfiniteGunAmmo") || !Util.IsPlayers(__instance)) return;
            AmmoCount(__instance) = Mathf.Max(1, (int)__instance.MagazineSize.Value);
        }
    }

    // With both target types on: the deck gun (88/105) looks for ships first, flak (incl. the 37 mm) for aircraft first;
    // only if none is found, the other type. Calls the game's own search twice (the inner call has one type only and
    // passes straight through this prefix).
    [HarmonyPatch(typeof(Gun), "FindBestTarget")]
    internal static class Weapons_PreferPatch
    {
        private static readonly MethodInfo Find = AccessTools.Method(typeof(Gun), "FindBestTarget");
        private static bool Prefix(Gun __instance, bool ships, bool allowAircraft, ref UBOAT.Game.Scene.Entities.Entity __result)
        {
            if (!ships || !allowAircraft || Find == null || !Settings.B("AAGunBothTargets") || !Util.IsPlayers(__instance)) return true;
            try
            {
                bool air = __instance.IsAntiAircraftWeapon;
                var first = Find.Invoke(__instance, new object[] { !air, air }) as UBOAT.Game.Scene.Entities.Entity;
                __result = first != null ? first : Find.Invoke(__instance, new object[] { air, !air }) as UBOAT.Game.Scene.Entities.Entity;
                Postfix(__instance, ref __result);
                return false;
            }
            catch { return true; }
        }
        // 0.3.38 (DoMe): aircraft far away = low priority. Our guns only take an aircraft as target when it is
        // within AAEngageKm; farther ones are ignored (ships still count), so no ammo is wasted.
        private static void Postfix(Gun __instance, ref UBOAT.Game.Scene.Entities.Entity __result)
        {
            if (__result == null || !(__result is UBOAT.Game.Scene.Entities.Aircraft) || !Settings.B("AASaveAmmo") || !Util.IsPlayers(__instance)) return;
            if (Vector3.Distance(__instance.transform.position, __result.transform.position) > Settings.F("AAEngageKm") * 1000f) __result = null;
        }
    }
}

namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::System.Linq;
    using global::System.Reflection;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Scene.Audio;
    using global::UBOAT.Game.Scene.Items;

    // The game sets every gun of the group to "Custom" (= hold fire) whenever a gunner starts at a gun whose mode
    // is not "Aircrafts" (alarm, attack dialog, crew change). The mode the player chose is put back here.
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Scene.Characters.Actions.UseGun), "StartMainCharacter")]
    internal static class Weapons_KeepGunModePatch
    {
        private static void Prefix(out System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<Gun, ArtilleryTargetingMode>> __state)
        {
            __state = null;
            if (!Settings.B("KeepGunMode")) return;
            __state = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<Gun, ArtilleryTargetingMode>>();
            foreach (var g in Util.Find<Gun>())
                if (g != null && Util.IsPlayers(g) && g.TargetingMode != ArtilleryTargetingMode.Custom)
                    __state.Add(new System.Collections.Generic.KeyValuePair<Gun, ArtilleryTargetingMode>(g, g.TargetingMode));
        }
        private static void Postfix(System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<Gun, ArtilleryTargetingMode>> __state)
        {
            if (__state == null) return;
            Util.Safe("Weapons.KeepMode", () =>
            {
                int n = 0;
                foreach (var kv in __state)
                    if (kv.Key != null && kv.Key.TargetingMode == ArtilleryTargetingMode.Custom) { kv.Key.TargetingMode = kv.Value; n++; }
                if (n > 0) Util.Log("Geschütze: Feuermodus von " + n + " Geschütz(en) behalten (Spiel wollte 'Feuer einstellen')");
            });
        }
    }
}


// ====================================================================================================
// Abschnitt: Gegner ist Pazifist (0.4.32)
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Items;

    // DoMe 2026-11-18: "cheat: Gegner ist Pazifist". Weakening the enemy is allowed, each effect with its own switch.
    // Scope: only weapons that do NOT belong to the player's boat and are not allied (same test as the enemy gun
    // spread, EnemyGunnery.InScope). The player's own weapons are never touched. Every blocked attack is counted and
    // written to the log once per minute, so the effect can be proven.
    internal static class EnemyPacifist
    {
        private static readonly Dictionary<string, int> counts = new Dictionary<string, int>();
        private static float nextLog;

        internal static bool Enemy(Component c)
        {
            if (c == null) return false;
            var e = c.GetComponentInParent<Entity>();
            return e != null && EnemyGunnery.InScope(e);
        }

        internal static void Count(string what)
        {
            int n; counts.TryGetValue(what, out n); counts[what] = n + 1;
            if (Time.unscaledTime < nextLog) return;
            nextLog = Time.unscaledTime + 60f;
            var sb = new System.Text.StringBuilder("Gegner-Cheats (nur fremde Waffen, nie eigene): ");
            foreach (var kv in counts) sb.Append(kv.Key).Append(" ").Append(kv.Value).Append("x, ");
            Util.Log(sb.ToString().TrimEnd(',', ' '));
            counts.Clear();
        }
    }

    [HarmonyPatch(typeof(Gun), "Fire")]
    internal static class EnemyPacifist_Gun
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Gun __instance, ref Shell __result)
        {
            if (!Settings.B("CheatPacifistGuns") || __instance == null || Util.IsPlayers(__instance) || !EnemyGunnery.InScope(__instance.ParentEntity)) return true;
            __result = null;
            EnemyPacifist.Count("Kanonenschuss verhindert");
            return false;
        }
    }

    [HarmonyPatch(typeof(BombLauncher), nameof(BombLauncher.LaunchSeries))]
    internal static class EnemyPacifist_Bombs
    {
        private static bool Prefix(BombLauncher __instance)
        {
            if (__instance == null) return true;
            bool bombs = __instance.UseBombs;
            if (!Settings.B(bombs ? "CheatPacifistBombs" : "CheatPacifistDepthCharges") || !EnemyPacifist.Enemy(__instance)) return true;
            EnemyPacifist.Count(bombs ? "Bombenwurf verhindert" : "Wasserbomben-Serie verhindert");
            return false;
        }
    }

    [HarmonyPatch(typeof(HedgehogLauncher), nameof(HedgehogLauncher.LaunchSalvo))]
    internal static class EnemyPacifist_Hedgehog
    {
        private static bool Prefix(HedgehogLauncher __instance)
        {
            if (!Settings.B("CheatPacifistHedgehog") || !EnemyPacifist.Enemy(__instance)) return true;
            EnemyPacifist.Count("Hedgehog-Salve verhindert");
            return false;
        }
    }

    [HarmonyPatch(typeof(RocketLauncher), nameof(RocketLauncher.TryLaunchSeries))]
    internal static class EnemyPacifist_Rockets
    {
        private static bool Prefix(RocketLauncher __instance)
        {
            if (!Settings.B("CheatPacifistRockets") || !EnemyPacifist.Enemy(__instance)) return true;
            EnemyPacifist.Count("Raketen verhindert");
            return false;
        }
    }

    [HarmonyPatch(typeof(NPCTorpedoLauncherGroup), "FindBestTarget")]
    internal static class EnemyPacifist_Torpedoes
    {
        private static void Postfix(NPCTorpedoLauncherGroup __instance, ref Entity __result)
        {
            if (__result == null || !Settings.B("CheatPacifistTorpedoes") || !EnemyPacifist.Enemy(__instance)) return;
            __result = null;
            EnemyPacifist.Count("Torpedo-Ziel verweigert");
        }
    }
}

// ====================================================================================================
// Abschnitt: Torpedo-Treffer im Log (0.4.38)
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Utilities;

    // DoMe 2026-11-18: "der frachter serpa pinto hat 4 torpedos gefressen und steht noch?" The log had no line for
    // hits on other ships. This part only reads: for every explosion of one of OUR torpedoes it writes which ships
    // were in the blast, their state before and after, and the damage value the game used. Changes nothing.
    [HarmonyPatch(typeof(DamageUtility), nameof(DamageUtility.ApplyDamage))]
    internal static class TorpedoHitLog_Patch
    {
        private static readonly Collider[] buf = new Collider[256];
        private static readonly List<Entity> ships = new List<Entity>();
        private static readonly List<float> before = new List<float>();
        private static readonly List<float> hullBefore = new List<float>();

        private static float HullOf(Entity e) { try { return e.Hull != null ? e.Hull.ProjectedHealth : -1f; } catch { return -1f; } }

        [HarmonyPriority(Priority.Last)]
        private static void Prefix(Entity appliedBy, Vector3 position, float damageRadius, DamageType damageType)
        {
            ships.Clear(); before.Clear(); hullBefore.Clear();
            var own = Util.PlayerShip;
            if (damageType != DamageType.Torpedo || own == null || appliedBy != own) return;
            int n = Physics.OverlapSphereNonAlloc(position, damageRadius * 1.35f, buf, 1090049, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n && i < buf.Length; i++)
            {
                var e = buf[i] != null ? buf[i].GetComponentInParent<Entity>() : null;
                if (e == null || ships.Contains(e)) continue;
                ships.Add(e); before.Add(e.Health); hullBefore.Add(HullOf(e));
            }
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Entity appliedBy, Vector3 position, float damageRadius, float damage, DamageType damageType)
        {
            var own = Util.PlayerShip;
            if (damageType != DamageType.Torpedo || own == null || appliedBy != own) return;
            if (ships.Count == 0) { Util.Log("Torpedo-Treffer: Explosion (Schaden " + damage.ToString("0") + ", Radius " + damageRadius.ToString("0.0") + " m), aber kein Schiff im Radius"); return; }
            for (int i = 0; i < ships.Count; i++)
            {
                var e = ships[i]; if (e == null) continue;
                string who = e == own ? "eigenes Boot" : (e.Country != null && own.Country != null ? e.Country.CountryCode + ", " + own.Country.GetRelationWith(e.Country) : "ohne Land");
                float hb = hullBefore[i], ha = HullOf(e);
                Util.Log("Torpedo-Treffer: " + e.Name + " (" + who + "), Abstand zur Explosion " + (e.transform.position - position).magnitude.ToString("0") + " m von Schiffsmitte, Schaden-Wert " + damage.ToString("0")
                    + ", Zustand " + (before[i] * 100f).ToString("0") + " % -> " + (e.Health * 100f).ToString("0") + " %"
                    + (hb >= 0f ? ", Rumpf " + (hb * 100f).ToString("0") + " % -> " + (ha * 100f).ToString("0") + " %" : ", ohne Rumpf-Daten"));
            }
            ships.Clear();
        }
    }
}
