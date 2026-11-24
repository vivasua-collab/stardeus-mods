using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Grid;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class ReinstallableTab : BaseEntityTab {
        public const string TabId = "Reinstallable";

        [SelfInit]
        private static void Register() {
            RegisterTab(new ReinstallableTab());
        }

        private ReinstallableComp reinstallable;

        public override string Id => TabId;
        public override int Order => 10;
        public override string TabName => T.NounSource;

        public override bool ShowFor(GameState s, Tile tile) {
            if (tile.IsConstructed) { return false; }
            var r = tile.Reinstallable;
            if (r == null || r.SourceId == 0 || r.Source == null) { return false; }
            reinstallable = r;
            return true;
        }

        public override bool LoadContent(WGroup group) {
            var sourceTile = reinstallable.Source.Tile;
            var block = UISpawn.Block(lifetime, group, 0);
            int idx = 0;

            var w = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, block.Container, idx++);
            w.Load(sourceTile.Definition.Preview, sourceTile.Definition.NameT,
                Texts.Grey(Pos.String(sourceTile.PosIdx)), () => {
                    EntityUtils.SelectOrFocus(sourceTile);
                });
            UIUtils.AddCodexTooltip(lifetime, w.gameObject, w, sourceTile.Definition);

            return true;
        }

        protected override void Cleanup() {
            reinstallable = null;
        }
    }
}
