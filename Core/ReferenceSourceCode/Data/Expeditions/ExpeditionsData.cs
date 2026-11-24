using System;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data.Space;
using KL.Utils;
using MessagePack;

namespace Game.Data {
    [MessagePackObject]
    public class ExpeditionsData {
        [Key("Expeditions")] public ExpeditionData[] Expeditions;
        [Key("Completed")] public int Completed;
        [Key("CrewExpeditionTimes")] public Dictionary<uint, long> CrewExpeditionTimes;
    }
}
