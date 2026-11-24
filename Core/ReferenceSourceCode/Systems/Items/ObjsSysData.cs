using System.Collections.Generic;
using Game.Systems.AI;
using MessagePack;

namespace Game.Systems.Items {
    [MessagePackObject]
    public class ObjsSysData {
        [Key("LastId")] public uint LastId;
        [Key("Objs")] public ObjDataData[] Objs;
        /// <summary>
        /// int1 = def id hash
        /// int2 = group id
        /// </summary>
        [Key("KnownStorables")] public (int, int)[] KnownStorables;
    }
}
