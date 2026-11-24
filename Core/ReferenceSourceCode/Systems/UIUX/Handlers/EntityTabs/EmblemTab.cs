using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Rendering;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class EmblemTab : BaseEntityTab {
        public const string TabId = "Emblem";
        private const string SubId = "Emblem";

        [SelfInit]
        private static void Register() {
            RegisterTab(new EmblemTab());
        }

        private Tile tile;
        private EmblemComp comp;
        private GameState S;
        private WBase contentBtn;

        public override string Id => TabId;
        public override int Order => 10;
        public override string TabName => T.Emblem;

        public override bool ShowFor(GameState s, Tile t) {
            if (!t.IsConstructed) { return false; }
            if (!t.TryGetComponent<EmblemComp>(out var c)) { return false; }
            tile = t;
            comp = c;
            S = s;
            return true;
        }

        public override bool LoadContent(WGroup group) {
            var block = UISpawn.Block(lifetime, group, 0);
            int idx = 0;

            if (comp.IsWalkDiffConfigurable) {
                var sliderRow = UISpawn.Widget<WSliderWithIconText2>(lifetime,
                    UIPrefabId.WContent2Text2Slider, block.Container, idx++);
                sliderRow.Item.Load(IconId.CHaul, T.WalkingDifficulty, null);
                sliderRow.Slider.Load(comp.ObjWalkDifficulty, v => {
                    comp.SetWalkDifficulty(v);
                });
            }

            var emblem = comp.Emblem;
            var row = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, block.Container, idx++);
            contentBtn = row;
            row.Load(emblem ?? IconId.CInfo, emblem ?? T.Empty, null, () => {
                ShowEmblemPanel(row);
            });

            if (SubPanelWidget.IsOpenFor == SubId && contentBtn != null) {
                LayoutRebuilder.ForceRebuildLayoutImmediate(group.Container);
                ShowEmblemPanel(contentBtn);
                var btn = contentBtn;
                Coroutines.OnNextFrame(() => {
                    var sp = S.UIQuery.GetEntitySubPanel.Ask();
                    sp.RepositionWidget(btn, connectorOnly: false);
                });
            }

            return true;
        }

        private void ShowEmblemPanel(WBase forWidget) {
            var subPanel = S.UIQuery.GetEntitySubPanel.Ask();
            var l = subPanel.CreateLifetime(lifetime);
            subPanel.SetTitle(T.Emblem);

            var block = UISpawn.Block(l, subPanel.Container, 0);
            int idx = 0;

            var header = UISpawn.Widget<WText>(l,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.Emblem);

            var opts = Images.Matching("Emblems/");
            foreach (var opt in opts) {
                if (opt.EndsWith("_D")
                        || opt.EndsWith("_L")
                        || opt.EndsWith("_R")
                        || opt.EndsWith("_U")) {
                    continue;
                }
                var capturedOpt = opt;
                var emblemBtn = UISpawn.Widget<WButtonIconText2>(l,
                    UIPrefabId.WContent2Button, block.Container, idx++);
                emblemBtn.Load(capturedOpt, capturedOpt, null, () => {
                    comp.SetEmblem(capturedOpt);
                    subPanel.Close();
                    ReloadTab();
                });
            }

            subPanel.Show(forWidget, SubId);
        }

        public override bool LoadFooter(WGroup group) {
            var folderBtn = UISpawn.Widget<WButtonText>(lifetime,
                UIPrefabId.WPanelButton, group.Container, -1);
            folderBtn.Load(Texts.ExternalLink(T.OpenEmblemsFolder), () => {
                comp.OpenEmblemsFolder();
            });
            UIUtils.AddTip(lifetime, folderBtn, comp.EmblemsPath);
            return true;
        }

        protected override void Cleanup() {
            tile = null;
            comp = null;
            S = null;
            contentBtn = null;
        }
    }
}
