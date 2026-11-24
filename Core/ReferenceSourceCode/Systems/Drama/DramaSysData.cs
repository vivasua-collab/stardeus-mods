using System.Collections.Generic;
using Game.Systems.Stats;
using MessagePack;

namespace Game.Systems.Drama {
    [MessagePackObject]
    public sealed class DramaSysData {
        [Key("Interactions")] public SocialInteractionData[] Interactions;
        [Key("Memory")] public DramaMemoryData Memory;
    }
}
