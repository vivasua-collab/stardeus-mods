using System;
using System.Collections.Generic;
using Game.Data;
using Game.Systems.Creatures;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Items {
    [Serializable]
    public class InventorySlotType {
        private static readonly Dictionary<int, InventorySlotType> known = new();
        [ExecuteOnReload]
        private static void CleanupStatics() {
            // We want them to be fresh in unity
            known.Clear();
        }
        [SelfInit(SelfInit.Stage.Finalize)]
        private static void Init() {
            LoadKnown();
        }
        public string Id;
        public string LookPart;
        public LookPartTypeDef LookPartType;
        public int IdH;
        public string Name;
        /// <summary>
        /// Gear worn on the body, as opposed to carried. Only worn slots track
        /// ownership history - a knife that changed hands is just a knife.
        /// Opt-in, so a modded slot never picks up mood consequences by accident.
        /// </summary>
        /// <see cref="ItemOwnership"/>
        public bool Worn;
        /// <summary>
        /// Higher number = lower priority
        /// </summary>
        public int Order;
        public static void LoadKnown() {
            if (known.Count > 0) { return; }
            // Both are [SelfInit(Stage.Finalize)]; load defensively since ordering between
            // Finalize-stage SelfInits across classes isn't guaranteed.
            LookPartTypeDef.LoadKnown();

            var items = The.ModLoader.LoadObjects<InventorySlotType>("Config/Inventory/SlotTypes");
            foreach (var item in items) {
                item.IdH = Hashes.S(item.Id);
                item.Name = $"inventory.slot.{item.Id}".T();
                item.LookPartType = item.LookPart != null
                    ? LookPartTypeDef.Get(item.LookPart)
                    : LookPartTypeDef.None;
                // TODO set all properties and shit
                if (!known.TryAdd(item.IdH, item)) {
                    D.Err("InventorySlot already added by id: {0}", item.Id);
                }
            }
        }

        public static Dictionary<int, InventorySlotType>.ValueCollection All => known.Values;

        public static InventorySlotType FastGet(int id) {
            if (known.TryGetValue(id, out var item)) {
                return item;
            }
            return null;
        }

        public static bool TryGet(string id, object ctx, out InventorySlotType slot) {
            slot = FastGet(Hashes.S(id));
            if (slot == null) {
                D.Err("Inventory slot not found by id: {0}. Context: {1}", id, ctx);
                return false;
            }
            return true;
        }

        public static InventorySlotType Get(string id) {
            return Get(id, Hashes.S(id));
        }

        public static InventorySlotType Get(string id, int idH) {
            if (known.TryGetValue(idH, out var item)) {
                return item;
            }
            D.Err("InventorySlot {0} ({1}) not found! See Config/Inventory/Slots folder",
                id, idH);
            return null;
        }

        public override string ToString() {
            return $"InventoryItem [{Id}]";
        }

        public LookPart CreateLookPart() {
            var part = new LookPart();
            part.SetType(LookPartType);
            return part;
        }
    }
}
