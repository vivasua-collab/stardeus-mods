using Game.Constants;
using Game.Data;
using Game.Systems.BodyParts;
using Game.Systems.Items;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class EntityWarning_Wounded : BaseBodyPartWarning {
        public const string WarningId = "Entity_Wounded";

        [SelfInit]
        private static void Register() {
            RegisterWarning(new EntityWarning_Wounded());
        }

        public override string Id => WarningId;
        public override int Order => 23;

        protected override bool Matches(ObjData obj) {
            var wounds = obj.BodyPartState.Wounds;
            if (Arrays.IsEmpty(wounds)) { return false; }
            for (int i = wounds.Count - 1; i >= 0; i--) {
                var w = wounds[i];
                if (w.Type != DamageType.Decay && w.Severity > Severity.Irrelevant) {
                    return true;
                }
            }
            return false;
        }

        public override void LoadContent(Lifetime life, WGroup group, ref int idx) {
            var worst = Severity.Irrelevant;
            int count = 0;
            var wounds = State.Wounds;
            for (int i = wounds.Count - 1; i >= 0; i--) {
                var w = wounds[i];
                if (w.Type == DamageType.Decay || w.Severity <= Severity.Irrelevant) { continue; }
                worst = Severities.Bigger(worst, w.Severity);
                count++;
            }
            var text2 = count > 1
                ? $"{Severities.NameFor(worst, false)} ({count})"
                : Severities.NameFor(worst, false);
            var warning = SpawnRow(life, group, ref idx,
                IconId.CBlood, IsBio ? T.CondInjured : T.Damaged, text2);
            UIUtils.AddTipFunc(life, warning, BuildTooltip);
        }

        private string BuildTooltip() {
            var sb = Caches.StringBuilder2;
            sb.Clear();
            var wounds = State.Wounds;
            bool first = true;
            for (int i = wounds.Count - 1; i >= 0; i--) {
                var w = wounds[i];
                if (w.Type == DamageType.Decay || w.Severity <= Severity.Irrelevant) { continue; }
                if (!first) {
                    sb.Append("\n\n");
                }
                first = false;
                sb.Append(Wound.TooltipFor(w, IsBio));
            }
            return sb.ToString();
        }
    }
}
