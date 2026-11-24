using MessagePack;

namespace Game.Systems.Creatures {
    [MessagePackObject]
    public struct DockableConfig {
        [Key("DockId")] public int DockId;
        [Key("NoAutoDock")] public bool NoAutoDock;
        [Key("NoAutoAssign")] public bool NoAutoAssign;
        public bool IsAssignable(bool skipAssigned, bool auto) {
            if (skipAssigned && DockId != 0) {
                return false;
            }
            if (auto && NoAutoAssign) { return false; }
            return true;
        }
    }
}
