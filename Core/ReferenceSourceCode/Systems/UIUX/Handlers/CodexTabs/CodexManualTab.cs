using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class CodexManualTab : BaseCodexTab {
        public override string Id => "Manual";
        public override int Order => 0;
        public override string TabName => T.CodexManual;
        public override string TabIcon => IconId.WDev;
        public override bool HasFooter => currentEntry != null;

        private CodexConceptPage currentGroup;
        private CodexConceptPage currentEntry;
        private WButtonIconText2 currentEntryButton;

        private BCallback ProgressChanged;

        private readonly Stack<CodexConceptPage> history = new();
        private bool suppressHistory;

        public void ShowEntry(CodexConceptPage concept) {
            if (concept == null) {
                return;
            }
            if (concept.IsGroup) {
                currentGroup = concept;
            } else {
                if (!suppressHistory && currentEntry != null && currentEntry != concept) {
                    history.Push(currentEntry);
                }
                currentEntry = concept;
                var group = CodexConceptPage.FindGroup(concept.GroupFromId);
                if (group != null) {
                    currentGroup = group;
                }
            }
        }

        public void ClearHistory() {
            history.Clear();
        }

        public override void LoadIndex(WGroup index) {
            CodexConceptPage.LoadKnown();
            if (ProgressChanged == null) {
                ProgressChanged = new BCallback(capacity: CodexConceptPage.All.Count + 1);
            }
            currentEntryButton = null;
            EnsureVisibleSelection();
            var block = UISpawn.Block(Lifetime, index, 0);
            int idx = 0;

            var header = UISpawn.Widget<WProgressWithLabel>(Lifetime,
                UIPrefabId.WContent2HeaderProgress, block.Container, idx++);
            header.Label.Load(T.NounProgress);
            ProgressChanged.Bind(Lifetime, () => {
                CodexConceptPage.GetGlobalCounts(out int total, out int learned);
                header.Progress.Load(Units.XOutOfY(learned, total), learned / (float) total);
            });

            foreach (var group in CodexConceptPage.Groups) {
                if (!TryGetVisibleItems(group, out _, out _)) {
                    continue;
                }

                group.GetCounts(out int gTotal, out int gLearned);
                var icon = gTotal == gLearned ? IconId.CCheck : IconId.CInfo;

                var listButton = UISpawn.Widget<WContentListButton>(Lifetime,
                    UIPrefabId.WContent2ListButton, block.Container, idx++);
                listButton.Load(icon, group.TitleT, Units.XOutOfY(gLearned, gTotal));

                var capturedGroup = group;
                listButton.LoadOnToggle(Lifetime, (life, container, willShow, wasLoaded) => {
                    return ToggleGroup(life, container, willShow, wasLoaded, capturedGroup);
                }, currentGroup != null && currentGroup == group);

                ProgressChanged.Bind(Lifetime, () => {
                    group.GetCounts(out int gTotal, out int gLearned);
                    var icon = gTotal == gLearned ? IconId.CCheck : IconId.CInfo;
                    listButton.Load(icon, group.TitleT, Units.XOutOfY(gLearned, gTotal));
                }, false);
            }
            var scroll = index.GetComponentInParent<ScrollRect>();
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.viewport);
            scroll.verticalNormalizedPosition = 1f;
        }

        private bool ToggleGroup(Lifetime life, RectTransform container,
                bool willShow, bool wasLoaded, CodexConceptPage group) {
            if (!willShow || wasLoaded) {
                return wasLoaded;
            }

            if (!TryGetVisibleItems(group, out var items, out _)) {
                return false;
            }

            int idx = 0;
            foreach (var item in items) {
                var capturedItem = item;
                var row = UISpawn.Widget<WButtonIconText2WithToggle>(life,
                    UIPrefabId.WContent2ButtonToggle, container, idx++);
                var w = row.Button;
                bool isSelected = item == currentEntry;
                var title = isSelected
                    ? Texts.Accented(item.TitleT, bold: false)
                    : item.TitleT;
                if (isSelected) {
                    currentEntryButton = w;
                }
                w.Load(null, title, null, () => {
                    if (currentEntry == capturedItem) {
                        return;
                    }
                    var previous = currentEntry;
                    var previousButton = currentEntryButton;
                    ClearHistory();
                    currentGroup = group;
                    currentEntry = capturedItem;
                    currentEntryButton = w;
                    previousButton?.SetText(previous.TitleT);
                    w.SetText(Texts.Accented(capturedItem.TitleT, bold: false));
                    RebuildContent();
                });

                row.Toggle.Load(item.IsLearned, on => SetLearned(capturedItem, on));
                item.IsLearned.Bind(life, b => row.Toggle.SetValue(b, false), false);
            }
            return true;
        }

        public override void LoadContent(WGroup content) {
            if (currentEntry == null) {
                return;
            }

            var block = UISpawn.Block(Lifetime, content, 0);
            int idx = 0;

            var titleW = UISpawn.Widget<WText>(Lifetime,
                UIPrefabId.WTabPanelItem_Header, block.Container, idx++);
            titleW.Load(Texts.Accented2(currentEntry.TitleT, bold: false));

            currentEntry.Build(S, Lifetime, block, idx);
            UIUtils.RebuildScroll(widget.Scroll, true);
        }

        public override void LoadFooter(WGroup footer) {
            if (currentEntry == null) { return; }

            int idx = 0;
            if (history.Count > 0) {
                var target = history.Peek();
                var backBtn = UISpawn.Widget<WButtonIconText>(Lifetime,
                    UIPrefabId.WPanelButtonIcon, footer.Container, idx++);
                backBtn.Load(IconId.WArrowLeft, T.VerbBack, () => {
                    var page = history.Pop();
                    suppressHistory = true;
                    S.UIQuery.ShowCodexEntry.Ask(page);
                    suppressHistory = false;
                });
                UIUtils.AddTip(Lifetime, backBtn, $"{T.VerbBack}: {target.TitleT}");
            }

            var btn = UISpawn.Widget<WButtonIconText>(Lifetime,
                UIPrefabId.WPanelButtonIcon, footer.Container, idx++);
            btn.Load(null, null, () => {
                SetLearned(currentEntry, !currentEntry.IsLearned);
            });
            currentEntry.IsLearned.Bind(Lifetime, b => UpdateFooterButton(btn, b));
        }

        private void SetLearned(CodexConceptPage page, bool learned) {
            if (page.IsLearned.Set(learned)) {
                ProgressChanged.Invoke();
            }
        }

        private void UpdateFooterButton(WButtonIconText btn, bool isLearned) {
            var icon = isLearned ? IconId.CCross : IconId.CCheck;
            var text = isLearned ? T.CodexUnlearn : T.CodexMarkLearned;
            btn.SetImage(icon);
            btn.SetText(text);
        }

        private void EnsureVisibleSelection() {
            if (currentEntry != null && IsVisibleEntry(currentEntry)) {
                currentGroup = CodexConceptPage.FindGroup(currentEntry.GroupFromId) ?? currentGroup;
                return;
            }

            currentGroup = null;
            currentEntry = null;
            foreach (var group in CodexConceptPage.Groups) {
                if (!TryGetVisibleItems(group, out var items, out _)) {
                    continue;
                }

                currentGroup = group;
                currentEntry = items[0];
                return;
            }
        }

        private bool IsVisibleEntry(CodexConceptPage concept) {
            if (concept == null || concept.IsGroup) {
                return false;
            }

            var group = CodexConceptPage.FindGroup(concept.GroupFromId);
            return group != null
                && TryGetVisibleItems(group, out var items, out _)
                && items.Contains(concept);
        }

        private bool TryGetVisibleItems(CodexConceptPage group,
                out List<CodexConceptPage> visibleItems, out bool groupMatches) {
            visibleItems = null;
            groupMatches = false;

            if (!CodexConceptPage.ByGroup.TryGetValue(group.GroupFromId, out var items)) {
                return false;
            }

            if (!HasSearchQuery) {
                visibleItems = items;
                return items.Count > 0;
            }

            groupMatches = MatchesSearch(group.TitleT);
            visibleItems = new List<CodexConceptPage>(items.Count);
            foreach (var item in items) {
                if (groupMatches || MatchesSearch(item.TitleT) || item.OnSearchPageBody(SearchQuery)) {
                    visibleItems.Add(item);
                }
            }

            return visibleItems.Count > 0;
        }
    }
}
