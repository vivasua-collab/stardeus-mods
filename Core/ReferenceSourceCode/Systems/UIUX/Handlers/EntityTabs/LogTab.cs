using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class LogTab : BaseEntityTab {
        public const string TabId = "Log";
        private const int PageSize = 100;

        [SelfInit]
        private static void Register() {
            RegisterTab(new LogTab());
        }

        private GameState S;
        private BeingData being;
        private Tile tile;
        private ObjData obj;

        public override string Id => TabId;
        public override int Order => 90;
        public override string TabName => T.EventLogShort;

        public override bool ShowFor(GameState s, BeingData being) {
            S = s;
            this.being = being;
            tile = null;
            obj = null;
            var entries = s.Query.GetBeingLogEntries.Ask(being.Id);
            return entries != null && entries.Count > 0;
        }

        public override bool ShowFor(GameState s, Tile tile) {
            S = s;
            this.tile = tile;
            being = null;
            obj = null;
            var entries = s.Query.GetTileLogEntries.Ask(tile.Id);
            return entries != null && entries.Count > 0;
        }

        public override bool ShowFor(GameState s, ObjData obj) {
            S = s;
            this.obj = obj;
            being = null;
            tile = null;
            var entries = s.Query.GetObjLogEntries.Ask(obj.Id);
            return entries != null && entries.Count > 0;
        }

        public override bool LoadContent(WGroup group) {
            var entries = GetEntries();
            if (entries == null || entries.Count == 0) { return false; }

            var block = UISpawn.Block(lifetime, group, 0);
            int idx = 0;

            int loadCount = Math.Min(PageSize, entries.Count);
            for (int i = entries.Count - 1; i >= entries.Count - loadCount; i--) {
                var textWidget = UISpawn.Widget<WText>(lifetime,
                    UIPrefabId.WContent2TextBlock, block.Container, idx++);
                textWidget.Load(entries[i].Text);
            }

            if (entries.Count > PageSize) {
                var scroll = block.Container.GetComponentInParent<ScrollRect>();
                var btn = UISpawn.Widget<WButtonText>(lifetime,
                    UIPrefabId.WContent2ButtonSimple, block.Container, idx);
                LoadMoreAction(btn, block.Container, entries, entries.Count - loadCount, scroll);
            }

            return true;
        }

        private void LoadMoreAction(WButtonText btn, UnityEngine.RectTransform container,
                List<LogEntry> entries, int remainingStart, ScrollRect scroll) {
            int remaining = remainingStart;
            btn.Load(Texts.Italic($"+{remaining}"), () => {
                int sibIdx = btn.transform.GetSiblingIndex();
                int batch = Math.Min(PageSize, remaining);
                int fromIdx = remaining - batch;
                for (int i = 0; i < batch; i++) {
                    var textWidget = UISpawn.Widget<WText>(lifetime,
                        UIPrefabId.WContent2TextBlock, container, sibIdx + i);
                    textWidget.Load(entries[fromIdx + i].Text);
                }
                int newRemaining = remaining - batch;
                if (newRemaining > 0) {
                    btn.transform.SetSiblingIndex(sibIdx + batch);
                    LoadMoreAction(btn, container, entries, newRemaining, scroll);
                } else {
                    btn.SetActiveFast(false);
                }
                if (scroll != null) {
                    UIUtils.RebuildScroll(scroll, false);
                }
            });
        }

        public override bool LoadFooter(WGroup group) {
            int idx = 0;
            var clearBtn = UISpawn.Widget<WButtonIconText>(lifetime,
                UIPrefabId.WPanelButtonIcon, group.Container, idx++);
            clearBtn.Load(IconId.CStop, T.ClearHistory, () => {
                ClearCurrentEntity();
                ReloadPanel();
            });

            var logBtn = UISpawn.Widget<WButtonText>(lifetime,
                UIPrefabId.WPanelButton, group.Container, idx++);
            logBtn.Load(T.OverlayTipEventlog, () => {
                A.S.UIQuery.ToggleLog.Ask(!UIShowing.EventLog);
            });

            return true;
        }

        private void ClearCurrentEntity() {
            if (being != null) {
                S.Sys.Log.ClearEntityEntries(LogEntry.TypeBeing, being.Id);
            } else if (tile != null) {
                S.Sys.Log.ClearEntityEntries(LogEntry.TypeTile, (uint)tile.Id);
            } else if (obj != null) {
                S.Sys.Log.ClearEntityEntries(LogEntry.TypeObj, obj.Id);
            }
        }

        private List<LogEntry> GetEntries() {
            if (being != null) { return S.Query.GetBeingLogEntries.Ask(being.Id); }
            if (tile != null) { return S.Query.GetTileLogEntries.Ask(tile.Id); }
            if (obj != null) { return S.Query.GetObjLogEntries.Ask(obj.Id); }
            return null;
        }

        protected override void Cleanup() {
            S = null;
            being = null;
            tile = null;
            obj = null;
        }
    }
}
