using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Items;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class BeingAshesTab : BaseEntityTab {
        public const string TabId = "BeingAshes";

        [SelfInit]
        private static void Register() {
            RegisterTab(new BeingAshesTab());
        }

        private BeingAshesComp comp;
        private GameState S;

        public override string Id => TabId;
        public override int Order => 5;
        public override string TabName => T.ObjMaterialsAshes;

        public override bool ShowFor(GameState s, ObjData obj) {
            if (obj.Obj == null) { return false; }
            if (!obj.Obj.TryGetComponent(out comp)) { return false; }
            if (!comp.IsNamed) { return false; }
            S = s;
            return true;
        }

        public override bool LoadContent(WGroup group) {
            var mind = comp.Obj.Data.Mind;
            if (mind == null) { return false; }

            var block = UISpawn.Block(lifetime, group, 0);
            int idx = 0;

            var row = UISpawn.Widget<WIconText>(lifetime,
                UIPrefabId.WContentText, block.Container, idx++);
            row.Load(IconId.CFuneralRibbon, T.AshesOf_V1.F(mind.Name.FullName));
            var row2 = UISpawn.Widget<WText>(lifetime, UIPrefabId.WContentTextBlock, block.Container, idx++);
            row2.Load(mind.DeceasedInfo(comp.Obj.Data.CustomString, comp.Obj.Data.CustomLong));
            // UIUtils.AddTipFunc(lifetime, row, () =>
            //     mind.DeceasedInfo(comp.Obj.Data.CustomString, comp.Obj.Data.CustomLong));

            return true;
        }

        public override bool LoadFooter(WGroup group) {
            var btn = UISpawn.Widget<WButtonIconText>(lifetime,
                UIPrefabId.WPanelButtonIcon, group.Container, 0);
            btn.Load(IconId.CFuneralRibbon, T.VerbForget, () => {
                BeingAshesComp.DoForget(S, comp.Obj.Data, true);
                A.S.Query.DeselectIfSelected.Ask(comp.Obj);
                EntityUtils.SelectOrFocus(comp.Obj);
            });
            UIUtils.AddTip(lifetime, btn, T.ForgetAshesTip);
            return true;
        }

        protected override void Cleanup() {
            comp = null;
            S = null;
        }
    }
}
