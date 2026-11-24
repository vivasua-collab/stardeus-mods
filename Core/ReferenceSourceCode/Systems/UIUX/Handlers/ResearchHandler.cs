using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class ResearchHandler : BaseWidgetHandler<ResearchWidget> {
        private readonly ResearchContext ctx;

        public ResearchHandler(GameState s, ResearchWidget widget) : base(s, widget) {
            ctx = new ResearchContext();
            widget.Initialize(s, ctx);
            s.UIQuery.ShowResearch.SetResponder(ShowResearch);
            s.UIQuery.ShowInResearchTree.SetResponder(ShowInTree);
            s.UIQuery.RebuildResearchMenu.SetResponder(OnRebuildResearchMenu);

            The.SysSig.UIThemeReloaded.AddListener(OnThemeReloaded);
            s.Sig.CPUResourcesUpdated.AddListener(OnResourcesUpdated);
            s.Sig.ResearchChanged.AddListener(OnResearchChanged);
            s.Sig.DatoidCountsUpdated.AddListener(OnDatoidCountsUpdated);
        }

        private bool OnRebuildResearchMenu() {
            if (UIShowing.Research) {
                widget.RebuildMenu();
                return false;
            }
            return true;
        }

        private void OnResearchChanged(Def def) {
            widget.OnResearchChanged(def);
        }

        private void OnDatoidCountsUpdated(Dictionary<MatType, BInt> counts) {
            widget.OnResourcesUpdated(counts, A.Grid.TotalDisk);
        }

        private void OnResourcesUpdated(int cpu, int mem, int storage, int gpu) {
            var dc = S.Research.DatoidCounts;
            widget.OnResourcesUpdated(dc, storage);
        }

        private bool ShowInTree(Def def) {
            if (def == null) {
                D.Err("Trying to show null def in research tree");
                return false;
            }
            if (!UIShowing.Research) {
                ShowResearch(true);
            }
            return widget.FocusOnDef(def);
        }

        private bool ShowResearch(bool show) {
            if (!show && widget.IsLoadingNow) {
                UISounds.PlayActionDenied();
                return false;
            }
            if (show == widget.gameObject.activeSelf) {
                D.Err("Show research == same: {0}", show);
                // If we return here, any crash of the widget will render the game unplayable
                // return false;
            }
            if (show && UIShowing.Starmap) {
                S.UIQuery.ShowStarmap.Ask(false);
            }
            UIShowing.Set(UIShowingFlags.FullScreenWidget, show);
            UIShowing.Set(UIShowingFlags.Research, show);
            S.UIQuery.ToggleGameUI.Ask(!show, false);
            S.Sig.ToggleRendering.Send(!show);
            S.Sig.OnShowFullScreenWidget.Send(show);
            widget.Toggle(show);
            if (show) {
                OnOpen();
            } else {
                OnClose();
            }
            return true;
        }

        public void Update() {
            if (UIShowing.Research) {
                widget.UpdateWidget();
            }
        }

        private void OnClose() {
            S.Sys.Ship.RestoreStarfieldPos();
        }

        private void OnOpen() {
            if (UIShowing.QuickSearch) {
                S.UIQuery.ShowQuickSearch.Ask(false);
            }
            S.Toolbox.Clear();
            S.Sys.Ship.TempMoveStarfieldTo(Consts.V2Right);
        }

        public void Cleanup() {
            The.SysSig.UIThemeReloaded.RemoveListener(OnThemeReloaded);
        }

        private void OnThemeReloaded() {
            widget.RefreshTheme();
        }
    }
}
