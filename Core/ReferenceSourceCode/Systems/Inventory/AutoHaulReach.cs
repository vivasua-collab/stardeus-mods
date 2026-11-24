using KL.Grid;

namespace Game.Systems.Inventory {
    // Which loose piles and items a storage may order hauled on its own. Auto-hauling used
    // to take the closest acceptable thing anywhere, so bots flew off to piles far out in
    // space before the player asked for any of it. A storage now hauls what lies within
    // Tunables.AutoHaulIslandReach tiles of its own island: the ship and the space right
    // around it, including a neighbouring section or a derelict that close. Anything
    // further out waits for an explicit Store order. IslandReach keeps the check a lookup,
    // since every storage runs it for every candidate.
    public static class AutoHaulReach {
        public static bool CanAutoHaul(AreaSys areas, int storageIsland, int itemPos) {
            if (Pos.IsOOB(itemPos)) { return false; }
            return areas.Reach.IsWithin(storageIsland, itemPos);
        }
    }
}
