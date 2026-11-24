using MessagePack;

namespace Game.Systems.Story {
    [MessagePackObject]
    public struct StoryLogEntry {
        [Key("Hide")] public bool Hide;
        [Key("Icon")] public string Icon;
        [Key("Title")] public string Title;
        [Key("Desc")] public string Desc;
        [IgnoreMember] public bool IsValid => Icon != null && Title != null && Desc != null;
        public override string ToString() {
            return $"StoryLogEntry[H:{Hide},I:{Icon},T:{Title},DLen:{Desc?.Length ?? 0}]";
        }
    }
}
