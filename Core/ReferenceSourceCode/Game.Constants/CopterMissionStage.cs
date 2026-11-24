using Game.Utils;
using Game.CodeGen;

namespace Game.Constants {
    [GenerateHashes]
    public static class CopterMissionStage {
        // Deployment algorithm
        // 0. Choose copter, miner, check validations, etc
        public const string Start = "Start";
        // 1. CargoCopter Tile is converted to Obj
        public const string CopterLiftoff = "CopterLiftoff";
        public const string CopterLeaveShip = "CopterLeaveShip";
        // 6. Copter obj gets removed from game map and added to starmap / starfield as SO
        // 7. Copter SO moves towards deployment site
        public const string CopterFlyToSite = "CopterFlyToSite";
        // 8. When site is reached, the deployment happens
        // 9. Copter SO tries to return to the ship, waits for ship if needed (handled by SOMotionHoming)
        // 10. When copter is near the ship, Copter SO is removed, Copter is added as Obj on game map
        public const string CopterFlyToShip = "CopterFlyToShip";
        // 12. Copter flies to the spot and installs itself back to Tile
        public const string CopterLandOnShip = "CopterLandOnShip";
        // 13. If Copter fails to land in the original position, it will try to find
        // a parking spot
        public const string CopterRandomPark = "CopterRandomPark";
        public const string Done = "Done";
        // If the stage gets broken due to some bug, this will attempt a recovery
        public const string Error = "Error";

        // Special stages
        public const string CopterFlyToMiner = "CopterFlyToMiner";
        public const string PickupMiner = "PickupMiner";
        public const string CopterAttachMiner = "CopterAttachMiner";
        public const string DeployMiner = "DeployMiner";
        public const string CollectResource = "CollectResource";
        public const string RetrieveMiner = "RetrieveMiner";
        // When copter returns from retrieval mission, it needs to spawn the faux sprite of a miner
        public const string SpawnMinerVisual = "SpawnMinerVisual";
        // This will try to place the miner in the last location where it was before
        public const string PutMinerOnShip = "PutMinerOnShip";
        // If the saved location cannot fit the miner, we will try this
        public const string PutMinerOnShipRandom = "PutMinerOnShipRandom";
    }
}
