using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Input;
using Game.Systems.Ships;
using Game.UI;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class StarmapHandler : BaseWidgetHandler<StarmapWidget> {
        private readonly StarmapContext ctx;
        private readonly StarmapMenuWidget menuWidget;
        private readonly BInt fuelSector = new();
        private readonly BInt fuelRegion = new();
        private readonly MatType fuelSectorType;
        private readonly MatType fuelRegionType;
        public StarmapHandler(GameState s, StarmapWidget widget) : base(s, widget) {
            ctx = new StarmapContext();
            menuWidget = widget.MenuWidget;

            fuelSectorType = MatType.FastGet(MatIdH.FusionCell);
            fuelRegionType = MatType.FastGet(MatIdH.FusionCellHeavy);

            widget.Initialize(s, ctx);
            widget.MenuWidget.BindFuel(fuelSectorType, fuelRegionType, fuelSector, fuelRegion);
            s.UIQuery.ShowStarmap.SetResponder(ShowStarmap);
            s.UIQuery.GetStarmapContext.SetResponder(() => ctx);
            s.UIQuery.StarmapFocusOn.SetResponder(StarmapFocusOn);
            s.UIQuery.StarmapCloseTo.SetResponder(CloseTo);
            s.UIQuery.ClickUpInStarmap.SetResponder(ClickUp);
            s.UIQuery.RebuildStarmap.SetResponder(OnRebuild);
            s.UIQuery.IsStarmapPosNearMouse.SetResponder(IsNearMouse);

            // S.Sig.ExpeditionStarted.AddListener(OnExpedition);
            // S.Sig.ExpeditionEnded.AddListener(OnExpedition);
            // S.Sig.CopterMissionStarted.AddListener(OnCopterMission);
            // S.Sig.CopterMissionEnded.AddListener(OnCopterMission);
            S.Sig.SpaceObjectAdded.AddListener(OnSOAdd);
            S.Sig.SpaceObjectRemoved.AddListener(OnSORemove);
            S.Sig.SpaceObjectChanged.AddListener(OnSOChanged);
            S.Sig.UpdateSpaceObjectUI.AddListener(UpdateUISpaceObject);
            S.UISig.OnHoverUISpaceObject.AddListener(OnHoverUISpaceObject);
            S.Sig.SpaceObjectEnter.AddListener(OnEnterSpaceObject);
            S.Sig.HyperjumpQueueChanged.AddListener(OnHyperjumpQueueChange);
            S.Sig.MatCountChanged.AddListener(OnMatCountChanged);

            S.Clock.OnTick.AddListener(OnTick);
        }

        private void OnSOChanged(SpaceObject so) {
            so.UI?.Reload();
        }

        private void OnTick(long ticks) {
            if (UIShowing.QuickChoice) {
                foreach (var act in Caches.SOActions) {
                    if (act is ITickListener tl) {
                        tl.Tick(ticks);
                    }
                }
            }
        }

        private bool IsNearMouse(Vector2 pos) {
            return widget.IsNearMouse(pos);
        }

        private void OnMatCountChanged(Dictionary<MatType, int> counts) {
            if (counts.TryGetValue(fuelSectorType, out var c1)) {
                fuelSector.Set(c1);
            } else {
                fuelSector.Set(0);
            }
            if (counts.TryGetValue(fuelRegionType, out var c2)) {
                fuelRegion.Set(c2);
            } else {
                fuelRegion.Set(0);
            }
        }

        public void Update() {
            widget.UpdateWidget();
        }

        private bool ClickUp(ActionType act) {
            if (act == ActionType.ClickCancel) {
                return widget.TryTravelToMouse();
            }
            return false;
        }

        private void OnHyperjumpQueueChange(HyperjumpQueue q, bool needsRedraw) {
            if (UIShowing.Starmap) {
                if (needsRedraw) {
                    widget.RedrawHyperjumpQueue(q);
                }
                menuWidget.Load();
            }
        }

        private List<UISpaceObject> CloseTo(UISpaceObject soUI) {
            if (!UIShowing.Starmap) {
                D.Err("Trying to get uiso close to without starmap showing!");
            }
            return widget.CloseTo(soUI);
        }

        private bool StarmapFocusOn(SpaceObject obj, bool openStarmapIfClosed) {
            bool wasShowing = UIShowing.Starmap;
            if (!wasShowing) {
                if (openStarmapIfClosed) {
                    ShowStarmap(true);
                } else {
                    return false;
                }
            }
            // Switch view if the target is on a different layer type, OR if it
            // lives in a different container than the one we're currently showing
            // (e.g. a sector-content item in a different sector/region while we
            // look at the Sector map - same layer type but a different parent).
            if (obj.Parent == null) {
                if (obj.IsShip) {
                    obj = obj.Ship.ParentSector;
                }
                if (obj?.Parent == null) {
                    D.Err("Can't focus on object that has no parent: {0}", obj);
                    return false;
                }
            }
            // Rebuild can only show the player's own sector / region, and neither
            // the Sector layer in hyperspace nor the Region layer during a
            // cross-region hyperjump. Climb to the closest visible ancestor.
            obj = ClosestVisibleAncestor(obj);
            if (obj == null) {
                return false;
            }
            var needsRebuild = obj.Parent.Layer != ctx.Layer || obj.Parent != ctx.ParentSO;
            if (needsRebuild) {
                ctx.Layer = obj.Parent.Layer;
            }
            if (!needsRebuild && wasShowing) {
                return widget.FocusOn(obj);
            }
            if (needsRebuild) {
                OnRebuild(false, true);
            }
            // Wait a frame for the rebuilt starmap UI to settle before focusing.
            Coroutines.OnNextFrame(() => widget.FocusOn(obj));
            return true;
        }

        // Must mirror how StarmapWidget.Rebuild derives ParentSO from the layer
        private SpaceObject ClosestVisibleAncestor(SpaceObject obj) {
            if (obj.Parent.Layer == UniverseLayer.Sector
                    && (A.IsInHyperspace || obj.Parent != A.Player.Parent)) {
                obj = obj.Parent;
            }
            if (obj.Parent?.Layer == UniverseLayer.Region
                    && (ctx.IsCrossRegionHyperjump
                        || obj.Parent != SOUtils.GetPlayerSector(A.S)?.Region?.SO)) {
                obj = obj.Parent;
            }
            if (obj.Parent == null) {
                D.Err("Can't find a visible starmap ancestor for: {0}", obj);
                return null;
            }
            return obj;
        }

        private void OnEnterSpaceObject(SpaceObject obj, SpaceObject prev) {
            widget.OnEnterSpaceObject(obj, prev);
        }

        private void OnHoverUISpaceObject(UISpaceObject obj, bool hover) {
            if (UIShowing.MainMenu) { return; }
            // don't kill this
            if (!hover && UIShowing.QuickChoice) { return; }
            widget.OnHoverUISpaceObject(obj, hover);
        }

        private void UpdateUISpaceObject(SpaceObject obj, Action<UISpaceObject, SpaceObjectLooks> act) {
            widget.UpdateUISpaceObject(obj, act);
        }

        private bool OnRebuild(bool refocus, bool resetParent) {
            #if DEBUG_STARMAP
            D.Warn("Rebuilding starmap {0} | refocus: {1} | reset: {2}",
                ParentSO, refocus, resetParent);
            #endif
            if (resetParent) {
                ctx.ParentSO = null;
            }
            if (UIShowing.Starmap) {
                widget.Rebuild(refocus);
                return true;
            }
            return false;
        }

        private void OnSOAdd(SpaceObject so) {
            if (so == null) {
                D.Err("Trying to add null SpaceObject!");
                return;
            }
            if (!UIShowing.Starmap) { return; }
            if (so.Parent == S.Universe.Player.Parent) {
                if (ctx.Layer == UniverseLayer.Sector) {
                    #if DEBUG_STARMAP
                    D.Warn("Adding so while looking at sector: {0}", so);
                    #endif
                    widget.AddObject(so, false);
                    widget.SetPlayerAsLast();
                } else {
                    #if DEBUG_STARMAP
                    D.Warn("Adding so while not looking at sector: {0}", so);
                    #endif
                    OnRebuild(false, false);
                }
            }
        }

        private void OnSORemove(SpaceObject so) {
            if (so == null) {
                D.Err("Trying to remove null SpaceObject!");
                return;
            }
            if (!UIShowing.Starmap) { return; }
            widget.RemoveObject(so);
        }

        private bool ShowStarmap(bool show) {
            var currentSector = show ? SOUtils.GetPlayerSector(S) : null;
            var focusCurrentLocation = show && ctx.ShouldRefocusOnOpen(currentSector);
            var isUsingRelay = S.Universe.IsUsingHyperspaceRelay
                && !S.Vars.IsShowingCutscene;
            var focusCurrentRegion = show && isUsingRelay;
            if (isUsingRelay) {
                ctx.SwitchLayer(UniverseLayer.Universe);
            }
            // if (show == widget.gameObject.activeSelf) {
                // D.Err("Show starmap == same: {0}", show);
                // If we return here, any crash of the starmap will render the game unplayable
                // return false;
            // }
            if (show && UIShowing.Research) {
                S.UIQuery.ShowResearch.Ask(false);
            }
            UIShowing.Set(UIShowingFlags.FullScreenWidget, show);
            UIShowing.Set(UIShowingFlags.Starmap, show);
            S.UIQuery.ToggleGameUI.Ask(!show, true);
            S.Sig.ToggleRendering.Send(!show);
            S.Sig.OnShowFullScreenWidget.Send(show);
            widget.Toggle(show);
            if (focusCurrentRegion) {
                widget.RefocusWidget("Opened near hyperspace relay");
            } else if (focusCurrentLocation) {
                StarmapFocusOn(A.IsInHyperspace ? A.PlayerSector : A.Player, false);
            }
            if (show) {
                ctx.RememberOpenedSector(currentSector);
                OnOpen();
            } else {
                OnClose();
            }
            return true;
        }

        private void OnOpen() {
            // S.Query.DeselectAny.Ask();
            if (UIShowing.QuickSearch) {
                S.UIQuery.ShowQuickSearch.Ask(false);
            }
            S.Toolbox.Clear();
            S.Sys.Ship.TempMoveStarfieldTo(Consts.V2Right);
        }

        private void OnClose() {
            S.BB.Set(BBKeyH.ProbeId, 0);
            S.Sys.Ship.RestoreStarfieldPos();
            // if (UIShowing.ContextMenu) {
            //
            // }
            Caches.SOActions.Clear();
        }

        public void Cleanup() {
            widget.Cleanup();
        }
    }
}
