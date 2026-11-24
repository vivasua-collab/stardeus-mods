using System;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Electricity;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class ElectricityTab_SwitchedOff : BaseElectricityTab {
        public override string Name => T.Off;

        public override void LoadContent(Lifetime parentLifetime, GameState s,
                WGroup container, int gridIdx, ElectricityWidget widget) {
            var life = parentLifetime.CreateChild("Elec.SwitchedOff");
            var sys = s.Sys.Electricity;
            if (!sys.TryGetGrid(gridIdx, out var grid)) { return; }

            var block = UISpawn.Block(life, container, 0);
            int idx = 0;
            int totalCount = 0;

            var groups = sys.NodeComponents.Values
                .Where(n => n.Node.GridId == gridIdx && !n.Node.IsOn && n.IsSwitchable)
                .GroupBy(n => n.Entity.Definition.Id)
                .OrderBy(g => g.First().Entity.Definition.NameT);

            foreach (var group in groups) {
                var groupNodes = group.ToList();
                var firstNode = groupNodes[0];

                var listBtn = UISpawn.Widget<WContentListButton>(life,
                    UIPrefabId.WContent2ListButton, block.Container, idx++);
                listBtn.Load(firstNode.Entity.Definition.Preview,
                    firstNode.Entity.Definition.NameT, Units.XNum(groupNodes.Count));
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
                                !n.Node.IsOn && n.IsSwitchable)
                            .ToList();
                        foreach (var n in toSwitch) { n.Switch(true, false); }
                        capturedListBtn.Dispose();
                    };

                    foreach (var node in capturedGroupNodes) {
                        var capturedNode = node;
                        var w = UISpawn.Widget<WContentButton2Text2>(toggleLife,
                            UIPrefabId.WContent2Button2Text2, innerContainer, subIdx++);
                        w.Item.Load(node.Entity.Definition.Preview, node.Tile.NameOrLabel,
                            null, () => EntityUtils.SelectOrFocus(node.Tile));
                        var capturedW = w;
                        w.Button.Load(T.SwitchOn, () => {
                            capturedNode.Switch(true, false);
                            capturedW.Dispose();
                            var remaining = sys.NodeComponents.Values
                                .Where(n => n.Node.GridId == gridIdx &&
                                    n.Entity.Definition.Id == firstNode.Entity.Definition.Id &&
                                    !n.Node.IsOn && n.IsSwitchable)
                                .ToList();
                            if (remaining.Count == 0) {
                                capturedListBtn.Dispose();
                                return;
                            }
                            capturedListBtn.Load(firstNode.Entity.Definition.Preview,
                                firstNode.Entity.Definition.NameT, Units.XNum(remaining.Count));
                            if (bulkBtnRef[0] != null && !bulkBtnRef[0].IsDisposed) {
                                bulkBtnRef[0].Load(Texts.Italic($"{T.SwitchOn}: x{remaining.Count}"), bulkAction);
                            }
                        });
                    }

                    var bulkBtn = UISpawn.Widget<WButtonText>(toggleLife,
                        UIPrefabId.WContent2ButtonSimple, innerContainer, subIdx++);
                    bulkBtnRef[0] = bulkBtn;
                    bulkBtn.Load(Texts.Italic($"{T.SwitchOn}: x{capturedGroupNodes.Count}"), bulkAction);

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
