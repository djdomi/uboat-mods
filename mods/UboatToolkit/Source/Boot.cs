// UBOAT Toolkit - Boot: Tiefe, Tauchzellen, Luft, Luken, Ruder, Navigation, Schäden
// Diese Datei fasst 23 frühere Einzeldateien zusammen (0.4.30). Jeder Abschnitt nennt seine alte Datei.

// ====================================================================================================
// Abschnitt: Modules/Air.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::HarmonyLib;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Scene.Items;
    using global::UBOAT.Game.Scene.Utilities;
    using global::UnityEngine;

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
                // 0.4.28 (DoMe): two independent switches
                if (!ship.SubmergedOrGoingToSubmerge) continue;
                if (Settings.B("AutoVentilation") && v.HasDisableFlag(DeviceDisableFlag.TurnedOff) && !Silent())
                {
                    v.SetDisableFlag(DeviceDisableFlag.TurnedOff, false);
                    Util.Log("Lüftung: automatisch eingeschaltet (unter Wasser)");
                }
                if (Settings.B("AutoAbsorber") && v.Item == null) GiveAbsorber(ship, v);
            }
            foreach (var a in Util.Find<PotassiumAbsorber>())
                Util.ScaleParameter(a.Duration, absorber);
            foreach (var c in Util.Find<AirCompressor>())
                Util.ScaleParameter(Traverse.Create(c).Field("oxygenUsage").GetValue<Parameter>(), compressorFree ? 0.001f : 1f);

            EmergencyOxygen(ship);
        }

        // 0.4.29 (contradiction audit): silent running switches the ventilation off; the automatic switched it on again
        // every second. Silent running wins.
        private static float silentLog;
        internal static bool Silent()
        {
            try
            {
                var src = DWS.Common.InjectionFramework.InjectionFramework.Instance.GetInstance<UBOAT.Game.Scene.Tasks.ISilentRunningController>();
                if (src == null || !src.IsSilentRunningEnabled) return false;
                if (Time.unscaledTime > silentLog) { silentLog = Time.unscaledTime + 120f; Util.Log("Lüftung: Schleichfahrt ist an, die Automatik schaltet die Lüftung nicht ein"); }
                return true;
            }
            catch { return false; }
        }

        private void EmergencyOxygen(UBOAT.Game.Scene.Entities.PlayerShip ship)
        {
            var oxygen = ship.GetResource("Oxygen");
            if (oxygen == null) return;
            emergency = Util.OneDelta(oxygen.Gain, "UboatToolkit Emergency Oxygen");
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

// ====================================================================================================
// Abschnitt: Modules/AwashGuns.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Environment;

    // QoL #24: deck awash with usable guns.
    // The game closes the exterior hatch whenever the boat runs deck awash
    // (PlayerShip.ShouldExteriorHatchBeClosed), so the crew cannot reach the guns.
    // The guns themselves are only disabled when the boat is submerged (target depth > 0.5 m),
    // and deck awash uses 0.45 m, so opening the hatch is enough.
    // Rain and strong wind still close the hatch, like in the original game.
    [HarmonyPatch(typeof(PlayerShip), nameof(PlayerShip.ShouldExteriorHatchBeClosed), MethodType.Getter)]
    internal static class AwashGuns_HatchPatch
    {
        private static void Postfix(PlayerShip __instance, ref bool __result)
        {
            if (!Settings.B("AwashGuns") || !__result) return;
            if (__instance.DepthPreset != DepthPreset.DeckAwash) return;

            // Re-check the weather part of the original rule; only the "deck awash" reason is removed.
            var weather = Traverse.Create(typeof(PlayerShip)).Field("weather").GetValue<Weather>();
            if (weather == null) return;
            float rain = (float)weather.Rain;
            float wind = weather.TotalWindSpeed;
            bool badWeather = rain > 0.025f || (wind > 17f && !__instance.Docked);
            if (!badWeather) __result = false;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Ballast.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System;
    using global::UnityEngine;
    using global::UBOAT.Game.Scene.Entities;

    // QoL #23 ballast (all in one, own version):
    //  - surfaced: the diesel exhaust slowly blows the rest of the water out of the ballast tanks,
    //    so the boat rides fully surfaced without using compressed air;
    //  - diving: the ballast tanks flood faster.
    // Works on the same tank values the game itself sets (e.g. during quick travel).
    public class Ballast : ModuleBase
    {
        private float lastTime = -1f;

        private static readonly System.Collections.Generic.Dictionary<UBOAT.Game.Scene.Physics.Tank, double> lastWater = new System.Collections.Generic.Dictionary<UBOAT.Game.Scene.Physics.Tank, double>();
        private static int helped; private static float nextLog, nextTanks;
        public override void Tick()
        {
            float now = Time.time; // game time incl. time compression
            float dt = lastTime < 0f ? 0f : Mathf.Clamp(now - lastTime, 0f, 600f);
            lastTime = now;
            if (dt <= 0f) return;

            var ship = Util.PlayerShip;
            var tanks = ship.BallastTanks;
            if (tanks == null) return;

            // 0.4.27: once a minute (every 10 s while the boat hangs more than 3 degrees) the trim angle and every tank,
            // so a nose-down boat can be read from the log.
            float pitch = Mathf.DeltaAngle(0f, ship.transform.eulerAngles.x);      // + = bow down
            if (Time.unscaledTime > nextTanks)
            {
                nextTanks = Time.unscaledTime + (Mathf.Abs(pitch) > 3f ? 10f : 60f);
                var sb = new System.Text.StringBuilder();
                foreach (var t in tanks)
                    if (t != null && t.Capacity > 0f)
                        sb.Append(" [").Append(t.name).Append(" ").Append(t.TankType).Append(": Wasser ").Append((t.Water / t.Capacity * 100.0).ToString("0")).Append(" %, Luft ").Append(t.AirPressure.ToString("0.0")).Append(" bar")
                          .Append(t.Fuel > 0.0 ? ", Diesel " + (t.Fuel / t.Capacity * 100.0).ToString("0") + " %" : "").Append(", Lage längs ").Append(ship.transform.InverseTransformPoint(t.transform.position).z.ToString("0")).Append(" m]");
                Util.Log("Ballast: Neigung " + (pitch > 0f ? "Bug " + pitch.ToString("0.0") + "° nach unten" : "Bug " + (-pitch).ToString("0.0") + "° nach oben") + ", Deck " + ship.DeckDepthUnclamped.ToString("0.0") + " m, Zieltiefe " + ship.TargetDepth.ToString("0.0") +
                    " m, Schnelltauchen x" + Settings.F("BallastFastDive").ToString("0.##") + ", Diesel-Anblasen " + (Settings.B("BallastDieselBlow") ? "an" : "aus") + "." + sb);
            }

            bool surfaced = ship.DepthPreset == DepthPreset.Surface && !ship.SubmergedOrGoingToSubmerge && ship.TargetDepth < 0.05f;
            bool diving = ship.TargetDepth > 1f && ship.DepthPreset != DepthPreset.DeckAwash;

            if (surfaced && Settings.B("BallastDieselBlow"))
            {
                double rate = Settings.F("BallastDieselBlowRate") / 100.0;
                foreach (var t in tanks)
                    if (t != null && t.Water > 0.0)
                    {
                        // 0.4.22 (option audit): the place of the water is filled with air (surface pressure 1 bar), else
                        // the tank was left at low pressure and sucked water back in.
                        double before = t.Water;
                        t.Water = Math.Max(0.0, t.Water - t.Capacity * rate * dt);
                        double free = t.Capacity - t.Water - t.Fuel;
                        if (t.Air < free) t.Air = Math.Min(free, t.Air + (before - t.Water));
                    }
            }

            float fast = Settings.F("BallastFastDive");
            if (diving && fast > 1f)
            {
                double extra = 0.05 * (fast - 1f); // share of the tank per second on top of the game's own flooding
                // 0.4.22 (option audit): only tanks the game itself is flooding right now (water rose since the last look).
                // Before, every tank was filled to the brim whenever the ordered depth was > 1 m: the quick-dive tank was
                // refilled at periscope depth, fuel bunkers overfilled and the tank air was squeezed to nothing.
                foreach (var t in tanks)
                {
                    if (t == null) continue;
                    double was; bool known = lastWater.TryGetValue(t, out was);
                    double room = t.Capacity - t.Fuel - t.Air;              // free space that is not fuel and not air
                    if (known && t.Water > was + 1e-6 && t.Water < room)
                    {
                        t.Water = Math.Min(room, t.Water + t.Capacity * extra * dt);
                        helped++;
                    }
                    lastWater[t] = t.Water;
                }
                if (helped > 0 && Time.unscaledTime > nextLog) { nextLog = Time.unscaledTime + 30f; Util.Log("Schnelltauchen: " + helped + " mal eine Tauchzelle beschleunigt, die das Spiel gerade flutet (x" + fast.ToString("0.#") + ")"); helped = 0; }
            }
            else lastWater.Clear();
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Controls.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Linq;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Core.Input;
    using global::UBOAT.Game.Sandbox;
    using global::UBOAT.Game.Sandbox.Missions;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Environment;
    using global::UBOAT.Game.Scene.Environment.Terrain;
    using global::UBOAT.Game.UI;

    // Standing orders: crash dive on enemy aircraft, "leave port" button right after undocking.
    public class Controls : ModuleBase
    {
        private PlayerShip hooked;
        private bool wasAlarmed, armed = true;
        private SandboxEntity lastDock, leaveArea;

        public override void Tick()
        {
            var ship = Util.PlayerShip;
            if (ship == null) return;
            if (ship != hooked)
            {
                if (hooked != null) hooked.ObservationAdded -= OnSeen;
                ship.ObservationAdded += OnSeen;
                hooked = ship;
                lastDock = leaveArea = null;
            }
            if (!ship.Alarmed) armed = true;
            wasAlarmed = ship.Alarmed;
            if (Settings.B("LeavePortButton")) Util.Safe("Controls.LeavePort", () => LeavePort(ship));
        }

        // Engine telegraph on keys (Tasten tab).
        private static readonly string[] gearKeys = { "KeyGearStop", "KeyGear1", "KeyGear2", "KeyGear3", "KeyGear4", "KeyGear5", "KeyGearBack1", "KeyGearBack2", "KeyGearBack3", "KeyGearBack4" };
        private static readonly int[] gears = { 0, 1, 2, 3, 4, 5, -2, -3, -4, -5 };
        public override void Frame()
        {
            var ship = Util.PlayerShip;
            if (ship == null) return;
            for (int i = 0; i < gearKeys.Length; i++)
            {
                if (!Keys.Down(gearKeys[i])) continue;
                if (InputBlocked()) return;                                  // typing in a text field, menu open ...
                if (Settings.B("TelegraphBlockScopes") && AtScopeOrFreeCam()) return;   // no engine order by accident
                int g = gears[i];
                Util.Safe("Controls.Gear", () => ship.OrderEngineGear(g, false));
                if (Settings.B("TelegraphLog")) Util.Log("Maschinen-Telegraf: " + gearKeys[i]);
                return;
            }
        }

        private static readonly System.Reflection.FieldInfo GameInputF = AccessTools.Field(typeof(UBOAT.Game.Scene.Camera.MainCamera), "gameInput");
        private static UBOAT.Game.Core.Input.GameInput gameInput;
        private static bool InputBlocked()
        {
            if (gameInput == null && GameInputF != null) gameInput = GameInputF.GetValue(null) as UBOAT.Game.Core.Input.GameInput;
            return gameInput != null && gameInput.HasActiveBlockades();
        }

        private static bool AtScopeOrFreeCam()
        {
            var cam = UBOAT.Game.Scene.Camera.MainCamera.Instance;
            if (cam == null) return false;
            if (cam.CurrentMode == UBOAT.Game.Scene.CameraMode.FreeFly) return true;
            return cam.GenericController != null && cam.GenericController.ExternalController is UBOAT.Game.Scene.Camera.ExternalControllers.PeriscopeController;
        }

        private void OnSeen(DirectObservationAddedEvent e)
        {
            if (!Settings.B("AutoCrashDive")) return;
            Util.Safe("Controls.CrashDive", () =>
            {
                var ship = hooked;
                var plane = e.Observation?.Entity as Aircraft;
                if (ship == null || plane == null || plane.FoldedUp || plane.Country == null) return;
                if (plane.Country.GetRelationWith(ship.Country) != Country.Relation.Enemy) return;
                if (ship.Docked || ship.SubmergedOrGoingToSubmerge || wasAlarmed || !armed) return;
                armed = false;
                ship.StartAlarm("Crash Diving");
                ship.FollowDiveSchedule = false;
                Owners.Preset(ship, "Alarmtauchen bei Flugzeug", Owners.Safety, DepthPreset.MaxSafeDepth, false);
                ship.OrderEngineGear(5, false);
                Util.Log("Alarmtauchen: feindliches Flugzeug gesichtet");
            });
        }

        private void LeavePort(PlayerShip ship)
        {
            if (ship.CurrentDock != null && ship.CurrentDock != lastDock)
            {
                lastDock = ship.CurrentDock;
                leaveArea = FindLeaveArea(lastDock);
            }
            if (lastDock == null || leaveArea == null || ship.Alarmed || ship.SandboxEntity == null) return;
            bool near = (ship.SandboxEntity.Position - lastDock.Position).sqrMagnitude < 25f;
            if (near && ship.CurrentQuickTravelTarget == null)
                ship.SetQuickTravelTarget(leaveArea, QuickTravelTargetType.LeavePortArea);
            else if (!near && ship.CurrentQuickTravelTarget == leaveArea)
            {
                ship.ClearQuickTravelTarget();
                lastDock = null;
            }
        }

        private static SandboxEntity FindLeaveArea(SandboxEntity port)
        {
            var all = port.Group?.Entities;
            if (all == null) return null;
            var hit = all.FirstOrDefault(x => x.Name != null && x.Name.ToLower().Contains("leave port"));
            return hit;   // only the game's own "leave port" point; the far "undock" points could lie behind land
        }
    }

    // Tonnage sunk on the way already counts for the patrol assignment.
    [HarmonyPatch(typeof(PatrolAssignment), "PlayerCareerOnTonnageSunkChanged")]
    internal static class Controls_TonnagePatch
    {
        private static void Prefix(ref int ___tonnageSunkBeforeArrival) { if (Settings.B("TonnageBeforePatrol")) ___tonnageSunkBeforeArrival = 0; }
    }
    [HarmonyPatch]
    internal static class Controls_TonnageStagePatch
    {
        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(PatrolAssignment), "OnStageStarted", new[] { typeof(PatrolAssignment.Stages), typeof(bool) });
            yield return AccessTools.DeclaredMethod(typeof(PatrolAssignment), "OnStageResumed", new[] { typeof(PatrolAssignment.Stages) });
        }
        private static void Postfix(PatrolAssignment __instance)
        {
            if (Settings.B("TonnageBeforePatrol"))
                Util.Safe("Controls.Tonnage", () => AccessTools.Method(typeof(PatrolAssignment), "PlayerCareerOnTonnageSunkChanged").Invoke(__instance, null));
        }
    }

    // Map: shows the real water depth under the mouse next to the rough range.
    [HarmonyPatch(typeof(MapUI), "LateUpdate")]
    internal static class Controls_MapDepthPatch
    {
        private static void Postfix(Component ___depthLabel, GameInput ___gameInput, TerrainController ___terrainController, Weather ___weather, UBOAT.Game.Sandbox.Sandbox ___sandbox)
        {
            if (!Settings.B("MapActualDepth") || ___depthLabel == null || ___gameInput == null || ___terrainController == null || ___weather == null) return;
            var grid = ___terrainController.MainGrid;
            var pos = ___gameInput.HoveredScenePosition;
            float h = grid != null ? grid.GetInterpolatedHeight(pos.x, pos.z, float.NaN) : float.NaN;
            string word = "genau";
            if (float.IsNaN(h) && ___sandbox != null)
            {
                // far away: world elevation map (good to a few metres)
                h = WorldMaps.ElevationMap.GetAtWorldSpace(___sandbox.SceneToEncounterSpace(pos));
                word = "ca.";
            }
            if (float.IsNaN(h)) return;
            int depth = Mathf.RoundToInt(___weather.Sea.VerticalOffset - h);
            if (depth <= 0) return;
            var prop = Traverse.Create(___depthLabel).Property("text");
            string t = prop.GetValue<string>() ?? "";
            int i = t.IndexOf("\n(");
            if (i >= 0) t = t.Substring(0, i);
            prop.SetValue(t + "\n(" + word + ": " + depth + " m)");
        }
    }

    // Ports stock an even mix of all torpedo types the country has, instead of random luck.
    [HarmonyPatch(typeof(CargoSpawner), "Spawn", new[] { typeof(ISandboxEntity), typeof(EntityBlueprintSlot), typeof(HashSet<string>) })]
    internal static class Controls_TorpedoMixPatch
    {
        private static readonly Dictionary<ISandboxEntity, int> turn = new Dictionary<ISandboxEntity, int>();

        // "G7a Torpedo T1 - Pi3 - LUT" -> base "G7a Torpedo T1 - LUT", pistol 3 (Pi1.1 -> 1.1)
        internal static string BaseName(string name) => System.Text.RegularExpressions.Regex.Replace(name, @" - Pi[0-9.]+", "");
        internal static float Pistol(string name)
        {
            var m = System.Text.RegularExpressions.Regex.Match(name, @" - Pi([0-9]+(\.[0-9]+)?)");
            return m.Success && float.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0f;
        }
        internal static bool IsMine(string name) => name != null && (name.IndexOf("Mine", StringComparison.OrdinalIgnoreCase) >= 0 || name.StartsWith("TM"));

        private static readonly Dictionary<ISandboxEntity, EntityBlueprintSlot> t5Slots = new Dictionary<ISandboxEntity, EntityBlueprintSlot>();
        private static readonly Dictionary<ISandboxEntity, EntityBlueprintSlot> mineSlots = new Dictionary<ISandboxEntity, EntityBlueprintSlot>();
        private static bool Prefix(ISandboxEntity entity, EntityBlueprintSlot slot, ref SandboxEquipment __result, WorldState ___worldState)
        {
            ours = false;
            TorpedoTab.Load();
            bool pick = TorpedoTab.PortPick.Count > 0;
            // 0.4.22 (option audit): "nur neueste Zündpistole" alone no longer runs the whole mixing logic (even turns,
            // first slot a mine). Then the game picks the torpedo itself and only its pistol is swapped (Postfix).
            if ((!Settings.B("TorpedoMix") && !pick && !Settings.B("PortT5Min")) || slot == null || slot.EquipmentTag != "Torpedo") return true;
            var se = entity as SandboxEntity;
            if (se == null || se.Blueprint.Type.Category != EntityTypeCategory.Port || se.Country == null) return true;
            var pool = se.Country.GetReservesByTagID(slot.EquipmentTagID);
            if (pool == null) return true;
            var list = pool.Where(x => x?.ItemData != null).OrderBy(x => x.ItemData.Name).ToList();
            if (pick)
            {
                var chosen = list.Where(x => TorpedoTab.PortPick.Contains(x.ItemData.Name) || IsMine(x.ItemData.Name)).ToList();
                if (chosen.Count > 0) list = chosen;
                else if (!Settings.B("TorpedoMix")) return true;
            }
            if (list.Count == 0) return true;
            // 0.3.50 (DoMe): only the newest firing pistol of each torpedo type ("... - Pi1/Pi2/..." -> highest)
            if (Settings.B("TorpedoLatestPistol"))
            {
                var newest = list.GroupBy(x => BaseName(x.ItemData.Name)).Select(g => g.OrderByDescending(x => Pistol(x.ItemData.Name)).First()).ToList();
                if (newest.Count > 0) list = newest.OrderBy(x => x.ItemData.Name).ToList();
            }
            turn.TryGetValue(entity, out int n);
            turn[entity] = n + 1;
            // Mines share the "Torpedo" slot: the first slot of every port is always a mine (if the country has any),
            // so mine-laying missions stay possible.
            var mines = list.Where(x => IsMine(x.ItemData.Name)).ToList();
            // 0.3.45: the same slot gets a mine on EVERY restock (before: only the first spawn per session)
            bool mineSlot = false;
            if (mines.Count > 0)
            {
                if (!mineSlots.TryGetValue(entity, out var ms)) { mineSlots[entity] = slot; mineSlot = true; }
                else mineSlot = ReferenceEquals(ms, slot);
            }
            // 0.3.51 (DoMe): one more slot per port always holds T5 Zaunkönig, at least 4 of them
            bool t5Slot = false;
            var t5s = list.Where(x => x.ItemData.Name.IndexOf("T5", StringComparison.Ordinal) >= 0 && !IsMine(x.ItemData.Name)).OrderByDescending(x => Pistol(x.ItemData.Name)).ToList();
            if (!mineSlot && Settings.B("PortT5Min") && t5s.Count > 0)
            {
                if (!t5Slots.TryGetValue(entity, out var ts)) { t5Slots[entity] = slot; t5Slot = true; }
                else t5Slot = ReferenceEquals(ts, slot);
            }
            var data = (mineSlot ? mines[n % mines.Count] : t5Slot ? t5s[0] : list[n % list.Count]).ItemData;
            if (mineSlot) Util.Log("Hafen-Laden: Mine im ersten Torpedo-Platz (" + data.Name + ")");
            se.Country.ConsumeItemFromReserves(data);
            float price = entity is SandboxDock dock && ___worldState != null ? dock.GetBuyPriceMultiplier(data) / ___worldState.BuyPriceMultiplier : 1f;
            var eq = (SandboxEquipment)Activator.CreateInstance(data.Type, se, slot, data);
            eq.Count = Mathf.Max(1f, Mathf.Round(UnityEngine.Random.Range(0.3f, 1f) * slot.Capacity / data.Mass / price));
            if (t5Slot && eq.Count < 4f) eq.Count = 4f;
            eq.Initialize();
            if (t5Slot) Util.Log("Hafen-Laden: " + eq.Count.ToString("0") + "x " + data.Name);
            __result = eq;
            ours = true;
            return false;
        }

        private static bool ours;   // this call's item was made by the mixing logic above
        private static int swapLines;
        private static void Postfix(ISandboxEntity entity, EntityBlueprintSlot slot, ref SandboxEquipment __result)
        {
            if (ours || __result == null || !Settings.B("TorpedoLatestPistol") || slot == null || slot.EquipmentTag != "Torpedo") return;
            try
            {
                var se = entity as SandboxEntity;
                if (se == null || se.Blueprint.Type.Category != EntityTypeCategory.Port || se.Country == null || __result.Data == null) return;
                string name = __result.Data.Name;
                if (IsMine(name)) return;
                var pool = se.Country.GetReservesByTagID(slot.EquipmentTagID);
                if (pool == null) return;
                string bn = BaseName(name); float have = Pistol(name);
                var best = pool.Where(x => x?.ItemData != null && BaseName(x.ItemData.Name) == bn && Pistol(x.ItemData.Name) > have).OrderByDescending(x => Pistol(x.ItemData.Name)).FirstOrDefault();
                if (best == null) return;
                var eq = (SandboxEquipment)Activator.CreateInstance(best.ItemData.Type, se, slot, best.ItemData);
                eq.Count = __result.Count;
                eq.Initialize();
                __result = eq;
                if (swapLines < 20) { swapLines++; Util.Log("Hafen-Laden: neueste Zündpistole, " + name + " -> " + best.ItemData.Name); }
            }
            catch (Exception e) { if (swapLines < 20) { swapLines++; Util.Log("Hafen-Laden: Zündpistole tauschen Fehler " + e.GetType().Name); } }
        }
    }

    // No scrolling of the section view when the mouse touches the screen edge.
    [HarmonyPatch(typeof(UBOAT.Game.Scene.Camera.SectionViewCameraController), "ParseMouse")]
    internal static class Controls_EdgeScrollPatch
    {
        private static void Prefix(ref bool ___wasOnCentralPartOfScreen) { if (Settings.B("NoEdgeScroll")) ___wasOnCentralPartOfScreen = false; }
    }
    // No jump to deck view when the mouse touches the top/bottom edge.
    [HarmonyPatch(typeof(UBOAT.Game.Scene.Camera.SectionViewCameraController), "Update")]
    internal static class Controls_EdgeDeckPatch
    {
        public static float Top() => Settings.B("NoEdgeDeck") ? 2f : 0.98f;
        public static float Bottom() => Settings.B("NoEdgeDeck") ? -1f : 0.02f;
        private static IEnumerable<HarmonyLib.CodeInstruction> Transpiler(IEnumerable<HarmonyLib.CodeInstruction> code)
        {
            var list = code.ToList();
            var top = list.Where(c => c.opcode == System.Reflection.Emit.OpCodes.Ldc_R4 && c.operand is float f && f == 0.98f).ToList();
            var bottom = list.Where(c => c.opcode == System.Reflection.Emit.OpCodes.Ldc_R4 && c.operand is float f && f == 0.02f).ToList();
            if (top.Count != 1 || bottom.Count != 1) return list;   // game changed: leave untouched
            top[0].opcode = System.Reflection.Emit.OpCodes.Call; top[0].operand = AccessTools.Method(typeof(Controls_EdgeDeckPatch), "Top");
            bottom[0].opcode = System.Reflection.Emit.OpCodes.Call; bottom[0].operand = AccessTools.Method(typeof(Controls_EdgeDeckPatch), "Bottom");
            return list;
        }
    }

}

namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Linq;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Core.Input;
    using global::UBOAT.Game.Sandbox;
    using global::UBOAT.Game.Sandbox.Missions;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Environment;
    using global::UBOAT.Game.Scene.Environment.Terrain;
    using global::UBOAT.Game.UI;

    // Game bug: OrderEngineGear throws IndexOutOfRange for a gear the boat doesn't have (UI telegraph). The order is
    // simply dropped instead of an exception popping the dev console; the gear number is logged once to find the source.
    [HarmonyPatch(typeof(PlayerShip), nameof(PlayerShip.OrderEngineGear))]
    internal static class Fix_OrderEngineGearRange
    {
        private static readonly HashSet<int> logged = new HashSet<int>();
        private static System.Exception Finalizer(System.Exception __exception, int gearIndex)
        {
            if (!(__exception is System.IndexOutOfRangeException)) return __exception;
            if (logged.Add(gearIndex)) Util.Log("Fahrstufe " + gearIndex + " gibt es auf diesem Boot nicht, Befehl ignoriert");
            return null;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/CourseKeys.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Reflection;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Scene.Camera;
    using global::UBOAT.Game.Scene.Camera.ExternalControllers;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene;

    // Course and depth keys: arrows turn 90° (Shift 45°, Ctrl 5°, Ctrl+Shift 1°), Down = turn about,
    // Up = rudder amidships, the turn is ordered when the modifiers are let go. With a modifier,
    // releasing F/R changes depth by 20/5/1 m; Ctrl+F+R holds the current depth; Ctrl+P periscope
    // depth (Ctrl+Shift+P exactly 20 m); Ctrl+C max safe depth; Ctrl+CapsLock locks course keys.
    // The compass port/starboard buttons follow the same step size.
    internal static class CourseCfg
    {
        public static bool On => Settings.B("CourseKeys");
        public static KeyCode K(string key, KeyCode def) => Keys.Code(key, def);
        public static bool Held(KeyCode k)
        {
            if (k == KeyCode.None) return false;
            if (Input.GetKey(k)) return true;
            if (k == KeyCode.LeftControl || k == KeyCode.RightControl) return Keys.Ctrl;
            if (k == KeyCode.LeftShift || k == KeyCode.RightShift) return Keys.Shift;
            if (k == KeyCode.LeftAlt || k == KeyCode.RightAlt) return Keys.Alt;
            return false;
        }
        public static bool Down(KeyCode k) => k != KeyCode.None && !Keys.Typing && Input.GetKeyDown(k);
        public static bool Up(KeyCode k) => k != KeyCode.None && !Keys.Typing && Input.GetKeyUp(k);
        public static KeyCode Fine => K("KeyCourseFine", KeyCode.LeftControl);
        public static KeyCode Coarse => K("KeyCourseCoarse", KeyCode.LeftShift);
        public static bool AnyModifier() => Held(Fine) || Held(Coarse);
        public static float Step() { bool f = Held(Fine), c = Held(Coarse); return f && c ? 1f : c ? 45f : f ? 5f : 90f; }
        public static void Log(string m) { if (Settings.B("CourseLog")) Util.Log("Kurs/Tiefe: " + m); }

        private static readonly FieldInfo GameInputF = AccessTools.Field(typeof(MainCamera), "gameInput");
        private static UBOAT.Game.Core.Input.GameInput gameInput;
        public static bool InputBlocked()
        {
            if (gameInput == null && GameInputF != null) gameInput = GameInputF.GetValue(null) as UBOAT.Game.Core.Input.GameInput;
            return gameInput != null && gameInput.HasActiveBlockades();
        }
        public static bool InFreeCam() { var c = MainCamera.Instance; return c != null && c.CurrentMode == CameraMode.FreeFly; }
        public static bool AtScope() { var c = MainCamera.Instance; return c != null && c.GenericController != null && c.GenericController.ExternalController is PeriscopeController; }

        public static readonly MethodInfo SetTargetCourse = AccessTools.Method(typeof(PlayerShip), "SetTargetCourse", new[] { typeof(float) });
        public static readonly MethodInfo SetRudderAmidships = AccessTools.Method(typeof(PlayerShip), "SetRudderAmidships");
        public static readonly MethodInfo SetTargetDepth = AccessTools.Method(typeof(PlayerShip), "SetTargetDepth", new[] { typeof(float), typeof(bool), typeof(bool) });
        public static readonly MethodInfo SetDepthPreset = AccessTools.Method(typeof(PlayerShip), "SetDepthPreset", new[] { typeof(DepthPreset), typeof(bool) });
        public static bool Ready => SetTargetCourse != null && SetRudderAmidships != null && SetTargetDepth != null && SetDepthPreset != null;
    }

    [HarmonyPatch(typeof(PlayerShip), "Update")]
    internal static class CourseKeys_Patch
    {
        private static float pendingCourse, pendingDepth;
        private static bool blocker, chordUsed, errorLogged;

        private static void Postfix(PlayerShip __instance)
        {
            if (!CourseCfg.On || !CourseCfg.Ready) return;
            try
            {
                if (CourseCfg.InputBlocked()) return;
                bool atScope = false;
                if (Settings.B("CourseBlockScopes")) { if (CourseCfg.InFreeCam()) return; atScope = CourseCfg.AtScope(); }
                bool fine = CourseCfg.Held(CourseCfg.Fine), coarse = CourseCfg.Held(CourseCfg.Coarse);
                Course(__instance, fine, coarse, atScope);
                if (!atScope) Depth(__instance, fine, coarse);
            }
            catch (Exception e) { if (!errorLogged) { errorLogged = true; Util.Log("Kurs/Tiefe: Tasten fehlgeschlagen (einmalig): " + e); } }
        }

        // At a scope only course keys on the arrow keys still work (they clash with no station control).
        private static bool CourseDown(KeyCode k, bool atScope) =>
            (!atScope || k == KeyCode.LeftArrow || k == KeyCode.RightArrow || k == KeyCode.UpArrow || k == KeyCode.DownArrow) && CourseCfg.Down(k);

        private static void Course(PlayerShip ship, bool fine, bool coarse, bool atScope)
        {
            if (!atScope && fine && CourseCfg.Down(CourseCfg.K("KeyCourseBlocker", KeyCode.CapsLock)))
            {
                blocker = !blocker;
                Util.Log("Kurs/Tiefe: Kurs-Sperre " + (blocker ? "an" : "aus"));
            }
            bool blocked = ship.Docked || blocker;
            float step = fine && coarse ? 1f : coarse ? 45f : fine ? 5f : 90f;
            if (CourseDown(CourseCfg.K("KeyCoursePort", KeyCode.LeftArrow), atScope)) pendingCourse -= step;
            else if (CourseDown(CourseCfg.K("KeyCourseStarboard", KeyCode.RightArrow), atScope)) pendingCourse += step;
            else if (CourseDown(CourseCfg.K("KeyCourseAbout", KeyCode.DownArrow), atScope)) pendingCourse += 180f;
            if (CourseDown(CourseCfg.K("KeyCourseAmidships", KeyCode.UpArrow), atScope) && !blocked) CourseCfg.SetRudderAmidships.Invoke(ship, null);
            if (!fine && !coarse && pendingCourse != 0f && !blocked)
            {
                CourseCfg.SetTargetCourse.Invoke(ship, new object[] { pendingCourse });
                CourseCfg.Log("Kurs geändert um " + pendingCourse + "°");
                pendingCourse = 0f;
            }
        }

        // 0.4.28: one owner for the ordered depth. A preset "None" is only the step before a metre value: Owners.Depth sets it itself.
        private static void Preset(PlayerShip s, DepthPreset p, bool instant) { if (p != DepthPreset.None) Owners.Preset(s, "Kurs-/Tiefentasten (du)", Owners.Player, p, instant); }
        private static void SetDepth(PlayerShip s, float d, bool instant) { Owners.Depth(s, "Kurs-/Tiefentasten (du)", Owners.Player, d, false, instant); }

        private static void Depth(PlayerShip ship, bool fine, bool coarse)
        {
            var deeper = CourseCfg.K("KeyCourseDeeper", KeyCode.F);
            var shallower = CourseCfg.K("KeyCourseShallower", KeyCode.R);
            bool dHeld = CourseCfg.Held(deeper), sHeld = CourseCfg.Held(shallower);
            if (chordUsed && !dHeld && !sHeld) { chordUsed = false; pendingDepth = 0f; }
            if (fine && dHeld && CourseCfg.Down(shallower))
            {
                chordUsed = true;
                float held = Mathf.Max(0f, ship.DeckDepthUnclamped);   // 0.4.22: deck line like the game's target depth (was the tower top: boat rose)
                Preset(ship, DepthPreset.None, true); SetDepth(ship, held, true);
                CourseCfg.Log("Tiefe gehalten bei " + held.ToString("F1") + " m");
                return;
            }
            if (fine && CourseCfg.Down(CourseCfg.K("KeyCoursePeriscope", KeyCode.P)))
            {
                if (coarse) { Preset(ship, DepthPreset.None, true); SetDepth(ship, 20f, true); CourseCfg.Log("Zieltiefe 20 m"); }
                else { Preset(ship, DepthPreset.Periscope, false); CourseCfg.Log("Sehrohrtiefe"); }
                return;
            }
            if (fine && CourseCfg.Down(CourseCfg.K("KeyCourseMaxDepth", KeyCode.C)))
            {
                Preset(ship, DepthPreset.MaxSafeDepth, false);
                CourseCfg.Log("größte sichere Tiefe");
                return;
            }
            if (!chordUsed)
            {
                float step = fine && coarse ? 1f : fine ? 5f : coarse ? 20f : 0f;
                if (CourseCfg.Up(deeper) && !sHeld) pendingDepth += step;
                else if (CourseCfg.Up(shallower) && !dHeld) pendingDepth -= step;
            }
            if (!fine && !coarse && pendingDepth != 0f)
            {
                float d = Mathf.Max(ship.TargetDepth + pendingDepth, 0f);
                Preset(ship, DepthPreset.None, true); SetDepth(ship, d, true);
                CourseCfg.Log("Tiefe auf " + d.ToString("F1") + " m");
                pendingDepth = 0f;
            }
        }
    }

    // A compass preset chosen while a modifier is held stays silent (no voice line per step).
    [HarmonyPatch(typeof(PlayerShip), "SetCompassPreset")]
    internal static class CourseKeys_SilentPreset
    {
        private static void Prefix(ref bool silent) { if (CourseCfg.On && CourseCfg.AnyModifier()) silent = true; }
    }

    // The compass port/starboard buttons turn by the modifier step (90/45/5/1°).
    [HarmonyPatch(typeof(PlayerShip), "ValidateCompassPreset")]
    internal static class CourseKeys_CompassStep
    {
        private static bool Prefix(ref CompassPreset ___compassPreset, PlayerShip __instance)
        {
            if (!CourseCfg.On || CourseCfg.SetTargetCourse == null) return true;
            try
            {
                if (___compassPreset == CompassPreset.CourseChangePort) { CourseCfg.SetTargetCourse.Invoke(__instance, new object[] { -CourseCfg.Step() }); return false; }
                if (___compassPreset == CompassPreset.CourseChangeStarboard) { CourseCfg.SetTargetCourse.Invoke(__instance, new object[] { CourseCfg.Step() }); return false; }
            }
            catch (Exception e) { Util.Log("Kurs/Tiefe: Kompass-Schritt fehlgeschlagen, Spielwert: " + e.Message); }
            return true;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/DamageReport.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.IO;
    using global::System.Text;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Items;
    using global::UBOAT.Game.Scene.Utilities;
    using global::UBOAT.Game.UI;
    using global::UnityEngine;

    // "What hit me": every damage to your boat is explained - what it was (shell, torpedo, depth charge,
    // bomb, mine, fire, collision, water pressure), who caused it (ship / aircraft, distance, bearing),
    // where it hit (compartment), how strong (hull and crew damage) and the boat's state at that moment
    // (depth, surfaced, speed). Shown as a game notification, kept in the F10 tab "Schaden" and written to
    // UboatToolkit/schadensbericht.txt. Repeats of the same cause within a few seconds are merged.
    public class DamageReport : ModuleBase
    {
        internal class Entry { public float Time; public string Clock; public string Text; public string Key; public int Count = 1; public float Hull, Crew; }
        internal static readonly List<Entry> History = new List<Entry>();
        private static bool subscribed;

        public override void Tick()
        {
            if (subscribed) return;
            DamageUtility.DamageApplied += OnDamage;
            subscribed = true;
        }

        private static void OnDamage(DamageEvent e)
        {
            try
            {
                if (!Settings.B("DamageReport") || e == null) return;
                var ship = Util.PlayerShip;
                if (ship == null || e.DamagedEntities == null || Array.IndexOf(e.DamagedEntities, ship) < 0) return;
                if (e.Damage <= 0f && e.CrewDamage <= 0f) return;

                string what = Cause(e);
                string who = Attacker(e, ship);
                string where = Where(ship, e.Position);
                string key = what + "|" + who + "|" + where;
                var last = History.Count > 0 ? History[History.Count - 1] : null;
                if (last != null && last.Key == key && Time.unscaledTime - last.Time < Mathf.Max(1f, Settings.F("DamageReportMerge")))
                {
                    last.Count++; last.Hull += e.Damage; last.Crew += e.CrewDamage; last.Time = Time.unscaledTime;
                    return;
                }
                string state = "Boot: " + (ship.Submerged ? Mathf.RoundToInt(ship.DeckDepth) + " m tief" : "aufgetaucht") +
                               ", " + (ship.AverageVelocity * 1.943844f).ToString("0.0") + " kn";
                var en = new Entry
                {
                    Time = Time.unscaledTime, Clock = DateTime.Now.ToString("HH:mm:ss"), Key = key, Hull = e.Damage, Crew = e.CrewDamage,
                    Text = what + (who.Length > 0 ? " von " + who : "") + " - Treffer: " + where + ". " + Why(e, ship) + " " + state + "."
                };
                History.Add(en);
                if (e.AppliedBy != null && e.AppliedBy != ship && e.DamageType != DamageType.Fire && e.DamageType != DamageType.WaterPressure)
                    UnderFire.Report(e.AppliedBy, "Treffer: " + what);
                if (Settings.B("DamageReportPause"))
                {
                    var gt = UnityEngine.Object.FindObjectOfType<UBOAT.Game.Core.Time.GameTime>();
                    if (gt != null && !gt.Paused && gt.IsPauseAllowed) gt.Paused = true;
                }
                if (History.Count > 200) History.RemoveAt(0);
                Write(en);
                if (Settings.B("DamageReportNotify"))
                {
                    var bar = UnityEngine.Object.FindObjectOfType<NotificationBarUI>();
                    string shortText = what + (who.Length > 0 ? " von " + who : "") + " - " + where;
                    if (bar != null) bar.OpenNow("Icons/Notification Bar/Notification - 40 - Danger", shortText);
                    else UboatToolkit.ToolkitRunner.ShowNotice(shortText, 6f);
                }
            }
            catch (Exception ex) { Util.Log("Schadensbericht: " + ex.Message); }
        }

        private static string Cause(DamageEvent e)
        {
            string src = e.DamageSource != null ? Clean(e.DamageSource.name).ToLowerInvariant() : "";
            switch (e.DamageType)
            {
                case DamageType.Projectile: return src.Contains("hedgehog") ? "Wasserbomben-Werfer (Igel)" : (e.AppliedBy is Aircraft ? "Bordwaffen-Beschuss eines Flugzeugs" : "Granate");
                case DamageType.Torpedo: return "Torpedo";
                case DamageType.Mine: return "Mine";
                case DamageType.Explosives: return src.Contains("depth") || src.Contains("charge") ? "Wasserbombe" : "Sprengladung";
                case DamageType.Bomb: return e.AppliedBy is Aircraft ? "Fliegerbombe / Wasserbombe vom Flugzeug" : "Bombe";
                case DamageType.Fire: return "Feuer an Bord";
                case DamageType.Collision: return "Zusammenstoß";
                case DamageType.WaterPressure: return "Wasserdruck";
                default: return "Ereignis aus der Mission";
            }
        }

        private static string Why(DamageEvent e, PlayerShip ship)
        {
            switch (e.DamageType)
            {
                case DamageType.WaterPressure: return "Grund: zu tief für den Rumpf (" + Mathf.RoundToInt(ship.DeckDepth) + " m).";
                case DamageType.Collision: return "Grund: Berührung mit " + (e.AppliedBy != null && e.AppliedBy != ship ? Clean(e.AppliedBy.name) : "Grund/Hindernis") + ".";
                case DamageType.Fire: return "Grund: Brand in diesem Raum.";
                case DamageType.Mine: return "Grund: in ein Minenfeld gefahren.";
                default:
                    if (e.AppliedBy == null || e.AppliedBy == ship) return "";
                    float d = Vector3.Distance(e.AppliedBy.transform.position, e.Position);
                    return "Detonation " + d.ToString("0") + " m vom Schützen entfernt, Schaden Rumpf " + e.Damage.ToString("0") +
                           (e.CrewDamage > 0f ? ", Crew " + e.CrewDamage.ToString("0") : "") + ".";
            }
        }

        private static string Attacker(DamageEvent e, PlayerShip ship)
        {
            var a = e.AppliedBy;
            if (a == null || a == ship) return "";
            Vector3 to = a.transform.position - ship.transform.position;
            float km = new Vector2(to.x, to.z).magnitude / 1000f;
            float abs = (Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg + 360f) % 360f;
            float rel = (abs - ship.transform.eulerAngles.y + 360f) % 360f;
            string kind = a is Aircraft ? "Flugzeug" : (a is Ship ? "Schiff" : "Gegner");
            return kind + " \"" + Clean(a.name) + "\" (" + km.ToString("0.0") + " km, Peilung " + Mathf.RoundToInt(abs).ToString("000") +
                   "°, " + Side(rel) + ")";
        }

        private static string Side(float rel)
        {
            if (rel < 22.5f || rel >= 337.5f) return "voraus";
            if (rel < 67.5f) return "Steuerbord voraus";
            if (rel < 112.5f) return "Steuerbord querab";
            if (rel < 157.5f) return "Steuerbord achtern";
            if (rel < 202.5f) return "achteraus";
            if (rel < 247.5f) return "Backbord achtern";
            if (rel < 292.5f) return "Backbord querab";
            return "Backbord voraus";
        }

        private static string Where(PlayerShip ship, Vector3 pos)
        {
            Compartment best = null; float bestD = float.MaxValue;
            foreach (var c in Util.Find<Compartment>())
            {
                if (c == null) continue;
                if (c.Contains(pos)) { best = c; break; }
                float d = (c.transform.position - pos).sqrMagnitude;
                if (d < bestD) { bestD = d; best = c; }
            }
            Vector3 local = ship.transform.InverseTransformPoint(pos);
            string part = local.z > 15f ? "Bug" : (local.z < -15f ? "Heck" : "Mitte");
            string side = local.x > 1f ? "Steuerbord" : (local.x < -1f ? "Backbord" : "mittig");
            string height = local.y > 2f ? ", über Wasser/Turm" : "";
            return (best != null ? German(Clean(best.name)) + " (" : "") + part + ", " + side + height + (best != null ? ")" : "");
        }

        private static readonly string[][] Words =
        {
            new[] { "Bow Torpedo Room", "Bug-Torpedoraum" }, new[] { "Stern Torpedo Room", "Heck-Torpedoraum" },
            new[] { "Torpedo Room", "Torpedoraum" }, new[] { "Control Room", "Zentrale" }, new[] { "Control", "Zentrale" },
            new[] { "Conning Tower", "Turm" }, new[] { "Diesel Engine", "Dieselraum" }, new[] { "Diesel", "Dieselraum" },
            new[] { "Electric Engine", "E-Maschinenraum" }, new[] { "Electric", "E-Maschinenraum" }, new[] { "Engine", "Maschinenraum" },
            new[] { "Galley", "Kombüse" }, new[] { "Officers", "Offiziersraum" }, new[] { "Petty Officers", "Unteroffiziersraum" },
            new[] { "Radio", "Funkraum" }, new[] { "Sonar", "Horchraum" }, new[] { "Captain", "Kommandantenraum" },
            new[] { "Quarters", "Quartier" }, new[] { "Deck", "Deck" }, new[] { "Exterior", "außen" }
        };

        private static string German(string n)
        {
            foreach (var w in Words) if (n.IndexOf(w[0], StringComparison.OrdinalIgnoreCase) >= 0) return w[1];
            return n;
        }

        private static string Clean(string n) => string.IsNullOrEmpty(n) ? "?" : n.Replace("(Clone)", "").Trim();

        private static void Write(Entry en)
        {
            try
            {
                Directory.CreateDirectory(Settings.Folder);
                File.AppendAllText(Path.Combine(Settings.Folder, "schadensbericht.txt"),
                    DateTime.Now.ToString("yyyy-MM-dd ") + en.Clock + "  " + en.Text + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        // F10 tab "Schaden": newest first.
        public static void Draw()
        {
            Imgui.Label("Schadensbericht: was hat uns getroffen, wo und warum (neueste oben). Datei: UboatToolkit/schadensbericht.txt");
            if (Imgui.Button("Liste leeren", 140)) History.Clear();
            Imgui.Space(4);
            UnderFire.DrawList();
            Imgui.Space(6);
            if (History.Count == 0) { Imgui.Label("Noch kein Schaden in dieser Sitzung."); return; }
            for (int i = History.Count - 1; i >= 0 && i >= History.Count - 50; i--)
            {
                var h = History[i];
                Imgui.Label(h.Clock + "  " + h.Text + (h.Count > 1 ? "  (x" + h.Count + ", zusammen Rumpf " + h.Hull.ToString("0") + ", Crew " + h.Crew.ToString("0") + ")" : ""), 660);
            }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/DepthGauge.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::System.Linq;
    using global::DWS.Common.InjectionFramework;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Environment.Terrain;
    using global::UnityEngine;

    // Own depth gauge + depth keys + keel-clearance autopilot (DoMe 2026-11-12).
    //  - Gauge: scale up to "DepthGaugeMax" m (no 260 m limit), current depth (white), target (yellow),
    //    sea floor (brown), history of the last minutes (dots). Click or mouse wheel on it sets the target.
    //  - Keys: "deeper"/"shallower" key alone = DepthStep m, with Shift x10, with Alt x100. Same for the wheel.
    //  - Autopilot: keeps "AutoDepthClearance" m of water under the keel, looks ahead along the course
    //    and rises early. Mode "Aufsetzen": sinks onto the sea floor and switches on silent running.
    public class DepthGauge : ModuleBase
    {
        private const float W = 170f, H = 320f;
        private static readonly List<float> history = new List<float>();
        private float nextSample, nextPilot, lastWarn, pilotSet = float.NaN, pilotHold;
        private static string pilotText = "";
        private static float lastBed = float.NaN;

        private static Rect Box() => new Rect(Mathf.Clamp(Screen.width * Settings.F("DepthGaugeXPos") / 100f, 0f, Screen.width - W), Mathf.Clamp(Screen.height * Settings.F("DepthGaugeY") / 100f, 0f, Screen.height - H), W, H);
        private static Rect Scale() { var r = Box(); return new Rect(r.x + 44, r.y + 26, 40, r.height - 70); }
        // Auto zoom: in shallow water the scale only goes a bit below the sea floor / the boat, in deep water up to the setting.
        private static float Max
        {
            get
            {
                float cap = Mathf.Max(30f, Settings.F("DepthGaugeMax"));
                var ship = Util.PlayerShip;
                if (ship == null || float.IsNaN(lastBed) || !Settings.B("DepthGaugeAutoZoom")) return cap;
                float need = Mathf.Max(lastBed, Mathf.Max(Current(ship), ship.TargetDepth)) * 1.25f;
                float step = need <= 50f ? 10f : need <= 200f ? 25f : 50f;
                return Mathf.Clamp(Mathf.Ceil(need / step) * step, 30f, cap);
            }
        }
        private static bool dragging; private static Vector2 dragOff; private static float lastClick = -1f;

        private static bool silentByUs;
        private static float Step()
        {
            float s = Mathf.Max(0.1f, Settings.F("DepthStep"));
            return Keys.Alt ? s * 100f : Keys.Shift ? s * 10f : s;
        }

        // Keel depth in "target depth" units: the game's target depth refers to the boat's origin, not the keel.
        private static float KeelOffset(PlayerShip ship)
        {
            // 0.3.56 (DoMe: autopilot measures the top, boat scrapes the sea floor): lowest hull point, not the centre.
            // 0.4.22 (option audit): ship.Depth is the TOP of the boat (tower), the game's target depth counts from the deck
            // line (DeckDepth). The old sum was too large by the height of the tower, so the boat stayed too high.
            float k = ship.BottomDepth - ship.DeckDepthUnclamped;
            if (k < 4f) k = 4f;                                          // keel at least 4 m below the origin (IXC draught ~4.7 m)
            float pitch = Mathf.Abs(Mathf.DeltaAngle(0f, ship.transform.eulerAngles.x));
            return k + 40f * Mathf.Sin(pitch * Mathf.Deg2Rad);           // bow/stern hang lower when the boat is tilted (half length ~40 m)
        }
        private static float Current(PlayerShip ship) => Mathf.Max(0f, ship.DeckDepthUnclamped);   // same value as the game's own depth meter and target depth

        // 0.4.28: the ordered depth is written only by Owners.Depth (one owner). "auto" = the autopilot asks.
        private static void SetTarget(PlayerShip ship, float depth) { SetTarget(ship, depth, false); }
        private static void SetTarget(PlayerShip ship, float depth, bool auto)
        {
            Owners.Depth(ship, auto ? "Bodenabstand-Autopilot" : "Tiefen-Anzeige (du)", auto ? Owners.Auto : Owners.Player, depth, true, false);
        }

        private bool lastPilotOn;

        public override void Frame()
        {
            var ship = Util.PlayerShip;
            if (ship == null) return;

            if (Settings.B("DepthHotkeys"))
            {
                bool deeper = !Keys.Typing && Keys.ModsHeld("KeyDepthDeeper") && Input.GetKeyDown(Keys.Code("KeyDepthDeeper", KeyCode.PageDown));
                bool higher = !Keys.Typing && Keys.ModsHeld("KeyDepthShallower") && Input.GetKeyDown(Keys.Code("KeyDepthShallower", KeyCode.PageUp));
                if ((deeper || higher) && !Keys.Ctrl)
                {
                    float s = Step() * (deeper ? 1f : -1f);
                    Util.Safe("Depth.Key", () => { SetTarget(ship, ship.TargetDepth + s); ToolkitRunner.ShowNotice("Zieltiefe " + ship.TargetDepth.ToString("F0") + " m", 2f); });
                }
            }
            // Speed factor right at the gauge (DoMe: no trip to F10 for long transits / overtaking a convoy)
            bool faster = Keys.Down("KeySpeedFaster"), slower = Keys.Down("KeySpeedSlower");
            if (faster || slower)
            {
                var o = Settings.Get("CheatSpeed");
                if (o != null)
                {
                    o.Value = Mathf.Clamp(Mathf.Round((o.Value + (faster ? 0.5f : -0.5f)) * 2f) / 2f, 1f, 5f);
                    Settings.Save();
                    ToolkitRunner.ShowNotice("Tempo x" + o.Value.ToString("0.0") + (o.Value <= 1f ? " (normal)" : ""), 2f);
                }
            }
            if (Keys.Down("KeyAutoDepth")) { var o = Settings.Get("AutoDepth"); if (o != null) { o.Value = o.Value > 0 ? 0 : 1; ToolkitRunner.ShowNotice("Bodenabstand-Autopilot " + (o.Value > 0 ? "AN" : "AUS"), 3f); } }
            if (Keys.Down("KeyAutoDepthBottom")) { var o = Settings.Get("AutoDepthBottom"); if (o != null) { o.Value = o.Value > 0 ? 0 : 1; ToolkitRunner.ShowNotice(o.Value > 0 ? "Aufsetzen auf den Grund" : "Aufsetzen beendet", 3f); } }

            if (Settings.B("DepthGauge"))
            {
                // Drag the box by its title line; the position is saved in percent of the screen.
                {
                    var bx = Box(); var mp = Input.mousePosition; var pp = new Vector2(mp.x, Screen.height - mp.y);
                    if (Input.GetMouseButtonDown(0) && new Rect(bx.x, bx.y, bx.width, 22).Contains(pp)) { dragging = true; dragOff = pp - bx.position; }
                    if (dragging && Input.GetMouseButton(0))
                    {
                        var np = pp - dragOff;
                        Settings.Set("DepthGaugeXPos", Mathf.Clamp(np.x / Screen.width * 100f, 0f, 100f));
                        Settings.Set("DepthGaugeY", Mathf.Clamp(np.y / Screen.height * 100f, 0f, 100f));
                    }
                    if (dragging && !Input.GetMouseButton(0)) { dragging = false; Util.Safe("Depth.SavePos", Settings.Save); }
                    if (dragging) return;
                }
                var sc = Scale();
                var m = Input.mousePosition; var p = new Vector2(m.x, Screen.height - m.y);
                var hit = new Rect(sc.x - 30, sc.y, sc.width + 60, sc.height);
                if (hit.Contains(p))
                {
                    // Double click only, so a stray click never sends the boat down.
                    if (Input.GetMouseButtonDown(0))
                    {
                        float now = Time.unscaledTime;
                        if (now - lastClick < 0.4f)
                        {
                            float d = (p.y - sc.y) / sc.height * Max;
                            Util.Safe("Depth.Click", () => SetTarget(ship, Mathf.Round(d)));
                            ToolkitRunner.ShowNotice("Zieltiefe " + Mathf.Round(d).ToString("F0") + " m", 2f);
                            lastClick = -1f;
                        }
                        else lastClick = now;
                    }
                    float wheel = Input.mouseScrollDelta.y;
                    if (Mathf.Abs(wheel) > 0.01f) Util.Safe("Depth.Wheel", () => SetTarget(ship, ship.TargetDepth - Mathf.Sign(wheel) * Step()));
                }
            }
        }

        public override void Tick() { }

        // Runs from Frame-like timing via the GUI pass; sampling + autopilot every 0.5 s real time.
        internal void Update(PlayerShip ship)
        {
            float now = Time.unscaledTime;
            if (now >= nextSample)
            {
                nextSample = now + Mathf.Max(0.2f, Settings.F("DepthGaugeSeconds") / 120f);
                history.Add(Current(ship));
                if (history.Count > 120) history.RemoveAt(0);
            }
            if (now < nextPilot) return;
            nextPilot = now + 0.5f;
            lastBed = Util.SafeGet(() => Seabed(ship, 0f), float.NaN);
            bool pilotOn = Settings.B("AutoDepth");
            if (pilotOn != lastPilotOn)
            {
                lastPilotOn = pilotOn;
                ToolkitRunner.ShowNotice("Bodenabstand-Autopilot " + (pilotOn ? "AN (Strg+B schaltet aus)" : "AUS"), 4f);
                Util.Log("Bodenabstand-Autopilot " + (pilotOn ? "an" : "aus"));
            }
            // 0.4.22 (option audit): "Aufsetzen" switched silent running on and never off again.
            if (silentByUs && !(pilotOn && Settings.B("AutoDepthBottom") && Settings.B("AutoDepthBottomSilent")))
            {
                silentByUs = false;
                Util.Safe("Depth.SilentOff", () =>
                {
                    var src = InjectionFramework.Instance.GetInstance<UBOAT.Game.Scene.Tasks.ISilentRunningController>();
                    if (src != null && src.IsSilentRunningEnabled) { src.SetSilentRunningEnabled(false, true); Util.Log("Aufsetzen aus: Schleichfahrt wieder aus"); }
                });
            }
            if (!pilotOn) { pilotText = ""; return; }
            // Player wants to surface or is in port: the autopilot never pushes the boat down then.
            if (ship.Docked || ship.DepthPreset == DepthPreset.Surface) { pilotText = "Autopilot pausiert (Auftauchen/Hafen)"; return; }

            float keel = KeelOffset(ship);
            if (Settings.B("AutoDepthBottom"))
            {
                if (float.IsNaN(lastBed)) { pilotText = "Aufsetzen: Grund unbekannt"; return; }
                float t = lastBed - keel - 0.3f;
                if (Mathf.Abs(ship.TargetDepth - t) > 0.5f) SetTarget(ship, t, true);
                if (Settings.B("AutoDepthBottomSilent")) Util.Safe("Depth.Silent", () =>
                {
                    var src = InjectionFramework.Instance.GetInstance<UBOAT.Game.Scene.Tasks.ISilentRunningController>();
                    if (src != null && !src.IsSilentRunningEnabled && ship.BottomDepth > lastBed - 5f) { src.SetSilentRunningEnabled(true, true); silentByUs = true; Util.Log("Aufsetzen: Schleichfahrt an"); }
                });
                pilotText = "Aufsetzen auf " + lastBed.ToString("F0") + " m";
                return;
            }

            // Shallowest sea floor now and ahead along the course.
            float look = Mathf.Max(0f, Settings.F("AutoDepthLookAhead"));
            float bed = lastBed;
            foreach (float dist in new[] { -40f, 40f, look * 0.25f, look * 0.5f, look })   // 0.3.56: also under stern and bow
            {
                float b = Util.SafeGet(() => Seabed(ship, dist), float.NaN);
                if (!float.IsNaN(b)) bed = float.IsNaN(bed) ? b : Mathf.Min(bed, b);
            }
            if (float.IsNaN(bed)) { pilotText = "Autopilot: Grund unbekannt"; return; }

            float target = bed - Mathf.Max(5f, Settings.F("AutoDepthClearance")) - keel;   // min 5 m: less scrapes the full-detail sea floor -> PhysX freeze + fling
            float maxD = Settings.F("AutoDepthMax");
            float minD = Settings.F("AutoDepthMin");
            if (target > maxD) target = maxD;
            if (target < minD)
            {
                // 0.3.57 (DoMe: boat scrapes the ground): in shallow water never force "min depth" into the sea floor;
                // go as deep as the water allows, at most up to the surface.
                target = Mathf.Max(0f, target);
                if (now - lastWarn > 30f)
                {
                    lastWarn = now;
                    Game.StopTimeCompression("Zu flach für den Bodenabstand! Grund bei " + bed.ToString("F0") + " m");
                }
            }
            // 0.4.29 (contradiction audit: the autopilot overwrote every other depth order every 0.5 s - game buttons,
            // periscope/snorkel depth, crash dive). Now: a depth order that is not from the autopilot, or a chosen depth
            // step of the game, is respected. The autopilot only steps in when that order would be deeper than the safe depth.
            if (!float.IsNaN(pilotSet) && Mathf.Abs(ship.TargetDepth - pilotSet) > 0.5f)
            {
                pilotHold = now + 60f; pilotSet = float.NaN;
                Util.Log("Bodenabstand: anderer Tiefenbefehl (" + ship.TargetDepth.ToString("F1") + " m), Autopilot wartet 60 s und greift nur ein, wenn das tiefer als sicher ist (" + target.ToString("F1") + " m)");
            }
            bool tooDeep = ship.TargetDepth > target + 0.5f;
            if (!tooDeep && (ship.DepthPreset != DepthPreset.None || now < pilotHold))
            {
                pilotText = "Autopilot wartet (dein Tiefenbefehl gilt), sicher bis " + target.ToString("F0") + " m";
                return;
            }
            if (Mathf.Abs(ship.TargetDepth - target) > 0.5f)
            {
                pilotSet = target;
                Util.Log("Bodenabstand: Grund=" + bed.ToString("F1") + " Kiel-Versatz=" + keel.ToString("F1") + " Ziel=" + target.ToString("F1")
                    + " altesZiel=" + ship.TargetDepth.ToString("F1") + " Depth=" + ship.Depth.ToString("F1") + " Offset=" + ship.VerticalOriginOffset.ToString("F1") + " Unterkante=" + ship.BottomDepth.ToString("F1"));
                SetTarget(ship, target, true);
            }
            pilotText = "Autopilot: Ziel " + target.ToString("F0") + " m, Grund " + bed.ToString("F0") + " m";
        }

        // Sea floor depth (m below surface) at "ahead" metres in front of the boat. Precise loaded terrain first,
        // otherwise the game's coarse world elevation map (km grid).
        private static float Seabed(PlayerShip ship, float ahead)
        {
            var fwd = ship.transform.forward; fwd.y = 0f; if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward; fwd.Normalize();
            var pos = ship.transform.position + fwd * ahead;
            var tc = Util.Find<TerrainController>(30f).FirstOrDefault();
            if (tc != null && tc.MainGrid != null)
            {
                float h = tc.MainGrid.GetInterpolatedHeight(pos.x, pos.z, float.NaN);
                if (!float.IsNaN(h)) return -h;
            }
            var wp = Game.Wolfpack;
            if (wp == null) return float.NaN;
            var km = wp.Position + new Vector2(fwd.x, fwd.z) * (ahead / 1000f);
            float e = WorldMaps.ElevationMap.GetAtWorldSpace(km);
            return e < 0f ? -e : 0f;
        }

        private static DepthGauge instance;
        public DepthGauge() { instance = this; }

        public static void Draw()
        {
            var ship = Util.PlayerShip;
            if (ship == null || instance == null) return;
            Util.Safe("Depth.Update", () => instance.Update(ship));
            if (!Settings.B("DepthGauge")) return;

            var r = Box(); var sc = Scale(); float max = Max;
            Imgui.Backdrop(r, 0.75f);
            Imgui.TextAt(new Rect(r.x + 6, r.y + 4, r.width - 12, 20), "Tiefe " + Current(ship).ToString("F0") + " m  Ziel " + ship.TargetDepth.ToString("F0"));
            Imgui.Fill(sc, new Color(0.10f, 0.22f, 0.35f, 0.9f));
            float Y(float d) => sc.y + Mathf.Clamp01(d / max) * sc.height;

            if (!float.IsNaN(lastBed) && lastBed < max)
                Imgui.Fill(new Rect(sc.x, Y(lastBed), sc.width, sc.yMax - Y(lastBed)), new Color(0.45f, 0.32f, 0.18f, 0.95f));
            float tick = max <= 60 ? 10 : max <= 150 ? 25 : max <= 300 ? 50 : max <= 1000 ? 100 : 500;
            for (float d = 0; d <= max + 0.1f; d += tick)
            {
                Imgui.Fill(new Rect(sc.x - 6, Y(d), 6, 1), Color.gray);
                Imgui.TextAt(new Rect(r.x + 4, Y(d) - 9, 40, 18), d.ToString("F0"));
            }
            for (int i = 0; i < history.Count; i++)
            {
                float x = sc.xMax + 4 + i * ((r.xMax - sc.xMax - 8) / 120f);
                Imgui.Fill(new Rect(x, Y(history[i]) - 1, 2, 2), new Color(0.6f, 0.9f, 1f, 0.9f));
            }
            Imgui.Fill(new Rect(sc.x - 4, Y(ship.TargetDepth) - 1, sc.width + 8, 3), Color.yellow);
            Imgui.Fill(new Rect(sc.x, Y(Current(ship)) - 2, sc.width, 4), Color.white);
            Imgui.TextAt(new Rect(r.x + 6, r.yMax - 42, r.width - 12, 20), float.IsNaN(lastBed) ? "Grund: ?" : "Grund " + lastBed.ToString("F0") + " m, frei " + (lastBed - ship.BottomDepth).ToString("F0") + " m");
            Imgui.TextAt(new Rect(r.x + 6, r.yMax - 22, r.width - 12, 20), pilotText);
            Imgui.TextAt(new Rect(r.x + 6, r.yMax + 2, r.width - 12, 20), "Tempo x" + Settings.F("CheatSpeed").ToString("0.0") + "  (Strg+Bild hoch/runter)");
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/DiveAngle.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Entities;
    using global::UnityEngine;

    // Dive angle: the game tilts the boat by at most "verticalSteersInclination" degrees (depth steers fully over).
    // Setting DiveAngleMax replaces this value for the player's boat (0 = game value). Not on Type XXX when the Warthog mod brings its own.
    public class DiveAngle : ModuleBase
    {
        private static readonly AccessTools.FieldRef<PlayableSubmarine, float> Incl = AccessTools.FieldRefAccess<PlayableSubmarine, float>("verticalSteersInclination");
        private float sinkLogAt; private PlayableSubmarine boat; private float original = float.NaN, applied = float.NaN, lastTarget = float.NaN, lastComp = float.NaN;

        public override void Tick()
        {
            var sub = Util.PlayerShip as PlayableSubmarine;
            if (sub == null || Incl == null) return;
            if (sub != boat)
            {
                // 0.4.24 (DoMe: "das Tool muss unterscheiden, wo es welche Höhe braucht"): the three heights, once per boat
                Util.Log("Tiefen-Maße: Deck " + sub.DeckDepthUnclamped.ToString("0.0") + " m, Kiel " + sub.BottomDepth.ToString("0.0") + " m, Turm-Oberkante " + sub.Depth.ToString("0.0") +
                    " m. Deck: Zieltiefe, Tiefe halten, Tiefenanzeige, Strahlruder, Flossen, Log. Kiel: Bodenabstand, Aufsetzen. Turm-Oberkante: nur Info (und Sprungschicht).");
            }
            if (sub != boat) { boat = sub; original = Incl(sub); applied = float.NaN; lastComp = float.NaN; }   // 0.4.22: NaN = factor is written once per boat, also the plain 1 (a factor kept in the savegame is reset)
            if (Util.WarthogModOwns(Util.PlayerShip)) return;
            // Speed cheat x f: plane force grows with speed^2 -> f^2 too strong (DoMe: nose-down dives at speed, stop = boat rises).
            // Compensate so the planes act like at normal speed.
            float sf = Mathf.Max(1f, Settings.F("CheatSpeed"));
            // 0.3.40: only while diving. At the surface (ordered depth <= 5 m) the planes keep full force, else they were
            // too weak to hold the boat up and it sank to 10+ m (DoMe, time compression + speed x2).
            float comp = Settings.B("DiveSpeedCompensate") && sub.TargetDepth > 5f ? 1f / (sf * sf) : 1f;
            if (comp != lastComp)
            {
                var par = HarmonyLib.Traverse.Create(sub).Field("verticalSteersForceScale").GetValue() as UBOAT.Game.Core.Data.Parameter;
                if (par != null) { Util.ScaleParameter(par, comp); bool first = float.IsNaN(lastComp); lastComp = comp; if (!first || comp != 1f) Util.Log("Tiefenruder-Ausgleich für Speed-Cheat: x" + comp.ToString("0.00")); }
            }
            // log every change of the ordered depth (to trace "forced dives")
            float td = sub.TargetDepth;
            if (float.IsNaN(lastTarget) || Mathf.Abs(td - lastTarget) > 1f) { if (!float.IsNaN(lastTarget)) Util.Log("Zieltiefe geändert: " + lastTarget.ToString("F0") + " -> " + td.ToString("F0") + " m (Preset " + sub.DepthPreset + ", Tiefe " + sub.DeckDepthUnclamped.ToString("F1") + ")"); lastTarget = td; }
            // log unwanted sinking: ordered depth at the surface but the boat is deeper than 8 m
            if (td <= 5f && sub.DeckDepthUnclamped > 8f && Time.unscaledTime >= sinkLogAt)
            {
                sinkLogAt = Time.unscaledTime + 30f;
                var rb = sub.RigidBody;
                Util.Log("Absinken: Tiefe " + sub.DeckDepthUnclamped.ToString("F1") + " m bei Zieltiefe " + td.ToString("F0") + ", Zeitraffer " + Time.timeScale.ToString("0.#") +
                    ", Speed-Cheat " + sf.ToString("0.#") + ", Fahrt " + (rb != null ? (rb.velocity.magnitude * 1.944f).ToString("0.0") : "?") + " kn, Ruder-Ausgleich x" + (float.IsNaN(lastComp) ? 1f : lastComp).ToString("0.00") +
                    ", Neigung " + (rb != null ? Vector3.SignedAngle(Vector3.ProjectOnPlane(sub.transform.forward, Vector3.up), sub.transform.forward, sub.transform.right).ToString("0.0") : "?") + "°");
            }
            float want = Settings.F("DiveAngleMax");
            // steep angle only while really diving to a depth; surfaced / near the surface the game value (no nose-down dips)
            bool diving = td > 5f;
            float v = want <= 0f || !diving ? original : Mathf.Clamp(want, 1f, 45f);
            if (v == applied) return;
            Incl(sub) = v; applied = v;
            Util.Log("Tauchwinkel: " + v.ToString("0") + "° (Spiel " + original.ToString("0") + "°)");
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Hatches.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.UI.CharacterSwitchUI;

    // QoL #22: when the boat starts to dive or gets hunted, the crew closes all hatches.
    // Uses the game's own "close all hatches" button logic.
    public class Hatches : ModuleBase
    {
        private bool lastDanger;
        private CharacterSwitchCloseHatchesUI ui;

        public override void Tick()
        {
            var ship = Util.PlayerShip;
            bool danger = ship.SubmergedOrGoingToSubmerge || ship.IsHunted;
            if (Settings.B("AutoHatches") && danger && !lastDanger)
            {
                if (ui == null) ui = Object.FindObjectOfType<CharacterSwitchCloseHatchesUI>(true);   // scene-wide search once, not at every dive
                if (ui != null)
                {
                    var t = Traverse.Create(ui);
                    t.Method("ValidateState").GetValue();
                    if (!t.Field("areHatchesClosed").GetValue<bool>()) t.Method("OnClick").GetValue();
                }
            }
            lastDanger = danger;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/HatchPassage.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Globalization;
    using global::System.Reflection;
    using global::System.Text;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Scene.Camera;
    using global::UBOAT.Game.Scene.Characters.Navigation;
    using global::UBOAT.Game.Scene.Entities.NavMesh;

    // Hatches in first person: keep the walk/run passage the player started with (the game
    // drops to the slow walk passage mid-hatch), speed the passage up, a short run tail after
    // the hatch, optional walk animation for sprinting, and a frame-by-frame log for checking.
    internal enum PassageVariant { Auto, Walk, Run }

    internal static class HtfSettings
    {
        public static bool Enabled => Settings.B("FastHatch");
        public static bool HoldPassageState => Settings.B("HatchHoldState");
        public static PassageVariant PassageVariant
        {
            get { int v = Mathf.RoundToInt(Settings.F("HatchVariant")); return v == 1 ? PassageVariant.Walk : v == 2 ? PassageVariant.Run : PassageVariant.Auto; }
        }
        public static bool WalkAnimationForSprint => Settings.B("HatchWalkForSprint");
        public static float SprintSpeedMultiplier => Settings.F("HatchSprintSpeed");
        public static float WalkSpeedMultiplier => Settings.F("HatchWalkSpeed");
        public static float MaxAnimatorSpeed => Settings.F("HatchMaxSpeed");
        public static float PostPassageRunTailSeconds => Settings.F("HatchRunTail");
        public static bool Diagnostics => Settings.B("HatchLog");
    }

    internal static class HatchLog
    {
        private static bool reported;
        public static void Line(string text)
        {
            if (!HtfSettings.Diagnostics) return;
            if (!reported)
            {
                reported = true;
                Util.Log("Luken: tracker=" + (FppNavigatorTracker.Resolved ? "ok" : "FEHLT") + " passageHold=" + (PassageStatePatch.Resolved ? "ok" : "FEHLT") + " passageSpeed=" + (PassageSpeedPatch.Resolved ? "ok" : "FEHLT"));
                Util.Log("Luken: " + HatchDiagnostics.PatchReport());
                Util.Log("Luken: " + HatchDiagnostics.KnownHashReport());
            }
            Util.Log("Luken: " + text);
        }
        public static string F(float v, int d) => v.ToString("F" + d.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        public static string I(int v) => v.ToString(CultureInfo.InvariantCulture);
        public static string B(bool v) => v ? "1" : "0";
    }

    [HarmonyPatch(typeof(FPPCameraController), "Update")]
    internal static class FppNavigatorTracker
    {
        private static readonly FieldInfo NavigatorField =
            AccessTools.Field(typeof(FPPCameraController), "characterNavigator");
        private static readonly FieldInfo RunInputField =
            AccessTools.Field(typeof(FPPCameraController), "runInput");
        private static NavMeshNavigator _navigator;
        private static bool _runKeyHeld;
        private static int _frame = -1;
        public static bool Resolved { get { return NavigatorField != null && RunInputField != null; } }
        public static bool RunKeyHeld
        {
            get { return _runKeyHeld && Time.frameCount - _frame <= 2; }
        }
        public static bool IsPlayerNavigator(NavMeshNavigator candidate)
        {
            if (_navigator == null || candidate == null) return false;
            if (!ReferenceEquals(_navigator, candidate)) return false;
            return Time.frameCount - _frame <= 2;
        }
        private static void Postfix(FPPCameraController __instance)
        {
            if (!Resolved) return;
            try
            {
                
                _navigator = NavigatorField.GetValue(__instance) as NavMeshNavigator;
                _runKeyHeld = ReadRunKey(__instance);
                _frame = Time.frameCount;
            }
            catch (Exception)
            {
            }
        }
        private static bool ReadRunKey(FPPCameraController controller)
        {
            var link = RunInputField.GetValue(controller);
            if (link == null) return false;
            var assetProperty = link.GetType().GetProperty("Asset");
            if (assetProperty == null) return false;
            var axis = assetProperty.GetValue(link, null);
            if (axis == null) return false;
            var pressedProperty = axis.GetType().GetProperty("Pressed");
            if (pressedProperty == null) return false;
            return (bool)pressedProperty.GetValue(axis, null);
        }
    }


    [HarmonyPatch(typeof(NavMeshNavigator), "Update")]
    internal static class PassageStatePatch
    {
        private static readonly FieldInfo LinkHandler =
            AccessTools.Field(typeof(NavMeshNavigator), "currentLinkEventHandler");
        private static readonly FieldInfo ParamRunning =
            AccessTools.Field(typeof(NavMeshNavigator), "paramRunning");
        private static readonly FieldInfo ParamWalking =
            AccessTools.Field(typeof(NavMeshNavigator), "paramWalking");
        private static bool _faulted;
        private static UnityEngine.AI.OffMeshLink _activeLink;
        private static bool _holdRunning;
        private static bool _sprintAsWalk;
        private static bool _crossfaded;
        private static int _crossfades;
        private static int _passages;
        private static int _framesHeld;
        private static float _tailUntil;
        private static int _tailFrames;
        public static bool Resolved { get { return LinkHandler != null && ParamRunning != null; } }
        public static bool IsHoldingRunPassage
        {
            get { return _activeLink != null && _holdRunning; }
        }
        public static bool IsSprintingAsWalk
        {
            get { return _activeLink != null && _sprintAsWalk; }
        }
        private const int NormalPassageHash = -1632352039;
        private const int AlertPassageHash = -155423246;
        private const float NormalClipSeconds = 2.72f;
        private const float CrossfadeSeconds = 0.15f;
        public static string Report()
        {
            return "passage hold: " + HatchLog.I(_passages) + " passage(s), "
                 + HatchLog.I(_framesHeld) + " frame(s) held, "
                 + HatchLog.I(_tailFrames) + " tail frame(s), "
                 + HatchLog.I(_crossfades) + " sprint-as-walk crossfade(s)";
        }
        private static void Postfix(NavMeshNavigator __instance)
        {
            if (_faulted || !Resolved) return;
            try
            {
                var link = HatchLinkFor(__instance);
                if (link == null)
                {
                    if (FppNavigatorTracker.IsPlayerNavigator(__instance))
                    {
                        if (_activeLink != null && (_holdRunning || _sprintAsWalk))
                            _tailUntil = Time.unscaledTime + HtfSettings.PostPassageRunTailSeconds;
                        _activeLink = null;
                        RunTail(__instance);
                    }
                    return;
                }
                _tailUntil = 0f;
                if (!HtfSettings.Enabled || !HtfSettings.HoldPassageState)
                {
                    _activeLink = null;
                    return;
                }
                var animator = __instance.Animator;
                if (animator == null) return;
                int runningHash = (int)ParamRunning.GetValue(null);
                if (!ReferenceEquals(link, _activeLink))
                {
                    _activeLink = link;
                    bool sprinting = SprintIntent(animator, runningHash);
                    _holdRunning = DecideVariant(sprinting);
                    _sprintAsWalk = sprinting && !_holdRunning && HtfSettings.WalkAnimationForSprint;
                    _crossfaded = false;
                    _passages++;
                    if (HtfSettings.Diagnostics)
                        HatchLog.Line("passage #" + HatchLog.I(_passages) + " held as "
                                      + (_holdRunning ? "RUN" : (_sprintAsWalk ? "WALK (sprint)" : "WALK"))
                                      + " (link '" + link.name + "')");
                }
                animator.SetBool(runningHash, _holdRunning);
                _framesHeld++;
                if (_sprintAsWalk && !_crossfaded) LeaveAlertState(animator);
            }
            catch (Exception e)
            {
                _faulted = true;
                _activeLink = null;
                HatchLog.Line("passage hold disabled after an exception: " + e);
            }
        }
        private static void RunTail(NavMeshNavigator navigator)
        {
            if (_tailUntil <= 0f || Time.unscaledTime >= _tailUntil) return;
            if (!HtfSettings.Enabled || !HtfSettings.HoldPassageState) return;
            if (ParamWalking == null) return;
            if (!FppNavigatorTracker.RunKeyHeld) return;
            var animator = navigator.Animator;
            if (animator == null) return;
            if (!animator.GetBool((int)ParamWalking.GetValue(null))) return;
            animator.SetBool((int)ParamRunning.GetValue(null), true);
            _tailFrames++;
        }
        private static bool DecideVariant(bool sprinting)
        {
            if (HtfSettings.PassageVariant == PassageVariant.Walk) return false;
            if (HtfSettings.PassageVariant == PassageVariant.Run) return true;
            if (HtfSettings.WalkAnimationForSprint) return false;
            return sprinting;
        }
        private static void LeaveAlertState(Animator animator)
        {
            float nt;
            if (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).shortNameHash == AlertPassageHash)
                nt = animator.GetNextAnimatorStateInfo(0).normalizedTime;
            else if (animator.GetCurrentAnimatorStateInfo(0).shortNameHash == AlertPassageHash)
                nt = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            else
                return;
            _crossfaded = true;
            _crossfades++;
            nt = Mathf.Clamp01(nt);
            animator.CrossFadeInFixedTime(NormalPassageHash, CrossfadeSeconds, 0, nt * NormalClipSeconds);
            if (HtfSettings.Diagnostics)
                HatchLog.Line("sprint-as-walk: Alert -> Normal crossfade at nt " + HatchLog.F(nt, 3));
        }
        private static bool SprintIntent(Animator animator, int runningHash)
        {
            if (FppNavigatorTracker.RunKeyHeld) return true;
            return animator.GetBool(runningHash);
        }
        private static UnityEngine.AI.OffMeshLink HatchLinkFor(NavMeshNavigator navigator)
        {
            if (!FppNavigatorTracker.IsPlayerNavigator(navigator)) return null;
            if (!navigator.IsOnOffMeshLink) return null;
            if (!(LinkHandler.GetValue(navigator) is NavMeshHatch)) return null;
            return navigator.CurrentLink;
        }
    }


    [HarmonyPatch(typeof(FPPCameraController), "ParseInputsFPPMode")]
    internal static class PassageSpeedPatch
    {
        public const float RunPassageParityMultiplier = 1.103f;
        private const float VanillaSpeedBase = 0.0001f;
        private const float VanillaSpeedGain = 1.25f;
        private static readonly FieldInfo FppNavigator =
            AccessTools.Field(typeof(FPPCameraController), "characterNavigator");
        private static readonly FieldInfo FppTravelDirection =
            AccessTools.Field(typeof(FPPCameraController), "offMeshLinkTravelDirection");
        private static readonly FieldInfo NavLinkHandler =
            AccessTools.Field(typeof(NavMeshNavigator), "currentLinkEventHandler");
        private static bool _faulted;
        private static int _frames;
        private static float _highestWritten;
        public static bool Resolved
        {
            get
            {
                return FppNavigator != null && FppTravelDirection != null && NavLinkHandler != null;
            }
        }
        public static string Report()
        {
            return "passage speed: " + HatchLog.I(_frames) + " frame(s), peak "
                 + HatchLog.F(_highestWritten, 3);
        }
        private static void Postfix(FPPCameraController __instance)
        {
            if (_faulted || !Resolved || !HtfSettings.Enabled) return;
            try
            {
                var navigator = FppNavigator.GetValue(__instance) as NavMeshNavigator;
                if (navigator == null || !navigator.IsOnOffMeshLink) return;
                if (!(NavLinkHandler.GetValue(navigator) is NavMeshHatch)) return;
                var animator = navigator.Animator;
                if (animator == null) return;
                float direction = (float)FppTravelDirection.GetValue(__instance);
                if (direction < 0f && !animator.IsInTransition(0)) return;
                float multiplier = Multiplier();
                if (multiplier <= 1.0001f) return;
                float vanilla = VanillaSpeedBase + Mathf.Max(0f, direction) * VanillaSpeedGain;
                float target = Mathf.Min(vanilla * multiplier, HtfSettings.MaxAnimatorSpeed);
                if (target <= vanilla) return;
                animator.speed = target;
                _frames++;
                if (target > _highestWritten) _highestWritten = target;
            }
            catch (Exception e)
            {
                _faulted = true;
                HatchLog.Line("passage speed disabled after an exception: " + e);
            }
        }
        private static float Multiplier()
        {
            if (PassageStatePatch.IsHoldingRunPassage)
                return Mathf.Max(HtfSettings.SprintSpeedMultiplier, RunPassageParityMultiplier);
            if (PassageStatePatch.IsSprintingAsWalk)
                return Mathf.Max(HtfSettings.SprintSpeedMultiplier, RunPassageParityMultiplier)
                       / RunPassageParityMultiplier;
            return HtfSettings.WalkSpeedMultiplier;
        }
    }


    [HarmonyPatch(typeof(FPPCameraController), "Update")]
    internal static class HatchDiagnostics
    {
        private static readonly FieldInfo FppNavigator =
            AccessTools.Field(typeof(FPPCameraController), "characterNavigator");
        private static readonly FieldInfo FppTravelDirection =
            AccessTools.Field(typeof(FPPCameraController), "offMeshLinkTravelDirection");
        private static readonly FieldInfo FppMovementByAnimation =
            AccessTools.Field(typeof(FPPCameraController), "movementControlledByAnimation");
        private static readonly FieldInfo FppRunInput =
            AccessTools.Field(typeof(FPPCameraController), "runInput");
        private static readonly FieldInfo NavLinkHandler =
            AccessTools.Field(typeof(NavMeshNavigator), "currentLinkEventHandler");
        private static readonly FieldInfo NavForceRun =
            AccessTools.Field(typeof(NavMeshNavigator), "forceRun");
        private static readonly FieldInfo NavParamWalking =
            AccessTools.Field(typeof(NavMeshNavigator), "paramWalking");
        private static readonly FieldInfo NavParamRunning =
            AccessTools.Field(typeof(NavMeshNavigator), "paramRunning");
        private static readonly FieldInfo NavMoveThroughHatch =
            AccessTools.Field(typeof(NavMeshNavigator), "moveThroughHatchHash");
        private static readonly FieldInfo NavLocomotionMode =
            AccessTools.Field(typeof(NavMeshNavigator), "locomotionModeHash");
        private static readonly FieldInfo HatchParamName =
            AccessTools.Field(typeof(NavMeshHatch), "animatorParameter");
        private static readonly FieldInfo HatchParamHash =
            AccessTools.Field(typeof(NavMeshHatch), "animatorParameterHash");
        private static readonly FieldInfo HatchNormalStates =
            AccessTools.Field(typeof(NavMeshHatch), "normalPassageStatesHashes");
        private static readonly FieldInfo HatchPassing =
            AccessTools.Field(typeof(NavMeshHatch), "passingNavigators");
        private static readonly FieldInfo HatchQueued =
            AccessTools.Field(typeof(NavMeshHatch), "navigators");
        private const int MaxLinesPerTraversal = 600;
        private const int PostPassageTraceFrames = 90;
        private static int _postFrames;
        private static bool _onLink;
        private static UnityEngine.AI.OffMeshLink _link;
        private static int _frames;
        private static int _lines;
        private static float _startTime;
        private static bool _runHeldAtEntry;
        private static int _lastStateHash;
        private static float _lastNormalizedTime;
        private static float _maxNormalizedTime;
        private static int _restarts;
        private static int _stateChanges;
        private static readonly List<string> _statesSeen = new List<string>();
        private static bool _faulted;
        public static string PatchReport()
        {
            var target = AccessTools.Method(typeof(FPPCameraController), "Update");
            var missing = new List<string>();
            if (target == null) missing.Add("FPPCameraController.Update");
            if (FppNavigator == null) missing.Add("characterNavigator");
            if (FppTravelDirection == null) missing.Add("offMeshLinkTravelDirection");
            if (FppMovementByAnimation == null) missing.Add("movementControlledByAnimation");
            if (FppRunInput == null) missing.Add("runInput");
            if (NavLinkHandler == null) missing.Add("currentLinkEventHandler");
            if (NavParamWalking == null) missing.Add("paramWalking");
            if (NavParamRunning == null) missing.Add("paramRunning");
            if (NavMoveThroughHatch == null) missing.Add("moveThroughHatchHash");
            if (HatchParamHash == null) missing.Add("animatorParameterHash");
            if (HatchNormalStates == null) missing.Add("normalPassageStatesHashes");
            if (missing.Count == 0)
                return "all 11 vanilla members resolved";
            return "MISSING " + missing.Count + ": " + string.Join(", ", missing.ToArray());
        }
        public static string KnownHashReport()
        {
            var sb = new StringBuilder();
            sb.Append("known hashes: ");
            sb.Append("'Get Through Hatch Normal'=").Append(HatchLog.I(Animator.StringToHash("Get Through Hatch Normal")));
            sb.Append(" 'Get Through Hatch Alert'=").Append(HatchLog.I(Animator.StringToHash("Get Through Hatch Alert")));
            sb.Append(" 'Bucket - Get Through Hole'=").Append(HatchLog.I(Animator.StringToHash("Bucket - Get Through Hole")));
            sb.Append(" | normalPassageStatesHashes=");
            var arr = HatchNormalStates == null ? null : HatchNormalStates.GetValue(null) as int[];
            if (arr == null)
            {
                sb.Append("unavailable");
            }
            else
            {
                for (int i = 0; i < arr.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(HatchLog.I(arr[i]));
                }
            }
            return sb.ToString();
        }
        private static void Postfix(FPPCameraController __instance)
        {
            if (_faulted) return;
            if (!HtfSettings.Diagnostics)
            {
                EndTraversal("diagnostics switched off");
                return;
            }
            try
            {
                Trace(__instance);
            }
            catch (Exception e)
            {
                _faulted = true;
                HatchLog.Line("diagnostic disabled after an exception: " + e);
            }
        }
        private static void Trace(FPPCameraController controller)
        {
            var navigator = FppNavigator.GetValue(controller) as NavMeshNavigator;
            if (navigator == null)
            {
                EndTraversal("navigator went null");
                return;
            }
            if (!navigator.IsOnOffMeshLink)
            {
                if (_onLink)
                {
                    EndTraversal("left link");
                    _postFrames = PostPassageTraceFrames;
                }
                if (_postFrames > 0)
                {
                    _postFrames--;
                    LogPostPassageFrame(controller, navigator);
                }
                return;
            }
            _postFrames = 0;
            var animator = navigator.Animator;
            if (animator == null) return;
            var handler = NavLinkHandler.GetValue(navigator);
            var hatch = handler as NavMeshHatch;
            var link = navigator.CurrentLink;
            var state = animator.GetCurrentAnimatorStateInfo(0);
            bool inTransition = animator.IsInTransition(0);
            float normalizedTime = state.normalizedTime;
            if (!_onLink || !ReferenceEquals(link, _link))
            {
                EndTraversal("superseded");
                BeginTraversal(controller, navigator, hatch, link);
            }
            _frames++;
            if (state.shortNameHash != _lastStateHash)
            {
                if (_lastStateHash != 0) _stateChanges++;
                _lastStateHash = state.shortNameHash;
                _lastNormalizedTime = normalizedTime;
                _maxNormalizedTime = normalizedTime;
                RecordState(animator, state);
            }
            else
            {
                if (normalizedTime < _lastNormalizedTime - 0.1f) _restarts++;
                _lastNormalizedTime = normalizedTime;
                if (normalizedTime > _maxNormalizedTime) _maxNormalizedTime = normalizedTime;
            }
            if (_lines >= MaxLinesPerTraversal) return;
            _lines++;
            var sb = new StringBuilder(320);
            sb.Append("f=").Append(HatchLog.I(Time.frameCount));
            sb.Append(" n=").Append(HatchLog.I(_frames));
            sb.Append(" state=").Append(HatchLog.I(state.shortNameHash));
            sb.Append(" known=").Append(HatchLog.B(IsNormalPassageState(state.shortNameHash)));
            sb.Append(" nt=").Append(HatchLog.F(normalizedTime, 3));
            sb.Append(" tr=").Append(HatchLog.B(inTransition));
            sb.Append(" clip=").Append(ClipDescription(animator));
            sb.Append(" spd=").Append(HatchLog.F(animator.speed, 4));
            sb.Append(" dir=").Append(HatchLog.F(ReadFloat(FppTravelDirection, controller), 3));
            sb.Append(" inter=").Append(HatchLog.F(animator.GetFloat("Interactivity"), 3));
            sb.Append(" walking=").Append(BoolParam(animator, NavParamWalking));
            sb.Append(" running=").Append(BoolParam(animator, NavParamRunning));
            sb.Append(" thruHatch=").Append(BoolParam(animator, NavMoveThroughHatch));
            sb.Append(" hatchBool=").Append(HatchBool(animator, hatch));
            sb.Append(" locMode=").Append(IntParam(animator, NavLocomotionMode));
            sb.Append(" mca=").Append(HatchLog.B(ReadBool(FppMovementByAnimation, controller)));
            sb.Append(" navRun=").Append(HatchLog.B(navigator.Run));
            sb.Append(" forceRun=").Append(HatchLog.B(ReadBool(NavForceRun, navigator)));
            sb.Append(" runKey=").Append(RunInputPressed(controller));
            if (inTransition)
            {
                var next = animator.GetNextAnimatorStateInfo(0);
                sb.Append(" next=").Append(HatchLog.I(next.shortNameHash));
                sb.Append(" nextNt=").Append(HatchLog.F(next.normalizedTime, 3));
            }
            HatchLog.Line(sb.ToString());
        }
        private static void BeginTraversal(FPPCameraController controller,
                                           NavMeshNavigator navigator,
                                           NavMeshHatch hatch,
                                           UnityEngine.AI.OffMeshLink link)
        {
            _onLink = true;
            _link = link;
            _frames = 0;
            _lines = 0;
            _startTime = Time.unscaledTime;
            _restarts = 0;
            _stateChanges = 0;
            _lastStateHash = 0;
            _maxNormalizedTime = 0f;
            _statesSeen.Clear();
            _runHeldAtEntry = navigator.Run;
            var sb = new StringBuilder(256);
            sb.Append("ENTER link='").Append(link == null ? "none" : link.name).Append('\'');
            sb.Append(" handler=").Append(HandlerName(navigator));
            sb.Append(" runHeld=").Append(HatchLog.B(_runHeldAtEntry));
            sb.Append(" runKey=").Append(RunInputPressed(controller));
            if (hatch != null)
            {
                var paramName = HatchParamName == null ? null : HatchParamName.GetValue(hatch) as string;
                sb.Append(" hatchParam='").Append(paramName ?? "?").Append('\'');
                sb.Append(" queued=").Append(HatchLog.I(ListCount(HatchQueued, hatch)));
                sb.Append(" passing=").Append(HatchLog.I(ListCount(HatchPassing, hatch)));
            }
            HatchLog.Line(sb.ToString());
        }
        private static void EndTraversal(string reason)
        {
            if (!_onLink) return;
            _onLink = false;
            _link = null;
            var sb = new StringBuilder(256);
            sb.Append("EXIT ").Append(reason);
            sb.Append(" runHeldAtEntry=").Append(HatchLog.B(_runHeldAtEntry));
            sb.Append(" frames=").Append(HatchLog.I(_frames));
            sb.Append(" dur=").Append(HatchLog.F(Time.unscaledTime - _startTime, 2)).Append('s');
            sb.Append(" restarts=").Append(HatchLog.I(_restarts));
            sb.Append(" stateChanges=").Append(HatchLog.I(_stateChanges));
            sb.Append(" maxNt=").Append(HatchLog.F(_maxNormalizedTime, 2));
            sb.Append(" states=[");
            for (int i = 0; i < _statesSeen.Count; i++)
            {
                if (i > 0) sb.Append(" | ");
                sb.Append(_statesSeen[i]);
            }
            sb.Append(']');
            if (_lines >= MaxLinesPerTraversal)
                sb.Append(" (per-frame lines were capped at ").Append(HatchLog.I(MaxLinesPerTraversal)).Append(')');
            HatchLog.Line(sb.ToString());
        }
        private static void LogPostPassageFrame(FPPCameraController controller,
                                                NavMeshNavigator navigator)
        {
            var animator = navigator.Animator;
            if (animator == null) return;
            var state = animator.GetCurrentAnimatorStateInfo(0);
            var sb = new StringBuilder(220);
            sb.Append("POST f=").Append(HatchLog.I(Time.frameCount));
            sb.Append(" left=").Append(HatchLog.I(_postFrames));
            sb.Append(" state=").Append(HatchLog.I(state.shortNameHash));
            sb.Append(" nt=").Append(HatchLog.F(state.normalizedTime, 3));
            sb.Append(" tr=").Append(HatchLog.B(animator.IsInTransition(0)));
            sb.Append(" clip=").Append(ClipDescription(animator));
            sb.Append(" spd=").Append(HatchLog.F(animator.speed, 3));
            sb.Append(" walking=").Append(BoolParam(animator, NavParamWalking));
            sb.Append(" running=").Append(BoolParam(animator, NavParamRunning));
            sb.Append(" thruHatch=").Append(BoolParam(animator, NavMoveThroughHatch));
            sb.Append(" mca=").Append(HatchLog.B(ReadBool(FppMovementByAnimation, controller)));
            sb.Append(" navRun=").Append(HatchLog.B(navigator.Run));
            sb.Append(" runKey=").Append(HatchLog.B(FppNavigatorTracker.RunKeyHeld));
            HatchLog.Line(sb.ToString());
        }
        private static void RecordState(Animator animator, AnimatorStateInfo state)
        {
            if (_statesSeen.Count >= 12) return;
            _statesSeen.Add(HatchLog.I(state.shortNameHash) + "=" + ClipDescription(animator)
                            + (IsNormalPassageState(state.shortNameHash) ? "*" : ""));
        }
        private static string ClipDescription(Animator animator)
        {
            var clips = animator.GetCurrentAnimatorClipInfo(0);
            if (clips == null || clips.Length == 0) return "'none'";
            var clip = clips[0].clip;
            if (clip == null) return "'null'";
            return "'" + clip.name + "'/len=" + HatchLog.F(clip.length, 2)
                   + "/loop=" + HatchLog.B(clip.isLooping)
                   + (clips.Length > 1 ? "/+" + HatchLog.I(clips.Length - 1) : "");
        }
        private static bool IsNormalPassageState(int shortNameHash)
        {
            var arr = HatchNormalStates == null ? null : HatchNormalStates.GetValue(null) as int[];
            if (arr == null) return false;
            for (int i = 0; i < arr.Length; i++)
                if (arr[i] == shortNameHash) return true;
            return false;
        }
        private static string HandlerName(NavMeshNavigator navigator)
        {
            var handler = NavLinkHandler == null ? null : NavLinkHandler.GetValue(navigator);
            if (handler == null) return "none";
            return handler.GetType().Name;
        }
        private static string BoolParam(Animator animator, FieldInfo hashField)
        {
            if (hashField == null) return "?";
            return HatchLog.B(animator.GetBool((int)hashField.GetValue(null)));
        }
        private static string IntParam(Animator animator, FieldInfo hashField)
        {
            if (hashField == null) return "?";
            return HatchLog.I(animator.GetInteger((int)hashField.GetValue(null)));
        }
        private static string HatchBool(Animator animator, NavMeshHatch hatch)
        {
            if (hatch == null || HatchParamHash == null) return "-";
            return HatchLog.B(animator.GetBool((int)HatchParamHash.GetValue(hatch)));
        }
        private static float ReadFloat(FieldInfo field, object target)
        {
            if (field == null) return float.NaN;
            return (float)field.GetValue(target);
        }
        private static bool ReadBool(FieldInfo field, object target)
        {
            if (field == null) return false;
            return (bool)field.GetValue(target);
        }
        private static int ListCount(FieldInfo field, object target)
        {
            if (field == null) return -1;
            var list = field.GetValue(target) as System.Collections.ICollection;
            return list == null ? -1 : list.Count;
        }
        private static string RunInputPressed(FPPCameraController controller)
        {
            if (FppRunInput == null) return "?";
            var link = FppRunInput.GetValue(controller);
            if (link == null) return "?";
            var assetProperty = link.GetType().GetProperty("Asset");
            if (assetProperty == null) return "?";
            var axis = assetProperty.GetValue(link, null);
            if (axis == null) return "?";
            var pressedProperty = axis.GetType().GetProperty("Pressed");
            if (pressedProperty == null) return "?";
            return HatchLog.B((bool)pressedProperty.GetValue(axis, null));
        }
    }

}

// ====================================================================================================
// Abschnitt: Modules/Helm.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::HarmonyLib;
    using global::UBOAT.Game.Sandbox;
    using global::UBOAT.Game.Sandbox.HQ;
    using global::UBOAT.Game.Scene.Entities;

    // Course and depth on keys (Tasten tab): turn by 1/5/45/180 degrees, rudder amidships,
    // 5/20 m deeper or shallower, periscope depth, max safe depth, hold current depth.
    public class Helm : ModuleBase
    {
        private static readonly System.Reflection.MethodInfo course = AccessTools.Method(typeof(PlayerShip), "SetTargetCourse", new[] { typeof(float) });
        private static readonly System.Reflection.MethodInfo amid = AccessTools.Method(typeof(PlayerShip), "SetRudderAmidships");
        private static readonly string[] turnKeys = { "KeyTurnL1", "KeyTurnR1", "KeyTurnL5", "KeyTurnR5", "KeyTurnL45", "KeyTurnR45", "KeyTurnAbout" };
        private static readonly float[] turns = { -1, 1, -5, 5, -45, 45, 180 };
        private static readonly string[] depthKeys = { "KeyDeeper5", "KeyShallower5", "KeyDeeper20", "KeyShallower20" };
        private static readonly float[] steps = { 5, -5, 20, -20 };

        // 0.4.11 (DoMe: rudder dial stuck at 35 right, boat turns on the spot). The game's rudder dial sets the rudder
        // by hand ("Locked") and deletes the route; the helmsman then no longer steers until a new course is ordered.
        private float nextHelmLine; private int helmEmptyLines;
        private int lastFn = -1; private float deadSince, nextDeadNote;
        private int lastLock = int.MinValue; private float lockSince, nextNote, nextLine;
        public override void Tick()
        {
            var ship = Util.PlayerShip;
            if (!Util.ShipReady || ship.Rudders == null || ship.Rudders.Length == 0 || ship.Rudders[0] == null) return;
            var r = ship.Rudders[0];
            HelmCalm_Patch.Tick();
            // 0.4.12: rudder out of order = nobody at the helm (game rule with expanded schedules)
            if (!r.IsFunctional && Settings.B("CheatRudderNoHelmsman")) { foreach (var x in ship.Rudders) if (x != null) x.IsFunctional = true; }
            if (UnityEngine.Time.unscaledTime >= nextHelmLine)
            {
                nextHelmLine = UnityEngine.Time.unscaledTime + 60f;
                Util.Safe("Helm.Status", () =>
                {
                    foreach (var h in Util.Find<UBOAT.Game.Scene.Items.HelmsmanStation>())
                    {
                        if (h == null) continue;
                        var a = h.GetComponent<UBOAT.Game.Scene.Characters.Activators.Activator>();
                        int users = a != null ? a.Users.Count : -1;
                        if (users <= 0) helmEmptyLines++; else helmEmptyLines = 0;
                        Util.Log("Steuerstand: " + (users > 0 ? "besetzt" : "LEER (seit " + helmEmptyLines + " Minuten-Zeilen)") + ", Leistung " + h.Performance.Value.ToString("0.##") + ", Ruder in Betrieb: " + (r.IsFunctional ? "ja" : "nein") + ", Zeitraffer x" + UnityEngine.Time.timeScale.ToString("0"));
                    }
                });
            }
            int fn = r.IsFunctional ? 1 : 0;
            if (fn != lastFn)
            {
                lastFn = fn; deadSince = UnityEngine.Time.unscaledTime;
                string perf = "?"; try { foreach (var h in Util.Find<UBOAT.Game.Scene.Items.HelmsmanStation>()) if (h != null) perf = h.Performance.Value.ToString("0.##") + ", Leute am Stand " + (h.GetComponent<UBOAT.Game.Scene.Characters.Activators.Activator>()?.Users.Count ?? -1); } catch { }
                Util.Log(fn == 1 ? "Ruder: in Betrieb (Steuerstand Leistung " + perf + ")"
                    : "Ruder: AUSSER BETRIEB, bleibt stehen auf " + Side(UnityEngine.Mathf.RoundToInt(r.TargetTurn * -35f)) + ". Grund im Spiel: Steuerstand unbesetzt (Leistung " + perf + ")");
            }
            if (fn == 0 && Settings.B("RudderLockNote") && UnityEngine.Time.unscaledTime - deadSince > 10f && UnityEngine.Time.unscaledTime >= nextDeadNote)
            {
                nextDeadNote = UnityEngine.Time.unscaledTime + 60f;
                ToolkitRunner.ShowNotice("Ruder reagiert nicht: niemand am Steuerstand. Einen Mann ans Steuer schicken.", 8f);
            }
            int deg = r.Locked ? UnityEngine.Mathf.RoundToInt(r.TargetTurn * -35f) : int.MinValue;
            float now = UnityEngine.Time.unscaledTime;
            if (deg != lastLock)
            {
                lastLock = deg; lockSince = now; nextLine = now + 60f;
                string pads = ""; try { pads = string.Join(", ", System.Array.FindAll(UnityEngine.Input.GetJoystickNames(), n => !string.IsNullOrEmpty(n))); } catch { }
                Util.Log(deg == int.MinValue ? "Ruder: wieder frei, Steuermann steuert"
                    : "Ruder: von Hand festgestellt auf " + Side(deg) + " (Ruder-Rad des Spiels, Route gelöscht). Toolkit setzt das Ruder nie fest. Gamepad/Joystick: " + (pads.Length == 0 ? "keiner" : pads));
            }
            if (deg == int.MinValue) return;
            if (now >= nextLine) { nextLine = now + 60f; Util.Log("Ruder: steht seit " + (now - lockSince).ToString("0") + " s fest auf " + Side(deg) + ", Ruder funktioniert: " + (r.IsFunctional ? "ja" : "nein")); }
            if (Settings.B("RudderLockNote") && UnityEngine.Mathf.Abs(deg) >= 20 && now - lockSince > 20f && now >= nextNote)
            {
                nextNote = now + 120f;
                ToolkitRunner.ShowNotice("Ruder steht fest auf " + Side(deg) + ". Neuer Kurs oder Taste Ruder mittschiffs löst es.", 8f);
            }
        }
        private static string Side(int deg) { return deg == 0 ? "0° (mittschiffs)" : System.Math.Abs(deg) + "° " + (deg > 0 ? "rechts" : "links"); }

        public override void Frame()
        {
            var ship = Util.PlayerShip;
            if (ship == null) return;
            for (int i = 0; i < turnKeys.Length; i++)
                if (Keys.Down(turnKeys[i])) { float d = turns[i]; Util.Safe("Helm.Turn", () => course?.Invoke(ship, new object[] { d })); return; }
            if (Keys.Down("KeyAmidships")) { Util.Safe("Helm.Amid", () => amid?.Invoke(ship, null)); return; }
            for (int i = 0; i < depthKeys.Length; i++)
                if (Keys.Down(depthKeys[i]))
                {
                    float s = steps[i];
                    Util.Safe("Helm.Depth", () => Owners.Depth(ship, "Tiefen-Taste (du)", Owners.Player, ship.TargetDepth + s, false, true));
                    return;
                }
            if (Keys.Down("KeyPeriscopeDepth")) { Util.Safe("Helm.Scope", () => Owners.Preset(ship, "Taste Sehrohrtiefe (du)", Owners.Player, DepthPreset.Periscope, false)); return; }
            if (Keys.Down("KeyMaxDepth")) { Util.Safe("Helm.Max", () => Owners.Preset(ship, "Taste größte Tiefe (du)", Owners.Player, DepthPreset.MaxSafeDepth, false)); return; }
            if (Keys.Down("KeyHoldDepth")) Util.Safe("Helm.Hold", () => Owners.Depth(ship, "Taste Tiefe halten (du)", Owners.Player, ship.DeckDepthUnclamped, false, true));
        }
    }

    // Wolfpack: if HQ is busy sending at that moment, try again later instead of dropping the wolfpack.
    [HarmonyPatch(typeof(Headquarters.WolfpackInitializer), "StartWolfpack")]
    internal static class Helm_WolfpackRetryPatch
    {
        private static readonly Dictionary<object, int> tries = new Dictionary<object, int>();
        private static bool Prefix(Headquarters.WolfpackInitializer __instance, Headquarters ___headquarters, ref float __result)
        {
            if (!Settings.B("FixWolfpackRetry") || ___headquarters == null) return true;
            var e = Traverse.Create(___headquarters).Field("sandboxEntity").GetValue<SandboxEntity>();
            if (e == null || e.Transmission == null) { tries.Remove(__instance); return true; }
            tries.TryGetValue(__instance, out int n);
            if (n >= 20) { tries.Remove(__instance); return true; }
            if (tries.Count > 200) tries.Clear();
            tries[__instance] = n + 1;
            __result = 30f;   // ask again in 30 s
            return false;
        }
    }
}

namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::HarmonyLib;
    using global::UBOAT.Game.Sandbox;
    using global::UBOAT.Game.Sandbox.HQ;
    using global::UBOAT.Game.Scene.Entities;

    // 0.4.16 (measured 2026-11-18 on DoMe's boat: the helmsman comes and goes every ~12 s, "Path to target is invalid:
    // Slot, Work, Helmsman Station on Conning Tower Interior"). The game has two helm places: control room and conning
    // tower. HelmsmanStation.ValidateSlotsState moves the helm to the tower whenever somebody RESERVES the attack
    // periscope (or the control room is flooded) and back when the reservation ends, switching the other place off.
    // Every change throws the helmsman out; with expanded schedules the rudder is dead while he walks.
    // Now the place changes at most every x seconds (flooding always switches at once), and every change is logged
    // with the name of the man at the attack periscope.
    [HarmonyPatch(typeof(UBOAT.Game.Scene.Items.HelmsmanStation), "ValidateSlotsState")]
    internal static class HelmCalm_Patch
    {
        private static readonly AccessTools.FieldRef<UBOAT.Game.Scene.Items.HelmsmanStation, UBOAT.Game.Scene.Interactions.InteractableSlot> scope =
            AccessTools.FieldRefAccess<UBOAT.Game.Scene.Items.HelmsmanStation, UBOAT.Game.Scene.Interactions.InteractableSlot>("attackPeriscopeInteractableSlot");
        private static readonly AccessTools.FieldRef<UBOAT.Game.Scene.Items.HelmsmanStation, UBOAT.Game.Scene.Interactions.InteractableSlot[]> tower =
            AccessTools.FieldRefAccess<UBOAT.Game.Scene.Items.HelmsmanStation, UBOAT.Game.Scene.Interactions.InteractableSlot[]>("conningTowerSlots");
        private static readonly AccessTools.FieldRef<UBOAT.Game.Scene.Items.HelmsmanStation, UBOAT.Game.Scene.Interactions.InteractableSlot[]> room =
            AccessTools.FieldRefAccess<UBOAT.Game.Scene.Items.HelmsmanStation, UBOAT.Game.Scene.Interactions.InteractableSlot[]>("controlRoomSlots");
        private static readonly System.Reflection.MethodInfo validate = AccessTools.Method(typeof(UBOAT.Game.Scene.Items.HelmsmanStation), "ValidateSlotsState");

        private static float lastSwitch = -999f, nextLine;
        private static UBOAT.Game.Scene.Items.HelmsmanStation pending;
        internal static int Switches, Held, Calls;
        private static bool bypass;

        private static bool Prefix(UBOAT.Game.Scene.Items.HelmsmanStation __instance)
        {
            Calls++;
            if (bypass) return true;
            try
            {
                var t = tower(__instance); var r = room(__instance); var s = scope(__instance);
                if (t == null || r == null || t.Length == 0 || r.Length == 0 || t[0] == null) return true;
                bool flooded = true;
                for (int i = 0; i < r.Length; i++) if (r[i] != null && !r[i].IsBelowWater) { flooded = false; break; }
                var who = s != null ? s.ReservingCharacter : null;
                bool wantTower = flooded || who != null;
                bool isTower = t[0].gameObject.activeSelf;
                if (wantTower == isTower) return true;            // nothing changes, let the game tidy up
                float now = UnityEngine.Time.unscaledTime;
                float min = Settings.B("HelmCalm") ? Settings.F("HelmCalmSeconds") : 0f;
                if (!flooded && now - lastSwitch < min) { Held++; pending = __instance; return false; }
                lastSwitch = now; Switches++; pending = null;
                if (Switches <= 60)
                    Util.Log("Steuerstand: Platz wechselt in " + (wantTower ? "den Turm" : "die Zentrale") + ". Grund: " +
                        (flooded ? "Zentrale unter Wasser" : who != null ? "Angriffssehrohr reserviert von " + who.name : "Angriffssehrohr wieder frei"));
            }
            catch (System.Exception e) { Util.Log("Steuerstand: Platz-Prüfung Fehler " + e.GetType().Name); }
            return true;
        }

        // called once per second from Helm.Tick: carry out a change that was held back, and write the minute line
        internal static void Tick()
        {
            float now = UnityEngine.Time.unscaledTime;
            if (pending != null && now - lastSwitch >= (Settings.B("HelmCalm") ? Settings.F("HelmCalmSeconds") : 0f))
            {
                var p = pending; pending = null;
                if (p) Util.Safe("Helm.Calm", () => { bypass = true; try { validate.Invoke(p, null); } finally { bypass = false; } lastSwitch = now; Switches++; });
            }
            if (now >= nextLine)
            {
                nextLine = now + 60f;
                if (Calls > 0) Util.Log("Steuerstand: Platz geprüft " + Calls + " mal, gewechselt " + Switches + " mal, zurückgehalten " + Held + " mal (höchstens alle " + Settings.F("HelmCalmSeconds").ToString("0") + " s)");
                Calls = 0; Switches = 0; Held = 0;
            }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/HullSystems.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UBOAT.Game.Scene.Characters.Activators;
    using global::UBOAT.Game.Scene.Items;
    using global::UBOAT.Game.Scene.Items.Statuses;
    using Activator = global::UBOAT.Game.Scene.Characters.Activators.Activator;

    // Ballast valve orders are always offered (only not when the valve is broken or in port).
    [HarmonyPatch]
    internal static class Hull_BallastAlwaysPatch
    {
        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var t in new[] { typeof(PartiallyFloodBallastTanksAction), typeof(PartiallyBlowBallastTanksAction), typeof(FloodBallastTanksAction) })
            {
                var m = AccessTools.Method(t, "GetState", new[] { typeof(Activator), typeof(PlayableCharacter), typeof(bool), typeof(bool), typeof(ActionContext) });
                if (m != null) yield return m;
            }
        }

        private static void Postfix(Activator activator, ref ActionStateData __result)
        {
            if (!Settings.B("BallastAlwaysAvailable") || __result.ActionState != ActionState.Hidden) return;
            var ship = Util.PlayerShip;
            if (ship == null || ship.Docked) return;
            if (!(activator?.AssociatedEquipment is Device d) || d.HasDisableFlag(DeviceDisableFlag.Damage)) return;
            __result = new ActionStateData(ActionState.Enabled);
        }
    }

    // Hatches show the air pressure on both sides and stay shut when it differs too much.
    [HarmonyPatch(typeof(Hatch), nameof(Hatch.GetStateText))]
    internal static class Hull_HatchTextPatch
    {
        private static void Postfix(Hatch __instance, ref string __result)
        {
            if (!Settings.B("HatchPressure")) return;
            var l = __instance.LeftCompartment; var r = __instance.RightCompartment;
            if (l == null || r == null) return;
            string p = "Luftdruck: " + l.AirPressure.ToString("0.00") + " / " + r.AirPressure.ToString("0.00") + " bar";
            if (Hull_HatchLockPatch.TooBig(__instance)) p += "  (gesperrt: Unterschied zu groß)";
            __result = string.IsNullOrEmpty(__result) ? p : __result + "\n" + p;
        }
    }

    [HarmonyPatch(typeof(Hatch), nameof(Hatch.CanBeOpened), MethodType.Getter)]
    internal static class Hull_HatchLockPatch
    {
        public static bool TooBig(Hatch h)
        {
            float lim = Settings.F("HatchPressureLimit");
            if (lim <= 0f || h.LeftCompartment == null || h.RightCompartment == null) return false;
            return Math.Abs(h.LeftCompartment.AirPressure - h.RightCompartment.AirPressure) > lim;
        }

        private static void Postfix(Hatch __instance, ref bool __result)
        {
            if (__result && Settings.B("HatchPressureLock") && TooBig(__instance)) __result = false;
        }
    }

    // Pressure equalizing valve. A hatch locked by pressure would stay shut forever,
    // so air slowly flows between both rooms (10 % of the difference per second) until the hatch can open again.
    public class HatchEqualizer : ModuleBase
    {
        public override void Tick()
        {
            Snorkel();
            if (!Settings.B("HatchPressureLock") || !Settings.B("HatchEqualize")) return;
            // Speed is set in the menu; running ventilation (no disable flag = on and working) multiplies it.
            double rate = Settings.F("HatchEqualizeSpeed") / 100.0;
            float boost = Settings.F("VentEqualizeFaster");
            if (boost > 1f)
                foreach (var v in Util.Find<Ventilation>())
                    if (v != null && v.DisableFlags == 0) { rate *= boost; break; }
            if (rate > 1.0) rate = 1.0;
            foreach (var h in Util.Find<Hatch>())
            {
                if (!Hull_HatchLockPatch.TooBig(h)) continue;
                var a = h.LeftCompartment; var b = h.RightCompartment;
                double pa = a.AirPressure, pb = b.AirPressure;
                if (pa <= 0.0001 || pb <= 0.0001) continue;
                double va = a.Air / pa, vb = b.Air / pb;
                double target = (a.Air + b.Air) / (va + vb);
                double move = (target * va - a.Air) * rate;
                a.Air += move;
                b.Air -= move;
            }
        }

        // Auto snorkel: the game has its own automatic raise logic (periscope depth 1-10 m), it is just never switched on.
        // With the mast up, fresh air comes in: ventilation runs and every room goes back to normal pressure (30 % per second).
        private static void Snorkel()
        {
            bool auto = Settings.B("AutoSnorkel");
            bool up = false;
            var ship = Util.PlayerShip;
            foreach (var s in Util.Find<UBOAT.Game.Scene.Items.Snorkel>())
            {
                if (s == null) continue;
                Traverse.Create(s).Field("automaticRaise").SetValue(auto);
                if (s.ShouldMastBeRaised && ship.DeckDepth < 10f && s.Performance.Value > 0.5f) up = true;
            }
            if (!up) return;
            if (Settings.B("SnorkelVent") && !Air.Silent()) foreach (var v in Util.Find<Ventilation>()) v.SetDisableFlag(DeviceDisableFlag.TurnedOff, false);
            if (!Settings.B("SnorkelAir")) return;
            foreach (var c in Util.Find<Compartment>())
            {
                double p = c.AirPressure;
                if (p <= 0.0001 || System.Math.Abs(p - 1.0) < 0.01) continue;
                double vol = c.Air / p;
                c.Air += (vol * 1.0 - c.Air) * 0.3;
            }
        }
    }

    // A leak is sealed like with two sailors (water comes in only half as fast as with one).
    [HarmonyPatch(typeof(BlockLeakAction), nameof(BlockLeakAction.UpdateAction))]
    internal static class Hull_LeakTwoMenPatch
    {
        private static void Postfix(BlockLeakAction __instance)
        {
            if (!Settings.B("LeakTwoSailors")) return;
            var m = Traverse.Create(__instance).Field("leakIntensityModifier").GetValue() as UBOAT.Game.Core.Data.Modifier;
            if (m != null && m.Value > 0.1f) m.Value = 0.1f;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Navigation.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::HarmonyLib;
    using global::UBOAT.Game.Core.Time;
    using global::UBOAT.Game.Sandbox;
    using global::UBOAT.Game.Scene.Items;
    using global::UnityEngine;

    // QoL #32: anti-grounding. The game measures the distance to the coast. If the boat gets
    // closer to land fast enough that it would reach it within N real seconds, time compression
    // is switched off (only when the boat is really heading towards land, not along the coast).
    // Cheat #33: speed multiplier (propeller force and expected speed per gear).
    public class Navigation : ModuleBase
    {
        private SandboxPlayerWolfpack wolfpack;
        private TimeCompressionController tcc;
        private GameTime gameTime;
        private float lastD = -1f, lastT, rate, nextCheck, quietUntil, limitedAt;
        private bool limited;
        private static readonly object BlockKey = new object();

        public override void Frame()
        {
            if (!Settings.B("AntiGrounding") && limited && tcc != null) { Util.Safe("Nav.Unlimit", () => tcc.RemoveTimeCompressionBlockade(BlockKey)); limited = false; }
            if (!Settings.B("AntiGrounding") || Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.2f;
            if (wolfpack == null) wolfpack = Traverse.Create(typeof(UBOAT.Game.UI.Notifications.ShallowAreaNotification)).Field("wolfpack").GetValue<SandboxPlayerWolfpack>();
            if (tcc == null) tcc = Traverse.Create(typeof(UBOAT.Game.Scene.Tasks.DisallowTimeCompressionDuringAircraftAttack)).Field("timeCompressionController").GetValue<TimeCompressionController>();
            if (gameTime == null) gameTime = Object.FindObjectOfType<GameTime>();
            if (wolfpack == null || tcc == null || gameTime == null) return;

            float d = wolfpack.LandDistance, now = Time.unscaledTime;
            if (lastD < 0f) { lastD = d; lastT = now; return; }
            if (d != lastD)
            {
                float dt = Mathf.Max(0.05f, now - lastT);
                float r = (lastD - d) / dt;                 // closing speed in map units per real second
                rate = rate <= 0f ? r : Mathf.Lerp(rate, r, 0.5f);
                lastD = d; lastT = now;
            }
            else if (now - lastT > 5f) rate = 0f;            // nothing changes: not closing in

            // Lift our own speed limit again once land is far enough away or we stopped closing in.
            if (limited && (d > Settings.F("AntiGroundingKm") * 1.5f || (rate <= 0f && now - limitedAt > 10f)))
            { Util.Safe("Nav.Unlimit", () => tcc.RemoveTimeCompressionBlockade(BlockKey)); limited = false; }

            if (!tcc.TimeCompression || gameTime.ArtificialTimeScale <= 1.5f || rate <= 0f) return;
            // LandDistance is in km. At high time compression the boat covers hundreds of km per real
            // minute, so the "seconds" rule alone fired far out at sea. Only warn when land is really near.
            if (d > Settings.F("AntiGroundingKm") || now < quietUntil) return;
            if (d / rate < Settings.F("AntiGroundingSeconds"))
            {
                rate = 0f;
                quietUntil = now + 30f;   // no repeat right away
                float cap = Settings.F("AntiGroundingMaxX");
                if (cap <= 1f) { tcc.TimeCompression = false; ToolkitRunner.ShowNotice("Land voraus! Zeitraffer gestoppt."); }
                else
                {
                    // Step down instead of off: the game's own limit mechanism (like near harbours), max x cap.
                    Util.Safe("Nav.Limit", () => tcc.SetTimeCompressionBlockade(BlockKey, cap));
                    limited = true; limitedAt = now;
                    ToolkitRunner.ShowNotice("Land voraus! Zeitraffer auf x" + cap.ToString("F0") + " begrenzt.");
                }
            }
        }

        public override void Tick()
        {
            float f = Mathf.Max(1f, Settings.F("CheatSpeed"));
            var ship = Util.PlayerShip;
            foreach (var p in Util.Find<Propeller>())
            {
                float orig = Util.Original<float>(p, "maxPower");
                bool ours = ship != null && p.transform.IsChildOf(ship.transform);   // only OUR boat, enemies keep game value
                Util.SetField(p, "maxPower", ours ? orig * Owners.PropellerFactor(f) : orig);   // drag grows with speed², so force x f² ≈ speed x f
            }
            foreach (var e in Util.Find<PlayerShipEngine>())
            {
                Scale(e, "expectedVelocityPerGear", f);
                Scale(e, "expectedVelocityPerGearUnderwater", f);
            }
        }

        private static void Scale(object obj, string field, float f)
        {
            var orig = Util.Original<float[]>(obj, field);
            var cur = Traverse.Create(obj).Field(field).GetValue<float[]>();
            if (orig == null || cur == null) return;
            for (int i = 0; i < cur.Length && i < orig.Length; i++) cur[i] = orig[i] * f;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/NavRescue.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Navigation;
    using global::UnityEngine;

    // QoL: crew members standing off the walkable area. Their walking agent is then "not placed on a
    // NavMesh" and the game spams "SetDestination / ResetPath / GetRemainingDistance can only be
    // called on an active agent ..." while the man is stuck and does nothing. Seen with many new
    // recruits. Every 3 seconds every stuck man is put back onto the nearest walkable spot (up to 5 m).
    public class NavRescue : ModuleBase
    {
        private PlayerCrew crew;
        private float next;

        private static bool Bad(Vector3 v) => float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z);

        public override void Tick()
        {
            if (!Settings.B("FixOffNavmeshCrew") || !Util.ShipReady || Time.unscaledTime < next) return;
            next = Time.unscaledTime + 3f;
            if (crew == null) crew = Object.FindObjectOfType<PlayerCrew>();
            if (crew == null || crew.Characters == null) return;
            int fixedCount = 0;
            foreach (var c in crew.Characters)
            {
                // A sailor with an invalid (NaN/Infinity) position makes Unity print "Infinity or NaN floating point
                // numbers ... Chest Collider" every frame. Put him back on walkable floor near the boat's middle.
                if (c != null && Bad(c.transform.position))
                {
                    var ch = c; var sh = Util.PlayerShip;
                    Util.Safe("NavRescue.NaN", () =>
                    {
                        var nv = ch.Navigator;
                        if (sh != null && nv != null && UnityEngine.AI.NavMesh.SamplePosition(sh.transform.position, out var h2, 60f, nv.WalkableAreasMask))
                        {
                            ch.transform.position = h2.position;
                            var rb = ch.GetComponent<Rigidbody>(); if (rb != null) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
                            nv.Warp(h2.position, LayerLookupMode.Auto, true);
                            Util.Log("Laufweg: " + ch.Name + " hatte eine ungültige Position, zurückgesetzt");
                        }
                    });
                    continue;
                }
                var nav = c != null ? c.Navigator : null;
                var agent = nav != null ? nav.Agent : null;
                if (agent == null || !agent.enabled || !agent.gameObject.activeInHierarchy || agent.isOnNavMesh) continue;
                Util.Safe("NavRescue", () =>
                {
                    if (UnityEngine.AI.NavMesh.SamplePosition(agent.transform.position, out var hit, 5f, nav.WalkableAreasMask))
                    {
                        nav.Warp(hit.position, LayerLookupMode.Auto, true);
                        fixedCount++;
                    }
                });
            }
            if (fixedCount > 0) Util.Log("Laufweg: " + fixedCount + " Matrosen zurück auf begehbaren Boden gesetzt");
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Pump.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::HarmonyLib;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Items;
    using global::UnityEngine;

    // QoL #38: pump switches on by itself when there is water in the boat.
    // Cheat #38: strong pump (x power), works at any depth, boat can never fill up with water.
    public class Pump : ModuleBase
    {
        public override void Tick()
        {
            var tw = Settings.Get("CheatPumpTestWater");
            if (tw != null && tw.On) { tw.Value = 0; Util.Safe("Testwasser", TestWater); }
            if (!Settings.B("AutoPump")) return;
            var ship = Util.PlayerShip;
            foreach (var p in Util.Find<TrimPump>())
                if (Util.IsPlayers(p) && !p.enabled && Pump_Patch.InteriorWater(ship) > 0.1)   // 0.4.0: only water in the rooms counts, not trim/hull tanks
                {
                    p.SetDisableFlag(DeviceDisableFlag.TurnedOff, false);
                    Util.Log("Pumpe automatisch an");
                }
        }

        // DoMe 2026-11-17: "put 1-10 m³ water into the boat, to test the pumps". Water goes into the compartment the
        // camera is in (else the control room, else the first one), at most up to the compartment's size.
        private static void TestWater()
        {
            var ship = Util.PlayerShip;
            if (ship == null || ship.Interior == null) { ToolkitRunner.ShowNotice("Testwasser: kein Boot", 3f); return; }
            float m3 = Mathf.Clamp(Settings.F("CheatPumpTestWaterM3"), 1f, 10f);
            UBOAT.Game.Scene.Items.Compartment c = null;
            var cam = Camera.main;
            if (cam != null) { try { c = ship.Interior.FindContainingCompartment(cam.transform.position); } catch { } }
            if (c == null)
                foreach (var x in ship.Interior.Compartments)
                    if (x != null && (x.name.IndexOf("Control", System.StringComparison.OrdinalIgnoreCase) >= 0 || x.name.IndexOf("Zentrale", System.StringComparison.OrdinalIgnoreCase) >= 0)) { c = x; break; }
            if (c == null && ship.Interior.Compartments.Length > 0) c = ship.Interior.Compartments[0];
            if (c == null) { ToolkitRunner.ShowNotice("Testwasser: kein Raum gefunden", 3f); return; }
            double before = c.Water;
            double after = System.Math.Min((double)c.Capacity, before + m3);
            c.Water = after;
            string note = Settings.B("CheatNeverFlood") ? " (Achtung: \"Boot kann nie volllaufen\" ist an, das Wasser verschwindet sofort)" : "";
            Util.Log("Testwasser: " + (after - before).ToString("0.0") + " m³ in " + c.name + " (vorher " + before.ToString("0.0") + ", jetzt " + after.ToString("0.0") + " m³)" + note);
            ToolkitRunner.ShowNotice("Testwasser: " + (after - before).ToString("0.0") + " m³ in " + c.name + note, 5f);
        }

        public override void Frame()
        {
            if (!Settings.B("CheatNeverFlood")) return;
            var ship = Util.PlayerShip;
            if (ship == null) return;
            foreach (var c in ship.Interior.Compartments)
                if (c != null && c.Water > 0.0) c.Water = 0.0;
        }
    }

    // Extra pumping on top of the game's own (only while the pump runs).
    [HarmonyPatch(typeof(TrimPump), "Update")]
    internal static class Pump_Patch
    {
        public static double InteriorWater(PlayerShip ship)
        {
            double w = 0;
            foreach (var c in ship.Interior.Compartments) if (c != null) w += c.Water;
            return w;
        }

        private static void Postfix(TrimPump __instance)
        {
            float power = Settings.F("CheatPumpPower");
            bool anyDepth = Settings.B("CheatPumpAnyDepth");
            if (power <= 1f && !anyDepth) return;
            if (!Util.IsPlayers(__instance)) return;

            var t = Traverse.Create(__instance);
            var sub = t.Field("submarine").GetValue<PlayableSubmarine>();
            var lps = t.Field("litresPerSecond").GetValue<Parameter>();
            var pressure = t.Field("litresPerSecondPressureModifier").GetValue<Modifier>();
            var tanks = t.Field("tanks").GetValue<TrimPump.ConnectedTank[]>();
            var gt = Traverse.Create(typeof(TrimPump)).Field("gameTime").GetValue<UBOAT.Game.Core.Time.GameTime>();
            if (sub == null || lps == null || tanks == null || gt == null) return;

            float baseLps = lps.GetValueExcludingModifier(pressure);
            float dt = Time.deltaTime * gt.ArtificialTimeScale;
            foreach (var ct in tanks)
            {
                // 0.4.0 (DoMe: "das Boot flutet übertrieben"): the pump is also connected to tanks that are not rooms
                // (trim / compensating / hull tanks). Emptying those x50 at depth made the boat's depth keeping flood
                // them again at once. The toolkit now only adds pumping for water standing in the ROOMS of the boat.
                if (ct == null || ct.Tank == null) continue;
                if (!(ct.Tank is UBOAT.Game.Scene.Items.Compartment))
                {
                    if (otherLogged.Count < 20 && otherLogged.Add(ct.Tank.name)) Util.Log("Pumpe: Tank \"" + ct.Tank.name + "\" ist kein Raum, Toolkit pumpt ihn nicht zusätzlich (Wasser " + ct.Tank.Water.ToString("0.00") + " von " + ct.Tank.Capacity.ToString("0.00") + ")");
                    continue;
                }
                float gameDepth = 1f - Mathf.Clamp01(sub.DeckDepth / ct.MaxDepth);
                float ourDepth = anyDepth ? 1f : gameDepth;
                gameRate += baseLps * ct.PerformanceModifier * gameDepth;
                ourRate += baseLps * ct.PerformanceModifier * ourDepth * Mathf.Max(1f, power);
                double extra = baseLps * ct.PerformanceModifier * (ourDepth * Mathf.Max(1f, power) - gameDepth) * dt;
                if (extra <= 0) continue;
                if (extra > ct.Tank.Water) extra = ct.Tank.Water;
                ct.Tank.Water -= extra;
                removed += extra;
            }
            samples++;
            LastDepth = sub.DeckDepth;
            LastGameRate = (float)(gameRate / samples); LastOurRate = (float)(ourRate / samples);
            if (Time.unscaledTime >= nextLog)
            {
                if (removed > 0)
                    Util.Log("Pumpe: Tiefe " + sub.DeckDepth.ToString("0") + " m, Spiel allein " + (LastGameRate * 1000f).ToString("0.0") + " l/s, mit Toolkit " + (LastOurRate * 1000f).ToString("0.0")
                        + " l/s (Pumpenleistung x" + Mathf.Max(1f, power).ToString("0.#") + (anyDepth ? ", volle Leistung in jeder Tiefe" : "") + "), in der letzten Minute zusätzlich " + (removed * 1000.0).ToString("0") + " l abgepumpt, Wasser in den Räumen jetzt " + (InteriorWater(Util.PlayerShip) * 1000.0).ToString("0") + " l");
                nextLog = Time.unscaledTime + 60f; removed = 0; gameRate = ourRate = 0; samples = 0;
            }
        }

        private static readonly System.Collections.Generic.HashSet<string> otherLogged = new System.Collections.Generic.HashSet<string>();
        private static double removed, gameRate, ourRate; private static int samples; private static float nextLog;
        internal static float LastDepth, LastGameRate, LastOurRate;
    }

    // 0.3.94 (DoMe: the pump tooltip still says reduced/red at depth, so he doubts the cheat works): with the pump
    // cheats on, the game's red/orange pressure line is replaced by the real toolkit rate.
    [HarmonyPatch(typeof(TrimPump), nameof(TrimPump.GetStateText))]
    internal static class Pump_TooltipPatch
    {
        private static void Postfix(TrimPump __instance, ref string __result)
        {
            try
            {
                float power = Settings.F("CheatPumpPower");
                bool anyDepth = Settings.B("CheatPumpAnyDepth");
                if ((power <= 1f && !anyDepth) || __result == null || !Util.IsPlayers(__instance)) return;
                int cut = __result.IndexOf("<line-height=50%>");
                string head = cut >= 0 ? __result.Substring(0, cut) : __result;
                string line = "Toolkit: Pumpenleistung x" + Mathf.Max(1f, power).ToString("0.#") + (anyDepth ? ", volle Leistung in jeder Tiefe" : ", Tiefe bremst wie im Spiel");
                if (Pump_Patch.LastOurRate > 0f) line += " (" + (Pump_Patch.LastOurRate * 1000f).ToString("0") + " l/s statt " + (Pump_Patch.LastGameRate * 1000f).ToString("0") + " l/s)";
                __result = head + "<line-height=50%>\n<line-height=100%>\n<color=#7CFC00>" + line + "</color>";
            }
            catch { }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Repairs.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::UBOAT.Game.Sandbox.Missions;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Items;
    using global::UBOAT.Game.Scene.Utilities;
    using global::UnityEngine;

    // QoL #30: automatic damage control. During an alarm, after the boat took damage,
    // engineer officers switch to damage control (the same toggle as on the portrait).
    // When the damage is checked or the alarm ends, they go back to their normal job.
    // QoL #31: automatic production. Scrap from all storages is moved into the workshop,
    // so the engineers keep making replacement parts (game's own salvage job).
    public class Repairs : ModuleBase
    {
        private PlayerCrew crew;
        private readonly HashSet<PlayableCharacter> switched = new HashSet<PlayableCharacter>();
        private float nextDc, nextWs;

        public override void Tick()
        {
            if (crew == null) crew = Object.FindObjectOfType<PlayerCrew>();
            if (crew == null || !Util.ShipReady) return;
            if (Time.time >= nextDc) { nextDc = Time.time + 2f; Util.Safe("DamageControl", DamageControl); }
            if (Time.time >= nextWs) { nextWs = Time.time + 10f; Util.Safe("Workshop", FeedWorkshop); }
        }

        private void DamageControl()
        {
            var ship = Util.PlayerShip;
            var cs = ship.CommandSpot != null ? ship.CommandSpot.GetComponent<CommandStation>() : null;
            bool need = Settings.B("AutoDamageControl") && ship.Alarmed && cs != null && cs.IsInspectionRequired;

            if (!need)
            {
                foreach (var o in switched)
                    if (o != null && o.RoleOverride != null && o.RoleOverride.Name == "ForcedRest")
                        MissionUtility.SetForcedRest(o, false);
                switched.Clear();
                return;
            }

            int wanted = Mathf.RoundToInt(Settings.F("AutoDamageControlCount"));
            switched.RemoveWhere(o => o == null || o.RoleOverride == null);
            var officers = new List<PlayableCharacter>(crew.Officers);
            officers.Sort((a, b) => DcScore(b).CompareTo(DcScore(a)));
            foreach (var o in officers)
            {
                if (switched.Count >= wanted) break;
                if (o == null || switched.Contains(o) || o.RoleOverride != null) continue;
                string cls = o.Data.characterClass;
                bool ok = cls == "Engineer" || (cls == "Radioman" && Settings.B("AutoDamageControlRadioman"));
                if (!ok || o.Injury || o.IsSelected || o.IsImpersonated) continue;
                MissionUtility.SetForcedRest(o, true);
                switched.Add(o);
                Util.Log("Schadenskontrolle: " + o.Name);
            }
        }

        // Higher = sent first. Handyman repairs 25% faster, Thrifty gets scrap back from
        // used replacement parts. Engine Specialist stays at the engines if possible.
        private static int DcScore(PlayableCharacter o)
        {
            if (o == null || !Settings.B("AutoDamageControlSkills")) return 0;
            int s = 0;
            if (o.Data.HasStatus("Handyman")) s += 4;
            if (o.Data.HasStatus("Thrifty")) s += 2;
            if (o.Data.HasStatus("Engine Specialist")) s -= 3;
            return s;
        }

        private static void FeedWorkshop()
        {
            if (!Settings.B("AutoWorkshop")) return;
            var ship = Util.PlayerShip;
            foreach (var ws in Util.Find<Workshop>())
            {
                var target = ws.Storage;
                if (target == null || target.GetTotalCount("Scrap") >= 2f) continue;
                foreach (var s in Util.Find<Storage>())
                {
                    if (s == target || s.IsShop) continue;
                    for (int i = Util.Slots(s) - 1; i >= 0; i--)
                    {
                        var cargo = s.GetItemAt(i) as Cargo;
                        if (cargo == null || cargo.TypeName != "Scrap") continue;
                        if (!StorageUtility.CanStoreItem(target, cargo)) return;
                        int free = target.GetFirstFreeSlotIndex();
                        if (free < 0) return;
                        StorageUtility.TransferInstantly(s, i, target, free, cargo.Count);
                        Util.Log("Werkstatt: Schrott eingeräumt");
                        if (target.GetTotalCount("Scrap") >= 2f) return;
                    }
                }
            }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/RouteLead.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Entities;
    using global::UnityEngine;

    // Course plotting: the game switches to the next waypoint only AFTER passing the current one, so the boat
    // overshoots and turns in a wide arc behind the point. Now the player's boat starts turning early
    // (lead distance = turn radius * tan(half the turn angle)), so it rounds the corner on the line. World units = km.
    [HarmonyPatch(typeof(EntityRoute), "Update", new[] { typeof(Vector2) })]
    internal static class RouteLeadPatch
    {
        private static readonly AccessTools.FieldRef<EntityRoute, Entity> EntityF = AccessTools.FieldRefAccess<EntityRoute, Entity>("entity");
        private static readonly AccessTools.FieldRef<EntityRoute, List<Vector2>> PointsF = AccessTools.FieldRefAccess<EntityRoute, List<Vector2>>("waypoints");

        private static void Prefix(EntityRoute __instance, Vector2 position)
        {
            if (!Settings.B("RouteLead")) return;
            var ship = Util.PlayerShip;
            if (ship == null || !ReferenceEquals(EntityF(__instance), ship)) return;
            var pts = PointsF(__instance);
            if (pts == null || pts.Count < 2) return;   // last point: drive to it exactly
            Vector2 toWp = pts[0] - position, next = pts[1] - pts[0];
            float dist = toWp.magnitude;
            if (dist < 1e-4f || next.sqrMagnitude < 1e-8f) return;
            float angle = Vector2.Angle(toWp, next);      // 0 = straight on, 90 = right angle
            if (angle < 5f) return;
            float radiusKm = Mathf.Max(0.05f, Settings.F("RouteLeadRadius") / 1000f);
            float lead = Mathf.Min(radiusKm * Mathf.Tan(Mathf.Min(angle, 170f) * 0.5f * Mathf.Deg2Rad), next.magnitude * 0.5f);
            if (dist <= lead) __instance.RemoveAtSilent(0);
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/SeaFloor.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Linq;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::DWS.Common.InjectionFramework;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Core.Time;
    using global::UBOAT.Game.Sandbox;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Environment.Terrain;

    // Always load the sea floor in open sea when water is shallower than ~300 m
    // (vanilla only loads it inside encounter areas). Own code.
    internal static class SeaFloor
    {
        [Inject] internal static UserSettings userSettings;
        [Inject] internal static GameTime gameTime;
        [Inject] internal static SandboxPlayerWolfpack wolfpack;

        internal static bool Forced;
        private static Vector2 lastPos = new Vector2(float.NaN, 0f);
        private static SandboxSceneOrigin origin;
        private static float nextOriginSearch;
        private static readonly Vector2[] probe =
        {
            Vector2.zero, new Vector2(8f, 0f), new Vector2(-8f, 0f), new Vector2(0f, 8f), new Vector2(0f, -8f),
            new Vector2(5.66f, 5.66f), new Vector2(5.66f, -5.66f), new Vector2(-5.66f, 5.66f), new Vector2(-5.66f, -5.66f)
        };

        internal static bool Active => Forced && wolfpack != null && !wolfpack.IsInArea;

        internal static void Update(TerrainController tc, WorldGrid[] grids)
        {
            if (!Settings.B("SeaFloorShallow") || wolfpack == null || wolfpack.IsInArea || grids == null || grids.Length == 0) { Forced = false; return; }
            if (userSettings?.GameplaySettings != null && userSettings.GameplaySettings.travelSystem == TravelSystem.Dynamic) { Forced = false; return; }
            if (gameTime != null && (float)gameTime.TimeScale * gameTime.ArtificialTimeScale > Settings.F("SeaFloorMaxCompression")) { Forced = false; lastPos.x = float.NaN; return; }

            var pos = wolfpack.Position;
            if (float.IsNaN(lastPos.x) || (pos - lastPos).sqrMagnitude > 0.01f)
            {
                lastPos = pos;
                float top = probe.Max(o => WorldMaps.ElevationMap.GetAtWorldSpace(pos + o));
                if (top > -300f) Forced = true;
                else if (top < -350f) Forced = false;
            }
            if (!Forced) return;

            var grid = grids[0];
            if (origin == null && Time.unscaledTime > nextOriginSearch)
            {
                nextOriginSearch = Time.unscaledTime + 2f;
                origin = Object.FindObjectOfType<SandboxSceneOrigin>();
            }
            if (origin != null) grid.SetOffset(origin.Position.x * 1000f, origin.Position.y * 1000f, false);
            grid.gameObject.SetActive(true);
            grid.GridEnabled = true;
        }

        // Real loaded seabed under the boat, NaN when not known.
        // Water depth from the game's world elevation map (good to a few metres) when the tile under the boat has no
        // built terrain (deeper than ~650 m). Same source as the map's depth under the mouse.
        internal static float MapDepthUnderShip()
        {
            try
            {
                var ship = Util.PlayerShip;
                var sb = Util.Find<UBOAT.Game.Sandbox.Sandbox>(30f).FirstOrDefault();
                if (ship == null || sb == null) return float.NaN;
                float h = WorldMaps.ElevationMap.GetAtWorldSpace(sb.SceneToEncounterSpace(ship.transform.position));
                return float.IsNaN(h) || h >= 0f ? float.NaN : -h;
            }
            catch { return float.NaN; }
        }

        internal static float SeabedDepthUnderShip()
        {
            var ship = Util.PlayerShip;
            var tc = Util.Find<TerrainController>(30f).FirstOrDefault();
            if (ship == null || tc == null || tc.MainGrid == null) return float.NaN;
            var p = ship.transform.position;
            float h = tc.MainGrid.GetInterpolatedHeight(p.x, p.z, float.NaN);
            return float.IsNaN(h) ? float.NaN : -h;
        }
    }

    [HarmonyPatch(typeof(TerrainController), "LateUpdate")]
    internal static class SeaFloor_TerrainPatch
    {
        private static void Postfix(TerrainController __instance, WorldGrid[] ___terrains)
            => Util.Safe("SeaFloor", () => SeaFloor.Update(__instance, ___terrains));
    }

    // Vanilla starts an "encounter" (no high time compression) when the boat is near the coarse map bottom.
    // With the real floor loaded this is only kept when the real floor is actually close.
    [HarmonyPatch(typeof(SandboxPlayerWolfpack), "DetermineEncounterMode")]
    internal static class SeaFloor_EncounterPatch
    {
        internal static bool Inside;
        private static void Prefix()
        {
            Inside = false;
            if (!SeaFloor.Active) return;
            var ship = Util.PlayerShip;
            float bed = SeaFloor.SeabedDepthUnderShip();
            if (ship == null || float.IsNaN(bed)) return;
            Inside = ship.CurrentColliderBottomDepth < bed - 15f && ship.TargetDepth < bed - 15f;
        }
        private static void Finalizer() => Inside = false;
    }

    [HarmonyPatch(typeof(PlayerShip), "CurrentColliderBottomDepth", MethodType.Getter)]
    internal static class SeaFloor_BottomPatch
    {
        private static bool Prefix(ref float __result)
        {
            if (!SeaFloor_EncounterPatch.Inside) return true;
            __result = 0f;
            return false;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/SeafloorLoad.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Environment.Terrain;
    using global::UnityEngine;

    // QoL (DoMe 2026-11-13): the sea floor around the boat is always really built. The game builds real terrain only for
    // 1 km tiles whose highest point is above -650 m; deeper tiles get a flat "sea floor" stand-in only, and depth queries
    // there (GetInterpolatedHeight) return -2000. After the game's own scan we build every not-yet-built tile within
    // the chosen radius. Sonar picture / depth gauge read the same terrain, so they see the real floor too.
    [HarmonyPatch(typeof(ProceduralTerrainGrid), "ScanNearbySegments")]
    internal static class SeafloorLoad_ScanPatch
    {
        private static float nextScan;
        private static int built;
        private static bool logged;

        private static void Postfix(ProceduralTerrainGrid __instance)
        {
            try
            {
                if (!Settings.B("SeafloorAlways") || Time.unscaledTime < nextScan) return;
                nextScan = Time.unscaledTime + 2f;
                var ship = Util.PlayerShip;
                if (ship == null) return;
                float size = Mathf.Max(100f, __instance.SegmentSize);
                float radius = Mathf.Clamp(Settings.F("SeafloorRadius"), 0.5f, 4f) * 1000f;
                Vector3 p = ship.transform.position;
                __instance.GetSegmentAtSceneCoordinates(p.x, p.z, out int cx, out int cz);
                int n = Mathf.CeilToInt(radius / size);
                for (int dz = -n; dz <= n; dz++)
                    for (int dx = -n; dx <= n; dx++)
                    {
                        // distance from the boat to the nearest point of this tile
                        float ox = Mathf.Max(0f, Mathf.Abs(dx) - 0.5f) * size, oz = Mathf.Max(0f, Mathf.Abs(dz) - 0.5f) * size;
                        if (ox * ox + oz * oz > radius * radius) continue;
                        var seg = __instance.GetSegment(cx + dx, cz + dz);
                        if (seg == null || seg.Loaded || seg.Loading) continue;
                        seg.Generate(1);
                        seg.Active = true;
                        built++;
                    }
                if (built > 0 && !logged) { logged = true; Util.Log("Meeresboden: tiefe Kacheln werden voll gebaut (Radius " + (radius / 1000f).ToString("0.0") + " km)"); }
            }
            catch (System.Exception e) { if (!logged) { logged = true; Util.Log("Meeresboden: " + e.Message); } }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/SeaLayer.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Core.Time;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Sandbox;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Environment;
    using global::UBOAT.Game.UI;

    // Realistic thermal layer ("Sprungschicht"), own model:
    // - depth 25-150 m from the sea temperature (warm water = shallow layer), slow random drift,
    //   sunny calm afternoons push it a bit higher
    // - strength from temperature: cold water weak layer (max -50 %), warm water strong (max -90 %)
    // - below the layer: enemy sonar/hydrophones find you worse, and your hydrophone hears surface ships worse
    internal static class SeaLayer
    {
        internal static float Depth = float.NaN;
        internal static float MaxCut = 0.9f;
        private static GameTime gameTime;
        private static bool wasBelow;

        internal static bool On => Settings.B("SeaLayer");

        internal static void Compute(Weather w, ref float layer)
        {
            float temp = w.Temperature;
            if (gameTime == null) gameTime = SeaFloor.gameTime;
            float sun = 0f;
            if (gameTime != null)
            {
                float hour = (float)gameTime.LocalDateTime.TimeOfDay.TotalHours;
                sun = Mathf.Max(0f, Mathf.Cos((hour - 15f) * Mathf.PI / 12f))
                      * Mathf.Clamp01(1f - w.Cloudiness.Value) * Mathf.Clamp01((12f - w.TotalWindSpeed) / 8f);
            }
            float drift = 0f;
            if (gameTime != null)
                drift = (Mathf.PerlinNoise((float)(gameTime.StoryTicksSinceCampaignStart / 864000000000.0) * 0.6f, 0.71f) - 0.5f) * 30f;
            layer = Mathf.Clamp(115f - temp * 3.5f - 8f * sun + drift, 25f, 150f);
            MaxCut = Mathf.Lerp(0.5f, 0.9f, Mathf.Clamp01((temp - 4f) / 16f + 0.1f * sun));
            Depth = layer;
            Notify();
        }

        internal static float Factor(float depth, float slope)
        {
            if (float.IsNaN(Depth)) return 1f;
            return 1f - Mathf.Clamp((depth - Depth) * slope, 0f, MaxCut);
        }

        // Short note when the boat goes under / comes over the layer.
        private static void Notify()
        {
            if (!Settings.B("SeaLayerNote")) return;
            var ship = Util.PlayerShip;
            if (ship == null) return;
            bool below = ship.Depth > Depth + 3f;
            if (below == wasBelow || (!below && ship.Depth > Depth - 3f)) return;
            wasBelow = below;
            string t = below ? "Unter der Sprungschicht (~" + Mathf.RoundToInt(Depth) + " m): schwerer zu orten, Horcher hört schlechter"
                             : "Über der Sprungschicht (~" + Mathf.RoundToInt(Depth) + " m)";
            var bar = Object.FindObjectOfType<NotificationBarUI>();
            if (bar != null) bar.OpenNow("Icons/Notification Bar/Notification - 40 - Danger", t);
        }
    }

    [HarmonyPatch(typeof(Weather), "LowFrequencyUpdate")]
    internal static class SeaLayer_DepthPatch
    {
        private static void Postfix(Weather __instance, ref float ___sonicLayerDepth)
        {
            if (!SeaLayer.On) { SeaLayer.Depth = float.NaN; return; }
            float d = ___sonicLayerDepth;
            Util.Safe("SeaLayer", () => SeaLayer.Compute(__instance, ref d));
            ___sonicLayerDepth = d;
        }
    }

    [HarmonyPatch(typeof(Entity), "UpdateDetectability")]
    internal static class SeaLayer_ShadowPatch
    {
        private static void Postfix(Entity __instance, Modifier ___sonarShadowZoneModifier, Modifier ___hydrophoneShadowZoneModifier)
        {
            if (!SeaLayer.On || float.IsNaN(SeaLayer.Depth)) return;
            float v = SeaLayer.Factor(__instance.Depth, 0.025f);
            if (___sonarShadowZoneModifier != null) ___sonarShadowZoneModifier.Value = v;
            if (___hydrophoneShadowZoneModifier != null) ___hydrophoneShadowZoneModifier.Value = SeaLayer.Factor(__instance.Depth, 0.02f);
        }
    }

    // Own hydrophone below the layer hears ships above it worse.
    [HarmonyPatch(typeof(SandboxGroup), "HydrophoneDetectabilityPlayerFunc")]
    internal static class SeaLayer_HearingPatch
    {
        private static void Postfix(ref float __result)
        {
            if (!SeaLayer.On || !Settings.B("SeaLayerHearing") || __result <= 0f) return;
            var ship = Util.PlayerShip;
            if (ship != null) __result *= Mathf.Max(0.4f, SeaLayer.Factor(ship.Depth, 0.015f));
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/SnorkelGuard.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Items;
    using global::UnityEngine;

    // DoMe 2026-11-18: "vlt kann es ja detektieren, wenn er Schnorchel im Wasser ist, WENN ER AKTIV IST? aber NUR dann?"
    // Only while the snorkel of OUR boat is ordered up, has been up for 20 s, the boat lies at its ordered depth
    // (within 1 m) and the snorkel head is under water for 5 s: the ordered depth is raised by half a metre.
    // In every other case the depth is not touched. Never shallower than 2 m.
    public class SnorkelGuard : ModuleBase
    {
        private float upSince = -1f, wetSince = -1f, nextStep, nextLine;
        // DoMe 2026-11-18: "Schnorchel liefert immer +10 was benötigt wird". While the snorkel of OUR boat really works
        // (ordered up and performing), the air in the boat gains at least +x: whatever is used, plus x on top.
        // Never lowers anything; snorkel down or not working = our value is 0.
        private float airLogAt; private float lastAdd = -1f;
        private static float factorCached; private static float factorAt = -999f;
        private static float DisplayFactor()
        {
            if (factorCached > 0f && Time.unscaledTime - factorAt < 60f) return factorCached;
            factorAt = Time.unscaledTime; float f = 6000f;
            try
            {
                foreach (var g in Resources.FindObjectsOfTypeAll<UBOAT.Game.UI.Resources.ResourceGUI>())
                {
                    if (g == null) continue;
                    var t = Traverse.Create(g);
                    var res = t.Field("resource").GetValue() as UBOAT.Game.Core.Data.Resource;
                    if (res == null || res.Name != "Oxygen") continue;
                    float v = t.Field("tooltipGainMultiplier").GetValue<float>();
                    if (v > 0f) { f = v; break; }
                }
            }
            catch { }
            return factorCached = f;
        }
        private void Air(PlayableSubmarine sub, Snorkel sn)
        {
            var oxygen = sub.GetResource("Oxygen");
            if (oxygen == null) return;
            var m = Util.OneDelta(oxygen.Gain, "Toolkit Schnorchel");
            // 0.4.25: the setting is the number the game SHOWS for the air ("+10 per min"). The game's own value is that
            // number divided by the display factor of its air gauge (6000 unless the gauge says otherwise).
            float shown = Settings.F("SnorkelAirPlus"), factor = DisplayFactor();
            float plus = shown / factor, add = 0f;
            bool works = sn != null && sn.ShouldMastBeRaised && sn.Performance.Value > 0.05f;
            if (plus > 0f && works)
            {
                // the game may scale the sum (factors on the gain), so the needed value is solved from two samples
                float v0 = oxygen.Gain.GetValueWithModifierOverride(m, 0f), v1 = oxygen.Gain.GetValueWithModifierOverride(m, 1f);
                if (v0 < plus && v1 - v0 > 1e-6f) add = (plus - v0) / (v1 - v0);
            }
            if (Mathf.Abs(m.Value - add) > 1e-4f) m.Value = add;
            if ((add > 0f) != (lastAdd > 0f) || (add > 0f && Time.unscaledTime > airLogAt))
            {
                airLogAt = Time.unscaledTime + 60f;
                Util.Log(add > 0f ? "Schnorchel-Luft: Verbrauch gedeckt und +" + shown.ToString("0.#") + " dazu, wie im Spiel angezeigt (Anzeige-Faktor " + factor.ToString("0") + "; unser Zusatz angezeigt +" + (add * factor).ToString("0.#") + ", Luft-Bilanz jetzt angezeigt " + (oxygen.Gain.Value * factor).ToString("+0.#;-0.#;0") + " pro Minute, Luft im Boot " + (oxygen.Amount / Mathf.Max(0.0001f, (float)oxygen.Capacity) * 100f).ToString("0") + " %)"
                                  : "Schnorchel-Luft: kein Zusatz (Schnorchel " + (sn == null ? "nicht an Bord" : works ? "liefert selbst genug" : "arbeitet nicht") + ")");
            }
            lastAdd = add;
        }
        public override void Tick()
        {
            var sub = Util.PlayerShip as PlayableSubmarine;
            if (sub == null) return;
            Snorkel sn = null;
            foreach (var x in Util.Find<Snorkel>()) if (x != null && Util.IsPlayers(x)) { sn = x; break; }
            Util.Safe("Snorkel.Air", () => Air(sub, sn));
            if (!Settings.B("SnorkelKeepHead")) { upSince = wetSince = -1f; return; }
            if (sn == null || !sn.ShouldMastBeRaised || !sub.SubmergedOrGoingToSubmerge) { upSince = wetSince = -1f; return; }
            float now = Time.time;
            if (upSince < 0f) upSince = now;
            var tip = Traverse.Create(sn).Field("tip").GetValue<Transform>();
            if (tip == null) return;
            // water level from the game's own deck depth (same basis as its depth meter)
            float water = sub.transform.position.y - sub.VerticalOriginOffset + sub.DeckDepthUnclamped;
            float head = water - tip.position.y;                      // + = head under water
            bool atDepth = Mathf.Abs(sub.DeckDepthUnclamped - sub.TargetDepth) < 1f;
            if (Time.unscaledTime > nextLine)
            {
                nextLine = Time.unscaledTime + 60f;
                Util.Log("Schnorchel aktiv: Kopf " + (head > 0f ? head.ToString("0.0") + " m UNTER Wasser" : (-head).ToString("0.0") + " m über Wasser") + ", Deck " + sub.DeckDepthUnclamped.ToString("0.0") + " m, Zieltiefe " + sub.TargetDepth.ToString("0.0") + " m");
            }
            if (head <= 0.2f || !atDepth || now - upSince < 20f) { wetSince = -1f; return; }
            if (wetSince < 0f) wetSince = now;
            if (now - wetSince < 5f || now < nextStep) return;
            float old = sub.TargetDepth, want = Mathf.Max(2f, old - 0.5f);
            if (want >= old - 0.01f) return;
            nextStep = now + 5f;
            if (!Owners.Depth(Util.PlayerShip, "Schnorchel-Wächter", Owners.Safety, want, true, false)) return;
            Util.Log("Schnorchel: Kopf " + head.ToString("0.0") + " m unter Wasser, Zieltiefe " + old.ToString("0.0") + " -> " + want.ToString("0.0") + " m");
            ToolkitRunner.ShowNotice("Schnorchel-Kopf unter Wasser: Zieltiefe " + want.ToString("0.0") + " m", 4f);
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/TankReset.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Items;
    using global::UnityEngine;
    using global::UBOAT.Game.Scene.Entities;

    // Emergency key: empty all ballast tanks, empty trim tanks too, log the tank state before/after.
    public class TankReset : ModuleBase
    {
        public override void Frame()
        {
            if (!Keys.Down("KeyTanksEmpty")) return;
            var ship = Util.PlayerShip;
            if (ship == null) return;
            Util.Log("Not-Taste Tanks: vorher " + State(ship));
            if (ship.BallastTanks != null)
                foreach (var t in ship.BallastTanks) if (t != null) t.Water = 0.0;
            foreach (var p in Util.Find<TrimPump>())
            {
                if (!Util.IsPlayers(p)) continue;
                var tanks = Traverse.Create(p).Field("tanks").GetValue<TrimPump.ConnectedTank[]>();
                if (tanks == null) continue;
                foreach (var ct in tanks)
                {
                    if (ct?.Tank == null) continue;
                    
                    
                    ct.Tank.Water = 0.0;
                }
            }
            Util.Log("Not-Taste Tanks: nachher " + State(ship));
            UboatToolkit.ToolkitRunner.ShowNotice("Alle Tauchzellen leer, Trimmtanks leer (Notauftauchen)", 5f);
        }

        // 0.3.41 (DoMe): tank levels in the info lines (in % of capacity)
        public static string Line;
        private float lineAt;
        public override void Tick()
        {
            if (Time.unscaledTime < lineAt) return;
            lineAt = Time.unscaledTime + 1f;
            var ship = Util.PlayerShip;
            if (ship == null || !Settings.B("TankDisplay")) { Line = null; return; }
            var b = new System.Collections.Generic.List<string>();
            if (ship.BallastTanks != null)
                foreach (var t in ship.BallastTanks) if (t != null && t.Capacity > 0) b.Add((t.Water / t.Capacity * 100.0).ToString("0") + "%");
            var tr = new System.Collections.Generic.List<string>();
            foreach (var p in Util.Find<TrimPump>())
            {
                if (!Util.IsPlayers(p)) continue;
                var tanks = Traverse.Create(p).Field("tanks").GetValue<TrimPump.ConnectedTank[]>();
                if (tanks == null) continue;
                foreach (var ct in tanks) if (ct?.Tank != null && ct.Tank.Capacity > 0 && !(ct.Tank is UBOAT.Game.Scene.Items.Compartment)) tr.Add((ct.Tank.Water / ct.Tank.Capacity * 100.0).ToString("0") + "%");
            }
            Line = Events.Join(b.Count > 0 ? "Tauchzellen " + string.Join(" ", b) : null, tr.Count > 0 ? "Trimm " + string.Join(" ", tr) : null);
        }

        private static string State(PlayerShip ship)
        {
            string s = "Tauchzellen:";
            if (ship.BallastTanks != null)
                foreach (var t in ship.BallastTanks) if (t != null) s += " " + t.Water.ToString("0") + "/" + t.Capacity.ToString("0");
            return s;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/UnderFire.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Collections.Generic;
    using global::HarmonyLib;
    using global::UBOAT.Game.Core.Time;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Items;
    using global::UnityEngine;

    // "Herr Kaleun, was tun wir?": the lookouts notice that we are being shot at (enemy gun aims at us,
    // or damage from an enemy). The game pauses and asks what to do, with a weighed recommendation:
    // aircraft -> crash dive if the water is deep enough, else AA fire back; merchant -> fire back with
    // the deck gun; warship -> crash dive if deep enough, else evade; already submerged -> evade.
    // Modes: 0 off, 1 ask (default), 2 do the recommendation automatically.
    // Ask without pause: only an icon on the right edge. "Ignore x minutes": messages are only collected.
    public static class UnderFire
    {
        internal enum Act { Dive, Evade, FireBack, Nothing }
        internal class Alert { public Entity Attacker; public string Text; public Act Advice; public string Why; public string Clock; public int Count = 1; }

        internal static readonly List<Alert> Collected = new List<Alert>();
        private static readonly Dictionary<int, float> lastSeen = new Dictionary<int, float>();
        private static Alert current;          // waiting for an answer
        private static bool windowOpen, pausedByUs;
        private static float ignoreUntil;
        private static int ignoredCount;
        private static GameTime gameTime;
        internal static float ManGunsUntil;

        private static readonly string[] ActName = { "Alarmtauchen", "Ausweichen (Heck zum Gegner)", "Zurückschießen", "Nichts tun" };

        // Called by the gun patch and by the damage report.
        internal static void Report(Entity attacker, string what)
        {
            try
            {
                int mode = Mathf.RoundToInt(Settings.F("UnderFireMode"));
                var ship = Util.PlayerShip;
                if (mode <= 0 || ship == null || attacker == null || attacker == ship) return;
                if (Settings.B("UnderFireNeedSighting") && ship.GetObservation(attacker) == null && !(attacker is Aircraft)) return;
                int id = attacker.GetInstanceID();
                float now = Time.unscaledTime;
                if (lastSeen.TryGetValue(id, out float t) && now - t < Mathf.Max(5f, Settings.F("UnderFireCooldown")))
                {
                    if (current != null && current.Attacker == attacker) current.Count++;
                    return;
                }
                lastSeen[id] = now;

                var a = new Alert { Attacker = attacker, Clock = DateTime.Now.ToString("HH:mm:ss") };
                a.Advice = Advise(ship, attacker, out a.Why);
                Vector3 to = attacker.transform.position - ship.transform.position;
                float km = new Vector2(to.x, to.z).magnitude / 1000f;
                float brg = (Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg + 360f) % 360f;
                a.Text = what + ": " + (attacker is Aircraft ? "Flugzeug" : "Schiff") + " \"" + attacker.name.Replace("(Clone)", "").Trim() +
                         "\", " + km.ToString("0.0") + " km, Peilung " + Mathf.RoundToInt(brg).ToString("000") + "°";
                Collected.Add(a);
                if (Collected.Count > 100) Collected.RemoveAt(0);
                Util.Log("Beschuss: " + a.Text + " | Vorschlag: " + ActName[(int)a.Advice]);

                if (now < ignoreUntil) { ignoredCount++; return; }   // ignored: only collect
                if (mode >= 2) { Do(a.Advice, a); return; }
                if (current != null && windowOpen) { current = a; return; }  // window already open: show the newest
                current = a;
                // More than 2 messages within 30 s: no pause (only the box at the edge).
                float tNow = Time.unscaledTime;
                recent.Add(tNow); recent.RemoveAll(x => tNow - x > 30f);
                Open(Settings.B("UnderFirePause") && recent.Count <= 2);
            }
            catch (Exception ex) { Util.Log("Beschuss: " + ex.Message); }
        }

        private static Act Advise(PlayerShip ship, Entity at, out string why)
        {
            float floor = SeaFloor.SeabedDepthUnderShip();
            bool deep = float.IsNaN(floor) || floor > 60f;
            float minFloor = float.IsNaN(floor) ? SeaFloor.MapDepthUnderShip() : float.NaN;
            string depthTxt = (!float.IsNaN(floor) ? "Wassertiefe " + Mathf.RoundToInt(floor) + " m"
                              : !float.IsNaN(minFloor) ? "Wassertiefe ca. " + Mathf.RoundToInt(minFloor) + " m"
                              : "Wassertiefe unbekannt") + (ship.SubmergedOrGoingToSubmerge ? ", wir auf " + Mathf.RoundToInt(Mathf.Max(0f, ship.DeckDepth)) + " m" : ", wir sind aufgetaucht");
            if (float.IsNaN(floor) && !float.IsNaN(minFloor)) deep = minFloor > 60f;
            if (ship.SubmergedOrGoingToSubmerge) { why = "Wir sind schon unter Wasser - wegdrehen und schnell weg."; return Act.Evade; }
            if (at is Aircraft)
            {
                if (deep) { why = "Flugzeug - Tauchen ist am sichersten (" + depthTxt + ")."; return Act.Dive; }
                why = "Flugzeug, aber zu flach zum Tauchen (" + depthTxt + ") - Flak schießt zurück."; return Act.FireBack;
            }
            string cat = (at.Category ?? "").ToLowerInvariant();
            bool merchant = cat.Contains("freighter") || cat.Contains("tanker") || cat.Contains("coaster") || cat.Contains("fishing");
            if (merchant && HasDeckGun()) { why = "Handelsschiff - unser Deckgeschütz ist stärker, zurückschießen."; return Act.FireBack; }
            if (deep) { why = "Kriegsschiff - tauchen (" + depthTxt + ")."; return Act.Dive; }
            why = "Kriegsschiff und zu flach zum Tauchen (" + depthTxt + ") - Heck zum Gegner und volle Fahrt."; return Act.Evade;
        }

        private static bool HasDeckGun()
        {
            foreach (var g in Util.Find<Gun>()) if (g != null && g.isActiveAndEnabled && Util.IsPlayers(g)) return true;
            return false;
        }

        internal static void Do(Act act, Alert a)
        {
            var ship = Util.PlayerShip;
            if (ship == null) return;
            try
            {
                switch (act)
                {
                    case Act.Dive:
                        ship.StartAlarm("Crash Diving", true);
                        ship.FollowDiveSchedule = false;
                        Owners.Preset(ship, "Unter Beschuss: Alarmtauchen", Owners.Safety, DepthPreset.MaxSafeDepth, false);
                        ship.OrderEngineGear(5, false);
                        break;
                    case Act.Evade:
                        if (a?.Attacker != null && CourseCfg.SetTargetCourse != null)
                        {
                            var route = ship.MobileSandboxGroup.Route;
                            Vector2 dir = route.Count > 0 ? (route[0] - ship.MobileSandboxGroup.Position).normalized : route.TargetDirection;
                            float baseAng = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                            Vector3 away = ship.transform.position - a.Attacker.transform.position;
                            float want = Mathf.Atan2(away.z, away.x) * Mathf.Rad2Deg;
                            CourseCfg.SetTargetCourse.Invoke(ship, new object[] { Mathf.DeltaAngle(want, baseAng) });
                        }
                        ship.StartAlarm("Alarm", true);
                        ship.OrderEngineGear(5, false);
                        break;
                    case Act.FireBack:
                        ship.StartAlarm("Alarm", true);
                        ManGunsUntil = Time.unscaledTime + 120f;
                        if (a?.Attacker != null)
                            foreach (var g in Util.Find<Gun>()) if (g != null && Util.IsPlayers(g)) Util.Safe("Beschuss.Ziel", () => g.TargetOverride = a.Attacker);
                        break;
                }
                UboatToolkit.ToolkitRunner.ShowNotice("Befehl: " + ActName[(int)act], 5f);
                Util.Log("Beschuss-Befehl ausgeführt: " + ActName[(int)act]);
            }
            catch (Exception ex) { Util.Log("Beschuss-Befehl: " + ex.Message); }
        }

        private static readonly System.Collections.Generic.List<float> recent = new System.Collections.Generic.List<float>();
        private static void Open(bool pause)
        {
            windowOpen = true;
            if (pause)
            {
                if (gameTime == null) gameTime = UnityEngine.Object.FindObjectOfType<GameTime>();
                if (gameTime != null && !gameTime.Paused && gameTime.IsPauseAllowed) { gameTime.Paused = true; pausedByUs = true; }
            }
        }

        private static void Close()
        {
            windowOpen = false; current = null;
            if (pausedByUs && gameTime != null) gameTime.Paused = false;
            pausedByUs = false;
        }

        // Drawn every frame from the toolkit GUI (also when F10 is closed).
        // Same choices as before, but as a box at the right edge (not in the middle); click or key Alt+1..4, Alt+5 = ignore.
        private static readonly string[] ActKeys = { "KeyUnderFire1", "KeyUnderFire2", "KeyUnderFire3", "KeyUnderFire4" };
        private static bool migrated;
        // 0.4.7: true while the under-fire box or its "ignored" button is on screen (they need all GUI passes for clicks)
        public static bool NeedsEvents => windowOpen || current != null || Time.unscaledTime < ignoreUntil;
        public static void Draw()
        {
            if (!migrated)
            {
                migrated = true;
                string[] ks = { "KeyUnderFire1", "KeyUnderFire2", "KeyUnderFire3", "KeyUnderFire4", "KeyUnderFireIgnore" };
                for (int i = 0; i < ks.Length; i++)
                {
                    var o = Settings.Get(ks[i]);
                    if (o != null && (o.Text == "Alt+Alpha" + (i + 1) || o.Text == "Ctrl+Alt+Alpha" + (i + 1))) { o.Text = "Ctrl+Shift+Alpha" + (i + 1); Util.Log("Beschuss-Taste " + ks[i] + " von Alt+" + (i + 1) + " auf Strg+Shift+" + (i + 1) + " geändert (Alt+Zahl / Strg+Alt+Zahl sind der Maschinentelegraf)"); Settings.Save(); }
                }
            }
            if (Mathf.RoundToInt(Settings.F("UnderFireMode")) != 1) { if (windowOpen) Close(); return; }
            float now = Time.unscaledTime;
            if (current != null && now >= ignoreUntil)
            {
                for (int i = 0; i < ActKeys.Length; i++)
                    if (Keys.Down(ActKeys[i])) { var a = current; Close(); current = null; if (i != (int)Act.Nothing) Do((Act)i, a); return; }
                if (Keys.Down("KeyUnderFireIgnore"))
                {
                    int min = Mathf.Max(1, Mathf.RoundToInt(Settings.F("UnderFireIgnoreMin")));
                    ignoreUntil = now + min * 60f; ignoredCount = 0; Close(); current = null;
                    UboatToolkit.ToolkitRunner.ShowNotice("Beschuss-Meldungen " + min + " min ignoriert", 3f);
                    return;
                }
            }
            string label = null;
            if (now < ignoreUntil)
                label = "⚠ Beschuss ignoriert noch " + Mathf.CeilToInt((ignoreUntil - now) / 60f) + " min, " + ignoredCount + " Meldungen gesammelt";
            if (label != null)
            {
                Imgui.BeginArea(new Rect(Screen.width - 330, Screen.height / 2f - 30, 320, 60));
                if (Imgui.Button(label, 315)) { ignoreUntil = 0f; ignoredCount = 0; if (Collected.Count > 0) current = Collected[Collected.Count - 1]; }
                Imgui.EndArea();
                return;
            }
            if (current == null) return;
            // Small box at the right edge (game keeps running): click or key.
            if (windowOpen) { Cursor.visible = true; Cursor.lockState = CursorLockMode.None; }
            var box = new Rect(Screen.width - 340, Screen.height / 2f - 125, 330, 250);
            Imgui.Box(box, "Herr Kaleun, was tun wir?");
            Imgui.BeginArea(new Rect(box.x + 6, box.y + 24, box.width - 12, box.height - 28));
            Imgui.Label(current.Clock + " " + current.Text + (current.Count > 1 ? " (x" + current.Count + ")" : ""), 315);
            for (int i = 0; i < ActName.Length; i++)
                if (Imgui.Button(ActName[i] + (i == (int)current.Advice ? " ★" : ""), 315))
                { var a = current; Close(); current = null; if (i != (int)Act.Nothing) Do((Act)i, a); Imgui.EndArea(); return; }
            if (Imgui.Button(Mathf.Max(1, Mathf.RoundToInt(Settings.F("UnderFireIgnoreMin"))) + " min ignorieren", 315))
            { ignoreUntil = now + Mathf.Max(1, Mathf.RoundToInt(Settings.F("UnderFireIgnoreMin"))) * 60f; ignoredCount = 0; Close(); current = null; }
            Imgui.EndArea();
        }
        private static string KeyText(string k) { var o = Settings.Get(k); return o == null || string.IsNullOrEmpty(o.Text) ? "-" : o.Text; }

        // F10 tab "Schaden": list of collected under-fire messages.
        public static void DrawList()
        {
            Imgui.Label("Beschuss-Meldungen (neueste oben):");
            if (Collected.Count == 0) { Imgui.Label("Keine."); return; }
            for (int i = Collected.Count - 1; i >= 0 && i >= Collected.Count - 30; i--)
            {
                var c = Collected[i];
                Imgui.Label(c.Clock + "  " + c.Text + " - Vorschlag: " + ActName[(int)c.Advice], 660);
            }
        }
    }

    // Lookouts: an enemy gun fires at our boat.
    [HarmonyPatch(typeof(Gun), "Fire")]
    internal static class UnderFire_GunPatch
    {
        private static void Postfix(Gun __instance, Entity target)
        {
            if (target == null || !(target is PlayerShip) || Util.IsPlayers(__instance)) return;
            var owner = __instance.GetComponentInParent<Entity>();
            if (owner != null) UnderFire.Report(owner, "Wir werden beschossen");
        }
    }
}

