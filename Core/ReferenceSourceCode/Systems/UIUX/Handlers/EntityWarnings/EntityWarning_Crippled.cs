using Game.Constants;
using Game.Data;
using Game.Systems.BodyParts;
using Game.Systems.Items;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class EntityWarning_Crippled : BaseBodyPartWarning {
        public const string WarningId = "Entity_Crippled";

        [SelfInit]
        private static void Register() {
            RegisterWarning(new EntityWarning_Crippled());
        }

        public override string Id => WarningId;
        public override int Order => 21;

        protected override bool Matches(ObjData obj) {
            return (obj.BodyPartState.Flags & BodyPartStateFlags.Crippled) == BodyPartStateFlags.Crippled;
        }

        public override void LoadContent(Lifetime life, WGroup group, ref int idx) {
            var warning = SpawnRow(life, group, ref idx, IconId.CHeartBroken, T.BodyPartCrippled);
            UIUtils.AddTip(life, warning,
                T.BodyPartCrippledTip_V1.F(Units.Percentage(Tunables.CrippledPartMaxCondition)));
        }
    }
}
