using Game.Constants;
using Game.Data;
using Game.Systems.Items;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class EntityWarning_Destroyed : BaseBodyPartWarning {
        public const string WarningId = "Entity_Destroyed";

        [SelfInit]
        private static void Register() {
            RegisterWarning(new EntityWarning_Destroyed());
        }

        public override string Id => WarningId;
        public override int Order => 20;

        protected override bool Matches(ObjData obj) {
            return obj.BodyPartState.IsDestroyed;
        }

        public override void LoadContent(Lifetime life, WGroup group, ref int idx) {
            // The crutch, as in the part's name in lists and storage
            var warning = SpawnRow(life, group, ref idx, IconId.CCrutch, T.BodyPartDestroyed);
            UIUtils.AddTip(life, warning, T.BodyPartUnusable);
        }
    }
}
