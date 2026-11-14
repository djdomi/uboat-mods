using System.Linq;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Scene.Entities;
using UnityEngine;

namespace UboatToolkit.Modules.Warthog
{
    // Hull + propulsion of U-1337:
    //  - rubber coating: enemy active sonar and hydrophones find us harder (slider)
    //  - fins: extra turning force with the rudder, extra roll damping, stronger diving planes
    //  - bow + stern thrusters: turn on the spot (keys) or help the rudder; use battery;
    //    keep the heading while the deck guns fire (counter the recoil)
    // (Astern speed = ahead speed is built into the boat itself: 4 propellers, see the Unity build.)
    public class WarthogHull : ModuleBase
    {
        const string Mod = "Warthog";
        private Modifier thrusterEnergy;
        private float heldHeading = float.NaN;
        internal static float LastGunShot = -99f;   // set by WarthogWeapons when one of our guns fires

        public override void Tick()
        {
            var ship = Util.PlayerShip;
            bool on = WarthogSettings.Active(ship);
            Scale(ship.SonarDetectability, on ? Settings.F("WhRubberCoating") : 1f, "Warthog Coating");
            Scale(ship.HydrophoneDetectability, on ? Settings.F("WhHydroNoise") : 1f, "Warthog Coating");
            Scale(ship.VerticalSteersPerformance, on && Settings.B("WhFins") ? Settings.F("WhFinDepth") : 1f, "Warthog Fins");
            if (on && WarthogSettings.IsWarthog(ship)) MirrorBadge(ship);
            if (WarthogSettings.IsWarthog(ship)) SpeedFactor(ship, on ? Settings.F("WhSpeedFactor") : 1f);
            if (WarthogSettings.IsWarthog(ship)) DiveSpeed(ship, on ? Settings.F("WhDiveSpeed") : 0f);
            if (on && WarthogSettings.IsWarthog(ship)) CrushLimit(ship, Settings.F("WhCrushDepth"));
            // berth position (used by the game at the next docking): forward shift + side shift away from the pier
            if (on && WarthogSettings.IsWarthog(ship))
            {
                var want = new Vector3(0.1f + Settings.F("WhDockSide"), 0.6f, Settings.F("WhDockShift"));
                var cur = Util.SafeGet(() => HarmonyLib.Traverse.Create(ship).Field("dockOffset").GetValue<Vector3>(), want);
                if ((cur - want).sqrMagnitude > 0.0001f) { HarmonyLib.Traverse.Create(ship).Field("dockOffset").SetValue(want); Util.Log("[Warthog] dock: berth offset " + want); }
            }
        }

        // speed factor (0.3.6, DoMe: optional, default 1.0): the game's propellers push with power * powerMultiplier *
        // maxPower; top speed grows about with the square root of the thrust -> maxPower x factor^2 = speed x factor.
        // Propellers are cached per boat (no component search every tick); plain reflection, no generic field refs.
        private static readonly System.Reflection.FieldInfo MaxPowerF = HarmonyLib.AccessTools.Field(typeof(UBOAT.Game.Scene.Items.Propeller), "maxPower");
        private readonly System.Collections.Generic.Dictionary<UBOAT.Game.Scene.Items.Propeller, float> basePower = new System.Collections.Generic.Dictionary<UBOAT.Game.Scene.Items.Propeller, float>();
        private UBOAT.Game.Scene.Items.Propeller[] speedProps;
        private object speedShip;
        private float lastFactor = -1f;
        private void SpeedFactor(PlayerShip ship, float f)
        {
            if (MaxPowerF == null) return;
            f = Mathf.Clamp(f, 0.5f, 2f);
            if (speedProps == null || !ReferenceEquals(speedShip, ship)) { speedProps = ship.GetComponentsInChildren<UBOAT.Game.Scene.Items.Propeller>(true); speedShip = ship; basePower.Clear(); lastFactor = -1f; }
            int n = 0;
            foreach (var p in speedProps)
            {
                if (p == null) continue;
                if (!basePower.TryGetValue(p, out var b)) { b = (float)MaxPowerF.GetValue(p); basePower[p] = b; }
                float want = b * f * f;
                if ((float)MaxPowerF.GetValue(p) != want) { MaxPowerF.SetValue(p, want); n++; }
            }
            if (n > 0 && f != lastFactor) Util.Log("[Warthog] speed factor " + f.ToString("0.00") + " -> " + n + " propeller(s), thrust x" + (f * f).ToString("0.00"));
            lastFactor = f;
        }

