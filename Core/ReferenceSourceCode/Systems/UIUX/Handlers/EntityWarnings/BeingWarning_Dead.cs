using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class BeingWarning_Dead : BaseEntityWarning {
        public const string WarningId = "Being_Dead";

        [SelfInit]
        private static void Register() {
            RegisterWarning(new BeingWarning_Dead());
        }

        private BeingData being;
        public override string Id => WarningId;
        public override int Order => 20;
        public override bool ShowFor(GameState s, BeingData being, out bool blockActions) {
            blockActions = false;
            if (being == null) {
                return false;
            }
            if (being.HasStateFlag(BeingState.Dead)) {
                this.being = being;
                return true;
            }
            return false;
        }

        public override void LoadContent(Lifetime life, WGroup group, ref int idx) {
            var warning = UISpawn.Widget<WIconText>(life,
                UIPrefabId.WContent2Text, group.Container, idx++);
            warning.Load(IconId.CSkull, T.Dead);
            UIUtils.AddTipFunc(life, warning, () => {
                var sb = Caches.StringBuilder2;
                sb.Clear();
                BeingUtils.AddCauseOfDeath(being.Body, sb);
                return sb.ToString();
            });
        }

        public override void Unload() {
            being = null;
        }
    }
}
