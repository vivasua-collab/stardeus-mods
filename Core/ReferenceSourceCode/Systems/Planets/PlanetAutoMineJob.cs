using Game.Data.Planets;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using KL.Utils;
using Game.CodeGen;

namespace Game.Systems.Planets {
    [BurstCompile]
    public struct PlanetAutoMineJob : IJob {
        public NativeArray<PlanetResource> Resources;
        public NativeList<int> MinedOwnedResources;
        // Flattened Mining/DepthRateMul_Difficulty curve points (x,y pairs, ascending x),
        // already clamped to MiningDifficultyCurve.MinMul. Empty array means no curve,
        // depth rate multiplier falls back to 1.
        [ReadOnly]
        public NativeArray<float> DepthRateMulCurve;
        public int Count;
        public bool MarkMinedOwnedResources;
        public float BaseAutoMiningSpeed;
        //public float BaseAutoMiningEfficiency;
        public float AutoMiningDifficultyIncrease;
        public float BaseManualMiningSpeed;
        public float ManualMiningDifficultyIncrease;
        public float DepthIncreaseRate;
        public float MiningOutputMul;
        public void Execute() {
            for (int i = 0; i < Count; i++) {
                ref var res = ref Resources.GetUnsafeRef(i);
                if (!res.IsValid) { continue; }
                var isAuto = res.AutoMineEfficiency > 0f;
                var isManual = res.ManualMineFactor > 0f;
                if ((isAuto || isManual) && (res.UnretrievedMax == 0 || (
                            res.UnretrievedMax > res.Unretrieved))) {
                    var diff = res.MiningDifficulty;
                    if (diff < 1f) {
                        diff = 1f;
                    }
                    // Same depth-burn multiplier for both branches this tick, evaluated
                    // before either branch's MiningDifficulty increment, so an auto miner
                    // and an expedition on the same deposit burn depth at the same rate.
                    var depthMul = res.MiningDepth >= 0f
                        ? EvaluateDepthRateMul(DepthRateMulCurve, res.MiningDifficulty)
                        : 1f;
                    if (res.AutoMineSpeed > 0f && res.AutoMineMaxDepth > res.MiningDepth) {
                        var rate = BaseAutoMiningSpeed * res.AutoMineSpeed;
                        var mined = rate / diff;
                        res.Unretrieved += mined;
                        res.TotalMined += mined;
                        res.MiningDifficulty += mined * AutoMiningDifficultyIncrease;
                        if (res.MiningDepth >= 0f) {
                            res.MiningDepth += mined * DepthIncreaseRate * depthMul;
                        }
                    }
                    if (res.ManualMineFactor > 0f && res.ManualMineMaxDepth > res.MiningDepth) {
                        var rate = BaseManualMiningSpeed * res.ManualMineFactor;
                        var mined = rate / diff;
                        var minedOutput = mined * MiningOutputMul;
                        res.Unretrieved += minedOutput;
                        res.TotalMined += minedOutput;
                        res.MiningDifficulty += mined * ManualMiningDifficultyIncrease;
                        if (res.MiningDepth >= 0f) {
                            res.MiningDepth += mined * DepthIncreaseRate * depthMul;
                        }
                    }
                    if (MarkMinedOwnedResources && res.FactionIdH != 0
                            && res.FactionIdH != FactionIdH.Player) {
                        MinedOwnedResources.Add(res.Id);
                    }
                }
                Resources[i] = res;
            }
        }

        // Burst-compatible linear evaluation mirroring KL.Utils.Curve.Evaluate semantics
        private static float EvaluateDepthRateMul(in NativeArray<float> points, float x) {
            if (points.Length < 2) { return 1f; }
            if (x <= points[0]) { return points[1]; }
            var last = points.Length - 2;
            if (x >= points[last]) { return points[last + 1]; }
            for (int i = 0; i < last; i += 2) {
                var x1 = points[i];
                var x2 = points[i + 2];
                if (x <= x2) {
                    if (x2 <= x1) { return points[i + 1]; }
                    var t = (x - x1) / (x2 - x1);
                    return math.lerp(points[i + 1], points[i + 3], t);
                }
            }
            return points[last + 1];
        }
    }
}
