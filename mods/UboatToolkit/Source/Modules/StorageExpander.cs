using System.Reflection;
using UnityEngine;
using UBOAT.Game.Core.Serialization;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;
using UBOAT.Game.Scene.Utilities;

namespace UboatToolkit.Modules
{
    // Storage grows by itself: when main storage, item storage or kitchen is almost full, x more slots are added
    // (up to a maximum). The new slots are saved with the game like other dynamic objects, so items stay after loading.
    // A minimum slot count can also be set; it is applied at once, also after a boat change.
    public class StorageExpander : ModuleBase
    {
        private static readonly string[] names = { "Main Storage", "Item Storage", "Kitchen" };
        private static readonly FieldInfo typeField = typeof(ShipEquipmentSlot).GetField("equipmentType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        public override void Tick()
        {
            bool grow = Settings.B("AutoExpandStorage");
            int min = (int)Settings.F("StorageMinSlots");
            if (!grow && min <= 0) return;
            var saves = Game.Saves;
            if (saves != null && (saves.IsLoading || saves.IsActivelyDeserializing)) return;
            var ship = Util.PlayerShip;
            int step = Mathf.Max(1, (int)Settings.F("AutoExpandStep"));
            int max = Mathf.Max(1, (int)Settings.F("AutoExpandMax"));
            foreach (var n in names)
            {
                var st = ship.GetEquipment<Storage>(n);
                if (st == null || Util.Slots(st) == 0) continue;
                int target = st.SlotCount;
                if (min > 0 && target < min && n != "Kitchen") target = min;
                if (grow)
                {
                    int free = 0;
                    for (int i = 0; i < Util.Slots(st); i++) if (st[i] != null && st[i].Item == null) free++;
                    if (free < step) target = Mathf.Max(target, st.SlotCount + step);
                }
                target = Mathf.Min(target, Mathf.Max(max, st.SlotCount));
                if (target > st.SlotCount) AddSlots(st, target);
            }
        }

        private static void AddSlots(Storage storage, int target)
        {
            storage.ValidateSlots();
            int before = storage.SlotCount;
            while (storage.SlotCount < target)
            {
                var go = new GameObject("Storage Slot Extra " + (storage.SlotCount + 1));
                go.layer = 10;
                go.transform.SetParent(storage.transform, false);
                go.AddComponent<DestroyBeforeLoading>();
                go.AddComponent<SerializableDynamicObject>();
                var slot = go.AddComponent<ShipEquipmentSlot>();
                typeField?.SetValue(slot, "Storage");
                slot.VariationName = "";
                storage.ValidateSlots();
                if (storage.SlotCount <= before) break; // safety: slot was not accepted
                before = storage.SlotCount;
            }
            Util.Log("Lager " + storage.name + ": jetzt " + storage.SlotCount + " Plätze");
        }
    }
}
