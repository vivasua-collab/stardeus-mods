using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Systems.Ships;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class CodexShipTagsTab : BaseCodexTab {
        public override string Id => "ShipTags";
        public override int Order => 70;
        public override string TabName => T.CodexShipTags;
        public override string TabIcon => IconId.CUpgrade;

        private ShipTagDef selected;
        private WButtonIconText2 selectedButton;

        public bool ShowEntry(string id) {
            foreach (var item in ShipTagDef.All.Values) {
                if (item.Id == id) {
                    selected = item;
                    return true;
                }
            }
            return false;
        }

        public override void LoadIndex(WGroup index) {
            selectedButton = null;
            var block = UISpawn.Block(Lifetime, index, 0);
            int idx = 0;

            var visibleTags = ShipTagDef.All.Values
                .Where(item => MatchesSearch(item.TagT, item.DescT, item.Tooltip(null)))
                .OrderBy(f => f.TagT)
                .ToList();

            if (selected != null && !visibleTags.Contains(selected)) {
                selected = null;
            }
            selected ??= visibleTags.FirstOrDefault();

            foreach (var item in visibleTags) {
                var capturedItem = item;
                var w = UISpawn.Widget<WButtonIconText2>(Lifetime,
                    UIPrefabId.WContent2Button, block.Container, idx++);
                bool isSelected = item == selected;
                var title = isSelected
                    ? Texts.Accented(item.DescT, bold: false)
                    : item.DescT;
                if (isSelected) {
                    selectedButton = w;
                }
                w.Load(IconId.CInfo, title, item.TagT, () => {
                    if (selected == capturedItem) {
                        return;
                    }
                    var previous = selected;
                    var previousButton = selectedButton;
                    selected = capturedItem;
                    selectedButton = w;
                    previousButton?.SetText(previous.DescT);
                    w.SetText(Texts.Accented(capturedItem.DescT, bold: false));
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

            var titleW = UISpawn.Widget<WText>(Lifetime,
                UIPrefabId.WTabPanelItem_Header, block.Container, idx++);
            titleW.Load(selected.TagT);

            var descW = UISpawn.Widget<WText>(Lifetime,
                UIPrefabId.WTabPanelItem_TextBlock, block.Container, idx++);
            descW.Load(selected.Tooltip(null));
            descW.RebuildTextLayout();
        }
    }
}
