using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Scene.Audio;
using UBOAT.Game.Scene.Items;

namespace UboatToolkit.Modules
{
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

        public override void Tick()
        {
            var ship = Util.PlayerShip;
            deck88 = null;
            foreach (var g in Util.Find<Gun>())
                if (g != null && !g.IsAntiAircraftWeapon && Util.IsPlayers(g)) { deck88 = g; break; }
            foreach (var gun in Util.Find<Gun>())
            {
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
                if (like88) minA = Mathf.Min(minA, Util.Original<float>(deck88, "minAngle"));
                float depress = Settings.F(p + "Depress");
                if (depress > 0f) minA = Mathf.Min(minA, -depress);
                Util.SetField(gun, "minAngle", minA);

                // Recoil (barrel jumps up after each shot, annoying when aiming yourself). 0 = none, 1 = game.
                if (Util.IsPlayers(gun))
                {
                    float rec = Mathf.Clamp(Settings.F(p + "Recoil"), 0f, 1f);
                    var tg = Traverse.Create(gun);
                    foreach (var f in new[] { "minVerticalRecoil", "maxVerticalRecoil", "horizontalRecoil" })
                    {
                        var par = tg.Field(f).GetValue<Parameter>();
                        if (par != null) par.GetScaleModifier(Util.ModifierName, true).Value = rec;
                    }
                }

                // Engagement range in km (0 = game, ~12 km for the 88). The crew only picks targets inside it.
                float km = Util.IsPlayers(gun) ? Settings.F(p + "Range") : 0f;
                var rangeP = Traverse.Create(gun).Field("range").GetValue<Parameter>();
                if (!(gun.IsAntiAircraftWeapon && UboatToolkit.Modules.Warthog.WarthogWeapons.On)) Util.SetParameter(rangeP, km > 0f ? km * 1000f : 0f);

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
                Util.SetParameter(gun.ReloadTime, Gap(gun.ReloadTime, rpm, rateMul));

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
            bool infinite = Settings.B("CheatInfiniteGunAmmo");
            if (infinite || lastInfinite)
            {
                if (ship.InfiniteAmmunition != infinite) ship.InfiniteAmmunition = infinite;
                lastInfinite = infinite;
            }
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
            float acc = Mathf.Clamp01(Settings.F("GunnerAccuracy"));
            if (acc > 0f) __instance.CurrentAccuracy = Mathf.Lerp(__instance.CurrentAccuracy, 1f, acc);
        }

        private static void Postfix() { Current = null; }
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
            if (!Settings.B("CheatInfiniteGunAmmo") || !Util.IsPlayers(__instance)) return;
            AmmoCount(__instance) = Mathf.Max(1, (int)__instance.MagazineSize.Value);
        }
    }
}
