using System.Collections.Generic;
using KL.Utils;
using MessagePack;
using UnityEngine;

namespace Game.Data {
    [MessagePackObject]
    public class CodexSysData {
        [Key("LearnedConcepts")]
        public List<string> LearnedConcepts;
    }
}
