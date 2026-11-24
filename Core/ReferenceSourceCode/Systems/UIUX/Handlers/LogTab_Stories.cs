using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.Story;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    internal sealed class LogTab_Stories {
        private const int PageSize = 100;

        private readonly GameState S;
        private readonly LogWidget widget;
        private Lifetime contentLifetime;
        private Action onRefreshRequested;

        internal LogTab_Stories(GameState s, LogWidget widget) {
            S = s;
            this.widget = widget;
            s.Sig.StoryEventStateChanged.AddListener(OnStoryEventStateChanged);
        }

        internal void Activate(Action onRefreshRequested) {
            this.onRefreshRequested = onRefreshRequested;
        }

        internal void Deactivate() {
            onRefreshRequested = null;
        }

        internal void Refresh(Lifetime lifetime) {
            contentLifetime = lifetime;
            var ctx = S.Query.GetStoryContext.Ask();
            if (ctx == null) { return; }

            int blockIdx = 0;
            var searchQuery = widget.SearchQuery;

            var ongoing = new List<StoryEventData>();
            for (int i = ctx.OngoingEvents.Count - 1; i >= 0; i--) {
                var data = ctx.OngoingEvents[i];
                if (data.State != StoryEventState.Ongoing) { continue; }
                var log = data.LogEntry;
                if (log.Hide || !log.IsValid) { continue; }
                if (!MatchesSearch(log.Title, searchQuery)) { continue; }
                ongoing.Add(data);
            }
            if (ongoing.Count > 0) {
                var ongoingBlock = UISpawn.Block(contentLifetime, widget.Content, blockIdx++);
                int idx = 0;
                var header = UISpawn.Widget<WText>(contentLifetime,
                    UIPrefabId.WContent2Header, ongoingBlock.Container, idx++);
                header.Load(T.AdjOngoing);
                int loadCount = Math.Min(PageSize, ongoing.Count);
                for (int i = 0; i < loadCount; i++) {
                    SpawnEventItem(ongoing[i], ongoingBlock.Container, idx++);
                }
                if (ongoing.Count > PageSize) {
                    var btn = UISpawn.Widget<WButtonText>(contentLifetime,
                        UIPrefabId.WContent2ButtonSimple, ongoingBlock.Container, idx);
                    LoadMoreEventItems(btn, ongoingBlock.Container, ongoing, loadCount);
                }
            }

            var history = new List<StoryEventData>();
            for (int i = ctx.PastEvents.Count - 1; i >= 0; i--) {
                var data = ctx.PastEvents[i];
                var log = data.LogEntry;
                if (log.Hide || !log.IsValid) { continue; }
                if (!MatchesSearch(log.Title, searchQuery)) { continue; }
                history.Add(data);
            }
            var historyBlock = UISpawn.Block(contentLifetime, widget.Content, blockIdx);
            int histIdx = 0;
            var histHeader = UISpawn.Widget<WText>(contentLifetime,
                UIPrefabId.WContent2Header, historyBlock.Container, histIdx++);
            histHeader.Load(T.NounHistory);
            int histLoadCount = Math.Min(PageSize, history.Count);
            for (int i = 0; i < histLoadCount; i++) {
                SpawnEventItem(history[i], historyBlock.Container, histIdx++);
            }
            if (history.Count > PageSize) {
                var btn = UISpawn.Widget<WButtonText>(contentLifetime,
                    UIPrefabId.WContent2ButtonSimple, historyBlock.Container, histIdx);
                LoadMoreEventItems(btn, historyBlock.Container, history, histLoadCount);
            }
        }

        private void LoadMoreEventItems(WButtonText btn, RectTransform container,
                List<StoryEventData> entries, int loadedCount) {
            btn.Load(Texts.Italic($"+{entries.Count - loadedCount}"), () => {
                int sibIdx = btn.transform.GetSiblingIndex();
                int batch = Math.Min(PageSize, entries.Count - loadedCount);
                for (int i = 0; i < batch; i++) {
                    SpawnEventItem(entries[loadedCount + i], container, sibIdx + i);
                }
                int newLoaded = loadedCount + batch;
                if (newLoaded < entries.Count) {
                    btn.transform.SetSiblingIndex(sibIdx + batch);
                    LoadMoreEventItems(btn, container, entries, newLoaded);
                } else {
                    btn.gameObject.SetActiveFast(false);
                }
                UIUtils.RebuildScroll(widget.ScrollRect, false);
            });
        }

        private void SpawnEventItem(StoryEventData data, RectTransform container, int siblingIdx) {
            var log = data.LogEntry;
            var timeAgo = Units.TicksAgo((int)(G.Ticks - data.StartTick));
            var listBtn = UISpawn.Widget<WContentListButton>(contentLifetime,
                UIPrefabId.WContent2ListButton, container, siblingIdx);
            listBtn.Load(log.Icon, log.Title, timeAgo);
            var capturedData = data;
            var capturedLog = log;
            listBtn.LoadOnToggle(contentLifetime, (life, innerContainer, willShow, wasLoaded) => {
                if (!willShow) { return wasLoaded; }
                if (wasLoaded) { return true; }
                LoadEventDetails(life, innerContainer, capturedData, capturedLog);
                return true;
            });
        }

        private void LoadEventDetails(Lifetime life, RectTransform container,
                StoryEventData data, StoryLogEntry log) {
            int idx = 0;
            var descWidget = UISpawn.Widget<WText>(life, UIPrefabId.WContent2TextBlock, container, idx++);
            descWidget.Load(log.Desc);

            if (data.Location != null) {
                var locWidget = UISpawn.Widget<WIconText>(life, UIPrefabId.WContent2Text, container, idx++);
                locWidget.Load(IconId.CDestination, data.Location.SO.ParentPath(false));
            }

            var timeAgo = Units.TicksAgo((int)(G.Ticks - data.StartTick));
            if (data.IsEnded) {
                timeAgo = T.EndedAgo_V1.F(timeAgo);
                timeAgo = Texts.GreenParentheses(timeAgo);
            } else {
                timeAgo = Texts.GreyParentheses(timeAgo);
            }
            var timeWidget = UISpawn.Widget<WIconText>(life, UIPrefabId.WContent2Text, container, idx++);
            timeWidget.Load(IconId.CTime, $"{Units.TicksToTime(data.StartTick)} {timeAgo}");

            if (data.IsEnded) {
                var dur = data.EndTick - data.StartTick;
                if (dur > 0) {
                    var durWidget = UISpawn.Widget<WIconText>(life, UIPrefabId.WContent2Text, container, idx++);
                    durWidget.Load(IconId.CTime, T.ConditionDuration_V1.F(Units.TicksToTime(dur)));
                }
            }

            var seenBeingIds = new HashSet<uint>();
            if (data.Participants != null) {
                foreach (var part in data.Participants) {
                    if (part.Owner != null && seenBeingIds.Add(part.Id)) {
                        var beingWidget = UISpawn.Widget<WIconText>(life, UIPrefabId.WContent2Text, container, idx++);
                        beingWidget.Load((Sprite)null, part.Owner.Name);
                        part.Owner.Avatar.Load(beingWidget.Image);
                    }
                }
            }
            if (data.TargetBeing != null && seenBeingIds.Add(data.TargetBeing.Mind?.Id ?? 0)) {
                var beingWidget = UISpawn.Widget<WIconText>(life, UIPrefabId.WContent2Text, container, idx++);
                beingWidget.Load((Sprite)null, data.TargetBeing.Name);
                data.TargetBeing.Avatar.Load(beingWidget.Image);
            }

            if (data.TargetTile?.IsActive == true) {
                var tileWidget = UISpawn.Widget<WIconText>(life, UIPrefabId.WContent2Text, container, idx++);
                tileWidget.Load(data.TargetTile.Definition.PreviewSprite, data.TargetTile.EntityName);
            }

            if (data.TargetObj?.Obj != null) {
                var objWidget = UISpawn.Widget<WIconText>(life, UIPrefabId.WContent2Text, container, idx++);
                objWidget.Load(data.TargetObj.Def.PreviewSprite, data.TargetObj.ListName);
            }

            if (!data.IsEnded) {
                var removeBtn = UISpawn.Widget<WButtonText>(life,
                    UIPrefabId.WContent2ButtonSimple, container, idx++);
                removeBtn.Load(T.Remove, () => {
                    data.LogEntry.Hide = true;
                    onRefreshRequested?.Invoke();
                });
            }
        }

        private static bool MatchesSearch(string text, string searchQuery) {
            if (searchQuery == null) { return true; }
            var stripped = Strings.ToSearchString(Texts.StripTags(text), allowNull: true);
            return stripped != null && stripped.Contains(searchQuery);
        }

        private void OnStoryEventStateChanged(StoryEventData data) {
            if (onRefreshRequested == null || !widget.gameObject.activeSelf) { return; }
            if (data.State == StoryEventState.Ongoing || data.State == StoryEventState.Ended) {
                onRefreshRequested();
            }
        }
    }
}
