using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.UIUX.Codex.Pages;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class TractorBeamTargetTab : BaseEntityTab {
        public const string TabId = "TractorBeamTarget";

        [SelfInit]
        private static void Register() {
            RegisterTab(new TractorBeamTargetTab());
        }

        private Tile tile;
        private TractorBeamTargetComp target;
        private GameState S;

        public override string Id => TabId;
        public override int Order => 11;
        public override string TabName => T.StructureFloorsTractorbeamtarget;

        public override bool ShowFor(GameState s, Tile tile) {
            if (!tile.IsConstructed) { return false; }
            if (tile.ENode?.IsReachable == false) { return false; }
            if (!tile.TryGetComponent<TractorBeamTargetComp>(out var t)) { return false; }
            this.tile = tile;
            this.target = t;
            S = s;
            return true;
        }

        public override bool LoadContent(WGroup group) {
            target.StateChangedB.Bind(lifetime, () => ReloadTab(), false);

            var block = UISpawn.Block(lifetime, group, 0);
            int idx = 0;

            var statusRow = UISpawn.Widget<WIconText>(lifetime,
                UIPrefabId.WContent2Text, block.Container, idx++);
            statusRow.Load(
                target.HasProjectile ? IconId.CTargetSpot : IconId.CTargetSpotOff,
                target.HasProjectile ? T.Active : T.Inactive);


            return true;
        }

        public override bool LoadFooter(WGroup group) {
            int idx = 0;
            var helpBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent4Button, group.Container, idx++);
            helpBtn.Load(IconId.CInfo, T.HowItWorks, null, () => {
                A.S.UIQuery.ShowCodexEntry.Ask(CodexConceptPage.FindById(TractorBeamPage.PageId));
            });
            return true;
        }

        protected override void Cleanup() {
            tile = null;
            target = null;
            S = null;
        }
    }
}
