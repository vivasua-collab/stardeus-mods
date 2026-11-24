using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class BatteryTab : BaseEntityTab {
        public const string TabId = "Battery";

        [SelfInit]
        private static void Register() {
            RegisterTab(new BatteryTab());
        }

        private Tile tile;
        private BatteryComp battery;
        private GameState S;

        public override string Id => TabId;
        public override int Order => 10;
        public override string TabName => T.Battery;

        public override bool ShowFor(GameState s, Tile tile) {
            if (!tile.IsConstructed) { return false; }
            if (tile.ENode?.IsReachable == false) { return false; }
            if (!tile.TryGetComponent<BatteryComp>(out var bat)) { return false; }
            this.tile = tile;
            this.battery = bat;
            S = s;
            return true;
        }

        public override bool LoadContent(WGroup group) {
            battery.StateChangedB.Bind(lifetime, () => ReloadTab(), false);

            var block = UISpawn.Block(lifetime, group, 0);
            int idx = 0;

            var header = UISpawn.Widget<WProgressWithLabel>(lifetime,
                UIPrefabId.WContentHeaderProgress, block.Container, idx++);
            header.Label.Load(battery.StateText);
            var maxCharge = battery.BatteryMaxCharge;
            var charge = battery.BatteryCharge;
            header.Progress.SetPercent(maxCharge > 0 ? charge / maxCharge : 0f, false);
            header.Progress.SetText(Units.BatteryCharge(charge, maxCharge));

            var ttdRow = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContentText2, block.Container, idx++);
            ttdRow.Load(IconId.CTime, T.BatteryTtd, battery.TtdText);
            UIUtils.AddTip(lifetime, ttdRow, battery.TtdTooltip);

            var energyOutput = battery.EnergyOutput;
            var chargeRateRow = UISpawn.Widget<WIconText2Progress>(lifetime,
                UIPrefabId.WContentProgress, block.Container, idx++);
            var chargeRate = battery.CurChargeRate;
            chargeRateRow.Load(IconId.CEnergy, T.BatteryChargeRate,
                energyOutput > 0 ? chargeRate / energyOutput : 0f);
            chargeRateRow.Item.SetText2(Units.ElectricityIO(energyOutput, chargeRate));

            var dischargeRateRow = UISpawn.Widget<WIconText2Progress>(lifetime,
                UIPrefabId.WContentProgress, block.Container, idx++);
            var dischargeRate = battery.CurDischargeRate;
            dischargeRateRow.Load(IconId.CEnergy, T.BatteryDischargeRate,
                energyOutput > 0 ? dischargeRate / energyOutput : 0f);
            dischargeRateRow.Item.SetText2(Units.ElectricityIO(energyOutput, dischargeRate));

            return true;
        }

        public override bool LoadFooter(WGroup group) {
            var skipBtn = UISpawn.Widget<WToggleText>(lifetime,
                UIPrefabId.WContent4Toggle, group.Container, 0);
            skipBtn.Load(T.NotifyOnDischarge,
                !battery.SkipDischargeWarning,
                (v) => {
                    battery.SkipDischargeWarning = !battery.SkipDischargeWarning;
                });
            return true;
        }

        protected override void Cleanup() {
            tile = null;
            battery = null;
            S = null;
        }
    }
}
