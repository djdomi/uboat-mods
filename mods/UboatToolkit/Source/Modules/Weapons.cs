using HarmonyLib;
using UnityEngine;
using UBOAT.Game.Scene.Items;

namespace UboatToolkit.Modules
{
    // QoL #27: turn/elevation speed and rounds per minute for the 88 mm deck gun and the AA guns.
    // QoL #28: magazine size (a big magazine plus RPM gives continuous fire instead of shoot-and-reload).
    // Cheat: unlimited ammunition (uses the game's own "infinite ammunition" switch of the boat).
    public class Weapons : ModuleBase
    {
        private bool lastInfinite;

        public override void Tick()
        {
            var ship = Util.PlayerShip;
            foreach (var gun in ship.GetComponentsInChildren<Gun>(true))
            {
                string p = gun.IsAntiAircraftWeapon ? "AAGun" : "DeckGun";
                float turn = Settings.F(p + "TurnSpeed");
                float rpm = Settings.F(p + "RPM");
                float mag = Settings.F(p + "Magazine");

                float origTurn = Util.Original<float>(gun, "rotationSpeed");
                Util.SetField(gun, "rotationSpeed", origTurn * (turn > 0f ? turn : 1f));

                var t = Traverse.Create(gun);
                float shotGap = rpm > 0f ? 60f / rpm : 0f;
                Util.SetParameter(gun.MagazineSize, mag >= 1f ? Mathf.Round(mag) : 0f);
                Util.SetParameter(t.Field("seriesTimeOffset").GetValue<UBOAT.Game.Core.Data.Parameter>(), shotGap);
                Util.SetParameter(gun.ReloadTime, shotGap);
            }

            bool infinite = Settings.B("CheatInfiniteGunAmmo");
            if (infinite != lastInfinite)
            {
                ship.InfiniteAmmunition = infinite;
                lastInfinite = infinite;
            }
        }
    }
}
