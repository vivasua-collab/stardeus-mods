using System.Collections.Generic;
using MessagePack;

namespace Game.Data.Economy {
    [MessagePackObject]
    public sealed class MarketPosition {
        [Key("Id")] public string Id;
        [IgnoreMember] public int IdH;
        [Key("Position")] public int Position;
        [Key("Cost")] public float Cost; // of one
        [Key("Tick")] public long Tick;
    }
}