        // 0.3.9 dive / rise speed (DoMe: "far too slow, the curve flattens at ~97 m"). Game (PlayableSubmarine): with the
        // ballast flooded the boat is about neutral and the DIVING PLANES make the depth change, with a FIXED force:
        //   F = rudder * (3.6 + |v| * 1.15) * balance * depthFalloff * 10 * VerticalSteersForceScale
        //   depthFalloff = 0.2 + 0.8 * exp(-(deckDepth - 12) * 0.02)  -> 1.0 at 12 m, 0.35 at 97 m, 0.2 below ~400 m
        // Not scaled with mass: our 1826 t boat (IXC 866 t) gets half the acceleration, and below 100 m a third or less.
        // Here: force x (mass ratio) x (1 / depthFalloff) x slider. Near the ordered depth (under 30 m away) the extra
        // slider part fades out, so the depth keeping does not overshoot. Force only (VerticalSteersForceScale) - the pitch
        // torque stays the game's, the boat keeps its normal dive angle.
        private float lastDive = -1f;
        private void DiveSpeed(PlayerShip ship, float slider)
        {
            var p = Util.SafeGet(() => ship.VerticalSteersForceScale, null);
            if (p == null) return;
            float f = 1f;
            if (slider > 0f && ship.RigidBody != null)
            {
                float massK = Mathf.Clamp(ship.RigidBody.mass / 866000f, 1f, 4f);
                float deck = Util.SafeGet(() => ship.DeckDepth, 0f);
                float falloff = 0.2f + 0.8f * Mathf.Exp(Mathf.Min(0f, (deck - 12f) * -0.02f));
                float err = Util.SafeGet(() => Mathf.Abs(ship.TargetDepth - ship.Depth), 100f);
                float near = Mathf.Clamp01((err - 8f) / 22f);   // 0 within 8 m of the ordered depth, 1 beyond 30 m
                float s = Mathf.Lerp(1f, Mathf.Clamp(slider, 0.25f, 10f), near);
                f = Mathf.Clamp(massK / falloff * s, 0.25f, 60f);
            }
            Scale(p, f, "Warthog Dive");
            if (Mathf.Abs(f - lastDive) > 0.5f * Mathf.Max(1f, lastDive * 0.25f)) { Util.Log("[Warthog] dive: diving-plane force x" + f.ToString("0.0") + " (slider " + slider.ToString("0.00") + ", deck depth " + Util.SafeGet(() => ship.DeckDepth, 0f).ToString("0") + " m)"); lastDive = f; }
        }

        // 0.3.9 crush depth (game: HullCrushController implodes the boat after a timeout once it is below the entity
        // parameter "CrushDepth"; the sheet value of Type XXX is -3000). Slider WhCrushDepth scales it, and the ORDERED
        // depth is clamped to 100 m above it, so the player can never order a depth that kills the boat silently.
        private static readonly System.Reflection.FieldInfo TargetDepthF = HarmonyLib.AccessTools.Field(typeof(PlayableSubmarine), "targetDepth");
        private float sheetCrush = float.NaN; private float lastClampLog = -99f;
        private void CrushLimit(PlayerShip ship, float wantMeters)
        {
            var p = Util.SafeGet(() => ship.SandboxEntity.Parameters.GetParameter("CrushDepth"), null);
            if (p == null) return;
            if (float.IsNaN(sheetCrush))
            {
                float mod = Util.SafeGet(() => p.GetScaleModifier("Warthog Crush Depth", true).Value, 1f);
                float raw = Mathf.Abs(Util.SafeGet(() => (float)p, 0f)) / Mathf.Max(0.0001f, mod);
                sheetCrush = raw > 1f ? raw : 3000f;
            }
            Scale(p, Mathf.Max(100f, wantMeters) / sheetCrush, "Warthog Crush Depth");
            float crush = Mathf.Abs(Util.SafeGet(() => (float)p, -wantMeters));
            float limit = crush - 100f;
            float ordered = Util.SafeGet(() => ship.TargetDepth, 0f);
            if (TargetDepthF != null && ordered > limit)
            {
                float td = (float)TargetDepthF.GetValue(ship);
                TargetDepthF.SetValue(ship, td + (ordered - limit));   // TargetDepth = -targetDepth + offset
                if (Time.unscaledTime - lastClampLog > 10f) { lastClampLog = Time.unscaledTime; Util.Log("[Warthog] depth: ordered " + ordered.ToString("0") + " m is below the crush depth limit, set to " + limit.ToString("0") + " m (crush " + crush.ToString("0") + " m, F10 > Warthog > Zerstörungstiefe)"); }
            }
        }

