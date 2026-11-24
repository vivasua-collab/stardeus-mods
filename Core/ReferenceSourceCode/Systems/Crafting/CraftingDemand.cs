using Game.Components;
using Game.Constants;
using Game.Utils;
using KL.Utils;
using Game.Systems.Creatures;
using Game.Data;
using Game.Migrations;

namespace Game.Systems.Crafting {
    public class CraftingDemand {
        public string ColorId;
        public CraftingDemandType Type;
        private int amountWanted;
        public int AmountWanted {
            get => amountWanted;
            set => amountWanted = value;
        }
        // It may be a total amount produced by all
        public int AmountProduced;
        // Only by me and only this last configuration
        public int AmountProducedThis;
        public static int DefaultAmount => Tunables.DefaultCraftingDemandAmount;
        public void ClearNameCache() {
            craftableItemName = null;
            craftableItemNameTip = null;
        }
        private string craftableItemName;
        private string craftableItemNameTip;
        public string CraftableItemNameSimple {
            get {
                var prodName = Product.NameT;
                var qty = OutputMultiplier;
                if (qty > 1) {
                    craftableItemName = $"{prodName} {Units.XNum(qty)}";
                } else {
                    craftableItemName = prodName;
                }
                return craftableItemName;
            }
        }
        public string CraftableItemName {
            get {
                if (craftableItemName != null) {
                    return craftableItemName;
                }
                var prodName = Texts.Accented(Product.NameT);
                var qty = OutputMultiplier;
                if (qty > 1) {
                    craftableItemName = $"{prodName} {Texts.Accented(Units.XNum(qty), false)}";
                } else {
                    craftableItemName = prodName;
                }
                return craftableItemName;
            }
        }
        public string CraftableItemNameTip {
            get {
                var prodName = Product.CodexLink;
                var qty = OutputMultiplier;
                if (qty > 1) {
                    craftableItemNameTip = $"{prodName} {Texts.Accented(Units.XNum(qty))}";
                } else {
                    craftableItemNameTip = prodName;
                }
                return craftableItemNameTip;
            }
        }
        public Def Product => Craftable.ProductDef;
        private string craftableId;
        // Default batch size == 1, you can set it to a bigger number,
        // to bring in more ingredients and get more output
        public int BatchSize { get; private set; }
        public void SetBatchSize(int bs) {
            if (bs < 1) { bs = 1; }
            BatchSize = bs;
            craftableItemName = null;
        }
        public int OutputMultiplier => Craftable.OutputMultiplier * BatchSize;
        private Craftable craftable;
        public Craftable Craftable {
            get {
                if (craftable != null) { return craftable; }
                if (craftableId == null) { return null; }
                var crHash = Hashes.S(craftableId);
                craftable = Craftable.Find(crHash);
                if (craftable == null) {
                    if (!Legacy.RemovedCraftables.Contains(crHash)) {
                        D.Err("Craftable not found by id: {0} -> {1}",
                            craftableId, this);
                    }
                    craftableId = null;
                }
                return craftable;
            }
            set {
                if (value == null) {
                    D.Err("Trying to set CraftingDemand.Craftable to null!");
                    craftableId = null;
                    craftable = null;
                } else {
                    craftable = value;
                    craftableId = craftable.Id;
                }
            }
        }

        public string TypeName => NameFor(Type);

        public string TypeIcon => IconFor(Type);

        public bool HasProducedEnough(GameState S) {
            switch (Type) {
                case CraftingDemandType.One: return AmountProduced > 0;
                case CraftingDemandType.Custom: return AmountProduced >= AmountWanted;
                // No cache of its own: InventorySys and ObjsSys already keep these counts
                // per tick
                case CraftingDemandType.Limit: return InventoryCount(S, Product) >= AmountWanted;
                case CraftingDemandType.Unlimited: return false;
                default: return false;
            }
        }

        public bool NeedsAmount {
            get {
                switch (Type) {
                    case CraftingDemandType.Limit:
                    case CraftingDemandType.Custom: return true;
                    case CraftingDemandType.One:
                    case CraftingDemandType.Unlimited: return false;
                }
                D.Err("Unhandled type: {0}", Type);
                return false;
            }
        }

        public int GlobalInventoryCount => InventoryCount(A.S, Product);

        // Counted the same way a Limit order checks itself, so the order starts exactly
        // DefaultAmount short of its target
        public void SetDefaultLimitAmount() {
            AmountWanted = GlobalInventoryCount + DefaultAmount;
        }

        private int InventoryCount(GameState S, Def def) {
            if (def.MatType != null) {
                AmountProduced = S.Sys.Inventory.AvailableCount(def.MatType);
            } else if (def.IsBeing) {
                var species = Species.ForDef(def.IdH);
                if (species == null) {
                    D.Err("Trying to count beings by unavailable species: {0}", def);
                    AmountWanted = 0;
                    Type = CraftingDemandType.Custom;
                    return 0;
                }
                if (species.IsWild) {
                    AmountProduced = S.Beings.CountAliveBySpecies(species);
                } else {
                    AmountProduced = S.Query.CountFriendlyBeingsBySpecies.Ask(species.IdH, false);
                }
            } else {
                AmountProduced = S.Query.ObjCountByDef.Ask(Craftable.ProductDef);
            }
            return AmountProduced;
        }

