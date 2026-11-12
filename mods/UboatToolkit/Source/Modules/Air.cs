using HarmonyLib;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Scene.Items;
using UBOAT.Game.Scene.Utilities;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // QoL #36: air / oxygen.
    // - Ventilation quieter (less noise for enemy hydrophones).
    // - Absorber (Kalipatrone) lasts longer.
    // - Electric compressor does not use up the air inside the boat.
    // - Under water: ventilation switches on by itself and gets an absorber from storage.
    // - Emergency oxygen: if the air still goes down under water, a bit more than the
    //   current use is produced, so it never runs out.
    public class Air : ModuleBase
    {
        private Modifier emergency;

        public override void Tick()
        {
            var ship = Util.PlayerShip;
            float noise = Settings.F("VentilationNoise");
            float absorber = Settings.F("AbsorberDuration");
            bool compressorFree = Settings.B("CompressorNoAir");

            foreach (var v in Util.Find<Ventilation>())
            {
                Util.ScaleParameter(Traverse.Create(v).Field("noise").GetValue<Parameter>(), Mathf.Max(0.01f, noise));
                if (Settings.B("AutoVentilation") && ship.SubmergedOrGoingToSubmerge)
                {
                    v.SetDisableFlag(DeviceDisableFlag.TurnedOff, false);
                    if (v.Item == null) GiveAbsorber(ship, v);
                }
            }
            foreach (var a in Util.Find<PotassiumAbsorber>())
                Util.ScaleParameter(a.Duration, absorber);
            foreach (var c in Util.Find<AirCompressor>())
                Util.ScaleParameter(Traverse.Create(c).Field("oxygenUsage").GetValue<Parameter>(), compressorFree ? 0.001f : 1f);

            EmergencyOxygen(ship);
        }

        private void EmergencyOxygen(UBOAT.Game.Scene.Entities.PlayerShip ship)
        {
            var oxygen = ship.GetResource("Oxygen");
            if (oxygen == null) return;
            if (emergency == null) emergency = oxygen.Gain.AddDeltaModifier("UboatToolkit Emergency Oxygen");
            if (!Settings.B("EmergencyOxygen") || !ship.SubmergedOrGoingToSubmerge) { emergency.Value = 0f; return; }
            float net = (float)oxygen.Gain - emergency.Value;      // what the boat does without us
            emergency.Value = net < 0f ? -net * 1.05f : 0f;        // just a bit more than the use
        }

        private static void GiveAbsorber(UBOAT.Game.Scene.Entities.PlayerShip ship, Ventilation v)
        {
            foreach (var s in Util.Find<Storage>())
            {
                if (s == v || s.IsShop) continue;
                for (int i = Util.Slots(s) - 1; i >= 0; i--)
                    if (s.GetItemAt(i) is PotassiumAbsorber)
                    {
                        StorageUtility.TransferInstantly(s, i, v, 0, 1f);
                        Util.Log("Lüftung: Absorber eingesetzt");
                        return;
                    }
            }
        }
    }
}
