using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class DeviceWarning_MustBeOutdoors : BaseEntityWarning {
        public const string WarningId = "Device_MustBeOutdoors";

        [SelfInit]
        private static void Register() {
            RegisterWarning(new DeviceWarning_MustBeOutdoors());
        }

        public override string Id => WarningId;
        public override int Order => 20;
        private Tile tile;

        public override bool ShowFor(GameState s, Tile tile, out bool blockActions) {
            blockActions = false;
            if (!tile.TryGetComponent<OutdoorsCheckComp>(out var comp)) { return false; }
            if (!comp.MustBeOutdoors) { return false; }
            if (comp.IsOutdoors) { return false; }
            this.tile = tile;
            return true;
        }

        public override void LoadContent(Lifetime life, WGroup group, ref int idx) {
            var warning = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button,
                group.Container, idx++);
            warning.Load(IconId.CWarning, T.MustBeOutdoors,
                () => A.S.Sig.ShowOverlay.Send(OverlayId.Areas));
            UIUtils.AddTip(life, warning, T.MustBeOutdoorsTip);
        }

        public override void Unload() {
            tile = null;
        }
    }
}
