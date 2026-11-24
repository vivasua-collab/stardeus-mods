using System;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.UI;
using Game.Utils;
using KL.Collections;
using KL.Grid;
using KL.Utils;

namespace Game.Systems.UIUX {
    public class ResearchNodeSearchResult : BaseSearchResult {
        private readonly Def def;
        public override string Id => def.Id;
        public override int IdH => def.IdH;

        public ResearchNodeSearchResult(BaseSearchAdapter adapter, Def def, int order) : base(adapter) {
            this.def = def;
            Order = order;
        }

        public override void SpawnWidget(Lifetime life, int idx, WGroup wContainer) {
            var w = UISpawn.Widget<WQuickSearchResult>(life, UIPrefabId.WQuickSearchResult, wContainer.Container, idx);
            w.Load(def.Preview, def.NameT, ListUnlocks(), OnSelect);
            w.SetNotPinnable();
        }
        private string ListUnlocks() {
            if (def.Researchable.IsCostless) { return null;}
            var allUnlockables = A.S.Research.GetUnlockableIconsFor(def);
            var res = Caches.StringList;
            res.Clear();
            foreach (var unl in allUnlockables) {
                if (unl.def != null) {
                    res.AddUniqueInefficient(unl.def.NameT);
                } else if (unl.unlockEff != null) {
                    res.AddUniqueInefficient(unl.unlockEff.Title);
                }
            }
            res.Sort();
            return string.Join(", ", res);
        }

        public override void OnSelect() {
            A.S.UIQuery.ShowInResearchTree.Ask(def);
            A.S.UIQuery.ShowQuickSearch.Ask(false);
        }

        public override void SpawnPinned(Lifetime life, int idx, WGroup wContainer) {
            throw new NotImplementedException();
        }
    }
}
