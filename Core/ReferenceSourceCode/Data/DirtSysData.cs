using KL.Utils;
using MessagePack;
using UnityEngine;

namespace Game.Data {
    [MessagePackObject]
    public class DirtSysData {
        [Key("Dirt")]
        public DirtData[] Dirt;
    }
}
