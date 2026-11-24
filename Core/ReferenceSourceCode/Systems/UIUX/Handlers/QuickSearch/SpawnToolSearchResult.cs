using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class SpawnToolSearchResult : BaseSearchResult {
        private readonly SpawnToolSearchAdapter adapter;
        private readonly Def def;

        public override string Id => def.Id;
        public override int IdH => def.IdH;
        public override Def Def => def;
        public override bool IsPinnedAvailable => adapter.IsResultAvailable(def.Id);

        public SpawnToolSearchResult(SpawnToolSearchAdapter adapter, Def def,
                int order) : base(adapter) {
            this.adapter = adapter;
            this.def = def;
            Order = order;
        }

        public override void SpawnWidget(Lifetime life, int idx, WGroup wContainer) {
            var w = UISpawn.Widget<WQuickSearchResult>(life,
                UIPrefabId.WQuickSearchResult, wContainer.Container, idx);
            w.Load(def.PreviewSprite, def.NameT, def.DescT, OnSelect);
            w.SetDef(life, def);
            w.SetPinnable(this);
        }

        public override void SpawnPinned(Lifetime life, int idx, WGroup wContainer) {
            var w = UISpawn.Widget<WQuickSearchPinnedItem>(life,
                UIPrefabId.WQuickSearchButton, wContainer.Container, idx);
            w.Load(def.Preview, def.NameT, OnSelect, this);
            w.SetDef(life, def);
        }

        public override void OnSelect() {
            if (adapter.TryActivate(def.Id)) {
                A.S.UIQuery.ShowQuickSearch.Ask(false);
            }
        }
    }
}
