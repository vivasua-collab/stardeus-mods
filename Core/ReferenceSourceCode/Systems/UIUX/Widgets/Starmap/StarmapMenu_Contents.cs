using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Space;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class StarmapMenu_Contents : StarmapMenuBlock {
        public override int Order => 40;
        private Lifetime lifetime;
        readonly KnownPOIsUI knownPoisUI;

        public StarmapMenu_Contents(StarmapMenuWidget parent, StarmapContext ctx, GameState s) : base(parent, ctx, s) {
            knownPoisUI = new KnownPOIsUI(s, ctx, parent);
        }

        public override void Load(int siblingIdx) {
            if (A.IsInHyperspace) { return; }
            if (!S.Sys.Sec.IsRadarActive()) { return; }

            if (lifetime != null) {
                lifetime.Dispose();
            }
            lifetime = ctx.MenuLifetime.CreateChild("Contents");

            var block = UISpawn.Block(lifetime, parent.WItems, siblingIdx);
            int idx = 0;

            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.Contents);

            knownPoisUI.AddMainButton(block, idx++);
        }
    }
}
