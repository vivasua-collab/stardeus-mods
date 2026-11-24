using System.Collections.Generic;
using Game.Systems.AI;
using Game.Systems.Drama;
using Game.Systems.Mental;
using Game.Systems.Creatures.Surgeries;
using MessagePack;

namespace Game.Systems.Creatures {
    [MessagePackObject]
    public class BeingsSysData {
        [Key("LastId")] public uint LastId;
        [Key("LastMindId")] public uint LastMindId;
        [Key("Beings")] public BeingDataData[] Beings;
        /// <summary>
        /// List because of legacy migrations, easier to append
        /// </summary>
        [Key("Minds")] public List<MindData> Minds;
        [Key("Bonds")] public List<BeingBondsData> Bonds;
        [Key("SurgicalQueue")] public SurgicalQueueData SurgicalQueue;
    }
}
