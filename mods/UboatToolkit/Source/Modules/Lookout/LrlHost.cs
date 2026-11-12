// Ausguck (Teil des UBOAT Toolkit): reagiert, wenn der Hauptschalter "Ausguck" im F10-Menü umgelegt wird
// (wie im Original sofort, ohne Neustart): beim Ausschalten wird eine gesetzte Zeitraffer-Bremse gelöst
// und die wartenden Meldungen werden verworfen.
namespace UboatToolkit.Modules.Lrl
{
    public class LrlHost : ModuleBase
    {
        private bool? last;

        public override void Tick()
        {
            bool on = Settings.B("Lookout");
            if (last.HasValue && last.Value != on) LrlInstall.SetEnabled(on);
            last = on;
        }
    }
}
