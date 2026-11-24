using MessagePack;

namespace Game.Systems.Story {
    [MessagePackObject]
    public sealed class StorySysData {
        [Key("LastEventDataId")] public uint LastEventDataId;
        [Key("GenId")] public string GenId;
        [Key("StoryCtx")] public StoryContextData Context;
    }
}
