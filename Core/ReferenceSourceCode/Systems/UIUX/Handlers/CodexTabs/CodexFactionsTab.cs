using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Input;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class CodexFactionsTab : BaseCodexTab {
        public override string Id => "Factions";
        public override int Order => 40;
        public override string TabName => T.CodexFactions;
        public override string TabIcon => IconId.WComms;

        private FactionDef selected;
        private WButtonIconText2 selectedButton;

        public bool ShowEntry(string id) {
            var faction = FactionDef.Get(id);
            if (faction == null) {
                return false;
            }
            selected = faction;
            return true;
        }

        public override void LoadIndex(WGroup index) {
            FactionDef.LoadKnown();
            selectedButton = null;
            var block = UISpawn.Block(Lifetime, index, 0);
            int idx = 0;

            var visibleFactions = FactionDef.AllOrdered
                .Where(item => MatchesSearch(item.NameT, item.AboutT))
                .ToList();

            if (selected != null && !visibleFactions.Contains(selected)) {
                selected = null;
            }
            selected ??= visibleFactions.FirstOrDefault();

            foreach (var item in visibleFactions) {
                var capturedItem = item;
                string standingText = null;
                if (!item.IsPlayer) {
                    var standing = S.Query.FactionAttitude.Ask(item, FactionDef.Player);
                    standingText = Units.PercentageSigned(standing, colored: true);
                }
                var w = UISpawn.Widget<WButtonIconText2>(Lifetime,
                    UIPrefabId.WContent2Button, block.Container, idx++);
                bool isSelected = item == selected;
                var title = isSelected
                    ? Texts.Accented(item.NameT, bold: false)
                    : item.NameT;
                if (isSelected) {
                    selectedButton = w;
                }
                w.Load(item.FlagIcon, title, standingText, () => {
                    if (selected == capturedItem) {
                        return;
                    }
                    var previous = selected;
                    var previousButton = selectedButton;
                    selected = capturedItem;
                    selectedButton = w;
                    previousButton?.SetText(previous.NameT);
                    w.SetText(Texts.Accented(capturedItem.NameT, bold: false));
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
            titleW.Load(selected.NameT);

            string aboutText;
            if (selected.IsPlayer || selected.IsAntiPlayer) {
                aboutText = Texts.Beautify(selected.AboutT);
            } else {
                var standing = S.Query.FactionAttitude.Ask(selected, FactionDef.Player);
                var str = Units.PercentageSigned(standing, colored: true);
                var standingText = T.FactionStandingTip_V2.F(selected.NameT, str);
                aboutText = Texts.Beautify(
                    $"{selected.AboutT}{GlyphMap.BRHR}{standingText}");
            }
            var descW = UISpawn.Widget<WText>(Lifetime,
                UIPrefabId.WTabPanelItem_TextBlock, block.Container, idx++);
            descW.Load(aboutText);
            descW.RebuildTextLayout();

            if (selected.FlagIcon != null) {
                var sprite = Images.SpriteWithResourceFallback(selected.FlagIcon);
                if (sprite != null) {
                    var capturedSprite = sprite;
                    var imgW = UISpawn.Widget<WButtonImageAutoSize>(Lifetime,
                        UIPrefabId.WTabPanelItem_ButtonImageBlock, block.Container, idx++);
                    imgW.Load(null, () => {
                        The.SysSig.ShowImageBlowout.Send(capturedSprite);
                    }, 256);
                    UIUtils.AddTip(Lifetime, imgW, T.ClickToEnlarge);
                    imgW.SetSprite(capturedSprite);
                }
            }


            if (!selected.IsAntiPlayer) {
                var relHeader = UISpawn.Widget<WText>(Lifetime,
                    UIPrefabId.WTabPanelItem_Header, block.Container, idx++);
                relHeader.Load(T.Relationships);

                foreach (var fac in FactionDef.AllOrdered) {
                    if (fac == selected || fac.IsAntiPlayer) {
                        continue;
                    }
                    float att;
                    if (selected.IsPlayer) {
                        att = S.Query.FactionAttitude.Ask(fac, selected);
                    } else {
                        att = S.Query.FactionAttitude.Ask(selected, fac);
                    }
                    var st = Units.PercentageSigned(att, colored: true);
                    var row = UISpawn.Widget<WIconText2>(Lifetime,
                        UIPrefabId.WContent2Text2, block.Container, idx++);
                    row.Load(fac.FlagIcon, fac.NameT, st);
                }
            }
        }
    }
}
