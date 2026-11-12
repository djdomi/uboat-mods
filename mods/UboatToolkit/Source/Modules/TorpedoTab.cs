using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;

namespace UboatToolkit.Modules
{
    // Tab "Torpedos" in the F10 menu:
    // - pick which torpedo types ports stock (empty = game / mix)
    // - cheat: create a chosen torpedo directly in a free torpedo slot of the boat
    internal static class TorpedoTab
    {
        internal static readonly HashSet<string> PortPick = new HashSet<string>();
        private static bool loaded;
        private static List<string> names;
        private static float namesAt = -100f;
        private static int count = 1;
        private static string File => Path.Combine(Settings.Folder, "toolkit-torpedos.txt");

        internal static void Load()
        {
            if (loaded) return;
            loaded = true;
            try { if (System.IO.File.Exists(File)) foreach (var l in System.IO.File.ReadAllLines(File)) if (l.Trim().Length > 0) PortPick.Add(l.Trim()); } catch { }
        }

        private static void Save()
        {
            try { Directory.CreateDirectory(Settings.Folder); System.IO.File.WriteAllLines(File, PortPick.ToArray()); } catch { }
        }

        // All torpedo types the German side knows (from the reserves) plus the ones on board.
        private static List<string> Names()
        {
            if (names != null && Time.unscaledTime - namesAt < 10f) return names;
            namesAt = Time.unscaledTime;
            var set = new HashSet<string>();
            var sb = Object.FindObjectOfType<UBOAT.Game.Sandbox.Sandbox>();
            var de = sb != null ? sb.GetCountryByCode("DE", false) : null;
            if (de != null)
                foreach (var r in de.Reserves)
                    if (r?.ItemData != null && r.ItemData.HasTag("Torpedo")) set.Add(r.ItemData.Name);
            foreach (var t in Util.Find<StoredTorpedo>(5f))
                if (t != null && !(t is StoredMinePacket) && !string.IsNullOrEmpty(t.TypeName)) set.Add(t.TypeName);
            names = set.OrderBy(n => n).ToList();
            return names;
        }

        public static void Draw()
        {
            Load();
            if (Util.PlayerShip == null) { Imgui.Label("Nur im Spiel verfügbar."); return; }
            var list = Names();
            if (list.Count == 0) { Imgui.Label("Keine Torpedo-Typen gefunden."); DrawAmmo(); return; }

            Imgui.Label("Hafen-Nachschub: Haken = dieser Typ kommt ins Hafenlager. Nichts angehakt = normal (oder Mischung, wenn eingeschaltet).");
            Imgui.Label("Gilt für Lager, die sich neu auffüllen.");
            foreach (var n in list)
            {
                bool on = PortPick.Contains(n);
                bool now = Imgui.Toggle(on, " " + n);
                if (now != on) { if (now) PortPick.Add(n); else PortPick.Remove(n); Save(); }
            }
            if (PortPick.Count > 0 && Imgui.Button("Auswahl löschen", 200)) { PortPick.Clear(); Save(); }

            Imgui.Space(10);
            Imgui.Label("Cheat: Torpedo direkt ins Boot legen (in freie Torpedo-Plätze).");
            Imgui.BeginH();
            Imgui.Label("Anzahl: " + count, 100);
            if (Imgui.Button("-", 40) && count > 1) count--;
            if (Imgui.Button("+", 40) && count < 30) count++;
            Imgui.EndH();
            foreach (var n in list)
                if (Imgui.Button("Erzeugen: " + n, 600))
                {
                    int made = Create(n, count);
                    ToolkitRunner.ShowNotice(made > 0 ? made + "x " + n + " ins Boot gelegt" : "Kein freier Torpedo-Platz", 4f);
                }
            DrawAmmo();
        }

        private static Storage[] BoatStorages() =>
            Util.Find<Storage>(1f).Where(s => s != null && Util.IsPlayers(s) && s.GetComponentInParent<PlayableCharacter>() == null).ToArray();

