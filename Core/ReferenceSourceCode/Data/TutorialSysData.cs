using System.Collections.Generic;
using MessagePack;

namespace Game.Data {
    [MessagePackObject]
    public sealed class TutorialSysData {
        [Key("PendingPopups")]
        public List<PendingTutorialPopup> PendingPopups;
        [Key("SeenConcepts")]
        public List<string> SeenConcepts;
    }
}
