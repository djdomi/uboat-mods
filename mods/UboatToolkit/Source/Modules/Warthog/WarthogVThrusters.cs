using UBOAT.Game.Scene.Entities;
using UnityEngine;

namespace UboatToolkit.Modules.Warthog
{
    // Vertical thrusters fore + aft, on EVERY boat (DoMe 2026-11-14), not tied to "only U-1337".
    //  - keys: whole boat up / down (both thrusters same direction)
    //  - auto: help the dive planes reach the ordered depth (push toward target depth, never against the planes)
    public class WarthogVThrusters : ModuleBase
    {
        const float Arm = 30f;

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
            if (cmd == 0f && Settings.B("WhVThrustAuto") && ship.SubmergedOrGoingToSubmerge)
            {
                float err = ship.TargetDepth - (ship.Depth + ship.VerticalOriginOffset);   // + = must go deeper
                cmd = -Mathf.Clamp(err / 10f, -1f, 1f);
                float vy = rb.velocity.y;                      // brake near target: no overshoot
                cmd -= Mathf.Clamp(vy * 0.3f, -0.5f, 0.5f) * (Mathf.Abs(err) < 15f ? 1f : 0f);
                cmd = Mathf.Clamp(cmd, -1f, 1f);
            }
            if (Mathf.Abs(cmd) < 0.02f) return;
            // strength in % of the boat's weight (100 = can lift the whole boat)
            float f = rb.mass * 9.81f * Settings.F("WhVThrustPower") / 100f * cmd * 0.5f;
            var t = ship.transform; var com = rb.worldCenterOfMass;
            rb.AddForceAtPosition(Vector3.up * f * k, com + t.forward * Arm, ForceMode.Force);
            rb.AddForceAtPosition(Vector3.up * f * k, com - t.forward * Arm, ForceMode.Force);
        }
    }
}
