using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Electricity;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class ElectricityTab_Disconnected : BaseElectricityTab {
        public override string Name => T.ElDisconnectedDevices;

        public override void LoadContent(Lifetime parentLifetime, GameState s,
                WGroup container, int gridIdx, ElectricityWidget widget) {
            var life = parentLifetime.CreateChild("Elec.Disconnected");
            var sys = s.Sys.Electricity;
            sys.UpdateDisconnectedCount(true);

            var block = UISpawn.Block(life, container, 0);
            int idx = 0;

            var tiles = new List<Tile>();
            var arrNodes = sys.ArrNodes;
            for (int i = 0; i < arrNodes.Count; i++) {
                if (arrNodes.TryGet(i, out var node)) {
                    if (node.NumConnections < 1 && node.IsInitialized) {
                        var tile = s.FindEntity<Tile>(node.EntityId);
                        if (tile != null) {
                            tiles.Add(tile);
                        }
                    }
                }
            }

            var groups = tiles
                .GroupBy(t => t.Definition.Id)
                .OrderBy(g => g.First().Definition.NameT);

            int totalCount = 0;
            foreach (var group in groups) {
                var groupTiles = group.ToList();
                var firstTile = groupTiles[0];

                var listBtn = UISpawn.Widget<WContentListButton>(life,
                    UIPrefabId.WContent2ListButton, block.Container, idx++);
                listBtn.Load(firstTile.Definition.Preview,
                    firstTile.Definition.NameT, Units.XNum(groupTiles.Count));
                var capturedGroupTiles = groupTiles;
                listBtn.LoadOnToggle(life, (toggleLife, innerContainer, willShow, wasLoaded) => {
                    if (!willShow) { return wasLoaded; }
                    if (wasLoaded) { return true; }
                    int subIdx = 0;
                    foreach (var tile in capturedGroupTiles) {
                        var w = UISpawn.Widget<WContentButton2Text2>(toggleLife,
                            UIPrefabId.WContent2Button2Text2, innerContainer, subIdx++);
                        w.Item.Load(tile.Definition.Preview, tile.NameOrLabel,
                            null, () => EntityUtils.SelectOrFocus(tile));
                        w.Button.gameObject.SetActiveFast(false);
                    }
                    return true;
                }, expanded: false);
                totalCount += groupTiles.Count;
            }

            if (totalCount == 0) {
                var emptyW = UISpawn.Widget<WIconText2>(life,
                    UIPrefabId.WContent2Text2, block.Container, idx++);
                emptyW.Load(IconId.CCheck, T.Empty);
            }
        }
    }
}
