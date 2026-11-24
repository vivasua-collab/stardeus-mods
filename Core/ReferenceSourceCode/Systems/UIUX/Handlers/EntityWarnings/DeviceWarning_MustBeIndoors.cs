using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class DeviceWarning_MustBeIndoors : BaseEntityWarning {
        public const string WarningId = "Device_MustBeIndoors";

        [SelfInit]
        private static void Register() {
            RegisterWarning(new DeviceWarning_MustBeIndoors());
        }

        public override string Id => WarningId;
        public override int Order => 21;
        private Tile tile;

        public override bool ShowFor(GameState s, Tile tile, out bool blockActions) {
            blockActions = false;
            if (!tile.TryGetComponent<OutdoorsCheckComp>(out var comp)) { return false; }
            if (comp.MustBeOutdoors) { return false; }
            if (!comp.IsOutdoors) { return false; }
            this.tile = tile;
            return true;
        }

        public override void LoadContent(Lifetime life, WGroup group, ref int idx) {
            var warning = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button,
                group.Container, idx++);
            warning.Load(IconId.CWarning, T.MustBeIndoors,
                () => A.S.Sig.ShowOverlay.Send(OverlayId.Areas));
            UIUtils.AddTip(life, warning, T.MustBeIndoorsTip);
        }

        public override void Unload() {
            tile = null;
        }
    }
}
