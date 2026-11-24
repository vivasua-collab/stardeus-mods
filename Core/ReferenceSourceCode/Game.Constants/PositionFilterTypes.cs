using Game.CodeGen;

namespace Game.Constants {
    [GenerateHashes]
    public static class PositionFilterTypes {
        public const string CopterHover = "CopterHover";
        public const string DroneHover = "DroneHover";
        public const string RobotHeadJitter = "RobotHeadJitter";
        public const string InSpaceHover = "InSpaceHover";
    }
}
