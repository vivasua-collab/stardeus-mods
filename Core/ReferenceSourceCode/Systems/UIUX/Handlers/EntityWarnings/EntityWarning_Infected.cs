using Game.Constants;
using Game.Data;
using Game.Systems.Items;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class EntityWarning_Infected : BaseBodyPartWarning {
        public const string WarningId = "Entity_Infected";

        [SelfInit]
        private static void Register() {
            RegisterWarning(new EntityWarning_Infected());
        }

        public override string Id => WarningId;
        public override int Order => 22;

        protected override bool Matches(ObjData obj) {
            return obj.BodyPartState.IsInfected;
        }

        public override void LoadContent(Lifetime life, WGroup group, ref int idx) {
            SpawnRow(life, group, ref idx, IconId.CPoison, T.AdjInfected);
        }
    }
}
