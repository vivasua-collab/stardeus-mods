using MessagePack;

namespace Game.Data.Planets {
    [MessagePackObject]
    public struct PlanetResource {
        [IgnoreMember] public bool IsValid => PlanetId != 0;
        public const string Idx = "Idx";
        public const string MatType = "MatType";
        // same as SpaceObject id for this POI
        [Key("Id")] public int Id;
        // same as SpaceObject.ParentId for this POD
        [Key("PlanetId")] public int PlanetId;
        [Key("FactionId")] public int FactionIdH;
        // Resource ID hash
        [Key("ResourceId")] public int ResourceId;
        // Mining difficulty will determine mining speed
        // and may require certain equipment to mine past certain difficulty level
        [Key("MiningDiff")] public float MiningDifficulty;
        // Depth is only for ore deposits. When max depth is reached for auto / manual miners, can't mine anymore
        [Key("MiningDepth")] public float MiningDepth;
        // Total amount of resource mined at this deposit
        [Key("TotalMined")] public float TotalMined;
        // Unretrieved amount of resource that can be delivered home
        [Key("Unretrieved")] public float Unretrieved;
        // A limit of how much unretrieved material can accumulate until auto mining will stop
        [Key("UnretrievedMax")] public int UnretrievedMax;

        // If auto miner is installed, speed tells how fast the mining happens
        [Key("AutoMineSpeed")] public float AutoMineSpeed;
        [Key("AutoMineMaxDepth")] public float AutoMineMaxDepth;
        // If auto miner is installed, efficiency tells how effective the miner is at dealing with extraction difficulty
        [Key("AutoMineEfficiency")] public float AutoMineEfficiency;

        // When an expedition to this resource is ongoing, manual mine factor will be used
        [IgnoreMember] public float ManualMineFactor;
        // How deep can the manual mining go?
        [IgnoreMember] public float ManualMineMaxDepth;

        public override string ToString() {
            return $"POIResource[SOId:{Id},ParentId:{PlanetId},Diff:{MiningDifficulty}.Unretrieved/UMax/Total:{Unretrieved}/{UnretrievedMax}/{TotalMined}].AutoMineSpeed/Eff:{AutoMineSpeed}/{AutoMineEfficiency}";
        }
    }
}
