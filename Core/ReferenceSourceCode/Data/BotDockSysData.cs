using System.Collections.Generic;
using Game.Systems;
using KL.Utils;
using MessagePack;
using UnityEngine;

namespace Game.Data {
    [MessagePackObject]
    public class BotDockSysData {
        [Key("DockAssignments")] public Dictionary<uint, int> DockAssignments;
    }
}
