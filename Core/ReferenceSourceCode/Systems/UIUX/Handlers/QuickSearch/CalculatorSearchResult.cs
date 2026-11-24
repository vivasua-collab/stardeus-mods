using System;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Grid;
using KL.Utils;

namespace Game.Systems.UIUX {
    public class CalculatorSearchResult : BaseSearchResult {
        public override string Id => "Calculator";
        public override int IdH => Hashes.S(Id);
        private readonly string input;
        private readonly string result;
        public CalculatorSearchResult(BaseSearchAdapter adapter, string input, float result, int order) : base(adapter) {
            this.result = result.ToString("0.####");
            this.input = $"{input} = {result}";
            Order = order;
        }

        public override void SpawnWidget(Lifetime life, int idx, WGroup wContainer) {
            var w = UISpawn.Widget<WQuickSearchResult>(life, UIPrefabId.WQuickSearchResult_Unclickable, wContainer.Container, idx);
            w.Load(IconId.CCalculator, result, input);
            w.SetNotPinnable();
        }

        public override void SpawnPinned(Lifetime life, int idx, WGroup wContainer) {
        }

        public override void OnSelect() {
        }
    }
}
