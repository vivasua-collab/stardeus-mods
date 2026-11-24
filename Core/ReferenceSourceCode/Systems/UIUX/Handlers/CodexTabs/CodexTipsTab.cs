using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Hints;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class CodexTipsTab : BaseCodexTab {
        private const int TipTitleMaxCharacters = 50;

        public override string Id => "Tips";
        public override int Order => 30;
        public override string TabName => T.CodexTips;
        public override string TabIcon => IconId.WEventLog;

        private string selectedTip;
        private WButtonIconText2 selectedButton;

        public override void LoadIndex(WGroup index) {
            LoadHints.BuildDatabase();
            selectedButton = null;
            var block = UISpawn.Block(Lifetime, index, 0);
            int idx = 0;

            var visibleTips = LoadHints.AllHints
                .Where(item => !VirtualMouseInput.IsCurrent || !item.Contains("Glyphs_Keyboard"))
                .Where(item => MatchesSearch(
                    Texts.FirstWords(item, TipTitleMaxCharacters), item))
                .ToList();

            if (selectedTip != null && !visibleTips.Contains(selectedTip)) {
                selectedTip = null;
            }
            selectedTip ??= visibleTips.FirstOrDefault();

            foreach (var item in visibleTips) {
                var capturedItem = item;
                var title = Texts.FirstWords(item, TipTitleMaxCharacters);
                var w = UISpawn.Widget<WButtonIconText2>(Lifetime,
                    UIPrefabId.WContent2Button, block.Container, idx++);
                bool isSelected = item == selectedTip;
                var displayTitle = isSelected
                    ? Texts.Accented(title, bold: false)
                    : title;
                if (isSelected) {
                    selectedButton = w;
                }
                w.Load(IconId.CInfo, displayTitle, null, () => {
                    if (selectedTip == capturedItem) {
                        return;
                    }
                    var previous = selectedTip;
                    var previousButton = selectedButton;
                    selectedTip = capturedItem;
                    selectedButton = w;
                    previousButton?.SetText(Texts.FirstWords(previous, TipTitleMaxCharacters));
                    w.SetText(Texts.Accented(title, bold: false));
                    RebuildContent();
                });
            }
        }

        public override void LoadContent(WGroup content) {
            if (selectedTip == null) {
                return;
            }

            var block = UISpawn.Block(Lifetime, content, 0);
            int idx = 0;

            var titleW = UISpawn.Widget<WText>(Lifetime,
                UIPrefabId.WTabPanelItem_Header, block.Container, idx++);
            titleW.Load(T.Tip);

            var descW = UISpawn.Widget<WText>(Lifetime,
                UIPrefabId.WTabPanelItem_TextBlock, block.Container, idx++);
            descW.Load(Texts.Beautify(selectedTip));
            descW.RebuildTextLayout();
        }
    }
}
