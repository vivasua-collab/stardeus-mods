using Game.Data;

namespace Game.Components {
    public static class UnconfiguredWarning {
        // An unconfigured device that runs on electricity is not news until it could run: off
        // the ship grid or switched off, the electric node's own icon is the warning the player
        // needs, and most config tabs are hidden until the node is reachable anyway
        public static bool IsApplicable(Tile tile) {
            var enode = tile.ENode;
            return enode == null || (enode.IsPowerable && enode.IsReachable);
        }
    }
}
