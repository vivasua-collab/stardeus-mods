using System.Collections.Generic;
using MessagePack;

namespace Game.Systems.Minigames {
    [MessagePackObject]
    public sealed class MinigameSysData {
        [Key("HighScores")] public Dictionary<string, int> HighScores;
    }
}
