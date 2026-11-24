using System;
using Game.Constants;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public class ResearchUnlocksSearchResult : BaseSearchResult {
        private readonly ResearchUnlocksSearchAdapter.Result res;
        public override string Id => res.Id;
        public override int IdH => res.IdH;

        public ResearchUnlocksSearchResult(BaseSearchAdapter adapter,
                ResearchUnlocksSearchAdapter.Result res, int order) : base(adapter) {
            this.res = res;
            Order = order;
        }

        public override void SpawnWidget(Lifetime life, int idx, WGroup wContainer) {
            var w = UISpawn.Widget<WQuickSearchResult>(life,
                UIPrefabId.WQuickSearchResult, wContainer.Container, idx);
            w.Load(res.Preview, res.Name, res.Desc, OnSelect);
            if (res.Def != null) {
                UIUtils.AddCodexTooltip(life, w.gameObject, w, res.Def,
                    optional: false, showDelay: Tooltip.DelaySeconds);
            }
            w.SetNotPinnable();
        }
        public override void OnSelect() {
            A.S.UIQuery.ShowInResearchTree.Ask(res.Research);
            A.S.UIQuery.ShowQuickSearch.Ask(false);
        }

        public override void SpawnPinned(Lifetime life, int idx, WGroup wContainer) {
            throw new NotImplementedException();
        }
    }
}
