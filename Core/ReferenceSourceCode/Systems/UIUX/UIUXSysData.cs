using System.Collections.Generic;
using MessagePack;

namespace Game.Systems.UIUX {
    [MessagePackObject]
    public sealed class UIUXSysData {
        [Key("Pinned")] public PinnedSearchResultData[] Pinned;
        [Key("Notes")] public string Notes;
        [Key("FavMats")] public Dictionary<string, int> FavMats;
        [Key("NoteToSelf")] public string NoteToSelf;
        // Build menu group id -> picked variant def id
        [Key("BuildPicks")] public Dictionary<string, string> BuildPicks;
    }
}
