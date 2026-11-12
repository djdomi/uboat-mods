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
            if (list.Count == 0) { Imgui.Label("Keine Torpedo-Typen gefunden."); return; }

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
        }

        private static int Create(string name, int want)
        {
            int made = 0;
            Util.Safe("TorpedoTab.Create", () =>
            {
                var rm = Torpedoes.Resources;
                if (rm == null) return;
                var storages = Util.Find<Storage>(1f).Where(s => s != null && Util.IsPlayers(s) && s.GetComponentInParent<PlayableCharacter>() == null).ToArray();
                foreach (var s in storages)
                {
                    var slots = Traverse.Create(s).Field("slots").GetValue<ShipEquipmentSlot[]>();
                    if (slots == null) continue;
                    for (int i = 0; i < slots.Length && made < want; i++)
                    {
                        var slot = slots[i];
                        if (slot == null || slot.Item != null || s.HasTransportOrders(i)) continue;
                        if (slot.SlotType == null || !slot.SlotType.Contains("Torpedo")) continue;
                        var go = rm.InstantiatePrefab("Equipment/" + name);
                        var t = go != null ? go.GetComponent<StoredTorpedo>() : null;
                        if (t == null) { if (go != null) Object.Destroy(go); return; }
                        s.SetItemAt(i, t);
                        made++;
                    }
                    if (made >= want) break;
                }
            });
            return made;
        }
    }
}
