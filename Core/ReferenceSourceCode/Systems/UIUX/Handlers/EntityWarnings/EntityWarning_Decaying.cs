using Game.Constants;
using Game.Data;
using Game.Systems.BodyParts;
using Game.Systems.Conditions;
using Game.Systems.Items;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class EntityWarning_Decaying : BaseBodyPartWarning {
        public const string WarningId = "Entity_Decaying";

        [SelfInit]
        private static void Register() {
            RegisterWarning(new EntityWarning_Decaying());
        }

        public override string Id => WarningId;
        public override int Order => 24;

        protected override bool Matches(ObjData obj) {
            return ConditionDecay.VisibleDecay(obj.BodyPartState.Decay) > 0f;
        }

        public override void LoadContent(Lifetime life, WGroup group, ref int idx) {
            var warning = SpawnRow(life, group, ref idx, IconId.WDamage,
                IsBio ? T.ConditionRotting : T.ConditionCorroding,
                Units.Percentage(ConditionDecay.VisibleDecay(State.Decay)));
            UIUtils.AddTipFunc(life, warning, BuildTooltip);
        }

        private string BuildTooltip() {
            var wounds = State.Wounds;
            if (wounds == null) { return null; }
            for (int i = wounds.Count - 1; i >= 0; i--) {
                var w = wounds[i];
                if (w.Type == DamageType.Decay) {
                    return Wound.TooltipFor(w, IsBio);
                }
            }
            return null;
        }
    }
}