        static void Scale(Parameter p, float f, string name)
        {
            if (p == null) return;
            var m = p.GetScaleModifier(name, true);
            float v = Mathf.Max(0.0001f, f);      // 0 would make the boat "undefined"; tiny = practically invisible
            if (m.Value != v) m.Value = v;
        }

        public override void Frame()
        {
            var ship = Util.PlayerShip;
            bool keys = Keys.Held("KeyWhThrustLeft") || Keys.Held("KeyWhThrustRight");
            if (!WarthogSettings.ThrustersActive(ship)) return;
            var rb = ship.RigidBody;
            if (keys && !Settings.B("WhThrusters")) Why("Strahlruder sind ausgeschaltet (F10 > Warthog)");
            else if (keys && ship.Docked) Why("Strahlruder: im Hafen festgemacht, erst ablegen");
            else if (keys && rb != null && rb.isKinematic) Why("Strahlruder: Boot wird gerade vom Spiel gehalten (Schnellreise/Andocken)");
            if (rb == null || Time.deltaTime <= 0f || Time.timeScale <= 0f) return;
            // Frame() runs in Update: forces added here are summed until the next physics step, so scale them per frame.
            float k = Mathf.Clamp(Time.deltaTime / Time.fixedDeltaTime, 0f, 4f);

            SpinProps(ship, rb);

            // ---- fins
            if (Settings.B("WhFins"))
            {
                float speed = Vector3.Dot(rb.velocity, ship.transform.forward);
                // more turning: the game turns the boat with the rudder; add (factor-1) of a rudder-like torque
                // game convention (Ship.FixedUpdate): Turn < 0 = turn to STARBOARD (positive yaw). 0.2.x used +Turn and
                // pushed against the rudder -> "unsteerable". Yaw torque must be -Turn.
                float turn = -ship.Turn; // -1..1, + = starboard
                float extra = (Settings.F("WhFinTurn") - 1f) * turn * Mathf.Clamp(Mathf.Abs(speed) / 6f, 0f, 1.5f) * Mathf.Sign(speed == 0 ? 1 : speed);
                if (Mathf.Abs(extra) > 0.001f) rb.AddTorque(ship.transform.up * extra * rb.inertiaTensor.y * 0.002f * k, ForceMode.Force);
                // less roll: damp the roll part of the angular velocity
                Vector3 local = ship.transform.InverseTransformDirection(rb.angularVelocity);
                float damp = (Settings.F("WhFinRoll") - 1f) * 0.6f;
                if (damp > 0f) rb.AddTorque(ship.transform.forward * (-local.z * damp) * rb.inertiaTensor.z * k, ForceMode.Force);
            }

            // ---- thrusters
            if (!Settings.B("WhThrusters")) { heldHeading = float.NaN; WarthogThrusterGauge.Cmd = 0f; WarthogThrusterGauge.Bow = WarthogThrusterGauge.Stern = 0f; return; }
            float cmd = 0f;
            if (Keys.Held("KeyWhThrustLeft")) cmd -= 1f;
            if (Keys.Held("KeyWhThrustRight")) cmd += 1f;
            // 0.3.2: Shift + Z/X = whole boat sideways (both thrusters the same way), for docking
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (cmd != 0f && shift && Settings.B("WhThrustShiftSide"))
            {
                heldHeading = float.NaN;
                Push(ship, rb, cmd, cmd, k, "Seitwärts", "Taste");
                return;
            }
            // F10: each thruster on its own (only while no key is held)
            float mb = Settings.F("WhThrustBowManual") / 100f, ms = Settings.F("WhThrustSternManual") / 100f;
            if (cmd == 0f && (Mathf.Abs(mb) > 0.01f || Mathf.Abs(ms) > 0.01f))
            {
                heldHeading = float.NaN;
                Push(ship, rb, mb, ms, k, "Einzeln", "F10");
                return;
            }
            // same sign fix as the fins; astern the game mirrors Turn (rudder works reversed), thrusters do not -> undo it
            bool tc = Time.timeScale > 1.5f;   // 0.3.35: no automatic thruster use in time compression (it swung around)
            if (cmd == 0f && !tc && Settings.B("WhThrusterWithRudder"))
            {
                float fwd = Vector3.Dot(rb.velocity, ship.transform.forward);
                cmd = Mathf.Clamp(-ship.Turn * (fwd < -0.2f ? -1f : 1f), -1f, 1f);
            }

            // hold the heading while our guns fire (recoil)
            if (!tc && Settings.B("WhThrusterHoldFiring") && Time.time - LastGunShot < 3f && Mathf.Abs(ship.Turn) < 0.05f && Mathf.Abs(cmd) < 0.05f)
            {
                if (float.IsNaN(heldHeading)) heldHeading = ship.transform.eulerAngles.y;
                float err = Mathf.DeltaAngle(ship.transform.eulerAngles.y, heldHeading);
                float yawRate = ship.transform.InverseTransformDirection(rb.angularVelocity).y * Mathf.Rad2Deg;
                cmd = Mathf.Clamp(err * 0.15f - yawRate * 0.6f, -1f, 1f);
            }
            else heldHeading = float.NaN;

            string src = Mathf.Abs(cmd) < 0.01f ? "" : (Keys.Held("KeyWhThrustLeft") || Keys.Held("KeyWhThrustRight")) ? "Taste" : !float.IsNaN(heldHeading) ? "Kurs halten" : "Ruder";
            // turning on the spot: bow pushes one way, stern the other
            Push(ship, rb, cmd, -cmd, k, "Drehen", src);
        }

