using System.Collections.Generic;
using Game.Data.Planets;
using Game.Systems;
using KL.Utils;
using MessagePack;
using UnityEngine;

namespace Game.Data {
    [MessagePackObject]
    public class PlanetsSysData {
        [Key("Resources")] public PlanetResource[] PlanetResources;
        [Key("MineRaidAt")] public long NextMiningConsequenceRaidAt;

    }
}
