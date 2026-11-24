using System.Linq;
using Game.Achievements;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class CodexAchievementsTab : BaseCodexTab {
        public override string Id => "Achievements";
        public override int Order => 10;
        public override string TabName => T.Achievements;
        public override string TabIcon => IconId.WTasks;

        private Achievement selected;
        private WButtonIconText2 selectedButton;

        public override void LoadIndex(WGroup index) {
            selectedButton = null;
            var block = UISpawn.Block(Lifetime, index, 0);
            int idx = 0;

            var total = The.AchievementTracker.Count;
            var achieved = The.AchievementTracker.Achieved;
            var header = UISpawn.Widget<WProgressWithLabel>(Lifetime,
                UIPrefabId.WContent2HeaderProgress, block.Container, idx++);
            header.Label.Load(T.NounProgress);
            header.Progress.Load(Units.XOutOfY(achieved, total), achieved / (float) total);

            var visibleAchievements = AchievementTracker.All
                .Where(ach => MatchesSearch(ach.ListTitle, ach.NameT))
                .OrderBy(i => i.NameT)
                .ToList();

            if (selected != null && !visibleAchievements.Contains(selected)) {
                selected = null;
            }
            selected ??= visibleAchievements.FirstOrDefault();

            foreach (var ach in visibleAchievements) {
                var capturedAch = ach;
                var icon = ach.Icon;
                var w = UISpawn.Widget<WButtonIconText2>(Lifetime,
                    UIPrefabId.WContent2Button, block.Container, idx++);
                bool isSelected = ach == selected;
                var title = isSelected
                    ? Texts.Accented(ach.ListTitle, bold: false)
                    : ach.ListTitle;
                if (isSelected) {
                    selectedButton = w;
                }
                w.Load(icon, title, ach.IsAchieved ? ach.Date : null, () => {
                    if (selected == capturedAch) {
                        return;
                    }
                    var previous = selected;
                    var previousButton = selectedButton;
                    selected = capturedAch;
                    selectedButton = w;
                    previousButton?.SetText(previous.ListTitle);
                    w.SetText(Texts.Accented(capturedAch.ListTitle, bold: false));
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

            string title;
            string desc = selected.DescT;
            string achStr;
            var sb = Caches.StringBuilder2;
            sb.Clear();

            if (selected.IsHidden && !selected.IsAchieved) {
                title = T.HiddenAchievement;
                desc = "???";
                achStr = T.NotAchievedYet;
            } else if (!selected.IsAchieved) {
                title = selected.NameT;
                achStr = T.NotAchievedYet;
            } else {
                title = selected.NameT;
                achStr = T.AchievementDate_V1.F(selected.Date);
            }
            sb.Append(Texts.Beautify(desc));
            sb.Append(GlyphMap.BRHR);
            sb.Append(achStr);

            var titleW = UISpawn.Widget<WText>(Lifetime,
                UIPrefabId.WTabPanelItem_Header, block.Container, idx++);
            titleW.Load(Texts.Accented(title));

            var descW = UISpawn.Widget<WText>(Lifetime,
                UIPrefabId.WTabPanelItem_TextBlock, block.Container, idx++);
            descW.Load(sb.ToString());
            descW.RebuildTextLayout();

            if (selected.Icon != null) {
                var sprite = Images.SpriteWithResourceFallback(selected.Icon);
                if (sprite != null) {
                    var capturedSprite = sprite;
                    var imgW = UISpawn.Widget<WButtonImageAutoSize>(Lifetime,
                        UIPrefabId.WTabPanelItem_ButtonImageBlock, block.Container, idx++);
                    imgW.Load(null, () => {
                        The.SysSig.ShowImageBlowout.Send(capturedSprite);
                    }, heightLimit: 256);
                    UIUtils.AddTip(lifetime, imgW, T.ClickToEnlarge);
                    imgW.SetSprite(capturedSprite);
                }
            }
        }
    }
}
