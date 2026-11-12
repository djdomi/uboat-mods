using System.Linq;
using UBOAT.Game.UI;
using UnityEngine;

namespace UboatToolkit.Modules.Warthog
{
    // Extra controls at the top of the "Warthog" tab.
    // Hook in ToolkitRunner (next to the other tab draws):  if (tab == WarthogSettings.Tab) UboatToolkit.Modules.Warthog.WarthogTab.Draw();
    public static class WarthogTab
    {
        private static string message;
        private static float messageUntil;

        public static void Draw()
        {
            Imgui.BeginH();
            if (Imgui.Button("Warthog übernehmen", 220)) TakeOver();
            if (message != null && Time.unscaledTime < messageUntil) Imgui.Label("  " + message);
            Imgui.EndH();
            Imgui.Label("Im Spiel selbst: im Hafen den Depot-Arbeiter ansprechen → \"Kommando über anderes U-Boot übernehmen\".");
            Imgui.Space(6);
        }

        // In port: unlock "Type XXX" for this career and open the game's own ship selector.
        private static void TakeOver()
        {
            var ship = Util.PlayerShip;
            if (ship == null || !ship.Docked) { Say("Nur im Hafen möglich"); return; }
            var career = Util.Career;
            if (career != null && !career.UnlockedShips.Contains("Type XXX")) career.UnlockShip("Type XXX", true);
            var ui = Resources.FindObjectsOfTypeAll<ShipSelectorUI>().FirstOrDefault(x => x != null && x.gameObject.scene.IsValid());
            if (ui == null) { Say("Bootsauswahl nicht gefunden"); return; }
            _ = ui.Open();   // the selector lists Type XXX (WarthogMod patch), purchase = free change
            Say("Bootsauswahl geöffnet");
        }

        private static void Say(string text) { message = text; messageUntil = Time.unscaledTime + 5f; ToolkitRunner.ShowNotice("Warthog: " + text, 4f); }
    }
}
