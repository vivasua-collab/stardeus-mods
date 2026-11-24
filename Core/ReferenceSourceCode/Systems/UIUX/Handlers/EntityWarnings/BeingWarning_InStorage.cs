using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class BeingWarning_InStorage : BaseEntityWarning {
        public const string WarningId = "Being_InStorage";

        [SelfInit]
        private static void Register() {
            RegisterWarning(new BeingWarning_InStorage());
        }

        public override string Id => WarningId;
        public override int Order => 20;
        public override bool ShowFor(GameState s, BeingData being, out bool blockActions) {
            blockActions = false;
            if (being == null) {
                return false;
            }
            if (being.HasStateFlag(BeingState.Stored)) {
                return true;
            }
            return false;
        }

        public override void LoadContent(Lifetime life, WGroup group, ref int idx) {
            var warning = UISpawn.Widget<WIconText>(life,
                UIPrefabId.WContent2Text, group.Container, idx++);
            warning.Load(IconId.CStore, T.ObjectInStorage);
        }

        public override void Unload() {
        }
    }
}
