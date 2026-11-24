using System;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Grid;
using KL.Utils;

namespace Game.Systems.UIUX {
    public class TileSearchResult : BaseSearchResult {
        private readonly Tile tile;
        public override string Id => Units.Num(IdH);
        private int initialTileId;
        public override int IdH => initialTileId;
        public override Def Def => tile.Definition;
        public override bool IsPinnedAvailable => tile?.IsActive == true;
        public TileSearchResult(BaseSearchAdapter adapter, Tile tile, int order) : base(adapter) {
            this.tile = tile;
            initialTileId = tile.Id;
            Order = order;
        }

        public override void SpawnWidget(Lifetime life, int idx, WGroup wContainer) {
            var w = UISpawn.Widget<WQuickSearchResult>(life, UIPrefabId.WQuickSearchResult_Small, wContainer.Container, idx);
            w.Load(tile.Definition.Preview, tile.Definition.NameT, Pos.String(tile.PosIdx), OnSelect);
            w.SetDef(life, tile.Definition);
            w.SetPinnable(this);
        }

        public override void SpawnPinned(Lifetime life, int idx, WGroup wContainer) {
            var w = UISpawn.Widget<WQuickSearchPinnedItem>(life, UIPrefabId.WQuickSearchButton, wContainer.Container, idx);
            w.Load(tile.Definition.Preview, Pos.String(tile.PosIdx), OnSelect, this);
            w.SetDef(life, tile.Definition);
        }

        public override void OnSelect() {
            if (tile?.IsActive == true) {
                if (!A.S.Query.IsExclusivelySelected.Ask(tile)) {
                    A.S.Sig.SelectEntity.Send(tile);
                }
                EntityUtils.CameraFocusOn(tile);
                // A.S.UIQuery.ShowQuickSearch.Ask(false);
            } else {
                WPopup.Warning(tile?.Definition.NameT ?? T.Unknown, T.ObjectNoLongerAvailable);
            }
        }
    }
}
