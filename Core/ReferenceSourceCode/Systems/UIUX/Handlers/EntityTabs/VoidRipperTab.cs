using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class VoidRipperTab : BaseEntityTab {
        public const string TabId = "VoidRipper";

        [SelfInit]
        private static void Register() {
            RegisterTab(new VoidRipperTab());
        }

        private Tile tile;
        private VoidRipperComp ripper;
        private GameState S;

        public override string Id => TabId;
        public override int Order => 10;
        public override string TabName => T.ObjectsDevicesVoidripper;

        public override bool ShowFor(GameState s, Tile tile) {
            if (!tile.IsConstructed) { return false; }
            if (tile.ENode?.IsReachable == false) { return false; }
            if (!tile.TryGetComponent<VoidRipperComp>(out var r)) { return false; }
            this.tile = tile;
            this.ripper = r;
            S = s;
            return true;
        }

        public override bool LoadContent(WGroup group) {
            ripper.StateChangedB.Bind(lifetime, () => ReloadTab(), false);

            var block = UISpawn.Block(lifetime, group, 0);
            int idx = 0;

            var header = UISpawn.Widget<WProgressWithLabel>(lifetime,
                UIPrefabId.WContentHeaderProgress, block.Container, idx++);
            var percentage = ripper.ChargePercentage;
            header.Label.Load(ripper.IsCharged ? T.Ready : T.Charging);
            header.Progress.SetPercent(percentage, false);
            header.Progress.SetText(Units.Percentage(percentage));

            if (A.InstantBuild) {
                var devBlock = UISpawn.Block(lifetime, group, 1);
                int dIdx = 0;
                var devHeader = UISpawn.Widget<WText>(lifetime,
                    UIPrefabId.WContent2Header, devBlock.Container, dIdx++);
                devHeader.Load("Dev");
                var devBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                    UIPrefabId.WContent2Button, devBlock.Container, dIdx++);
                devBtn.Load(IconId.WDev, "DEV: Refill", null, () => {
                    ripper.DevFillCharge();
                });
            }

            return true;
        }

        protected override void Cleanup() {
            tile = null;
            ripper = null;
            S = null;
        }
    }
}
