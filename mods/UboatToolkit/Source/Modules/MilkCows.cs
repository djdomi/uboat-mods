using System.Collections.Generic;
using UBOAT.Game.Sandbox.HQ;

namespace UboatToolkit.Modules
{
    // QoL #25: supply U-boats (Type XIV "milk cows") are at sea from the start of the campaign,
    // without first ordering them at the flotilla commander in port.
    // The game defines them as headquarters tasks "Spawn Milk Cow", "Spawn Milk Cow II", ...
    // The mod starts each such task once if it has not spawned its supply boat yet.
    public class MilkCows : ModuleBase
    {
        private static readonly string[] Names =
        {
            "Spawn Milk Cow", "Spawn Milk Cow II", "Spawn Milk Cow III", "Spawn Milk Cow IV", "Spawn Milk Cow V",
            "Spawn Milk Cow VI", "Spawn Milk Cow VII", "Spawn Milk Cow VIII", "Spawn Milk Cow IX", "Spawn Milk Cow X",
        };

        private readonly HashSet<AddSupplyShip> started = new HashSet<AddSupplyShip>();

        public override void Tick()
        {
            if (!Settings.B("MilkCowsAtStart")) return;
            var entity = Util.PlayerShip.SandboxEntity;
            var hq = entity?.Country?.Headquarters;
            if (hq == null) return;

            foreach (var name in Names)
            {
                if (!(hq.GetTask(name) is AddSupplyShip task)) continue;
                if (task.SupplyShip != null || started.Contains(task)) continue;
                started.Add(task);
                task.Start();
                Util.Log("started supply U-boat task: " + name);
            }
        }
    }
}
