using HarmonyLib;
using UnityEngine;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Scene.Environment;
using UBOAT.Game.Scene.Items;

namespace UboatToolkit.Modules
{
    // Deck gun and heavy sea: from wind force 4 the gun crew reloads slower and shots scatter more.
    public class SeaGun : ModuleBase
    {
        private const string Tag = "Toolkit Sea State";
        private Weather weather;

        public override void Tick()
        {
            if (weather == null) weather = Object.FindObjectOfType<Weather>();
            var ship = Util.PlayerShip;
            float bft = weather != null ? weather.SeaCondition : 0f;
            bool on = Settings.B("DeckGunSeaState") && ship != null;
            float rough = on ? Mathf.Clamp(bft - 3f, 0f, 6f) : 0f;
            foreach (var gun in Util.Find<Gun>())
            {
                if (gun == null || gun.IsAntiAircraftWeapon || !Util.IsPlayers(gun)) continue;
                Set(gun.ReloadTime, 1f + rough * 0.2f);
                var t = Traverse.Create(gun);
                float scatter = 1f + rough * 0.35f;
                Set(t.Field("horizontalRecoil").GetValue<Parameter>(), scatter);
                Set(t.Field("minVerticalRecoil").GetValue<Parameter>(), scatter);
                Set(t.Field("maxVerticalRecoil").GetValue<Parameter>(), scatter);
            }
        }

        private static void Set(Parameter p, float v)
        {
            if (p == null) return;
            Modifier m = null;
            foreach (var x in p.ScaleModifiers) if (x.Name == Tag) { m = x; break; }
            if (m == null) { if (Mathf.Approximately(v, 1f)) return; m = p.AddScaleModifier(Tag); }
            if (!Mathf.Approximately(m.Value, v)) m.Value = v;
        }
    }
}
