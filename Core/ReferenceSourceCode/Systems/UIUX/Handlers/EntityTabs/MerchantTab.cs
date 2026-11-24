using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class MerchantTab : BaseEntityTab {
        public const string TabId = "Merchant";

        [SelfInit]
        private static void Register() {
            RegisterTab(new MerchantTab());
        }

        private Tile tile;
        private StasisPodMerchantComp comp;
        private GameState S;

        public override string Id => TabId;
        public override int Order => 10;
        public override string TabName => T.SpaceObjectShipmerchant;

        public override bool ShowFor(GameState s, Tile t) {
            if (!t.IsConstructed) { return false; }
            if (!t.TryGetComponent<StasisPodMerchantComp>(out var c)) { return false; }
            if (!c.CanTrade) { return false; }
            S = s;
            tile = t;
            comp = c;
            return true;
        }

        public override bool LoadContent(WGroup group) {
            var block = UISpawn.Block(lifetime, group, 0);
            int idx = 0;
            var info = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContentTextBlock, block.Container, idx++);
            info.Load(T.MerchantOfferingTrade);
            return true;
        }

        public override bool LoadFooter(WGroup group) {
            var btn = UISpawn.Widget<WButtonIconText>(lifetime,
                UIPrefabId.WPanelButtonIcon, group.Container, 0);
            btn.Load(IconId.CTrade, T.Trade, () => comp.OpenTradeView());
            return true;
        }

        protected override void Cleanup() {
            tile = null;
            comp = null;
            S = null;
        }
    }
}
