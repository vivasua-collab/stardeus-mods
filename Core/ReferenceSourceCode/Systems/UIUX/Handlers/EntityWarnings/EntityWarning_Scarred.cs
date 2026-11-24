using Game.Constants;
using Game.Data;
using Game.Systems.BodyParts;
using Game.Systems.Items;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    // Installed parts show scars as slot icons; a removed part shows them here, next to its
    // Crippled / Injured rows
    public sealed class EntityWarning_Scarred : BaseBodyPartWarning {
        public const string WarningId = "Entity_Scarred";

        [SelfInit]
        private static void Register() {
            RegisterWarning(new EntityWarning_Scarred());
        }

        public override string Id => WarningId;
        public override int Order => 25;

        protected override bool Matches(ObjData obj) {
            return !Arrays.IsEmpty(obj.BodyPartState.Scars);
        }

        public override void LoadContent(Lifetime life, WGroup group, ref int idx) {
            var scars = State.Scars;
            var worst = Severity.Irrelevant;
            for (int i = scars.Count - 1; i >= 0; i--) {
                worst = Severities.Bigger(worst, scars[i].Severity);
            }
            var severity = Severities.NameForNegative(worst);
            var title = scars.Count > 1 ? T.NounScars : T.NounScar;
            var text2 = scars.Count > 1 ? $"{severity} ({scars.Count})" : severity;
            var warning = SpawnRow(life, group, ref idx, IconId.CScar, title, text2);
            UIUtils.AddTipFunc(life, warning, BuildTooltip);
        }

        private string BuildTooltip() {
            var sb = Caches.StringBuilder2;
            sb.Clear();
            var scars = State.Scars;
            for (int i = 0; i < scars.Count; i++) {
                if (i > 0) {
                    sb.Append('\n');
                }
                sb.Append(Scar.TooltipFor(scars[i]));
            }
            return sb.ToString();
        }
    }
}
