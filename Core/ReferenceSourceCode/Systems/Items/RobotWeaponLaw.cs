using Game.Data;
using Game.Data.Factions;
using Game.Systems.Creatures;

namespace Game.Systems.Items {
    /// <summary>
    /// Robots may not wield the heaviest weapons: Asimov's laws and interstellar law
    /// enforcement forbid it, a robot frame could hold them just fine. The player's Asimov
    /// Override lifts the law, and factions that ignore it (Virus) never followed it.
    /// The weapons say which they are with RequiresBodyFlags Humanoid
    /// (InventoryItemDef.IsIllegalFor).
    /// </summary>
    public static class RobotWeaponLaw {
        public static bool Exempts(FactionDef faction) {
            if (faction == null) { return false; }
            if (faction.IgnoresAsimovLaws) { return true; }
            // no game state (or no blackboard yet) in the main menu codex
            return faction.IsPlayer && A.S?.BB != null && A.HasAsimovOverride;
        }

        /// <summary>
        /// Drops what the being may no longer hold next to it, e.g. after a Virus robot
        /// joined the colony or the Asimov Override was lost. Beings that are not on the
        /// map keep it until the load check catches them back on it.
        /// </summary>
        public static void DropIllegal(BeingData being) {
            var inventory = being?.Inventory;
            if (inventory == null) { return; }
            if (being.Being == null || being.HasStateFlag(BeingState.Dead)) { return; }
            foreach (var slot in inventory.Equipment.Values) {
                var item = slot.Item;
                if (item == null) { continue; }
                if (item.IsAllowedFor(being.Species, being.EffectiveFaction)) { continue; }
                slot.RemoveItem();
            }
        }

        public static void DropIllegalAll(GameState S) {
            foreach (var being in S.Query.AllBeingData.Ask().Values) {
                DropIllegal(being);
            }
        }
    }
}
