using System.Collections.Generic;
using Game.Systems;
using Game.Systems.Sleep;
using KL.Utils;
using MessagePack;
using UnityEngine;

namespace Game.Data {
    [MessagePackObject]
    public class SleepSysData {
        [Key("Assignments")] public Dictionary<uint, BedAssignment> Assignments;
    }
}
