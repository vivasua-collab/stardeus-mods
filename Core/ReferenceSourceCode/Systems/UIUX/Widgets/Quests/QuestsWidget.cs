using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Input;
using Game.Systems.Quests;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class QuestsWidget : BaseWidget, ICloseableWidget {
        private const int PageSize = 50;

        [SerializeField] private WButtonText wPanelHeader;
        [SerializeField] private WTabRow wTabs;
        [SerializeField] private ScrollRect scroll;
        [SerializeField] private WGroup wItems;
        [SerializeField] private CanvasGroup subPanelCG;
        public SubPanelWidget SubPanel;
        public WGroup WItems => wItems;
        private QuestContext ctx;
        private Lifetime lifetime;
        private Lifetime footLife;
        private GameState S;
        private readonly Dictionary<string, WTabButton> tabs = new();
        private WTabButton hubTab;
        public void Initialize(GameState s, QuestContext ctx) {
            gameObject.SetActiveFast(false);
            Register(UIWidgetId.Quests, this);
            S = s;
            this.ctx = ctx;
            wPanelHeader.Load(T.Quests, Close);
            wTabs.DestroyChildren();
            wItems.DestroyChildren();
            SubPanel.Initialize();
            CreateTabs();
        }

        private void CreateTabs() {
            int idx = 0;
            tabs.Clear();
            CreateTab(idx++, T.NounQuestHub);
            CreateTab(idx++, T.AdjCurrent);
            CreateTab(idx++, T.NounArchive);
            hubTab = tabs[T.NounQuestHub];
            hubTab.gameObject.SetActiveFast(false);
        }

        private void CreateTab(int idx, string name) {
            // Tabs have no lifetime, they will last for the whole game
            var prefab = ResourceLibrary.FindPrefab<WTabButton>(UIPrefabId.WTabButton);
            var w = PrefabPool.Spawn(prefab, wTabs.Container);

            w.Load(name, () => {
                ctx.SelectedQuest = null;
                scroll.verticalNormalizedPosition = 1f;
                ctx.ShowTab(name);
            });
            w.SetSiblingIdx(idx++);
            tabs[name] = w;
        }

        public override void Toggle(bool show) {
            if (show) {
                UIShowing.Set(UIShowingFlags.Quests);
            } else {
                UIShowing.Unset(UIShowingFlags.Quests);
            }
            base.Toggle(show);
            if (show) {
                Load();
                Rect.SetAsLastSibling();
            } else {
                Cleanup();
                ctx.SelectedQuest = null;
                ctx.SelectedTab = null;
                scroll.verticalNormalizedPosition = 1f;
                ctx.ClearHub();
            }
        }


        public void Load() {
            D.Warn("Loading quests widget for tab {0}", ctx.SelectedTab);
            Cleanup();
            lifetime = ctx.MainLifetime = Lifetime.Create("Quests");

            if (ctx.SelectedTab == null) {
                ctx.SelectedTab = T.AdjCurrent;
            }
            bool showHub = ctx.HubQuests != null;
            hubTab.gameObject.SetActiveFast(showHub);
            if (!showHub && ctx.SelectedTab == T.NounQuestHub) {
                ctx.SelectedTab = T.AdjCurrent;
            }
            foreach (var tab in tabs) {
                tab.Value.SetSelected(tab.Key == ctx.SelectedTab);
            }

            int idx = 0;
            var all = S.Query.GetAllPlayerQuests.Ask();
            int added = 0;
            if (ctx.SelectedTab == T.AdjCurrent) {
                added += AddQuests(all, lifetime, wItems, QuestState.Available, T.AdjAvailable, ref idx);
                added += AddQuests(all, lifetime, wItems, QuestState.Active, T.QuestsActive, ref idx);
            } else if (ctx.SelectedTab == T.NounArchive) {
                added += AddQuests(all, lifetime, wItems, QuestState.Completed, T.QuestsCompleted, ref idx, true);
                added += AddQuests(all, lifetime, wItems, QuestState.Failed, T.QuestsFailed, ref idx, true);
                added += AddQuests(all, lifetime, wItems, QuestState.Rejected, T.AdvertRejected, ref idx, true);
            } else if (ctx.SelectedTab == T.NounQuestHub && ctx.HubQuests != null) {
                added += AddQuests(ctx.HubQuests, lifetime, wItems, QuestState.Undefined, T.NounAvailableQuests, ref idx);
            } else {
                D.Err("Unknown quests tab: {0}", ctx.SelectedTab);
            }

            if (added == 0) {
                var empty = UISpawn.Widget<WIconText>(lifetime,
                    UIPrefabId.WContentText, wItems.Container, idx++);
                empty.Load(IconId.CInfo, T.Empty);
            }
            if (ctx.AfterLoadWidget != null) {
                ctx.AfterLoadWidget();
                ctx.AfterLoadWidget = null;
            }
            S.UISig.QuestsContentChanged.Send();
        }
        private int AddQuests(List<Quest> all, Lifetime life, WGroup group,
                QuestState state, string label,
                ref int blockIdx,
                bool reverse = false) {

            var matching = new List<Quest>();
            if (reverse) {
                for (int i = all.Count - 1; i >= 0; i--) {
                    var q = all[i];
                    if (q.Data.State == state) matching.Add(q);
                }
            } else {
                foreach (var q in all) {
                    if (q.Data.State == state) matching.Add(q);
                }
            }

            if (matching.Count == 0) return 0;

            var block = UISpawn.Block(life, group, blockIdx++);
            int idx = 0;
            var wLabel = UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header, block.Container, idx++);
            wLabel.Load(label);

            int loadCount = Math.Min(PageSize, matching.Count);
            for (int i = 0; i < loadCount; i++) {
                AddQuestItemToBlock(life, block.Container, idx++, matching[i]);
            }

            if (matching.Count > loadCount) {
                var btn = UISpawn.Widget<WButtonText>(life, UIPrefabId.WContent2ButtonSimple, block.Container, idx);
                LoadMoreQuestsAction(btn, life, block.Container, matching, loadCount);
            }

            return matching.Count;
        }

        private void LoadMoreQuestsAction(WButtonText btn, Lifetime life,
                RectTransform container, List<Quest> quests, int startIdx) {
            int remaining = quests.Count - startIdx;
            btn.Load(Texts.Italic($"+{remaining}"), () => {
                int sibIdx = btn.transform.GetSiblingIndex();
                int batch = Math.Min(PageSize, remaining);
                for (int i = 0; i < batch; i++) {
                    AddQuestItemToBlock(life, container, sibIdx + i, quests[startIdx + i]);
                }
                int newStartIdx = startIdx + batch;
                if (newStartIdx < quests.Count) {
                    btn.transform.SetSiblingIndex(sibIdx + batch);
                    LoadMoreQuestsAction(btn, life, container, quests, newStartIdx);
                } else {
                    btn.SetActiveFast(false);
                }
                UIUtils.RebuildScroll(scroll, false);
            });
        }

        private void AddQuestItemToBlock(Lifetime life, RectTransform container, int idx, Quest q) {
            var isAvailable = q.Data.State == QuestState.Available;
            var isTracked = S.Query.IsTrackedQuest.Ask(q);
            var icon = QuestUtils.IconFor(q, isTracked);
            var title = QuestUtils.DecoratedTitle(q.Data);
            if (isTracked) {
                title = $"{title} {Texts.Accented(GlyphMap.TriangleLeft)}";
            }

            if (isAvailable) {
                var w = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, container, idx);
                w.Load(icon, title, Texts.Accented2(T.QuestNewLabel), () => ShowQuest(w, q));
                if (ctx.SelectedQuest == q) {
                    ctx.AfterLoadWidget = () => {
                        ShowSelectedQuest(q, w);
                    };
                }
            } else {
                q.GetProgress(out var done, out var total);
                var w = UISpawn.Widget<WButtonIconText2Progress>(life, UIPrefabId.WContent2ProgressButton, container, idx);
                w.Load(icon, title, done / (float)total, () => ShowQuest(w, q), Units.XOutOfY(done, total));
                if (ctx.SelectedQuest == q) {
                    ctx.AfterLoadWidget = () => {
                        ShowSelectedQuest(q, w);
                    };
                }
            }
        }

        private void ShowSelectedQuest(Quest q, WBase w) {
            LayoutRebuilder.ForceRebuildLayoutImmediate(wItems.Container);
            Canvas.ForceUpdateCanvases();
            ShowQuest(w, q, true);
        }

        private void ScrollFullyIntoView(RectTransform target) {
            Canvas.ForceUpdateCanvases();
            var viewport = scroll.viewport;
            var viewportRect = viewport.rect;
            var targetBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
                viewport, target);

            float clippedDistance;
            if (targetBounds.min.y < viewportRect.yMin) {
                clippedDistance = targetBounds.min.y - viewportRect.yMin;
            } else if (targetBounds.max.y > viewportRect.yMax) {
                clippedDistance = targetBounds.max.y - viewportRect.yMax;
            } else {
                return;
            }

            var contentBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
                viewport, scroll.content);
            var scrollableHeight = contentBounds.size.y - viewportRect.height;
            if (scrollableHeight <= 0f) {
                return;
            }

            scroll.StopMovement();
            scroll.verticalNormalizedPosition = Mathf.Clamp01(
                scroll.verticalNormalizedPosition + clippedDistance / scrollableHeight);
        }

        public void ShowQuest(Quest q) {
            ConsiderAutoAdd(q);
            scroll.verticalNormalizedPosition = 1f;
            ctx.SelectedQuest = q;
            ctx.SelectedTab = null;
            Toggle(true);
        }

        public void ShowQuestHub(List<Quest> quests) {
            ctx.HubQuests = quests;
            ctx.SelectedTab = T.NounQuestHub;
            Toggle(true);
        }

        private void ShowQuest(WBase w, Quest q, bool keepVisibleAfterLoad = false) {
            if (SubPanel.IsShowing && ctx.SelectedQuest == q) {
                ctx.SelectedQuest = null;
                SubPanel.Close();
                return;
            }
            ConsiderAutoAdd(q);
            ctx.SelectedQuest = q;
            if (q.IsActive && !S.Query.IsTrackedQuest.Ask(q)) {
                S.Query.TrackQuest.Ask(q, true);
                return; // ReloadUI → AfterLoadWidget will reopen with fresh references
            }
            SubPanel.SetTitle(T.NounQuest);
            if (ctx.MenuLifetime != null) {
                ctx.MenuLifetime.Dispose();
            }
            var life = ctx.MenuLifetime = lifetime.CreateChild("QuestDetails");
            QuestDetailsUI.LoadBlock(life, SubPanel.Container, q);

            LoadQuestFooter(life, w, q);

            SubPanel.Show(w, true);
            S.UISig.QuestsContentChanged.Send();
            // On a cold-start open (e.g. via the QuestWidget "show available quests"
            // link), the SubPanel's first activation happens before Unity has run a
            // layout pass on it, so RepositionWidget above may compute against stale
            // geometry. Schedule a safety-net reposition once layout has settled.
            Coroutines.OnNextFrame(() => {
                if (SubPanel.IsShowingFor(w)) {
                    if (keepVisibleAfterLoad && ctx.SelectedQuest == q) {
                        ScrollFullyIntoView(w.Rect);
                    }
                    SubPanel.RepositionWidget(w, connectorOnly: false);
                }
            });
        }

        private bool ConsiderAutoAdd(Quest q) {
            if (q.IsAutoAdd && q.IsAvailable) {
                // auto activate main quests
                S.Query.AddQuest.Ask(q.Data, true);
                return true;
            }
            return false;
        }

        private void LoadQuestFooter(Lifetime life, WBase w, Quest q) {
            if (footLife != null) {
                footLife.Dispose();
            }
            footLife = life.CreateChild("QuestFooter");
            int idx = 0;
            var isHubQuest = ctx.HubQuests != null && q.Data.State == QuestState.Undefined;
            if (q.IsAvailable || isHubQuest) {
                var acceptBtn = UISpawn.Widget<WButtonText>(footLife, UIPrefabId.WPanelButton_Confirm,
                    SubPanel.Footer.Container, idx++);
                acceptBtn.Load(T.VerbAccept, () => {
                    S.Query.AddQuest.Ask(q.Data, true);
                    // ShowQuest(w, q);
                });
                if (q.Data.Def.IsMain && !q.Data.Def.Flags.HasFlag(QuestFlags.AutoAdd)) {
                    var rejectBtn = UISpawn.Widget<WButtonText>(footLife, UIPrefabId.WPanelButton_Red,
                        SubPanel.Footer.Container, idx++);
                    rejectBtn.Load(T.Reject, () => {
                        q.Data.SetState(QuestState.Rejected);
                        SubPanel.Close();
                        Load();
                    });
                }
            }
            if (q.IsEnded) {
                var removeBtn = UISpawn.Widget<WButtonText>(footLife, UIPrefabId.WPanelButton,
                    SubPanel.Footer.Container, idx++);
                removeBtn.Load(T.Remove, () => {
                    q.Data.SetState(QuestState.Deleted);
                    SubPanel.Close();
                    Load();
                });
            } else if (!q.IsMain && !isHubQuest) {
                // A plain content button, not a panel button: as a full-size panel button it
                // read as the call to action, and playtesters kept hovering it while looking
                // for what to do next
                var abandonBtn = UISpawn.Widget<WButtonText>(footLife,
                    UIPrefabId.WContent2ButtonSimple, SubPanel.Footer.Container, idx++);
                var isTutorial = q.Data.Def.Flags.HasFlag(QuestFlags.Tutorial);
                abandonBtn.Load(Texts.Italic(Texts.Grey(T.Abandon)), () => {
                    var qc = QuickChoice.Create(T.Confirm);
                    qc.AddButton(T.Yes, () => {
                        q.Data.SetState(QuestState.Rejected);
                        SubPanel.Close();
                        Load();
                    });
                    if (isTutorial) {
                        qc.AddButton(SkipAllLabel, ConfirmSkipTutorial);
                    }
                    S.UIQuery.ShowQuickChoice.Ask(qc);
                });
                if (isTutorial) {
                    UIUtils.AddTip(footLife, abandonBtn, T.AbandonTutorialQuestTip);
                }
            }
            SubPanel.ShowFooter(idx > 0);
        }

        // Next to Yes in the Abandon choice: abandon all of them
        public static string SkipAllLabel => $"{T.All} ({T.SkipTutorial})";

        // Abandons every tutorial quest, offered or not, as `quest skip_tutorials` does. It
        // cannot be undone, so it asks once more.
        private void ConfirmSkipTutorial() {
            The.Dialog.ShowChoice(IconId.CComputing, T.SkipTutorialConfirm,
                left: null,
                right: () => {
                    S.Sig.SkipTutorial.Send();
                    SubPanel.Close();
                    Load();
                },
                leftButtonText: T.Cancel,
                rightButtonText: T.SkipTutorial,
                canCloseWithEsc: true);
        }

        public void Close() {
            S.UIQuery.ShowQuests.Ask(false);
        }

        public void Cleanup() {
            footLife?.Dispose();
            footLife = null;
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
            ctx.MainLifetime = null;
            ctx.MenuLifetime?.Dispose();
            ctx.MenuLifetime = null;
            SubPanel.Close();
        }

        public bool TryCloseWidget() {
            if (SubPanel.IsShowing) {
                SubPanel.Close();
                ctx.SelectedQuest = null;
                return true;
            }
            Toggle(false);
            return true;
        }
    }
}
