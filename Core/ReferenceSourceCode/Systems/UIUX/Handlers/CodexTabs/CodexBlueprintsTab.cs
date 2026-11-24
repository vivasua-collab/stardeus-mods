using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Research;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class CodexBlueprintsTab : BaseCodexTab {
        public override string Id => "Blueprints";
        public override int Order => 20;
        public override string TabName => T.CodexBlueprints;
        public override string TabIcon => IconId.WHammer;
        public override bool HasFooter => selected != null
            && (S.CountByDef(selected) > 0 || CanOpenResearch(selected));

        private Def selected;
        private WButtonIconText2 selectedButton;
        private int cycleIdx;
        private Def lastCycled;
        private int cycledCount;
        private readonly Entity[] cycled = new Entity[64];

        public override void LoadIndex(WGroup index) {
            selectedButton = null;
            var block = UISpawn.Block(Lifetime, index, 0);
            int idx = 0;

            var visibleDefs = The.Defs.Defs.Values
                .Where(v => v.IsConstructable)
                .Where(v => v.IsInSeason || A.InstantBuild)
                .Where(def => Researchable.IsEmptyPrereq(def)
                    || S.Research.IsDiscovered(Hashes.S(def.Researchable.Prerequisites[0])))
                .Where(def => MatchesSearch(def.NameT, def.DescT, def.ExtendedDesc(false)))
                .OrderBy(d => d.NameT)
                .ToList();

            if (selected != null && !visibleDefs.Contains(selected)) {
                selected = null;
            }
            selected ??= visibleDefs.FirstOrDefault();

            foreach (var def in visibleDefs) {
                var capturedDef = def;
                var isUnlocked = S.Research.IsDefUnlocked(def);
                if (!isUnlocked && Researchable.IsEmptyPrereq(def.Researchable)) {
                    isUnlocked = true;
                }
                var icon = isUnlocked ? def.Preview : IconId.CLocked;
                var w = UISpawn.Widget<WButtonIconText2>(Lifetime,
                    UIPrefabId.WContent2Button, block.Container, idx++);
                bool isSelected = def == selected;
                var title = isSelected
                    ? Texts.Accented(def.NameT, bold: false)
                    : def.NameT;
                if (isSelected) {
                    selectedButton = w;
                }
                var count = S.CountByDef(def);
                w.Load(icon, title, count > 0 ? Units.XNum(count) : null, () => {
                    if (selected == capturedDef) {
                        return;
                    }
                    var previous = selected;
                    var previousButton = selectedButton;
                    selected = capturedDef;
                    selectedButton = w;
                    previousButton?.SetText(previous.NameT);
                    w.SetText(Texts.Accented(capturedDef.NameT, bold: false));
                    RefreshContentAndFooter();
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

            if (selected.HasDescT) {
                var descInfoW = UISpawn.Widget<WText>(Lifetime,
                    UIPrefabId.WTabPanelItem_TextBlock, block.Container, idx++);
                descInfoW.Load(selected.DescT);
                descInfoW.RebuildTextLayout();
            }

            var sb = Caches.StringBuilder3;
            sb.Clear();
            if (!Researchable.IsEmptyPrereq(selected.Researchable)) {
                sb.AppendFormat("{0}:\n", T.ResearchTree);
                sb.AppendLine(S.Research.TreePathFor(selected));
            } else {
                sb.AppendFormat("{0}\n", Texts.Grey(T.DeviceLootableOnly));
            }
            sb.Append(Texts.Beautify(selected.ExtendedDesc(false)));

            var descW = UISpawn.Widget<WText>(Lifetime,
                UIPrefabId.WTabPanelItem_TextBlock, block.Container, idx++);
            descW.Load(sb.ToString());
            descW.RebuildTextLayout();

            if (selected.PreviewSprite != null) {
                var capturedSprite = selected.PreviewSprite;
                var imgW = UISpawn.Widget<WButtonImageAutoSize>(Lifetime,
                    UIPrefabId.WTabPanelItem_ButtonImageBlock, block.Container, idx++);
                imgW.Load(null, () => {
                    The.SysSig.ShowImageBlowout.Send(capturedSprite);
                }, heightLimit: 256);
                imgW.SetSprite(capturedSprite);
                UIUtils.AddTip(Lifetime, imgW, T.ClickToEnlarge);
                Coroutines.OnNextFrame(content.RebuildLayout);
            }
        }

        public override void LoadFooter(WGroup footer) {
            if (selected == null) {
                return;
            }

            int idx = 0;
            if (S.CountByDef(selected) > 0) {
                var findBtn = UISpawn.Widget<WButtonText>(Lifetime,
                    UIPrefabId.WPanelButton, footer.Container, idx++);
                findBtn.Load(T.Find, () => {
                    FindBlueprint(selected);
                });
            }

            if (CanOpenResearch(selected)) {
                var researchBtn = UISpawn.Widget<WButtonText>(Lifetime,
                    UIPrefabId.WPanelButton, footer.Container, idx++);
                researchBtn.Load(T.OpenResearch, () => {
                    S.UIQuery.ShowInResearchTree.Ask(selected);
                });
            }
        }

        private void RefreshContentAndFooter() {
            LoadContentSection(widget.Content);
            widget.ShowFooter(HasFooter);
            if (HasFooter) {
                LoadFooterSection(widget.Footer);
            }
        }

        private static bool CanOpenResearch(Def def) {
            return def?.Researchable?.IsTreeNode == true || !Researchable.IsEmptyPrereq(def);
        }

        private void FindBlueprint(Def def) {
            if (lastCycled != def) {
                lastCycled = def;
                cycleIdx = 0;
                cycledCount = 0;
                if (S.AllByDef(def, out var found)) {
                    foreach (var item in found) {
                        cycled[cycledCount++] = item.Value;
                        if (cycledCount == cycled.Length) {
                            break;
                        }
                    }
                }
            }

            var entity = cycled.SafeGet(cycleIdx++);
            if (entity == null) {
                return;
            }

            S.Sig.SelectEntity.Send(entity);
            EntityUtils.CameraFocusOn(entity);
            if (cycleIdx >= cycledCount) {
                cycleIdx = 0;
            }
        }
    }
}
