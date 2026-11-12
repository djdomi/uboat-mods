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
            // berth position (used by the game at the next docking): forward shift + side shift away from the pier
            if (on && WarthogSettings.IsWarthog(ship))
            {
                var want = new Vector3(0.1f + Settings.F("WhDockSide"), 0.6f, Settings.F("WhDockShift"));
                var cur = Util.SafeGet(() => HarmonyLib.Traverse.Create(ship).Field("dockOffset").GetValue<Vector3>(), want);
                if ((cur - want).sqrMagnitude > 0.0001f) { HarmonyLib.Traverse.Create(ship).Field("dockOffset").SetValue(want); Util.Log("[Warthog] dock: berth offset " + want); }
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
            if (!WarthogSettings.Active(ship)) { if (keys) Why("Strahlruder gibt es nur auf dem Warthog (U-1337)"); return; }
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
            if (cmd == 0f && Settings.B("WhThrusterWithRudder"))
            {
                float fwd = Vector3.Dot(rb.velocity, ship.transform.forward);
                cmd = Mathf.Clamp(-ship.Turn * (fwd < -0.2f ? -1f : 1f), -1f, 1f);
            }

            // hold the heading while our guns fire (recoil)
            if (Settings.B("WhThrusterHoldFiring") && Time.time - LastGunShot < 3f && Mathf.Abs(ship.Turn) < 0.05f && Mathf.Abs(cmd) < 0.05f)
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
