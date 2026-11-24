using MessagePack;

namespace Game.Systems.Mental {
    /// <summary>
    /// One cause of a colonist's trust in the AI: a deed (timed) or a standing state that lasts
    /// as long as its cause (EndTick 0). Saved with the Mind; Trust turns the effects of one
    /// source into a single Trust (AIAlignment) modifier.
    /// </summary>
    [MessagePackObject]
    public sealed class TrustEffect {
        [Key("SourceId")] public int SourceId;
        // Percentage points of Trust
        [Key("Points")] public sbyte Points;
        [Key("StartTick")] public long StartTick;
        [Key("EndTick")] public long EndTick;
        // The key, not the text: a saved translation would stay in the old language
        [Key("ReasonKey")] public string ReasonKey;

        [IgnoreMember] public bool IsTimed => EndTick != 0;
    }
}
