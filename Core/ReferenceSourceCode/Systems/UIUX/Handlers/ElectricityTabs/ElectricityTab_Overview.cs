using System.Linq;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class ElectricityTab_Overview : BaseElectricityTab {
        public override string Name => T.EnergyOverview;
        private float peakConsumption;

        public override void LoadContent(Lifetime parentLifetime, GameState s,
                WGroup container, int gridIdx, ElectricityWidget widget) {
            var life = parentLifetime.CreateChild("Elec.Overview");
            var sys = s.Sys.Electricity;
            if (!sys.TryGetGrid(gridIdx, out var grid)) { return; }

            var block = UISpawn.Block(life, container, 0);
            int idx = 0;

            // Current Demand
            var iconProd = grid.Deficit > 0.000001f ? IconId.CWarning : IconId.CCheck;
            var curDemand = UISpawn.Widget<WIconText2>(life,
                UIPrefabId.WContentText2, block.Container, idx++);
            var demand = grid.Consumption + grid.Deficit;
            curDemand.Load(iconProd, T.EnergyCurDemand,
                Units.Electricity(demand));
            UIUtils.AddTip(life, curDemand, T.EnergyCurDemandDesc);

            // Peak Demand (with reset button)
            var iconPeak = grid.MaxOutput * 0.85f > grid.Output
                ? IconId.CCheck : IconId.CWarning;
            var peakDemand = UISpawn.Widget<WButtonIconText2>(life,
                UIPrefabId.WContentTextButton, block.Container, idx++);

            if (peakConsumption < demand) {
                peakConsumption = demand;
            }
            peakDemand.Load(iconPeak, T.EnergyPeakDemand,
                Units.Electricity(peakConsumption), () => {
                    peakConsumption = 0;
                    peakDemand.SetText2(Texts.Grey("-"));
                });
            UIUtils.AddTip(life, peakDemand, $"{T.Max}: {T.EnergyCurDemand} + {T.EnergyCurDeficit}");

            // Max Output
            var maxOut = UISpawn.Widget<WIconText2>(life,
                UIPrefabId.WContentText2, block.Container, idx++);
            maxOut.Load(iconProd, T.EnergyMaxOutput,
                Units.Electricity(grid.MaxOutputWithoutBat));
            UIUtils.AddTip(life, maxOut, T.EnergyMaxOutputDesc);

            // Renewable Output
            var renewOut = UISpawn.Widget<WIconText2>(life,
                UIPrefabId.WContentText2, block.Container, idx++);
            renewOut.Load(IconId.CLeaf, T.EnergyRenewable,
                Units.Electricity(grid.OutputRenewable));
            UIUtils.AddTip(life, renewOut, T.EnergyRenewableDesc);

            // Current Deficit
            var curDeficit = UISpawn.Widget<WIconText2>(life,
                UIPrefabId.WContentText2, block.Container, idx++);
            curDeficit.Load(iconProd, T.EnergyCurDeficit,
                Units.Electricity(grid.Deficit));
            UIUtils.AddTip(life, curDeficit, T.EnergyCurDeficitDesc);

            // Battery info (if applicable)
            WIconText2 maxBatOut = null;
            WIconText2 batCharge = null;
            if (grid.MaxOutputBat > 0f) {
                var iconBat = grid.BatCharge > 0 ? IconId.CCheck : IconId.CWarning;
                maxBatOut = UISpawn.Widget<WIconText2>(life,
                    UIPrefabId.WContentText2, block.Container, idx++);
                maxBatOut.Load(iconBat, T.EnergyMaxBatOutput,
                    Units.Electricity(grid.MaxOutputBat));
                UIUtils.AddTip(life, maxBatOut, T.EnergyMaxBatOutputDesc);

                var iconBatCharge = grid.BatCharge < grid.OutputBat * 0.5f
                    ? IconId.CWarning : IconId.CCheck;
                batCharge = UISpawn.Widget<WIconText2>(life,
                    UIPrefabId.WContentText2, block.Container, idx++);
                batCharge.Load(iconBatCharge, T.EnergyBatCharge,
                    Units.Electricity(grid.BatCharge / grid.BatCapacity * grid.MaxOutputBat));
                UIUtils.AddTip(life, batCharge, T.EnergyBatChargeDesc);
            }

            // Bind all electricity values to tick updates so they stay current
            widget.Ctx.TickB.Bind(life, (_) => {
                if (!sys.TryGetGrid(gridIdx, out var g)) { return; }
                var demand = g.Consumption + g.Deficit;
                var defIcon = g.Deficit > 0.000001f ? IconId.CWarning : IconId.CCheck;
                curDemand.SetImage(defIcon);
                curDemand.SetText2(Units.Electricity(demand));
                var pkIcon = g.MaxOutput * 0.85f > g.Output ? IconId.CCheck : IconId.CWarning;
                if (peakConsumption < demand) {
                    peakConsumption = demand;
                }
                peakDemand.SetImage(pkIcon);
                peakDemand.SetText2(Units.Electricity(peakConsumption));
                maxOut.SetImage(defIcon);
                maxOut.SetText2(Units.Electricity(g.MaxOutputWithoutBat));
                renewOut.SetText2(Units.Electricity(g.OutputRenewable));
                curDeficit.SetImage(defIcon);
                curDeficit.SetText2(Units.Electricity(g.Deficit));
                if (maxBatOut != null) {
                    maxBatOut.SetImage(g.BatCharge > 0 ? IconId.CCheck : IconId.CWarning);
                    maxBatOut.SetText2(Units.Electricity(g.MaxOutputBat));
                }
                if (batCharge != null) {
                    batCharge.SetImage(g.BatCharge < g.OutputBat * 0.5f ? IconId.CWarning : IconId.CCheck);
                    batCharge.SetText2(Units.Electricity(g.BatCharge / g.BatCapacity * g.MaxOutputBat));
                }
            }, notifyImmediately: false);

        }
    }
}
