using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Electricity;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class ElectricityTab_Connectors : BaseElectricityTab {
        public override string Name => T.EnergyConnectors;

        public override void LoadContent(Lifetime parentLifetime, GameState s,
                WGroup container, int gridIdx, ElectricityWidget widget) {
            var life = parentLifetime.CreateChild("Elec.Connectors");
            var sys = s.Sys.Electricity;
            if (!sys.TryGetGrid(gridIdx, out var grid)) { return; }

            var block = UISpawn.Block(life, container, 0);
            int idx = 0;
            int totalCount = 0;

            var groups = sys.NodeComponents.Values
                .Where(n => n.Node.GridId == gridIdx && n.Node.IsConnector)
                .GroupBy(n => n.Entity.Definition.Id)
                .OrderBy(g => g.First().Entity.Definition.NameT);

            foreach (var group in groups) {
                var groupNodes = group
                    .OrderByDescending(n => n.Node.NumConnections)
                    .ThenByDescending(n => n.Node.MaxConnections)
                    .ToList();
                var firstNode = groupNodes[0];

                var listBtn = UISpawn.Widget<WContentListButton>(life,
                    UIPrefabId.WContent2ListButton, block.Container, idx++);
                listBtn.Load(firstNode.Entity.Definition.Preview,
                    firstNode.Entity.Definition.NameT, Units.XNum(groupNodes.Count));
                var capturedGroupNodes = groupNodes;
                listBtn.LoadOnToggle(life, (toggleLife, innerContainer, willShow, wasLoaded) => {
                    if (!willShow) { return wasLoaded; }
                    if (wasLoaded) { return true; }
                    int subIdx = 0;
                    foreach (var node in capturedGroupNodes) {
                        var w = UISpawn.Widget<WContentButton2Text2>(toggleLife,
                            UIPrefabId.WContent2Button2Text2, innerContainer, subIdx++);
                        w.Item.Load(node.Entity.Definition.Preview, node.Tile.NameOrLabel,
                            Units.XOutOfY(node.Node.NumConnections, node.Node.MaxConnections),
                            () => EntityUtils.SelectOrFocus(node.Tile));
                        w.Button.gameObject.SetActiveFast(false);
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
