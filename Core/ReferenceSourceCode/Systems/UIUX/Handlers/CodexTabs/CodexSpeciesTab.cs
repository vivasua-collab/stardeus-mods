using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Research;
using Game.Systems.Creatures;
using Game.Systems.Path;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class CodexSpeciesTab : BaseCodexTab {
        public override string Id => "Species";
        public override int Order => 50;
        public override string TabName => T.Species;
        public override string TabIcon => IconId.WPerson;

        private Species selected;
        private WButtonIconText2 selectedButton;

        public override void LoadIndex(WGroup index) {
            selectedButton = null;
            var block = UISpawn.Block(Lifetime, index, 0);
            int idx = 0;

            var visibleSpecies = Species.Known.Values
                .Where(sp => {
                    var def = sp.Def;
                    if (def == null) {
                        return false;
                    }

                    bool isDiscovered = false;
                    if (Researchable.IsEmptyPrereq(def)) {
                        isDiscovered = true;
                    } else if (S.Research.IsDiscovered(Hashes.S(
                            def.Researchable.Prerequisites[0]))) {
                        isDiscovered = true;
                    }

                    return isDiscovered && MatchesSearch(sp.Name, def.NameT, def.DescT, def.ExtendedDesc(true));
                })
                .OrderBy(s => s.Name)
                .ToList();

            if (selected != null && !visibleSpecies.Contains(selected)) {
                selected = null;
            }
            selected ??= visibleSpecies.FirstOrDefault();

            foreach (var sp in visibleSpecies) {
                var def = sp.Def;
                var capturedSp = sp;
                var count = S.Query.CountFriendlyBeingsBySpecies.Ask(sp.IdH, true);
                var w = UISpawn.Widget<WButtonIconText2>(Lifetime,
                    UIPrefabId.WContent2Button, block.Container, idx++);
                bool isSelected = sp == selected;
                var title = isSelected
                    ? Texts.Accented(sp.Name, bold: false)
                    : sp.Name;
                if (isSelected) {
                    selectedButton = w;
                }
                w.Load(def.Preview, title, count > 0 ? Units.XNum(count) : null, () => {
                    if (selected == capturedSp) {
                        return;
                    }
                    var previous = selected;
                    var previousButton = selectedButton;
                    selected = capturedSp;
                    selectedButton = w;
                    previousButton?.SetText(previous.Name);
                    w.SetText(Texts.Accented(capturedSp.Name, bold: false));
                    RebuildContent();
                });
            }
        }

        public override void LoadContent(WGroup content) {
            if (selected == null) {
                return;
            }

            var block = UISpawn.Block(Lifetime, content, 0);
            int idx = 0;

            var title = selected.Def.NameWithPrice(S, false);
            var titleW = UISpawn.Widget<WText>(Lifetime,
                UIPrefabId.WTabPanelItem_Header, block.Container, idx++);
            titleW.Load(title);

            if (selected.Def.HasDescT) {
                var descInfoW = UISpawn.Widget<WText>(Lifetime,
                    UIPrefabId.WTabPanelItem_TextBlock, block.Container, idx++);
                descInfoW.Load(selected.Def.DescT);
                descInfoW.RebuildTextLayout();
            }

            var descW = UISpawn.Widget<WText>(Lifetime,
                UIPrefabId.WTabPanelItem_TextBlock, block.Container, idx++);
            descW.Load(selected.Def.ExtendedDesc(true));
            descW.RebuildTextLayout();

            if (selected.Def.PreviewSprite != null) {
                var capturedSprite = selected.Def.PreviewSprite;
                var imgW = UISpawn.Widget<WButtonImageAutoSize>(Lifetime,
                    UIPrefabId.WTabPanelItem_ButtonImageBlock, block.Container, idx++);
                imgW.Load(null, () => {
                    The.SysSig.ShowImageBlowout.Send(capturedSprite);
                }, 256);
                UIUtils.AddTip(Lifetime, imgW, T.ClickToEnlarge);
                imgW.SetSprite(capturedSprite);
            }
        }
    }
}
