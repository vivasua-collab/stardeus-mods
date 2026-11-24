using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class OxygenProducerTab : BaseEntityTab {
        public const string TabId = "OxygenProducer";

        [SelfInit]
        private static void Register() {
            RegisterTab(new OxygenProducerTab());
        }

        private Tile tile;
        private OxygenProducerComp comp;
        private GameState S;

        public override string Id => TabId;
        public override int Order => 10;
        public override string TabName => T.ObjectsDevicesOxygenpump;

        public override bool ShowFor(GameState s, Tile t) {
            if (!t.IsConstructed) { return false; }
            if (t.ENode?.IsReachable == false) { return false; }
            if (!t.TryGetComponent<OxygenProducerComp>(out var c)) { return false; }
            if (c.IsDisabled) { return false; }
            tile = t;
            comp = c;
            S = s;
            return true;
        }

        public override bool LoadContent(WGroup group) {
            var block = UISpawn.Block(lifetime, group, 0);
            int idx = 0;

            var sliderRow = UISpawn.Widget<WSliderWithIconText2>(lifetime,
                UIPrefabId.WContent2Text2Slider, block.Container, idx++);
            sliderRow.Item.Load(IconId.COxygen, T.TargetOxygen,
                Units.Percentage(comp.Limit));
            sliderRow.Slider.Load(comp.Limit, v => {
                comp.SetLimit(v);
            }, 0f, 0.21f);
            sliderRow.Slider.SetValueTextFunc(v => Units.Percentage(v));

            return true;
        }

        public override bool LoadFooter(WGroup group) {
            var resetBtn = UISpawn.Widget<WButtonIconText>(lifetime,
                UIPrefabId.WContent4Button, group.Container, 0);
            resetBtn.Load(IconId.CCog, T.Reset, () => {
                comp.ResetLimit();
                ReloadTab();
            });
            return true;
        }

        protected override void Cleanup() {
            tile = null;
            comp = null;
            S = null;
        }
    }
}