        // bow / stern thruster command -1..1 each (+ = push to starboard). Equal = sideways, opposite = turning.
        // Force per thruster F = Iy * power * 0.00175 / L (L = 2 x 40 m arm): with bow = -stern that gives the same turning
        // torque as before (power 6 = about 0.6 deg/s^2); with bow = stern the boat moves sideways.
        const float Arm = 40f;
        private void Push(PlayerShip ship, Rigidbody rb, float bow, float stern, float k, string mode, string src)
        {
            bow = Mathf.Clamp(bow, -1f, 1f); stern = Mathf.Clamp(stern, -1f, 1f);
            WarthogThrusterGauge.Bow = bow; WarthogThrusterGauge.Stern = stern; WarthogThrusterGauge.Mode = mode;
            WarthogThrusterGauge.Cmd = (bow - stern) * 0.5f;
            WarthogThrusterGauge.Source = src;
            if (Mathf.Abs(bow) < 0.01f && Mathf.Abs(stern) < 0.01f) { SetEnergy(ship, 0f); return; }
            float f = rb.inertiaTensor.y * Settings.F("WhThrusterPower") * 0.00175f / (2f * Arm);
            var t = ship.transform;
            Vector3 com = rb.worldCenterOfMass;
            // forces at the thruster tunnels (same height as the centre of mass: no extra roll)
            rb.AddForceAtPosition(t.right * (f * bow * k), com + t.forward * Arm, ForceMode.Force);
            rb.AddForceAtPosition(t.right * (f * stern * k), com - t.forward * Arm, ForceMode.Force);
            SetEnergy(ship, (Mathf.Abs(bow) + Mathf.Abs(stern)) * 0.5f * Settings.F("WhThrusterEnergy") * 0.0001f);
        }

