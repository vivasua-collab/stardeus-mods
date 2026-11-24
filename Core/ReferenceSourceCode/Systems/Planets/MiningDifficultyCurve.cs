using Game.CodeGen;
using Game.Data;
using KL.Utils;

namespace Game.Systems.Planets {
    public static class MiningDifficultyCurve {
        public const float MinMul = 0.05f;
        private static Curve curve;
        public static Curve Curve {
            get {
                if (curve == null) {
                    CurveDef.All.TryGetValue(CurveIdH.Mining_DepthRateMul_Difficulty, out curve);
                }
                return curve;
            }
        }

        public static float Evaluate(float difficulty) {
            var c = Curve;
            if (c == null) {
                return 1f;
            }
            var mul = c.Evaluate(difficulty);
            return mul < MinMul ? MinMul : mul;
        }
    }
}
