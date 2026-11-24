using System;
using System.Linq;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Electricity;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class ElectricityTab_Batteries : BaseElectricityTab {
        public override string Name => T.EnergyBatteries;

        public override void LoadContent(Lifetime parentLifetime, GameState s,
                WGroup container, int gridIdx, ElectricityWidget widget) {
            var life = parentLifetime.CreateChild("Elec.Batteries");
            var sys = s.Sys.Electricity;
            if (!sys.TryGetGrid(gridIdx, out var grid)) { return; }

            var block = UISpawn.Block(life, container, 0);
            int idx = 0;
            int totalCount = 0;

            var groups = sys.NodeComponents.Values
                .Where(n => n.Node.GridId == gridIdx && n.Node.IsBattery)
                .GroupBy(n => n.Entity.Definition.Id)
                .OrderByDescending(g => g.Sum(n => n.IsProducing ? n.Node.Produced : n.Node.Consumed));

            foreach (var group in groups) {
                var groupNodes = group
                    .OrderByDescending(n => n.Node.Produced)
                    .ThenByDescending(n => n.Node.Consumed)
                    .ToList();
                var firstNode = groupNodes[0];
                var totalRate = groupNodes.Sum(n => n.IsProducing ? n.Node.Produced : n.Node.Consumed);
                string groupText2;
                if (grid.HasShipComputer) {
                    groupText2 = $"{Units.XNum(groupNodes.Count)} {Texts.GreyPipe} {Texts.WithColor(Units.Electricity(totalRate), Texts.TMPColorYellow)}";
                } else {
                    groupText2 = Units.XNum(groupNodes.Count);
                }

                var listBtn = UISpawn.Widget<WContentListButton>(life,
                    UIPrefabId.WContent2ListButton, block.Container, idx++);
                listBtn.Load(firstNode.Entity.Definition.Preview,
                    firstNode.Entity.Definition.NameT, groupText2);
                var capturedGroupNodes = groupNodes;
                var capturedListBtn = listBtn;
                listBtn.LoadOnToggle(life, (toggleLife, innerContainer, willShow, wasLoaded) => {
                    if (!willShow) { return wasLoaded; }
                    if (wasLoaded) { return true; }
                    int subIdx = 0;

                    var bulkBtnRef = new WButtonText[1];
                    Action bulkAction = () => {
                        var toSwitch = sys.NodeComponents.Values
                            .Where(n => n.Node.GridId == gridIdx &&
                                n.Entity.Definition.Id == firstNode.Entity.Definition.Id &&
                                n.Node.IsBattery && n.IsSwitchable && n.IsSwitchedOn)
                            .ToList();
                        foreach (var n in toSwitch) { n.Switch(false, false); }
                        capturedListBtn.Dispose();
                    };

                    foreach (var node in capturedGroupNodes) {
                        var capturedNode = node;
                        string rateText = null;
                        string tooltip = null;
                        if (grid.HasShipComputer) {
                            if (!node.Tile.TryGetComponent<BatteryComp>(out var bat)) {
                                continue;
                            }
                            var statText = T.BatteryChargeBar_V1.F(
                                Units.Percentage(bat.ChargePercent));
                            if (node.IsProducing) {
                                rateText = Texts.WithColor(Units.Electricity(node.Node.Produced),
                                    Texts.TMPColorYellow);
                                tooltip = $"{statText}\n{T.BatteryStateDischarging}";
                            } else if (node.IsConsuming) {
                                rateText = Texts.WithColor(Units.Electricity(node.Node.Consumed),
                                    Texts.TMPColorGreen);
                                tooltip = $"{statText}\n{T.BatteryStateCharging}";
                            } else {
                                rateText = Texts.WithColor(Units.Electricity(node.Node.Consumed),
                                    Texts.TMPColorGreen);
                                tooltip = $"{statText}\n{T.BatteryStateIdle}";
                            }
                        }
                        var w = UISpawn.Widget<WContentButton2Text2>(toggleLife,
                            UIPrefabId.WContent2Button2Text2, innerContainer, subIdx++);
                        w.Item.Load(node.Entity.Definition.Preview, node.Tile.NameOrLabel,
                            rateText, () => EntityUtils.SelectOrFocus(node.Tile));
                        if (tooltip != null) {
                            UIUtils.AddTip(toggleLife, w.Item, tooltip);
                        }
                        if (grid.HasShipComputer && node.IsSwitchable) {
                            var capturedW = w;
                            w.Button.Load(T.SwitchOff, () => {
                                capturedNode.Switch(false, false);
                                capturedW.Dispose();
                                var remaining = sys.NodeComponents.Values
                                    .Where(n => n.Node.GridId == gridIdx &&
                                        n.Entity.Definition.Id == firstNode.Entity.Definition.Id &&
                                        n.Node.IsBattery && n.IsSwitchedOn)
                                    .ToList();
                                var newRate = remaining.Sum(n => n.IsProducing ? n.Node.Produced : n.Node.Consumed);
                                if (remaining.Count == 0) {
                                    capturedListBtn.Dispose();
                                    return;
                                }
                                string newText2;
                                if (grid.HasShipComputer) {
                                    newText2 = $"{Units.XNum(remaining.Count)} {Texts.GreyPipe} {Texts.WithColor(Units.Electricity(newRate), Texts.TMPColorYellow)}";
                                } else {
                                    newText2 = Units.XNum(remaining.Count);
                                }
                                capturedListBtn.Load(firstNode.Entity.Definition.Preview,
                                    firstNode.Entity.Definition.NameT, newText2);
                                var switchableRemaining = remaining.Count(n => n.IsSwitchable);
                                if (switchableRemaining == 0) {
                                    if (bulkBtnRef[0] != null && !bulkBtnRef[0].IsDisposed) {
                                        bulkBtnRef[0].Dispose();
                                    }
                                } else if (bulkBtnRef[0] != null && !bulkBtnRef[0].IsDisposed) {
                                    bulkBtnRef[0].Load(Texts.Italic($"{T.SwitchOff}: x{switchableRemaining}"), bulkAction);
                                }
                            });
                        } else {
                            w.Button.gameObject.SetActiveFast(false);
                        }
                    }

                    var initialSwitchableCount = capturedGroupNodes.Count(n => n.IsSwitchable);
                    if (grid.HasShipComputer && initialSwitchableCount > 0) {
                        var bulkBtn = UISpawn.Widget<WButtonText>(toggleLife,
                            UIPrefabId.WContent2ButtonSimple, innerContainer, subIdx++);
                        bulkBtnRef[0] = bulkBtn;
                        bulkBtn.Load(Texts.Italic($"{T.SwitchOff}: x{initialSwitchableCount}"), bulkAction);
                    }

                    return true;
                }, expanded: false);
                totalCount += groupNodes.Count;
            }

            if (totalCount == 0) {
                var emptyW = UISpawn.Widget<WIconText2>(life,
                    UIPrefabId.WContent2Text2, block.Container, idx++);
                emptyW.Load(IconId.CCheck, T.Empty);
            }
        }
    }
}