        // Our 4 visible propellers (WH_Prop_* hubs + WH_Blade_* blades in the hull model) are static meshes; the game's
        // working propellers are the IXC ones hidden inside the hull. Spin ours with the boat's speed through the water,
        // counter-rotating like real twin screws: starboard clockwise, port anticlockwise (seen from astern, going ahead).
        private readonly System.Collections.Generic.List<(Transform hub, float sense)> props = new System.Collections.Generic.List<(Transform, float)>();
        private object propsFor;
        private void SpinProps(PlayerShip ship, Rigidbody rb)
        {
            if (propsFor != (object)ship)
            {
                propsFor = ship; props.Clear();
                var all = ship.GetComponentsInChildren<Transform>(true);
                foreach (var hub in all.Where(t => t.name.StartsWith("WH_Prop_")))
                {
                    string key = hub.name.Substring("WH_Prop_".Length);          // e.g. "Aft_S"
                    foreach (var b in all.Where(t => t.name.StartsWith("WH_Blade_" + key)).ToList()) b.SetParent(hub, true);
                    props.Add((hub, key.EndsWith("_S") ? 1f : -1f));
                }
                Util.Log("[Warthog] propellers: " + props.Count + " visible propellers will turn");
            }
            if (props.Count == 0) return;
            float speed = Vector3.Dot(rb.velocity, ship.transform.forward);   // m/s, negative = astern
            float rpm = Mathf.Clamp(speed * 25f, -260f, 260f);                // ~250 rpm at full speed, like the type IX
            float deg = rpm * 6f * Time.deltaTime;                             // rpm -> degrees per frame
            foreach (var p in props) if (p.hub != null) p.hub.Rotate(ship.transform.forward, deg * p.sense, Space.World);
        }

        private float nextWhy;
        // When Z/X do nothing, say why (once every 5 s) instead of silently ignoring the key.
        private void Why(string text)
        {
            if (Time.unscaledTime < nextWhy) return;
            nextWhy = Time.unscaledTime + 5f;
            ToolkitRunner.ShowNotice(text, 3f);
        }

        // emblem on BOTH tower drums: the game shows the chosen badge at the "Badge" slot (first drum); we keep a
        // render-only mirror copy on the second drum (mirrored at the boat's middle, turned 180 degrees)
        private GameObject badgeCopy; private int badgeOf;
        private void MirrorBadge(PlayerShip ship)
        {
            UBOAT.Game.Scene.Entities.ShipEquipmentSlot slot = null;
            foreach (var s in ship.GetComponentsInChildren<UBOAT.Game.Scene.Entities.ShipEquipmentSlot>(true)) if (s.name == "Badge") { slot = s; break; }
            // the badge the game placed in the slot (its Item), not just the slot's last child (that can be e.g. "Banner Attachment Point")
            var item = slot != null && slot.Item != null ? slot.Item.gameObject : null;
            int id = item != null ? item.GetInstanceID() : 0;
            if (id == badgeOf && (badgeCopy != null) == (item != null)) return;
            if (badgeCopy != null) Object.Destroy(badgeCopy);
            badgeCopy = null; badgeOf = id;
            if (item == null) return;
            badgeCopy = new GameObject("Warthog Badge (second drum)");
            badgeCopy.transform.SetParent(ship.transform, false);
            foreach (var r in item.GetComponentsInChildren<Renderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                var go = new GameObject(r.name); go.layer = r.gameObject.layer;
                go.transform.SetParent(badgeCopy.transform, false);
                Vector3 lp = ship.transform.InverseTransformPoint(r.transform.position);
                go.transform.localPosition = new Vector3(lp.x, lp.y, -lp.z);
                go.transform.localRotation = Quaternion.Euler(0, 180f, 0) * (Quaternion.Inverse(ship.transform.rotation) * r.transform.rotation);
                go.transform.localScale = r.transform.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = r.sharedMaterials;
            }
            Util.Log("[Warthog] badge: '" + item.name + "' mirrored onto the second tower drum");
        }

        private void SetEnergy(PlayerShip ship, float use)
        {
            var energy = ship.Energy;
            if (energy == null) return;
            if (thrusterEnergy == null) thrusterEnergy = energy.Gain.AddDeltaModifier("Warthog Thrusters");
            use = WarthogPower.Apply("WarthogThrusters", use);
            if (thrusterEnergy.Value != -use) thrusterEnergy.Value = -use;
        }
    }
}
