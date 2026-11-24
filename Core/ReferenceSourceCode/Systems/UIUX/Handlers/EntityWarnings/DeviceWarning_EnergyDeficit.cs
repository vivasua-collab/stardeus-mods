using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class DeviceWarning_EnergyDeficit : BaseEntityWarning {
        public const string WarningId = "Device_EnergyDeficit";

        [SelfInit]
        private static void Register() {
            RegisterWarning(new DeviceWarning_EnergyDeficit());
        }

        public override string Id => WarningId;
        public override int Order => 1;
        private Tile tile;

        public override bool ShowFor(GameState s, Tile tile, out bool blockActions) {
            blockActions = false;
            if (!tile.IsConstructed) { return false; }
            if (tile.ENode == null || !tile.ENode.HasDeficit) { return false; }
            this.tile = tile;
            return true;
        }

        public override void LoadContent(Lifetime life, WGroup group, ref int idx) {
            var warning = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button,
                group.Container, idx++);
            warning.Load(IconId.CNoEnergy, T.ElectricityDeficit, ShowQuickChoice);
        }

        private void ShowQuickChoice() {
            var node = tile?.ENode;
            if (node == null) { return; }
            var qc = QuickChoice.Create();
            if (node.IsSwitchable) {
                qc.AddButton(T.SwitchOff, () => {
                    if (node.TryGetGoal(out var goal)) {
                        AIGoal.Cancel(goal, G.Ticks);
                    }
                    A.S.UIQuery.ReloadEntityIfSelected.Ask(tile);
                });
            }
            qc.AddButton(T.OverlayTipElectricity, () => {
                A.S.UIQuery.ToggleElectricity.Ask(true);
            });
            A.S.UIQuery.ShowQuickChoice.Ask(qc);
        }

        public override void Unload() {
            tile = null;
        }
    }
}
