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
            if (!Settings.B("WhThrusters")) { heldHeading = float.NaN; return; }
            float cmd = 0f;
            if (Keys.Held("KeyWhThrustLeft")) cmd -= 1f;
            if (Keys.Held("KeyWhThrustRight")) cmd += 1f;
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

            if (Mathf.Abs(cmd) < 0.01f) { SetEnergy(ship, 0f); return; }
            // bow pushes one way, stern the other = pure turning torque. 0.2.x/0.3.0 used a fixed 6 MNm: against the
            // 92 m boat's yaw inertia (~1e9 kg m^2) and the game's angular drag that was hardly noticeable ("Z/X do nothing").
            // Now scaled with the boat's inertia: power 6 = about 0.6 deg/s^2 turning acceleration.
            float torque = cmd * rb.inertiaTensor.y * Settings.F("WhThrusterPower") * 0.00175f;
            rb.AddTorque(ship.transform.up * torque * k, ForceMode.Force);
            SetEnergy(ship, Mathf.Abs(cmd) * Settings.F("WhThrusterEnergy") * 0.0001f);
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
            var item = slot != null && slot.transform.childCount > 0 ? slot.transform.GetChild(slot.transform.childCount - 1).gameObject : null;
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
