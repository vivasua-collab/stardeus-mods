using Unity.Mathematics;

namespace Game.Systems.RangeCheck {
    public struct RangeCheckScanner {
        public int Id;
        public float2 Position;
        public float RangeSqr;
        public float ErrorRadius;
    }
}