        private void SaveData(ComponentData data) {
            data.SetBool("CraftDemand.Present", true);
            data.SetInt("CraftDemand.Type", (int) Type);
            data.SetString("CraftDemand.CraftableId", craftableId);
            data.SetString("CraftDemand.ColorId", ColorId);
            data.SetInt("CraftDemand.BatchSize", BatchSize);
            data.SetInt("CraftDemand.AmountWanted", AmountWanted);
            data.SetInt("CraftDemand.AmountProduced", AmountProduced);
            data.SetInt("CraftDemand.AmountProducedThis", AmountProducedThis);
        }
        private void LoadData(ComponentData data) {
            Type = (CraftingDemandType) data.GetInt("CraftDemand.Type");
            ColorId = data.GetString("CraftDemand.ColorId", null);

            craftableId = data.GetString("CraftDemand.CraftableId", null);
            BatchSize = data.GetInt("CraftDemand.BatchSize", 1);
            if (BatchSize < 1) {
                BatchSize = 1;
            }
            AmountWanted = data.GetInt("CraftDemand.AmountWanted", 1);
            AmountProduced = data.GetInt("CraftDemand.AmountProduced", 0);
            AmountProducedThis = data.GetInt("CraftDemand.AmountProducedThis", 0);
        }

        public static CraftingDemand CreateFor(Craftable craftable) {
            var d = new CraftingDemand();
            d.AmountWanted = DefaultAmount;
            d.Craftable = craftable;
            d.BatchSize = 1;
            d.Type = CraftingDemandType.Limit;
            return d;
        }

        public static void Save(CraftingDemand demand, ComponentData data) {
            if (demand == null) {
                data.SetBool("CraftDemand.Present", false);
            } else {
                demand.SaveData(data);
            }
        }

        public static CraftingDemand Load(ComponentData data) {
            var isPresent = data.GetBool("CraftDemand.Present", false);
            if (!isPresent) { return null; }
            var d = new CraftingDemand();
            d.LoadData(data);
            if (d.craftableId == null) {
                D.ErrSoft("Failed to load CraftingDemand: {0}/{1}",
                    data.GetString("CraftDemand.Id", null),
                    data.GetString("CraftDemand.CraftableId", null));
                return null;
            } else if (d.Craftable == null) {
                D.ErrSoft("Failed to load CraftingDemand - Craftable does not exist anymore: {0}",
                    data.GetString("CraftDemand.CraftableId", null));
                return null;
            }
            return d;
        }

        public override bool Equals(object obj) {
            if (obj != null && obj is CraftingDemand cd) {
                return cd.AmountWanted == AmountWanted
                    && cd.Type == Type
                    && cd.ColorId == ColorId
                    && cd.BatchSize == BatchSize
                    && cd.craftableId == craftableId;
            }
            return false;
        }

        public static bool IsSameCraftable(CraftingDemand a, CraftingDemand b) {
            return a.craftable == b.craftable;
        }

        public override int GetHashCode() {
            var hash = Hashes.Quick(23, Type.GetHashCode());
            if (craftableId != null) {
                hash = Hashes.Quick(hash, craftableId.GetHashCode());
            }
            hash = Hashes.Quick(hash, AmountWanted);
            hash = Hashes.Quick(hash, BatchSize);
            if (ColorId != null) {
                hash = Hashes.Quick(hash, ColorId.GetHashCode());
            }
            return hash;
        }
        public bool CopyTo(CraftingDemand other) {
            bool copied = false;
            if (other.craftable != craftable) {
                D.Err("Can only copy crafting demand if id matches! {0} != {1}",
                    other.craftable, craftable);
                return false;
            }
            if (other.Type != Type) {
                other.Type = Type;
                copied = true;
            }
            if (other.amountWanted != amountWanted) {
                other.AmountWanted = amountWanted;
                copied = true;
            }
            if (other.ColorId != ColorId) {
                other.ColorId = ColorId;
                copied = true;
            }
            if (other.BatchSize != BatchSize) {
                other.BatchSize = BatchSize;
                copied = true;
            }
            if (copied) {
                other.AmountProduced = 0;
                other.AmountProducedThis = 0;
            }

            return copied;
        }

        public static CraftingDemand Copy(CraftingDemand orig) {
            if (orig == null) { return null; }
            return new CraftingDemand() {
                craftableId = orig.craftableId,
                craftable = orig.craftable,
                Type = orig.Type,
                AmountWanted = orig.AmountWanted,
                BatchSize = orig.BatchSize,
                ColorId = orig.ColorId,
            };
        }
        public static string NameFor(CraftingDemandType t) {
            switch (t) {
                case CraftingDemandType.One: return T.DemandTypeOne;
                case CraftingDemandType.Custom: return T.DemandTypeCustom;
                case CraftingDemandType.Limit: return T.DemandTypeLimit;
                case CraftingDemandType.Unlimited: return T.DemandTypeUnlimited;
            }
            D.Err("No cache for demand type name: {0}", t);
            return $"demand.type.{t}".T();
        }
        public static string IconFor(CraftingDemandType t) {
            switch (t) {
                case CraftingDemandType.One: return IconId.CTargetOne;
                case CraftingDemandType.Custom: return IconId.CTargetCustom;
                case CraftingDemandType.Limit: return IconId.CTargetGlobal;
                case CraftingDemandType.Unlimited: return IconId.CTargetInfinity;
            }
            D.Err("No icon for demand type name: {0}", t);
            return IconId.CWarning;
        }
    }
}
