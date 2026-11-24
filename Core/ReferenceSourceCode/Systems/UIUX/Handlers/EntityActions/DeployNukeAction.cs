using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class DeployNukeAction : BaseEntityAction {
        public const string ActionId = "DeployNuke";

        [SelfInit]
        private static void Register() {
            RegisterAction(new DeployNukeAction());
        }

        public override string Id => ActionId;

        protected override bool ApplyFor(GameState s, Tile tile,
                out EntityActionData data) {
            tile = EntityUtils.ParentObj(tile);
            if (!tile.IsConstructed
                    || !tile.TryGetComponent<SectorNukeComp>(out var nuke)) {
                data = EntityActionData.Unavailable;
                return false;
            }
            var applicable = !A.IsInHyperspace && !tile.Health.IsHeavilyDamaged;
            data = new EntityActionData(this, (l, p, i) => {
                var btn = SpawnButton(l, applicable, p, i);
                btn.Load(IconId.CWarningRadioactive, T.Deploy,
                    () => {
                        nuke.ExecuteDeploy();
                        UIUtils.RefreshSelectedEntityUI();
                    });
                return btn;
            });
            return true;
        }
    }
}
