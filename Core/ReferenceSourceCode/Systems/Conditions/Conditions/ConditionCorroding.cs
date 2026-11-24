using Game.CodeGen;
using Game.Constants;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Conditions {
    public sealed class ConditionCorroding : ConditionDecay {
        [SelfInit]
        private static void Register() {
            RegisterCondition(new ConditionCorroding());
        }

        protected override float QualityLossPerWeek => Tunables.CorrodingQualityLossPerWeek;
        protected override float ConditionLossPerWeek => Tunables.CorrodingConditionLossPerWeek;

        private ConditionCorroding() : base(ConditionId.Corroding, IconId.WDamage) {
            nameTFunc = () => T.ConditionCorroding;
            descTFunc = () => T.ConditionCorrodingDesc;
        }
    }
}