        // Torpedo: one per free slot that the game allows for it.
        private static int Create(string name, int want)
        {
            int made = 0;
            Util.Safe("TorpedoTab.Create", () =>
            {
                var rm = Torpedoes.Resources;
                if (rm == null) return;
                StoredTorpedo t = null;
                foreach (var s in BoatStorages())
                    for (int i = 0; i < Util.Slots(s) && made < want; i++)
                    {
                        if ((s.GetItemAt(i) as Object) != null || s.HasTransportOrders(i)) continue;
                        if (t == null)
                        {
                            var go = rm.InstantiatePrefab("Equipment/" + name);
                            t = go != null ? go.GetComponent<StoredTorpedo>() : null;
                            if (t == null) { if (go != null) Object.Destroy(go); return; }
                        }
                        if (!s.IsItemAllowed(i, t)) continue;
                        s.SetItemAt(i, t);
                        t = null;
                        made++;
                    }
                if (t != null) Object.Destroy(t.gameObject);
            });
            return made;
        }

        // Ammunition: stacks into the boat storages like loading in port.
        private static float CreateAmmo(string name, float amount)
        {
            float stored = 0f;
            Util.Safe("TorpedoTab.Ammo", () =>
            {
                var rm = Torpedoes.Resources;
                var go = rm != null ? rm.InstantiatePrefab("Equipment/" + name) : null;
                var c = go != null ? go.GetComponent<Cargo>() : null;
                if (c == null) { if (go != null) Object.Destroy(go); return; }
                c.Count = amount;
                foreach (var s in BoatStorages())
                {
                    for (int k = 0; k < 20 && c != null; k++)
                    {
                        float before = c.Count;
                        if (s.StoreItem(c)) c = null;
                        else if (c.Count >= before - 0.0001f) break;
                    }
                    if (c == null) break;
                }
                if (c == null) { stored = amount; return; }
                if (c != null) { stored = amount - c.Count; Object.Destroy(c.gameObject); }
            });
            return stored;
        }

        private static List<string> ammoNames;
        private static float ammoAt = -100f;
        private static int ammoAmount = 100;
        private static List<string> AmmoNames()
        {
            if (ammoNames != null && Time.unscaledTime - ammoAt < 10f) return ammoNames;
            ammoAt = Time.unscaledTime;
            var set = new HashSet<string>();
            var sb = Object.FindObjectOfType<UBOAT.Game.Sandbox.Sandbox>();
            var de = sb != null ? sb.GetCountryByCode("DE", false) : null;
            if (de != null)
                foreach (var r in de.Reserves)
                    if (r?.ItemData?.Type != null && typeof(Ammunition).IsAssignableFrom(r.ItemData.Type)) set.Add(r.ItemData.Name);
            foreach (var a in Util.Find<Ammunition>(5f)) if (a != null && !string.IsNullOrEmpty(a.TypeName)) set.Add(a.TypeName);
            ammoNames = set.OrderBy(n => n).ToList();
            return ammoNames;
        }

        private static void DrawAmmo()
        {
            Imgui.Space(10);
            Imgui.Label("Cheat: Munition direkt ins Boot legen (stapelt sich ins Lager wie beim Beladen im Hafen).");
            var list = AmmoNames();
            if (list.Count == 0) { Imgui.Label("Keine Munitions-Typen gefunden."); return; }
            Imgui.BeginH();
            Imgui.Label("Menge: " + ammoAmount, 120);
            foreach (var n in new[] { 10, 50, 100, 500, 1000 })
                if (Imgui.Button(n.ToString(), 60)) ammoAmount = n;
            Imgui.EndH();
            foreach (var n in list)
                if (Imgui.Button("Erzeugen: " + n, 600))
                {
                    float got = CreateAmmo(n, ammoAmount);
                    ToolkitRunner.ShowNotice(got > 0.5f ? Mathf.RoundToInt(got) + "x " + n + " ins Lager gelegt" : "Kein Platz im Lager (oder zu schwer)", 4f);
                }
        }
    }
}
