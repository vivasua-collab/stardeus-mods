using System.Text;
using Game.Constants;
using Game.Systems.Stats;
using Game.Utils;

namespace Game.Systems.BodyParts {
    // The one formula for what a body part adds to a being's stat. The installed slot, the
    // part's own stats panel and its hover tooltip all use it, so their numbers can't drift apart.
    public static class BodyPartStatMath {
        public static float WithQuality(in StatSourceData src, float quality) {
            var qEff = BodyPartDef.QualityStatMulCurve.Evaluate(quality);
            return BodyPartDef.AdjustAmountWithStateMul(src.Amount, qEff, src.QualityMul);
        }

        // A source without a StateMul ignores the part's condition and usability entirely
        public static bool DependsOnState(in StatSourceData src) {
            return src.StateMul > 0f;
        }

        public static float WithCondition(in StatSourceData src, float amount, float condition) {
            if (!DependsOnState(src)) { return amount; }
            return BodyPartDef.AdjustAmountWithStateMul(amount, condition, src.StateMul);
        }

        // Explains the value a removed part shows (at its quality) and what its current
        // condition does to it once installed
        public static void AppendBreakdown(StringBuilder sb, Stat stat, in StatSourceData src,
                BodyPartState state) {
            sb.Append(T.StatValueBase_V1.F(stat.Display(src.Amount)));
            var atQuality = WithQuality(src, state.Quality);
            sb.Append('\n');
            sb.AppendFormat("{0} {1}: {2}", Stat.NameFor(stat),
                T.AtQuality_V1.F(Units.Percentage(state.Quality, false)),
                Texts.Accented(stat.Display(atQuality)));
            if (!DependsOnState(src)) { return; }
            if (!state.IsUsableSelf) {
                sb.Append('\n');
                sb.Append(Texts.Red(T.BodyPartUnusable));
                return;
            }
            if (state.Condition < 1f) {
                sb.Append('\n');
                sb.Append(T.BodyPartStatAtCondition_V3.F(Stat.NameFor(stat),
                    Units.Percentage(state.Condition),
                    Texts.Accented(stat.Display(WithCondition(src, atQuality, state.Condition)))));
            }
            if ((state.Flags & BodyPartStateFlags.Crippled) == BodyPartStateFlags.Crippled) {
                sb.Append('\n');
                sb.Append(Texts.Red(T.BodyPartCrippledTip_V1.F(
                    Units.Percentage(Tunables.CrippledPartMaxCondition))));
            }
        }
    }
}
