using Unity.Mathematics;

namespace Game.Systems.RangeCheck {
    public struct RangeCheckHit {
        public int TargetId;
        public int ScannerId;
        public float2 TargetPosition;
        public float2 ScannerPosition;
        public float ErrorRadius;
        public float Priority;
        public float SqrDist => math.distancesq(ScannerPosition, TargetPosition);

        public override string ToString() {
            return $"RangeCheckHit[{TargetId}@{TargetPosition}<{ScannerId}@{ScannerPosition}]";
        }
    }
}
