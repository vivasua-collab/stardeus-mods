using MessagePack;

namespace Game.Data {
    [MessagePackObject]
    public sealed class PendingTutorialPopup {
        // CodexConceptPage page id of the tutorial concept to pop up
        [Key("Id")]
        public string ConceptId;
        // realtime seconds left before showing the popup
        [Key("T")]
        public float Remaining;
        // runtime-only: true while the toast itself is on screen
        [IgnoreMember]
        public bool Shown;
        // runtime-only: Hashes.S(ConceptId) for zero-alloc gating in ProcessPopupQueue
        [IgnoreMember]
        public int ConceptIdH;
    }
}
