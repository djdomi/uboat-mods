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

        public static string Prefix(Gun gun) => gun.IsAntiAircraftWeapon ? "AAGun" : "DeckGun";

        public override void Tick()
        {
            var ship = Util.PlayerShip;
            foreach (var gun in ship.GetComponentsInChildren<Gun>(true))
            {
                string p = Prefix(gun);
                float turn = Settings.F(p + "TurnSpeed");
                float rpm = Settings.F(p + "RPM");
                float mag = Settings.F(p + "Magazine");
                float speed = Settings.F(p + "ShellSpeed");

                Util.SetField(gun, "rotationSpeed", Util.Original<float>(gun, "rotationSpeed") * (turn > 0f ? turn : 1f));
                Util.SetField(gun, "shellSpeed", Util.Original<float>(gun, "shellSpeed") * (speed > 0f ? speed : 1f));

                float shotGap = rpm > 0f ? 60f / rpm : 0f;
                Util.SetParameter(gun.MagazineSize, mag >= 1f ? Mathf.Round(mag) : 0f);
                Util.SetParameter(Traverse.Create(gun).Field("seriesTimeOffset").GetValue<Parameter>(), shotGap);
                Util.SetParameter(gun.ReloadTime, shotGap);

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

            bool infinite = Settings.B("CheatInfiniteGunAmmo");
            if (infinite != lastInfinite)
            {
                ship.InfiniteAmmunition = infinite;
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
}
