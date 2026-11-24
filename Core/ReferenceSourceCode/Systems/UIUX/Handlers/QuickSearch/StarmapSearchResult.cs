using System;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.UI;
using Game.Utils;
using KL.Grid;
using KL.Utils;

namespace Game.Systems.UIUX {
    public class StarmapSearchResult : BaseSearchResult {
        private readonly SpaceObject so;
        public override string Id => Units.Num(IdH);
        public override int IdH => so.Id;

        public StarmapSearchResult(BaseSearchAdapter adapter, SpaceObject so, int order) : base(adapter) {
            this.so = so;
            Order = order;
        }

        public override void SpawnWidget(Lifetime life, int idx, WGroup wContainer) {
            var w = UISpawn.Widget<WQuickSearchResult>(life, UIPrefabId.WQuickSearchResult, wContainer.Container, idx);
            w.Load(so.Icon, so.ListName, so.Type.NameT, OnSelect);
            UIUtils.AddTipFunc(life, w, () => so.Info.UITooltip(true));
            w.SetNotPinnable();
        }

        public override void OnSelect() {
            A.S.Sys.Universe.ShowInStarmap(so);
            A.S.UIQuery.ShowQuickSearch.Ask(false);
        }

        public override void SpawnPinned(Lifetime life, int idx, WGroup wContainer) {
            throw new NotImplementedException();
        }
    }
}
