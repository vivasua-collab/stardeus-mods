using System.Collections.Generic;
using MessagePack;

namespace Game.Systems.Naming {
    [MessagePackObject]
    public sealed class NamingSysData {
        [Key("UsedNames")] public Dictionary<int, HashSet<int>> UsedNames;
    }
}
