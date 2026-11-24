using System;
using System.Collections.Generic;
using Game.Systems.Electricity;
using KL.Utils;
using MessagePack;
using UnityEngine;

namespace Game.Data {
    [MessagePackObject]
    public class EnergySysData {
        [Key("StateHistory")]
        public List<EnergyStateSnapshot> StateHistory;
        // 720 is 1 day
        private const int histSize = 720;// * 3;
        public void Add(EnergyStateSnapshot snap) {
            StateHistory.Add(snap);
            while (StateHistory.Count > histSize) {
                StateHistory.RemoveAt(0);
            }
        }
        [Key("TotalMem")] public int TotalMem;
        [Key("TotalCPU")] public int TotalCPU;
        [Key("TotalDisk")] public int TotalDisk;
        [Key("TotalGPU")] public int TotalGPU;

        [Key("NodesCount")] public int NodesCount;
        [Key("ConnectionsCount")] public int ConnectionsCount;
        [Key("GridsCount")] public int GridsCount;
        [Key("NodesRecycler")] public int[] NodesRecycler;
        [Key("ConnectionsRecycler")] public int[] ConnectionsRecycler;
        [Key("Nodes")] public ElectricNode[] Nodes;
        [Key("Connections")] public ElectricConnection[] Connections;
        [Key("Grids")] public ElectricGrid[] Grids;
    }
}
