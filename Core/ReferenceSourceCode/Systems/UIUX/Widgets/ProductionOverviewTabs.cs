using Game.Data;
using Game.Systems.BodyParts;
using Game.Systems.Items;

namespace Game.Systems.UIUX {
    // Which Production Overview tabs list a product. Materials also takes every product
    // that no other tab claims, so a craftable that is neither a material nor equipment
    // (C42, STA-1627) is never left out of the overview.
    public static class ProductionOverviewTabs {
        public const int Materials = 0;
        public const int Equipment = 1;
        public const int BodyParts = 2;
        public const int Beings = 3;

        public static bool Lists(int tab, Def def) {
            return tab switch {
                Materials => def.MatType != null || !IsClaimedByOtherTab(def),
                Equipment => InventoryItemDef.HasForDef(def.IdH),
                BodyParts => BodyPartDef.HasForDef(def.IdH),
                Beings => def.IsBeing,
                _ => true,
            };
        }

        private static bool IsClaimedByOtherTab(Def def) {
            return InventoryItemDef.HasForDef(def.IdH)
                || BodyPartDef.HasForDef(def.IdH)
                || def.IsBeing;
        }
    }
}
