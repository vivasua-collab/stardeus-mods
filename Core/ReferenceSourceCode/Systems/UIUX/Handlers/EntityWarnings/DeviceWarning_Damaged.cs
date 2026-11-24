using System;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class DeviceWarning_Damaged : BaseEntityWarning {
        public const string WarningId = "Device_Damaged";

        [SelfInit]
        private static void Register() {
            RegisterWarning(new DeviceWarning_Damaged());
        }
        public override string Id => WarningId;
        public override int Order => 10;
        private Tile tile;

        public override bool ShowFor(GameState s, Tile tile, out bool blockActions) {
            blockActions = false;
            if (!tile.IsConstructed) { return false; }
            if (!tile.Health.IsDamaged) { return false; }
            this.tile = tile;
            blockActions = tile.Health.IsHeavilyDamaged;
            return true;
        }

        public override void LoadContent(Lifetime life, WGroup group, ref int idx) {
            var warning = UISpawn.Widget<WButtonIconText2Progress>(life,
                    UIPrefabId.WContent2ProgressButton,
                group.Container, idx++);
            warning.Load(IconId.WDamage, null, 0f, ShowQuickChoice, null);
            tile.Health.HealthPercentB.Bind(life, (h) => {
                if (h >= 1f) {
                    A.S.UIQuery.ReloadCurrentEntity.Ask();
                    A.S.UIQuery.RebuildEntityActions.Ask();
                    return;
                }
                warning.SetPercent(h, false);
                warning.Item.SetText(tile.Health.IsHeavilyDamaged ? T.ElDeviceHeavilyDamaged : T.Damaged);
                warning.Item.SetText2(Units.XOutOfY(tile.Health.Health, tile.Health.MaxHealth));
            });
        }

        private void ShowQuickChoice() {
            if (!tile.Health.TryGetGoal(out var goal)) {
                var qc = QuickChoice.Create();
                qc.AddButton(T.OrderRepair, () => tile.Health.CreateRepairGoal());
                A.S.UIQuery.ShowQuickChoice.Ask(qc);
            } else {
                var qc = QuickChoice.Create(T.Repairing);
                qc.AddButton($"{T.CancelTask}: {goal.TaskLine}", () => AIGoal.Cancel(goal, G.Ticks));
                A.S.UIQuery.ShowQuickChoice.Ask(qc);
            }
        }


        public override void Unload() {
            tile = null;
        }
    }
}
