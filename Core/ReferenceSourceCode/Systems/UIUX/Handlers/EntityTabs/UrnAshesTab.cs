using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class UrnAshesTab : BaseEntityTab {
        public const string TabId = "UrnAshes";

        [SelfInit]
        private static void Register() {
            RegisterTab(new UrnAshesTab());
        }

        private UrnNameComp comp;

        public override string Id => TabId;
        public override int Order => 5;
        public override string TabName => T.ObjMaterialsAshes;

        public override bool ShowFor(GameState s, Tile tile) {
            if (!tile.IsConstructed) { return false; }
            if (!tile.TryGetComponent<UrnNameComp>(out var c)) { return false; }
            if (c.Mind == null) { return false; }
            comp = c;
            return true;
        }

        public override bool LoadContent(WGroup group) {
            comp.StateChangedB.Bind(lifetime, () => ReloadTab(), false);

            var mind = comp.Mind;
            if (mind == null) { return false; }

            var block = UISpawn.Block(lifetime, group, 0);
            int idx = 0;

            var row = UISpawn.Widget<WIconText>(lifetime,
                UIPrefabId.WContentText, block.Container, idx++);
            row.Load(IconId.CFuneralRibbon, T.AshesOf_V1.F(mind.Name.FullName));
            var row2 = UISpawn.Widget<WText>(lifetime, UIPrefabId.WContentTextBlock, block.Container, idx++);
            row2.Load(mind.DeceasedInfo(comp.CauseOfDeath, comp.TimeOfDeath));

            return true;
        }

        protected override void Cleanup() {
            comp = null;
        }
    }
}
