using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class BeingWarning_BeyondRepair : BaseEntityWarning {
        public const string WarningId = "Being_BeyondRepair";

        [SelfInit]
        private static void Register() {
            RegisterWarning(new BeingWarning_BeyondRepair());
        }

        private BeingData being;
        public override string Id => WarningId;
        public override int Order => 21;
        public override bool ShowFor(GameState s, BeingData being, out bool blockActions) {
            blockActions = false;
            if (being == null) {
                return false;
            }
            if (being.Body.IsBeyondRepair) {
                this.being = being;
                return true;
            }
            return false;
        }

        public override void LoadContent(Lifetime life, WGroup group, ref int idx) {
            var warning = UISpawn.Widget<WIconText>(life,
                UIPrefabId.WContent2Text, group.Container, idx++);
            // T.BodyBeyondRepair not yet generated from English.csv - raw key until codegen runs
            warning.Load(IconId.CCrutch, "body.beyond.repair".T());
            UIUtils.AddTipFunc(life, warning, () => {
                var slot = being.Body.FirstBeyondRepairSlot;
                var slotName = slot != null ? slot.NameT : T.Unknown;
                return "body.beyond.repair.desc".T().F(slotName);
            });
        }

        public override void Unload() {
            being = null;
        }
    }
}
