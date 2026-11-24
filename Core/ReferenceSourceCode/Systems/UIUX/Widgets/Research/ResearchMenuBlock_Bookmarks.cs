using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Research;
using Game.Systems.Space;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class ResearchMenuBlock_Bookmarks : ResearchMenuBlock {
        public override int Order => 10;
        private bool bookmarksExpanded = true;
        private Lifetime lifetime;

        private sealed class BookmarkRow {
            public ResearchProgress Prog;
            public WContentButtonWithX Widget;
            public string TextPlain;
            public string TextGreen;
            // null when Def.Seasonal == null
            public string TextGrey;
            public Tooltip Tip;
        }
        private readonly List<BookmarkRow> rows = new(8);

        public ResearchMenuBlock_Bookmarks(ResearchMenuWidget parent,
            ResearchContext ctx, GameState s) : base(parent, ctx, s) { }

        public override void Load(int siblingIdx) {
            if (lifetime != null) {
                lifetime.Dispose();
            }
            lifetime = ctx.MenuLifetime.CreateChild("Bookmarks");
            // Covers both paths that kill the row widgets: the Dispose at the top of
            // Load(), and ResearchMenuWidget.Cleanup() cascading to this child.
            lifetime.OnDispose(rows.Clear);
            var block = UISpawn.Block(lifetime, parent.WItems, siblingIdx);
            int idx = 0;


            if (S.Sys.Research.IsAnythingBookmarked) {
                AddBookmarks(block, idx++);
            } else {
                AddNoBookmarks(block, idx++);
            }

        }
        private void AddNoBookmarks(WContentBlock block, int idx) {
            var header = UISpawn.Widget<WText>(lifetime, UIPrefabId.WContentHeader, block.Container, idx++);
            header.Load(T.Bookmarks);
            var w = UISpawn.Widget<WIconText>(lifetime, UIPrefabId.WContentText, block.Container, idx);
            w.Load(IconId.CBookmarkInactive, T.Empty);
            return;
        }
        private void AddBookmarks(WContentBlock block, int idx) {
            var header = UISpawn.Widget<WText>(lifetime, UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.Bookmarks);
            var w0 = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2ListButton, block.Container, idx);
            w0.Load(IconId.CBookmark, T.Bookmarks);
            w0.SetText2(Units.Num(S.Sys.Research.Bookmarks.Count));
            var listButton = w0.GetComponent<WContentListButton>();

            listButton.LoadOnToggle(lifetime, ToggleBookmarks, bookmarksExpanded);
        }

        private bool ToggleBookmarks(Lifetime life, RectTransform container, bool willShow, bool wasLoaded) {
            bookmarksExpanded = willShow;
            if (!willShow) { return wasLoaded; }
            if (!wasLoaded) {
                int idx = 0;
                foreach (var prog in S.Sys.Research.Bookmarks) {
                    AddBookmark(life, container, prog, idx++);
                }
                AddClearAll(life, container, idx++);
            }
            return true;
        }

        private void AddClearAll(Lifetime life, RectTransform tr, int idx) {
            var w2 = UISpawn.Widget<WButtonText>(life, UIPrefabId.WContent2ButtonSimple, tr, idx);
            w2.Load(Texts.Italic(T.ClearAll), () => {
                S.Sys.Research.ClearBookmarks();
            });
        }
        private void AddBookmark(Lifetime life, RectTransform block, ResearchProgress prog, int idx) {
            var w = UISpawn.Widget<WContentButtonWithX>(life, UIPrefabId.WContent2ButtonX,
                block, idx);

            var row = new BookmarkRow {
                Prog = prog,
                Widget = w,
                TextPlain = prog.Def.NameT,
                TextGreen = Texts.Green(prog.Def.NameT),
                TextGrey = prog.Def.Seasonal != null ? Texts.Grey(prog.Def.NameT) : null,
            };
            w.Button.Load(prog.Def.Preview, TextFor(row));
            if (prog.Def.Seasonal != null) {
                row.Tip = UIUtils.AddTip(life, w.Button,
                    T.ResearchSeasonalLocked_V1.F(prog.Def.Seasonal.RangeText));
            }
            w.Button.SetOnClick(() => {
                S.UIQuery.ShowInResearchTree.Ask(prog.Def);
            });
            w.XButton.SetOnClick(() => {
                S.Sys.Research.TryRemoveBookmark(prog, false);
                // Remove before Dispose so Refresh() never writes text into a widget
                // already returned to the pool.
                rows.Remove(row);
                row.Tip?.Dispose();
                w.Dispose();
                if (!S.Sys.Research.IsAnythingBookmarked) {
                    S.UIQuery.RebuildResearchMenu.Ask();
                }
            });
            rows.Add(row);
        }

        public override void Refresh() {
            for (int i = 0; i < rows.Count; i++) {
                var row = rows[i];
                row.Widget.Button.SetText(TextFor(row));
            }
        }

        private string TextFor(BookmarkRow row) {
            // Season wins over unlockable, matching WResearchNode's ordering.
            if (row.TextGrey != null && !row.Prog.Def.IsInSeason) { return row.TextGrey; }
            return S.Sys.Research.CanUnlockNow(row.Prog) ? row.TextGreen : row.TextPlain;
        }
    }
}
