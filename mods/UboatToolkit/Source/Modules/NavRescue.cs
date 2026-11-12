using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Characters.Navigation;
using UnityEngine;

namespace UboatToolkit.Modules
{
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
