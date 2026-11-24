using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using UnityEngine;
using Game.Systems.Creatures;
using KL.Utils;
using KL.Text;
using Game.Systems.Stats;
using Game.Components;
using Game.Systems.UIUX;
using Game.Input;

namespace Game.Systems.Sleep {
    public sealed class BedComp : BaseComponent<BedComp>,
            IRelocatable {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new BedComp());
        }
        public BedSlot[] Slots;
        public bool HideBeing;
        public int NumSlots;
        public bool SlotUsePorts;
        private int[] slotOwnerIds;
        private int[] autoAssign;
        public bool hasSpotRotation;
        public Facing.Type spotRotation;
        public Facing.Type SpotRotation {
            get {
                if (hasSpotRotation) {
                    return Facing.Rotate(Tile.Transform.Rotation, spotRotation);
                } else {
                    return Tile.Transform.Rotation;
                }
            }
        }

        private BCallback stateChangedB;
        public BCallback StateChangedB => stateChangedB ??= new BCallback();
        private bool showOwnerNames;
        private bool showPetNames;
        private float ownerNameFontSize;
        private GameTextTMP[] ownerLabels;
        protected override void OnConfig() {
            HideBeing = Config.GetBool(PropertyIdH.HideBeing, false);
            NumSlots = Config.GetInt(PropertyIdH.Slots, 1);
            SlotUsePorts = Config.GetBool(PropertyIdH.SlotUsePorts, false);
            var spotRotStr = Config.GetString(PropertyIdH.SpotRotation, null);
            if (spotRotStr != null) {
                hasSpotRotation = true;
                spotRotation = Facing.ConvertRotation(spotRotStr[0]);
            }
            showOwnerNames = Config.GetBool(Hashes.S("ShowOwnerNames"), true);
            showPetNames = Config.GetBool(Hashes.S("ShowPetNames"), false);
            ownerNameFontSize = Config.GetFloat(Hashes.S("OwnerNameFontSize"), 1.5f);
            for (int i = 0; i < NumSlots; i++) {
                Config.MarkUsed(Hashes.S($"SlotFlags_{i}"));
                Config.MarkUsed(Hashes.S($"SlotHidesBody_{i}"));
                Config.MarkUsed(Hashes.S($"SlotOffset_D_{i}"));
                Config.MarkUsed(Hashes.S($"SlotOffset_U_{i}"));
                Config.MarkUsed(Hashes.S($"SlotOffset_L_{i}"));
                Config.MarkUsed(Hashes.S($"SlotOffset_R_{i}"));
                Config.MarkUsed(Hashes.S($"SlotTemporary_{i}"));
                Config.MarkUsed(Hashes.S($"SlotNameKey_{i}"));
                Config.MarkUsed(Hashes.S($"SlotRotation_{i}"));
                Config.MarkUsed(Hashes.S($"SlotNameOffset_D_{i}"));
                Config.MarkUsed(Hashes.S($"SlotNameOffset_U_{i}"));
                Config.MarkUsed(Hashes.S($"SlotNameOffset_L_{i}"));
                Config.MarkUsed(Hashes.S($"SlotNameOffset_R_{i}"));
                Config.MarkUsed(Hashes.S($"SlotNameRotation_D_{i}"));
                Config.MarkUsed(Hashes.S($"SlotNameRotation_U_{i}"));
                Config.MarkUsed(Hashes.S($"SlotNameRotation_L_{i}"));
                Config.MarkUsed(Hashes.S($"SlotNameRotation_R_{i}"));
            }
        }

        private void BuildSlots() {
            Slots = new BedSlot[NumSlots];
            for (int i = 0; i < NumSlots; i++) {
                var flagArr = Config.GetStringSet(Hashes.S($"SlotFlags_{i}"));
                var flags = Enums.FlagsFromStringArray_UInt<StatFlags>(flagArr,
                    Tile.DefinitionId);
                var hidesBody = Config.GetBool(Hashes.S($"SlotHidesBody_{i}"), false);
                var offset_D = Config.GetVector2(Hashes.S($"SlotOffset_D_{i}"), Consts.V2Zero);
                var offset_U = Config.GetVector2(Hashes.S($"SlotOffset_U_{i}"), Consts.V2Zero);
                var offset_L = Config.GetVector2(Hashes.S($"SlotOffset_L_{i}"), Consts.V2Zero);
                var offset_R = Config.GetVector2(Hashes.S($"SlotOffset_R_{i}"), Consts.V2Zero);
                var isTemp = Config.GetBool(Hashes.S($"SlotTemporary_{i}"), false);
                var name = Config.GetString(Hashes.S($"SlotNameKey_{i}")).TOrNull() ?? $"#{i}";
                var nameOffset_D = Config.GetVector2(Hashes.S($"SlotNameOffset_D_{i}"), Consts.V2Zero);
                var nameOffset_U = Config.GetVector2(Hashes.S($"SlotNameOffset_U_{i}"), Consts.V2Zero);
                var nameOffset_L = Config.GetVector2(Hashes.S($"SlotNameOffset_L_{i}"), Consts.V2Zero);
                var nameOffset_R = Config.GetVector2(Hashes.S($"SlotNameOffset_R_{i}"), Consts.V2Zero);
                var nameRotation_D = Config.GetFloat(Hashes.S($"SlotNameRotation_D_{i}"), 0f);
                var nameRotation_U = Config.GetFloat(Hashes.S($"SlotNameRotation_U_{i}"), 0f);
                var nameRotation_L = Config.GetFloat(Hashes.S($"SlotNameRotation_L_{i}"), 0f);
                var nameRotation_R = Config.GetFloat(Hashes.S($"SlotNameRotation_R_{i}"), 0f);
                uint ownerId = slotOwnerIds == null ? 0 : (uint) slotOwnerIds[i];
                bool auto = autoAssign == null ? true : autoAssign[i] > 0;
                Slots[i] = new BedSlot() {
                    S = S,
                    Number = (byte) i,
                    Name = name,
                    Comp = this,
                    AutoAssign = auto,
                    HidesBody = hidesBody,
                    Compatibility = flags,
                    IsTemporary = isTemp,
                    Offset_D = offset_D,
                    Offset_U = offset_U,
                    Offset_R = offset_R,
                    Offset_L = offset_L,
                    NameOffset_D = nameOffset_D,
                    NameOffset_U = nameOffset_U,
                    NameOffset_L = nameOffset_L,
                    NameOffset_R = nameOffset_R,
                    NameRotation_D = nameRotation_D,
                    NameRotation_U = nameRotation_U,
                    NameRotation_L = nameRotation_L,
                    NameRotation_R = nameRotation_R,
                    OwnerDataId = ownerId,
                };
                var rot = Config.GetString(Hashes.S($"SlotRotation_{i}"), null);
                if (rot != null) {
                    Slots[i].SetRotation(Facing.ConvertRotation(rot[0]));
                }
            }
        }
        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ConstructionFinished) {
                RegisterBed(true, false);
            } else if (message == MsgIdH.HealthWentToZero) {
                RegisterBed(false, false);
            }
        }
        public override void OnLateReady(bool wasLoaded) {
            if (Tile.IsConstructed) {
                RegisterBed(true, delay: true);
            }
        }
        public override void OnRemove() {
            DestroyOwnerLabels();
            if (!Tile.Reinstallable.IsMidRelocation) {
                // unregister will happen after we relocateTo
                RegisterBed(false, false);
            }
        }

        private void DestroyOwnerLabels() {
            if (ownerLabels == null) { return; }
            for (int i = 0; i < ownerLabels.Length; i++) {
                ownerLabels[i]?.Destroy();
                ownerLabels[i] = null;
            }
            ownerLabels = null;
        }

        public override void OnSave() {
            if (!Tile.IsConstructed) { return; }
            if (Slots == null) {
                D.Err("Bed slots are null for a constructed bed on save! {0}", Tile);
                return;
            }
            var data = GetOrCreateData();
            if (Slots != null)
            slotOwnerIds ??= new int[NumSlots];
            autoAssign ??= new int[NumSlots];
            for (int i = 0; i < NumSlots; i++) {
                slotOwnerIds[i] = (int) Slots[i].OwnerDataId;
                autoAssign[i] = Slots[i].AutoAssign ? 1 : 0;
            }
            data.SetIntSet("SlotOwners", slotOwnerIds);
            data.SetIntSet("AutoAssign", autoAssign);
        }

        protected override void OnLoad(ComponentData data) {
            autoAssign = data.GetIntSet("AutoAssign", null);
            slotOwnerIds = data.GetIntSet("SlotOwners", null);
        }

        private bool isRegistered;
        private void RegisterBed(bool yes, bool delay) {
            // D.Warn("Registering bed: {0}", Tile);
            if (yes != isRegistered) {
                if (Slots == null) {
                    BuildSlots();
                }
                isRegistered = yes;
                S.Sig.RegisterBed.Send(this, yes);
                if (yes) {
                    if (delay) {
                        Ready.WhenGameLate(this, "Bed Label", RefreshOwnerLabels);
                    } else {
                        RefreshOwnerLabels();
                    }
                } else {
                    DestroyOwnerLabels();
                }
            }
        }

        public void UpdateUIBlock(bool wasUpdated) {
            stateChangedB?.Invoke();
            RefreshOwnerLabels();
        }

        private void RefreshOwnerLabels() {
            if (Slots == null) { return; }
            ownerLabels ??= new GameTextTMP[Slots.Length];
            for (int i = 0; i < Slots.Length; i++) {
                var slot = Slots[i];
                var owner = slot.Owner;
                // A null mind could be a load failure after mods disabled. Either way the
                // slot has nobody to name, and a label left from its last owner must go.
                bool shouldShow = showOwnerNames
                    && owner?.Mind != null
                    && !slot.IsInUse
                    && (!slot.IsPetSlot || showPetNames);
                if (shouldShow) {
                    var pos = (Vector3)(Tile.Position + slot.Offset + slot.NameOffset);
                    var rot = Quaternion.Euler(0f, 0f, slot.NameRotation);
                    string name = owner.Mind.Name.ShortName;
                    if (slot.IsPetSlot) {
                        name = $"{GlyphMap.SpriteAnimalPaw} {name}";
                    }
                    if (ownerLabels[i] == null) {
                        ownerLabels[i] = GameTextTMP.Create("TileLabel", name, pos, rot);
                        ownerLabels[i].SetFontSize(ownerNameFontSize);
                        ownerLabels[i].SetColor(Color.grey);
                    } else {
                        ownerLabels[i].SetText(name);
                        ownerLabels[i].MoveTo(pos);
                        ownerLabels[i].SetRotation(rot);
                    }
                } else {
                    ownerLabels[i]?.Destroy();
                    ownerLabels[i] = null;
                }
            }
        }

        public override void UpdatePosIdx(int pos, int dx, int dy) {
            if (ownerLabels == null) { return; }
            for (int i = 0; i < ownerLabels.Length; i++) {
                ownerLabels[i]?.MoveBy(dx, dy);
            }
        }

        public bool IsAssignable(BeingData owner, bool skipAssigned, bool auto, bool temporary) {
            foreach (var slot in Slots) {
                if (slot.IsTemporary != temporary) { continue; }
                if (auto && !slot.AutoAssign) { continue; }
                if (skipAssigned && slot.OwnerDataId != 0) { continue; }
                if (Tile.Health.IsDamaged) { continue; }
                if (auto && !owner.Safety.IsActuallySafeAt(slot.InPort)) {
                    continue;
                }
                if (slot.IsCompatibleWith(owner)) {
                    return true;
                }
            }
            return false;
        }

        public BedSlot SlotFor(BeingData beingData) {
            for (int i = 0; i < NumSlots; i++) {
                BedSlot sl = Slots[i];
                if (sl.OwnerDataId == beingData.Id) {
                    return sl;
                }
                if (sl.IsTemporary && sl.IsCompatibleWith(beingData)) {
                    return sl;
                }
            }
            return null;
        }

        public void RelocateTo(Entity target) {
            if (target.TryGetComponent<BedComp>(out var other)) {
                if (other.isRegistered) {
                    MigrateSlotsTo(other);
                } else {
                    S.Clock.ExecuteAfterFramesForUI(1, () => {
                        MigrateSlotsTo(other);
                    });
                }
            } else {
                D.Err("No BedComp on bed during relocation! {0}", Tile);
                RegisterBed(false, false);
            }
        }

        private void MigrateSlotsTo(BedComp other) {
            for (int i = Slots.Length - 1; i >= 0; i--) {
                BedSlot slot = Slots[i];
                if (slot.Owner != null) {
                    S.Query.AssignBedSlot.Ask(slot.Owner, other.Slots[i]);
                    slot.Owner = null;
                }
            }
            RegisterBed(false, false);
        }

    }
}
