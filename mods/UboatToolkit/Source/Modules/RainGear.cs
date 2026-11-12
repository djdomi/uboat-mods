using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using DWS.Common.Characters;
using UnityEngine;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Characters.Navigation;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Environment;
using UBOAT.Game.Scene.Tasks;

namespace UboatToolkit.Modules
{
    // Rain coats for the deck watch. Replaces the game's rain coat task with our own pass:
    // coat on deck (always or only in rain), sou'wester only in rain or wind, the man you look
    // through is dressed too, gear the game or another mod put on is left alone.
    [HarmonyPatch(typeof(EquipRainCoats), "DoUpdate")]
    internal static class RainGear_Patch
    {
        private static readonly FieldInfo WeatherF = AccessTools.Field(typeof(EquipRainCoats), "weather");
        private static readonly FieldInfo ShipF = AccessTools.Field(typeof(EquipRainCoats), "playerShip");
        private static readonly FieldInfo CrewF = AccessTools.Field(typeof(EquipRainCoats), "playerCrew");
        private static bool givenUp;
        private static int failures;

        private static bool Prefix(ref float __result)
        {
            if (givenUp || !Settings.B("RainGearWind")) return true;
            float iv = Settings.F("RainGearInterval");
            __result = float.IsNaN(iv) || iv <= 0f ? 2f : Mathf.Max(0.25f, iv);
            try
            {
                RainGear.Tick(WeatherF?.GetValue(null) as Weather, ShipF?.GetValue(null) as PlayerShip, CrewF?.GetValue(null) as PlayerCrew);
                failures = 0;
            }
            catch (Exception e)
            {
                failures++;
                Util.Log("Regenkleidung: Fehler (" + failures + "/3) " + e.Message);
                if (failures >= 3) { givenUp = true; Util.Log("Regenkleidung: gebe an das Spiel zurück."); }
            }
            return false;
        }
    }

    internal static class RainGear
    {
        private const string SlotCoat = "Rain Coat", SlotCap = "Rain Coat Cap";
        private const string CoatBlack = "Character/Other/Rain Coat Black", CoatGray = "Character/Other/Rain Coat Gray", Cap = "Character/Other/Rain Coat Cap";
        private static readonly string[] CoatAssets = { CoatBlack, CoatGray };
        private static readonly string[] CapAssets = { Cap };
        private static readonly HashSet<int> foreignReported = new HashSet<int>();
        private static float nextPass, settleUntil;
        private static int lastShip;

        public static void Tick(Weather weather, PlayerShip ship, PlayerCrew crew)
        {
            float now = Time.unscaledTime;
            if (now < nextPass) return;
            nextPass = now + Mathf.Max(0.25f, Settings.F("RainGearInterval"));
            if (!weather || !ship || crew == null) return;
            int id = ship.GetInstanceID();
            if (id != lastShip) { lastShip = id; settleUntil = now + 30f; }   // let a fresh load settle first
            if (now < settleUntil) return;

            int mode = Mathf.RoundToInt(Settings.F("RainGearMode"));   // 0 rain only, 1 always on deck, 2 off
            bool sheltered = ship.IsSheltered;
            float rain = (float)weather.Rain;
            float bft = 0f;
            try { bft = weather.SeaCondition; } catch { }
            bool permits = Settings.B("RainGearSheltered") || !sheltered;
            bool coatWeather = mode == 1 || rain >= Settings.F("RainGearRain");
            bool coats = mode != 2 && permits && coatWeather;
            bool capWeather = rain >= Settings.F("RainGearCapRain") || bft >= Settings.F("RainGearCapWind");
            bool log = Settings.B("RainGearLog");

            for (int i = crew.Count - 1; i >= 0; i--)
            {
                var c = crew[i];
                if (!(bool)c || !c.isActiveAndEnabled) continue;
                var data = c.Data;
                if (data == null || data.CharacterType == PlayableCharacterData.Type.Captive) continue;
                if (c.IsImpersonated && !Settings.B("RainGearImpersonated")) continue;
                var nav = c.Navigator;
                if (nav == null) continue;
                var layer = nav.Layer;
                bool outside = layer == NavMeshLayer.UboatExterior || layer == NavMeshLayer.World;
                bool wantCoat = coats && outside;
                if (IsForeign(c, SlotCoat, CoatAssets))
                {
                    if (log && foreignReported.Add(c.GetInstanceID())) Util.Log("Regenkleidung: " + data.Name + " trägt fremden Mantel - bleibt unberührt.");
                    if (foreignReported.Count > 512) foreignReported.Clear();
                    continue;
                }
                bool wantCap = wantCoat && capWeather && (!c.IsSkipper || Settings.B("RainGearSkipperCap"));
                bool a = Apply(c, SlotCoat, wantCoat, CoatFor(c), CoatAssets);
                bool b = Apply(c, SlotCap, wantCap, Cap, CapAssets);
                if (log && (a || b))
                    Util.Log(string.Format("Regenkleidung: {0}{1} Ebene={2} Regen={3:0.###} Wind={4:0.#} geschützt={5} -> Mantel={6} Südwester={7}",
                        data.Name, c.IsImpersonated ? " [du]" : "", layer, rain, bft, sheltered, wantCoat, wantCap));
            }
        }

        private static bool Apply(Character c, string slot, bool wanted, string asset, string[] owned)
        {
            var cur = c.GetWearableAt(slot);
            bool occupied = (bool)cur;
            if (wanted)
            {
                if (occupied) return false;
                c.LoadWearableIfNotAlreadyLoadedAsync(slot, asset, true);
                return true;
            }
            foreach (var o in owned) c.RemoveLoadingWearables(o);
            if (occupied && IsOwned(cur, owned)) { c.RemoveWearable(cur, Character.WearableRemovalAction.Destroy); return true; }
            return false;
        }

        private static bool IsForeign(Character c, string slot, string[] owned)
        {
            var cur = c.GetWearableAt(slot);
            return (bool)cur && !IsOwned(cur, owned);
        }

        private static bool IsOwned(Wearable w, string[] owned)
        {
            var prefab = w.GetComponent<UBOAT.Game.Serialization.GameObjectPrefab>();
            if (!prefab) return false;
            foreach (var o in owned) if (string.Equals(o, prefab.PrefabPath, StringComparison.Ordinal)) return true;
            return false;
        }

        // The same man always gets the same coat colour.
        private static string CoatFor(PlayableCharacter c)
        {
            var key = c.Data != null && !string.IsNullOrEmpty(c.Data.Name) ? c.Data.Name : c.name;
            if (string.IsNullOrEmpty(key)) return CoatBlack;
            int h = 17;
            foreach (char ch in key) h = unchecked(h * 31 + ch);
            return (h & 1) == 0 ? CoatBlack : CoatGray;
        }
    }
}
