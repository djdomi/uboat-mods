using UBOAT.Game.Scene.Entities;
using UnityEngine;

namespace UboatToolkit.Modules.Warthog
{
    // Vertical thrusters fore + aft, on EVERY boat (DoMe 2026-11-14), not tied to "only U-1337".
    //  - keys: whole boat up / down (both thrusters same direction)
    //  - auto: help the dive planes reach the ordered depth (push toward target depth, never against the planes)
    public class WarthogVThrusters : ModuleBase
    {
        private float arm = -1f; private object armShip;

        public override void Frame()
        {
            var ship = Util.PlayerShip;
            if (!Settings.B("WhVThrusters") || !WarthogSettings.ThrustersActive(ship) || ship.Docked) return;
            var rb = ship.RigidBody;
            if (rb == null || rb.isKinematic || Time.deltaTime <= 0f || Time.timeScale <= 0f) return;
            float k = Mathf.Clamp(Time.deltaTime / Time.fixedDeltaTime, 0f, 4f);

            float cmd = 0f;                                    // + = up
            if (Keys.Held("KeyWhVThrustUp")) cmd += 1f;
            if (Keys.Held("KeyWhVThrustDown")) cmd -= 1f;
            // 0.3.34: time compression = many physics steps per frame, the auto controllers overshoot -> keys only
            bool tc = Time.timeScale > 1.5f;
            if (cmd == 0f && !tc && Settings.B("WhVThrustAuto") && ship.SubmergedOrGoingToSubmerge)
            {
                float err = ship.TargetDepth - (ship.Depth + ship.VerticalOriginOffset);   // + = must go deeper
                cmd = -Mathf.Clamp(err / 10f, -1f, 1f);
                float vy = rb.velocity.y;                      // brake near target: no overshoot
                cmd -= Mathf.Clamp(vy * 0.3f, -0.5f, 0.5f) * (Mathf.Abs(err) < 15f ? 1f : 0f);
                cmd = Mathf.Clamp(cmd, -1f, 1f);
            }
            // DoMe: one thruster at the very bow, one at the very stern -> arm = half the hull length
            if (!ReferenceEquals(armShip, ship))
            {
                armShip = ship; arm = 35f;
                try
                {
                    Bounds b = new Bounds(ship.transform.position, Vector3.zero); bool any = false;
                    foreach (var c in ship.GetComponentsInChildren<Collider>(true))
                        if (c != null && !c.isTrigger) { if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds); }
                    if (any) arm = Mathf.Clamp(Vector3.Scale(b.size, new Vector3(Mathf.Abs(ship.transform.forward.x), Mathf.Abs(ship.transform.forward.y), Mathf.Abs(ship.transform.forward.z))).magnitude * 0.5f, 20f, 70f);
                }
                catch { }
                Util.Log("Vertikal-Strahlruder: Abstand Bug/Heck je " + arm.ToString("F0") + " m");
            }
            // level the bow (keeps the nose down at high speed / holds the boat flat): bow and stern push opposite
            float pitchCmd = 0f;
            if (!tc && Settings.B("WhVThrustLevel") && cmd == 0f && ship.TargetDepth <= 5f)
            {
                float pitch = Vector3.SignedAngle(Vector3.ProjectOnPlane(ship.transform.forward, Vector3.up), ship.transform.forward, ship.transform.right); // + = bow down
                pitchCmd = Mathf.Clamp(pitch / 5f, -1f, 1f);   // bow up (negative) -> push bow down
            }
            if (Mathf.Abs(cmd) < 0.02f && Mathf.Abs(pitchCmd) < 0.02f) return;
            // strength in % of the boat's weight (100 = can lift the whole boat)
            float f = rb.mass * 9.81f * Settings.F("WhVThrustPower") / 100f * cmd * 0.5f;
            float p = rb.mass * 9.81f * Settings.F("WhVThrustPower") / 100f * pitchCmd * 0.5f;
            var t = ship.transform; var com = rb.worldCenterOfMass;
            rb.AddForceAtPosition(Vector3.up * (f + p) * k, com + t.forward * arm, ForceMode.Force);   // bow
            rb.AddForceAtPosition(Vector3.up * (f - p) * k, com - t.forward * arm, ForceMode.Force);   // stern
        }
    }
}
