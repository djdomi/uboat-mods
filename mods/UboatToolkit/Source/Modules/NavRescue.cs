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

        public override void Tick()
        {
            if (!Settings.B("FixOffNavmeshCrew") || !Util.ShipReady || Time.unscaledTime < next) return;
            next = Time.unscaledTime + 3f;
            if (crew == null) crew = Object.FindObjectOfType<PlayerCrew>();
            if (crew == null || crew.Characters == null) return;
            int fixedCount = 0;
            foreach (var c in crew.Characters)
            {
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
