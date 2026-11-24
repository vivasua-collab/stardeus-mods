using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class CPUBonusDeviceWarning : BaseEntityWarning {
        public const string WarningId = "Device_CPUBonus";

        [SelfInit]
        private static void Register() {
            RegisterWarning(new CPUBonusDeviceWarning());
        }

        public override string Id => WarningId;
        public override int Order => 40;
        private Tile tile;
        private IWithCPUBonus comp;

        public override bool ShowFor(GameState s, Tile tile, out bool blockActions) {
            blockActions = false;
            if (!tile.IsConstructed) { return false; }
            if (tile.ENode == null || !tile.ENode.Grid.IsInitialized) { return false; }
            if (!tile.TryGetComponent<IWithCPUBonus>(out var c)) { return false; }
            this.tile = tile;
            comp = c;
            return true;
        }

        public override void LoadContent(Lifetime life, WGroup group, ref int idx) {
            var warning = UISpawn.Widget<WIconText>(life, UIPrefabId.WContent2Text,
                group.Container, idx++);
            var bonus = comp.GetCPUBonusMultiplier(out var statName, out var finalValue);
            warning.Load(IconId.CCPU,
                tile.ENode.Grid.BonusTooltipFor(bonus, statName, finalValue));
        }

        public override void Unload() {
            tile = null;
            comp = null;
        }
    }
}
