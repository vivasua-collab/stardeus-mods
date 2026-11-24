using System;
using Game.Constants;
using Game.Data;
using Game.Tools;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public class BuildTileSearchResult : BaseSearchResult {
        private readonly Def def;
        public override string Id => def.Id;
        public override int IdH => def.IdH;
        public BuildTileSearchResult(BaseSearchAdapter adapter, Def def, int order) : base(adapter) {
            this.def = def;
            Order = order;
        }

        public override void SpawnWidget(Lifetime life, int idx, WGroup wContainer) {
            var w = UISpawn.Widget<WQuickSearchResult>(life, UIPrefabId.WQuickSearchResult, wContainer.Container, idx);
            w.Load(def.PreviewSprite, def.NameT, def.DescT, OnSelect);
            w.SetDef(life, def);
            w.SetPinnable(this);
        }

        public override void SpawnPinned(Lifetime life, int idx, WGroup wContainer) {
            var w = UISpawn.Widget<WQuickSearchPinnedItem>(life, UIPrefabId.WQuickSearchButton, wContainer.Container, idx);
            w.Load(def.PreviewSprite, def.NameT, OnSelect, this);
            w.SetDef(life, def);
        }

        public override void OnSelect() {
            ToolPlaceTiles.StartBuilding(def.Id);
            A.S.UIQuery.ShowQuickSearch.Ask(false);
        }
    }
}
