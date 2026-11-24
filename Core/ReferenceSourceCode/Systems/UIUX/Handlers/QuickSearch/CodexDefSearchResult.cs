using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public class CodexDefSearchResult : BaseSearchResult {
        private readonly Def def;
        public override string Id => def.Id;
        public override int IdH => def.IdH;

        public CodexDefSearchResult(BaseSearchAdapter adapter, Def def, int order) : base(adapter) {
            this.def = def;
            Order = order;
        }

        public override void SpawnWidget(Lifetime life, int idx, WGroup wContainer) {
            var w = UISpawn.Widget<WQuickSearchResult>(life, UIPrefabId.WQuickSearchResult, wContainer.Container, idx);
            w.Load(def.PreviewSprite, def.NameT, def.DescT, OnSelect);
            w.SetDef(life, def);
            w.SetNotPinnable();
        }

        public override void SpawnPinned(Lifetime life, int idx, WGroup wContainer) {
            // these results won't be pinnable
        }


        public override void OnSelect() {
            def.ShowManualEntry();
        }
    }
}
