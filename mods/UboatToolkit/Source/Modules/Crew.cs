using UnityEngine;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Characters.Navigation;
using UBOAT.Game.Scene.Interactions;
using UBOAT.Game.Scene.Items;

namespace UboatToolkit.Modules
{
    // QoL #21: crew walks through each other (same idea as shole's "No Crew Collisions"); doors are not touched.
    // QoL #14: sailors may man the navigation table.
    public class Crew : ModuleBase
    {
        private PlayerCrew crew;
        private readonly System.Collections.Generic.Dictionary<int, AgentMode> changed = new System.Collections.Generic.Dictionary<int, AgentMode>();

        public override void Tick()
        {
            if (crew == null) crew = Object.FindObjectOfType<PlayerCrew>();
            if (crew != null && crew.Characters != null)
            {
                bool through = Settings.B("NoCrewCollisions");
                foreach (var c in crew.Characters)
                {
                    if (c == null || c.Navigator == null) continue;
                    var nav = c.Navigator;
                    int id = c.GetInstanceID();
                    if (through && (nav.AgentMode == AgentMode.Obstacle || nav.AgentMode == AgentMode.CarvedObstacle))
                    {
                        changed[id] = nav.AgentMode;
                        nav.AgentMode = AgentMode.Disabled;
                    }
                    else if (!through && changed.TryGetValue(id, out var before))
                    {
                        if (nav.AgentMode == AgentMode.Disabled) nav.AgentMode = before;
                        changed.Remove(id);
                    }
                }
            }

            foreach (var table in Util.PlayerShip.GetComponentsInChildren<NavigationTable>(true))
            {
                var i = table.GetComponent<Interactable>();
                if (i == null) continue;
                bool orig = Util.Original<bool>(i, "disallowSailors");
                i.DisallowSailors = Settings.B("SailorsNavTable") ? false : orig;
            }
        }
    }
}
