using System;
using System.Collections.Generic;
using System.Linq;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Systems.Mental;
using Game.Systems.Policies;
using Game.Systems.Stats;
using Game.Systems.UIUX;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Items {
    public sealed class BeingInventory {
        public BeingData Owner { get; private set; }
        /// <summary>
        /// For clothing, weapons, armor.
        /// Key is InventorySlotIdHash
        /// </summary>
        public readonly Dictionary<int, InventoryItemSlot> Equipment = new();
        private readonly Dictionary<int, InventoryItemSlot> VisibleEquipment = new();
        private readonly HashSet<int> EquipmentParts = new();
        /// <summary>
        /// For visibility / looks
        /// </summary>
        public InventoryItemSlot[] OrderedEquipment;
        [SkipDumpInstance]
        public bool NeedsLooksRebuild;
        [SkipDumpInstance]
        public bool NeedsStatsUpdate;
        private BCallback equipmentChangedB;
        public BCallback EquipmentChangedB => equipmentChangedB ??= new BCallback();
        private bool isSpaceSuitModifiersAdded;
        private static (int, StatModifier)[] spaceSuitModifiers;
        public bool HasRemovableEquipment {
            get {
                foreach (var s in Equipment.Values) {
                    if (s.Item != null) {
                        return true;
                    }
                }
                return false;
            }
        }
        public bool HasMissingEquipment {
            get {
                foreach (var s in Equipment.Values) {
                    if (s.Item == null) {
                        return true;
                    }
                }
                return false;
            }
        }

        // Cached answer to "does any slot auto-equip at all", so AINeedEquipment.Tick
        // (every tick, every being) costs one int compare instead of a full policy
        // resolution per slot. Keyed on the policy version and the mind, since a mind
        // transfer swaps the individual policy without touching the version.
        private int policyVersion;
        private Mind policyForMind;
        private bool anyAutoEquip;

        public bool AnyAutoEquip(GameState s) {
            var version = s.Query.EquipmentPolicyVersion.Ask();
            var mind = Owner.Mind;
            if (policyVersion == version && ReferenceEquals(policyForMind, mind)) {
                return anyAutoEquip;
            }
            policyVersion = version;
            policyForMind = mind;
            anyAutoEquip = false;
            foreach (var slot in Equipment.Values) {
                var policy = s.Query.EquipmentPolicyFor.Ask(Owner, slot.Type);
                if (policy != null && policy.Mode != AutoEquipMode.Manual) {
                    anyAutoEquip = true;
                    break;
                }
            }
            return anyAutoEquip;
        }

        public bool IsNaked {
            get {
                if (Equipment.TryGetValue(InventorySlotIdH.Clothing, out var clothing)) {
                    if (clothing.Item != null) {
                        return false;
                    }
                }
                if (Equipment.TryGetValue(InventorySlotIdH.Suit, out var suit)) {
                    if (suit.Item != null) {
                        return false;
                    }
                }
                return true;
            }
        }

        public string EquippedCount {
            get {
                int equipped = 0;
                int total = 0;
                foreach (var slot in Equipment) {
                    total++;
                    if (slot.Value.Item != null) {
                        equipped++;
                    }
                }
                return Units.XOutOfY(equipped, total);
            }
        }

        public override string ToString() {
            return $"Inventory [{Owner}]";
        }

        public void LooksChanged() {
            NeedsLooksRebuild = true;
        }

        internal void NotifyEquipmentChanged() => equipmentChangedB?.Invoke();

        public void UpdateLooks(bool force = false) {
            if (!force && !NeedsLooksRebuild) { return; }
            NeedsLooksRebuild = false;
            if (ApplyInventoryLooks(Owner.Looks)) {
                // being.Looks.ClearCache();
                Owner.Being?.Looks.Rebuild(force: true);
                // rebuild the avatar
                Owner.Looks.Avatar?.Build(Owner);
            }
        }

        // A generated suit and helmet roll their colors independently; one color makes them
        // read as a set. Only for items sharing a palette, so a modded helmet keeps its own.
        // Run before the slots apply their looks, they copy ObjData.Color into the look part.
        public void MatchHelmetColorToSuit() {
            if (!Equipment.TryGetValue(InventorySlotIdH.Suit, out var suit) || suit.ObjData == null) {
                return;
            }
            if (!Equipment.TryGetValue(InventorySlotIdH.Helmet, out var helmet) || helmet.ObjData == null) {
                return;
            }
            var group = suit.Item.ColorGroupId;
            if (group == null || group != helmet.Item.ColorGroupId) { return; }
            helmet.ObjData.Color = suit.ObjData.Color;
        }

        // The flags that decide which equipment shows. Shared by the looks and by the
        // when-shown stat modifiers, so what the player sees and what slows the wearer agree.
        private InventoryLooksFlags LooksFlagsNow() {
            InventoryLooksFlags looksFlags = InventoryLooksFlags.None;
            if ((Owner.EnvFlags & (BeingEnvFlags.UnsafeBase | BeingEnvFlags.Outdoors)) == BeingEnvFlags.None
                    && Owner.Being?.Flammable.IsOnFire != true) {
                // not unsafe without equipment and not on fire, show safe env looks
                looksFlags |= InventoryLooksFlags.SafeEnv;
            }
            if ((Owner.EnvFlags & BeingEnvFlags.InCombat) == BeingEnvFlags.InCombat) {
                looksFlags |= InventoryLooksFlags.Combat;
            } else if (Owner.Being?.AIAgent.DirectControl.IsUnderDirectControl == true) {
                looksFlags |= InventoryLooksFlags.Combat;
            }
            return looksFlags;
        }

        /// <summary>
        /// Applies the when-shown stat modifiers (InventoryItemDef.AddsStatModifiersWhenShown)
        /// of the items that show right now and removes the others. Called when the env flags,
        /// the draft or the equipment change, so it runs off gameplay state, not rendering.
        /// </summary>
        public void UpdateShownStatModifiers() {
            var looksFlags = LooksFlagsNow();
            shownLookParts.Clear();
            for (int i = OrderedEquipment.Length - 1; i >= 0; i--) {
                InventoryItemSlot eq = OrderedEquipment[i];
                if (eq.Item == null) { continue; }
                var lookPart = eq.Type.LookPartType.IdH;
                var flags = looksFlags;
                if (!shownLookParts.Contains(lookPart)) {
                    flags |= InventoryLooksFlags.SlotEmpty;
                }
                var shown = eq.IsVisibleNow(flags);
                if (shown) {
                    shownLookParts.Add(lookPart);
                }
                eq.SetShown(shown);
            }
        }
        private readonly HashSet<int> shownLookParts = new();

        private bool ApplyInventoryLooks(BeingLooks looks) {
            // D.Warn("Applying inv looks for {0}", Owner);
            VisibleEquipment.Clear();
            var looksFlags = LooksFlagsNow();
            bool changed = false;
            for (int i = OrderedEquipment.Length - 1; i >= 0; i--) {
                InventoryItemSlot eq = OrderedEquipment[i];
                if (eq.Item != null) {
                    var lookPart = eq.Type.LookPartType.IdH;
                    var flags = looksFlags;
                    if (!VisibleEquipment.ContainsKey(lookPart)) {
                        flags |= InventoryLooksFlags.SlotEmpty;
                    }
                    if (eq.IsVisibleNow(flags)) {
                        VisibleEquipment[lookPart] = eq;
                    }
                }
            }
            for (int i = 0; i < looks.NumParts; i++) {
                LookPart part = looks.Parts[i];
                if (!EquipmentParts.Contains(part.Type.IdH)) { continue; }
                if (VisibleEquipment.TryGetValue(part.Type.IdH, out var eqSlot)) {
                    if (part.CopyLookFrom(eqSlot.LookPart)) {
                        changed = true;
                    }
                } else {
                    if (part.Clear()) {
                        changed = true;
                    }
                }
            }
            // D.Warn("Applying inv looks. Changed: {0}", changed);
            return changed;
        }

        public static BeingInventory BlankFor(GameState s, BeingData data) {
            var slotTypes = data.Species.InventorySlotTypes;
            if (Arrays.IsEmpty(slotTypes)) {
                return null;
            }
            var inv = new BeingInventory();
            inv.SetOwner(data);
            if (!Arrays.IsEmpty(slotTypes)) {
                foreach (var slotId in slotTypes) {
                    if (InventorySlotType.TryGet(slotId, data.Species, out var slot)) {
                        var itemSlot = InventoryItemSlot.NewFor(s, slot, inv);
                        if (itemSlot.LookPart != null) {
                            inv.EquipmentParts.Add(slot.LookPartType.IdH);
                        }
                        if (!inv.Equipment.TryAdd(slot.IdH, itemSlot)) {
                            D.Err("Slot already added: {0} for {1}",
                                slot.Id, data.Species);
                        }
                    }
                }
                inv.OrderedEquipment = inv.Equipment.Values.Where(e => e.Type != null)
                    .OrderBy(e => e.Type.Order).ToArray();
            }
            return inv;
        }

        private void SetOwner(BeingData data) {
            Owner = data;
            data.OnEnvChange.AddListener(OnEnvChange);
        }

        private void OnEnvChange(BeingData _, BeingEnvFlags _1, BeingEnvFlags _2) {
            LooksChanged();
        }

        public bool TryUnequip(GameState s, int slotIdH, out Obj obj) {
            if (Equipment.TryGetValue(slotIdH, out var slot)) {
                if (slot.Item != null) {
                    obj = slot.RemoveItem();
                    return true;
                }
            }
            obj = null;
            return false;
        }

        public bool TryEquip(GameState s, ObjData objData) {
            var item = objData.Item;
            if (objData.State == ObjState.Destroyed
                    || objData.State == ObjState.Equipped) {
                return false;
            }
            if (item == null) { return false; }
            var slot = item.SlotType;
            if (Equipment.TryGetValue(slot.IdH, out var itemSlot)) {
                var oldItemData = itemSlot.Equip(objData);
                if (objData.Obj != null) {
                    s.Objs.Destroy(objData.Obj);
                }
                if (oldItemData != null) {
                    s.Objs.Create(Owner.Being.Position, oldItemData);
                }
                RebuildLooksNow();
                return true;
            } else {
                D.Err("Trying to equip an item that does not have a slot for the agent: {0}->{1}",
                    objData, Owner);
                return false;
            }
        }

        public void RebuildLooksNow() {
            Update();
            if (Owner.Being != null) {
                Owner.Being.Graphics.RebuildLooks();
                Owner.Being.Graphics.Update();
            }
        }

        public void Update() {
            UpdateLooks();
            if (!NeedsStatsUpdate) {
                return;
            }
            var ssi = Owner.StatsBeing.SpaceSuitIntegrity;
            var suitTegrity = ssi.ValueFresh;
            if (suitTegrity >= 0.9999f) {
                if (!isSpaceSuitModifiersAdded) {
                    isSpaceSuitModifiersAdded = true;
                    if (spaceSuitModifiers == null) {
                        CreateSpaceSuitModifiers();
                    }
                    foreach (var mod in spaceSuitModifiers) {
                        if (Owner.StatsBeing.Find(mod.Item1, out var stat)) {
                            // D.Warn("Adding space suit modifier");
                            stat.AddModifier(mod.Item2);
                        }
                    }
                }
            } else {
                if (isSpaceSuitModifiersAdded) {
                    isSpaceSuitModifiersAdded = false;
                    foreach (var mod in spaceSuitModifiers) {
                        if (Owner.StatsBeing.Find(mod.Item1, out var stat)) {
                            // D.Warn("Removing space suit modifier");
                            stat.RemoveModifier(mod.Item2);
                        }
                    }
                }
            }
        }

        private static void CreateSpaceSuitModifiers() {
            spaceSuitModifiers = new (int, StatModifier)[] {
                (StatIdH.O2Min, StatModifier.Sets(0f, T.ResearchSpacesuit)),
                (StatIdH.TempMin, StatModifier.Sets(0f, T.ResearchSpacesuit)),
            };
        }

        public void Initialize(GameState s) {
            foreach (var slot in Equipment.Values) {
                if (slot.Item != null) {
                    slot.ObjData.BeingDataId = Owner.Id;
                    // Generated gear reaches Set() before the being has an id, so
                    // this is where a raider's starting shirt becomes theirs - and
                    // second-hand to whoever loots it later.
                    ItemOwnership.OnEquipped(slot.ObjData, Owner);
                    s.Query.PersistObjData.Ask(slot.ObjData);
                }
            }
        }

        public void AfterLoad(bool isLoading) {
            if (!Arrays.IsEmpty(Equipment)) {
                foreach (var inv in Equipment.Values) {
                    inv.AfterLoad(isLoading);
                }
            }
            UpdateShownStatModifiers();
        }

        // The item left the game with its owner (ObjsSys drops it): the slot lets go of it too,
        // or every later save writes an object id that no longer exists
        public void ForgetItem(ObjData data) {
            foreach (var slot in Equipment.Values) {
                if (slot.ObjData == data) {
                    slot.Unequip();
                }
            }
        }

        // Through Unequip, so the item's stat modifiers, attacks, abilities and conditions
        // leave with it. Nulling the slot kept them on the data: a colonist came back naked
        // with the oxygen and temperature limits of the suit it no longer had (STA-1900).
        public void Destroy(GameState s) {
            foreach (var slot in Equipment.Values) {
                if (slot.ObjData != null) {
                    var objData = slot.Unequip();
                    s.Query.DeactivateObjData.Ask(objData, ObjState.Destroyed);
                }
            }
        }
    }
}
