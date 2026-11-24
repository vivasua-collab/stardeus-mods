using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Systems.Space;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class ResearchMenuWidget : BaseWidget {
        [SerializeField] private WButtonText wPanelHeader;
        [SerializeField] private ScrollRect scroll;
        [SerializeField] private WGroup wItems;
        public SubPanelWidget SubPanel;
        public WGroup WItems => wItems;
        private Lifetime lifetime;
        private GameState S;
        private ResearchContext ctx;
        private readonly List<ResearchMenuBlock> menuBlocks = new();
        private bool menuBlocksSorted;
        public void Initialize(GameState s, ResearchContext ctx) {
            S = s;
            this.ctx = ctx;
            wPanelHeader.Load(T.OverlayTipResearch, Close);
            wItems.DestroyChildren();
            SubPanel.Initialize();
            gameObject.SetActiveFast(false);
            CreateMenuBlocks(s);
        }

        private void CreateMenuBlocks(GameState s) {
            menuBlocks.Add(new ResearchMenuBlock_Resources(this, ctx, s));
            menuBlocks.Add(new ResearchMenuBlock_Bookmarks(this, ctx, s));
            // mods can hook in here
        }


        public void Load() {
            Cleanup();
            ctx.MenuLifetime = lifetime = ctx.MainLifetime.CreateChild("ResearchMenu");

            var ploc = SOUtils.GetPlayerSector(S);
            var text = ploc.SO.ParentPath(false);
            scroll.verticalNormalizedPosition = 1f;

            int idx = 0;

            if (!menuBlocksSorted) {
                menuBlocks.Sort((a, b) => a.Order.CompareTo(b.Order));
                menuBlocksSorted = true;
            }

            foreach (var block in menuBlocks) {
                block.Load(idx);
                idx++;
            }
            if (!gameObject.activeSelf) {
                gameObject.SetActiveFast(true);
                UISounds.PlayHover(UISounds.HoverType.Lowest);
            }
        }

        public void Refresh() {
            foreach (var block in menuBlocks) {
                block.Refresh();
            }
        }

        public void Close() {
            S.UIQuery.ShowResearch.Ask(false);
        }

        public void Cleanup() {
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
            ctx.MenuLifetime = null;
            SubPanel.Close();
        }
    }
}
