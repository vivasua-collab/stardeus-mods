using Game.Systems.Story;
using MessagePack;

namespace Game.Data {
    [MessagePackObject]
    public sealed class PendingSpaceBodySysData {
        [Key("Bodies")] public PendingSpaceBody[] Bodies;
    }
}
