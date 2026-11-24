using MessagePack;

namespace Game.Data {
    [MessagePackObject]
    public struct LogEntry {
        public const byte TypeContextFree = 0;
        public const byte TypeBeing = 1;
        public const byte TypeTile = 2;
        public const byte TypeObj = 3;

        [Key(0)] public long Tick;
        [Key(1)] public string Text;
        [Key(2)] public byte EntityType;
        [Key(3)] public uint EntityId;
        // Shown only in its entity's log tab, never in the combined log: either a copy of an
        // entry already logged on another entity (a worker's task failure copied onto the
        // device), or detail that would flood the combined log (a failure on every blueprint
        // that lacks materials)
        [Key(4)] public bool EntityOnly;
    }
}
