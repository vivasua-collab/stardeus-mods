using Game.CodeGen;
using Game.Constants;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Conditions {
    public sealed class ConditionRotting : ConditionDecay {
        [SelfInit]
        private static void Register() {
            RegisterCondition(new ConditionRotting());
        }

        protected override float QualityLossPerWeek => Tunables.RottingQualityLossPerWeek;
        protected override float ConditionLossPerWeek => Tunables.RottingConditionLossPerWeek;

        private ConditionRotting() : base(ConditionId.Rotting, IconId.WDamage) {
            nameTFunc = () => T.ConditionRotting;
            descTFunc = () => T.ConditionRottingDesc;
        }
    }
}
