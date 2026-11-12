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
            if (!WarthogSettings.Active(ship)) return;
            var rb = ship.RigidBody;
            if (rb == null || Time.deltaTime <= 0f || Time.timeScale <= 0f) return;
            // Frame() runs in Update: forces added here are summed until the next physics step, so scale them per frame.
            float k = Mathf.Clamp(Time.deltaTime / Time.fixedDeltaTime, 0f, 4f);

            // ---- fins
            if (Settings.B("WhFins"))
            {
                float speed = Vector3.Dot(rb.velocity, ship.transform.forward);
                // more turning: the game turns the boat with the rudder; add (factor-1) of a rudder-like torque
                float turn = ship.Turn; // -1..1
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
            if (cmd == 0f && Settings.B("WhThrusterWithRudder")) cmd = Mathf.Clamp(ship.Turn, -1f, 1f);

            // hold the heading while our guns fire (recoil)
            if (Settings.B("WhThrusterHoldFiring") && Time.time - LastGunShot < 3f && Mathf.Abs(ship.Turn) < 0.05f && cmd == 0f)
            {
                if (float.IsNaN(heldHeading)) heldHeading = ship.transform.eulerAngles.y;
                float err = Mathf.DeltaAngle(ship.transform.eulerAngles.y, heldHeading);
                float yawRate = ship.transform.InverseTransformDirection(rb.angularVelocity).y * Mathf.Rad2Deg;
                cmd = Mathf.Clamp(err * 0.15f - yawRate * 0.6f, -1f, 1f);
            }
            else heldHeading = float.NaN;

            if (Mathf.Abs(cmd) < 0.01f) { SetEnergy(ship, 0f); return; }
            // bow pushes one way, stern the other = pure turning torque (MNm -> Nm)
            float torque = cmd * Settings.F("WhThrusterPower") * 1e6f;
            rb.AddTorque(ship.transform.up * torque * k, ForceMode.Force);
            SetEnergy(ship, Mathf.Abs(cmd) * Settings.F("WhThrusterEnergy") * 0.0001f);
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
