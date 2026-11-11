using System.Linq;
using HarmonyLib;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Scene.Entities;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // QoL #1: time compression stops when a new ship or aircraft contact appears.
    // QoL #6: autosave every X minutes (uses the game's own autosave slots).
    // Cheat #11: backup save right before a cheat is switched on.
    // QoL #8: warnings before diesel, air or battery run low.
    public class Watch : ModuleBase
    {
        private DynamicContactsController contacts;
        private float nextAutosave = -1f, lastBackup = -9999f;
        private int lastCheatCount = -1;
        private readonly System.Collections.Generic.Dictionary<string, bool> warned = new System.Collections.Generic.Dictionary<string, bool>();

        public override void Tick()
        {
            HookContacts();
            Autosave();
            Backup();
            Warnings();
        }

        private void HookContacts()
        {
            if (contacts != null) return;
            contacts = Traverse.Create(typeof(UBOAT.Game.UI.GameInputModule)).Field("dynamicContactsController").GetValue() as DynamicContactsController;
            if (contacts != null) contacts.ContactAdded += OnContact;
        }

        private static void OnContact(ContactEvent e)
        {
            try
            {
                var c = e.Contact;
                if (c == null) return;
                Events.Diary((c.IsAirborne ? "Flugzeug" : "Kontakt") + ": " + c.Name);
                if (Traffic.Ignore(c)) return;
                bool air = c.IsAirborne;
                if (air ? !Settings.B("StopTimeOnAircraft") : !Settings.B("StopTimeOnShip")) return;
                Game.StopTimeCompression((air ? "Flugzeug" : "Kontakt") + " gesichtet: " + c.Name);
            }
            catch (System.Exception ex) { Util.Log("Kontakt-Stopp: " + ex.Message); }
        }

        private void Autosave()
        {
            float min = Settings.F("AutoSaveMinutes");
            if (min <= 0f) { nextAutosave = -1f; return; }
            if (nextAutosave < 0f) { nextAutosave = Time.unscaledTime + min * 60f; return; }
            if (Time.unscaledTime < nextAutosave || !Game.CanSave()) return;
            nextAutosave = Time.unscaledTime + min * 60f;
            var saver = Game.Tasks?.GetRunningTask<AutoSaver>();
            if (saver == null) return;
            saver.DoAutoSave();
            Util.Log("Auto-Speichern");
        }

        private void Backup()
        {
            int n = Settings.All.Count(o => o.Tab == Settings.Cheat && o.Key != "CheatShowActive" && (o.IsBool ? o.On : o.Value != o.Default));
            if (lastCheatCount >= 0 && n > lastCheatCount && Settings.B("BackupBeforeCheats")
                && Time.unscaledTime - lastBackup > 600f && Game.CanSave())
            {
                lastBackup = Time.unscaledTime;
                Game.Saves.Save("UboatToolkit_Backup");
                ToolkitRunner.ShowNotice("Backup gespeichert (UboatToolkit_Backup), Cheat ist an.");
            }
            lastCheatCount = n;
        }

        private void Warnings()
        {
            float lim = Settings.F("LowWarnPercent") / 100f;
            if (lim <= 0f) return;
            var ship = Util.PlayerShip;
            Warn(ship, "Fuel", "Diesel", lim);
            Warn(ship, "Energy", "Batterie", lim);
            if (ship.SubmergedOrGoingToSubmerge) Warn(ship, "Oxygen", "Luft", lim); else warned["Oxygen"] = false;
        }

        private void Warn(PlayerShip ship, string res, string label, float lim)
        {
            var r = ship.GetResource(res);
            if (r == null) return;
            float v = (float)r.Amount;
            warned.TryGetValue(res, out bool w);
            if (v < lim && !w)
            {
                warned[res] = true;
                Game.StopTimeCompression("Warnung: " + label + " nur noch " + (v * 100f).ToString("0") + " %");
                ToolkitRunner.ShowNotice("Warnung: " + label + " nur noch " + (v * 100f).ToString("0") + " %", 10f);
            }
            else if (v > lim + 0.05f) warned[res] = false;
        }
    }
}
