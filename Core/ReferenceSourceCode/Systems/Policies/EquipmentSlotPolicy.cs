using Game.CodeGen;
using Game.Systems.Items;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Policies {
    [MessagePackObject]
    public sealed class EquipmentSlotPolicy {
        // InventorySlotType.Id. Stored as a string so mods and version changes can't
        // invalidate the mapping the way a serialized hash would.
        [Key("Slot")] public string SlotId;
        [Key("Mode")] public AutoEquipMode Mode;
        // 0..1, though item quality itself is not clamped to 1.
        [Key("QMin")] public float QualityMin;
        // QualityMax >= UnboundedQuality means "and above".
        [Key("QMax")] public float QualityMax;
        // null = any colour
        [Key("Color")] public string ColorId;
        // InventoryItemDef.Id, null = any item
        [Key("Item")] public string ItemDefId;
        // Weapon slot only, inert elsewhere
        [Key("WKind")] public WeaponKind WeaponKind;
        // Weapon slot only. Applies regardless of Mode - a being drops its weapon when
        // incapacitated whether or not it auto-equips.
        [Key("Forbid")] public bool ForbidWhenDropped;
        [Key("Incompat")] public IncompatibleAction Incompatible;
        // Both inert for beings that cannot care where their gear came from - see
        // ItemOwnership.CaresAboutOrigin, applied when the filter is built.
        [Key("NoUsed")] public bool ForbidSecondHand;
        [Key("NoDead")] public bool ForbidDeadMans;

        public const float UnboundedQuality = 1f;

        private int slotIdH;
        [IgnoreMember] public int SlotIdH {
            get {
                if (slotIdH == 0 && SlotId != null) {
                    slotIdH = Hashes.S(SlotId);
                }
                return slotIdH;
            }
        }

        private int itemDefIdH;
        private string itemDefIdHFor;
        [IgnoreMember] public int ItemDefIdH {
            get {
                if (ItemDefId == null) {
                    return 0;
                }
                if (itemDefIdHFor != ItemDefId) {
                    itemDefIdHFor = ItemDefId;
                    itemDefIdH = Hashes.S(ItemDefId);
                }
                return itemDefIdH;
            }
        }

        // A policy built outside EquipmentPolicyDefaults - or loaded from a save
        // written before this field existed - has QualityMax 0. That has to mean
        // "no ceiling", not "nothing above 0% quality", or the slot would silently
        // forbid every item in the game.
        [IgnoreMember] public bool IsQualityBounded => QualityMax > 0f && QualityMax < UnboundedQuality;
        [IgnoreMember] public float EffectiveQualityMax => IsQualityBounded ? QualityMax : UnboundedQuality;

        // Cheap pre-filter for the compliance sweep: does this slot ever want gear
        // taken off without a replacement? Manual slots are the player's business.
        [IgnoreMember] public bool WantsImmediateUnequip => Mode != AutoEquipMode.Manual
            && Incompatible == IncompatibleAction.UnequipNow;

        public EquipmentSlotPolicy Clone() {
            return new EquipmentSlotPolicy {
                SlotId = SlotId,
                Mode = Mode,
                QualityMin = QualityMin,
                QualityMax = QualityMax,
                ColorId = ColorId,
                ItemDefId = ItemDefId,
                WeaponKind = WeaponKind,
                ForbidWhenDropped = ForbidWhenDropped,
                Incompatible = Incompatible,
                ForbidSecondHand = ForbidSecondHand,
                ForbidDeadMans = ForbidDeadMans,
            };
        }

        public override string ToString() {
            return $"EquipmentSlotPolicy[{SlotId}|{Mode}]";
        }
    }
}
