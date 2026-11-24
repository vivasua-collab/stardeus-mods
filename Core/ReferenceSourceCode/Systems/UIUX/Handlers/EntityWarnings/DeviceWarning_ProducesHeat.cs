using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class DeviceWarning_ProducesHeat : BaseEntityWarning {
        public const string WarningId = "Device_ProducesHeat";

        [SelfInit]
        private static void Register() {
            RegisterWarning(new DeviceWarning_ProducesHeat());
        }

        public override string Id => WarningId;
        public override int Order => 30;
        private Tile tile;

        public override bool ShowFor(GameState s, Tile tile, out bool blockActions) {
            blockActions = false;
            if (!tile.IsConstructed) { return false; }
            if (!tile.TryGetComponent<HeatProducerComp>(out var heat)) { return false; }
            if (!heat.IsProducingHeat) { return false; }
            if (!s.Sys.Areas.IsEnclosed(tile.CenterIdx)) { return false; }
            this.tile = tile;
            return true;
        }

        public override void LoadContent(Lifetime life, WGroup group, ref int idx) {
            var warning = UISpawn.Widget<WIconText>(life, UIPrefabId.WContent2Text,
                group.Container, idx++);
            warning.Load(IconId.CTemperature, T.HeatProducer);
            UIUtils.AddTip(life, warning, T.HeatProducerTip);
        }

        public override void Unload() {
            tile = null;
        }
    }
}
