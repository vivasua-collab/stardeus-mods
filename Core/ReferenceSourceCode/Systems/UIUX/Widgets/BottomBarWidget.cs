using System;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Tools;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class BottomBarWidget : BaseWidget {
        // special green overlays button
        public WButtonSubmenu OverlaysButton;
        public WButtonIconText SubmenuButtonPrefab;
        public WButtonOverlay ButtonOverlayPrefab;
        public WButtonTool ButtonToolPrefab;
        public WButtonToolWithSubmenu ButtonToolWithSubmenuPrefab;
        // tools and actions
        public WBottomBarButton ButtonG1Prefab;
        // starmap, research
        public WBottomBarButton ButtonG2Prefab;
        /// <summary>
        /// Main groups is the main bar that is displayed all the time if
        /// nothing is selected
        /// </summary>
        public WGroup[] MainGroups;
        /// <summary>
        /// Displayed instead of main groups when something is selected
        /// </summary>
        public WGroup TempGroup;
        Lifetime actionsLife;
        BottomBarSwap swap;
        List<EntityActionData> pendingActions;
        public bool IsAnimating => swap != null && swap.IsPlaying;
        // The player pressed one of the selection's action buttons, by click or hotkey. Set
        // for each game by EntityActionsHintHandler
        public Action ActionPressed;
        // Lets tests tell a redundant rebuild from none
        public int RebuildCount { get; private set; }
        // Buttons the HUD reveal may hide, by tool id or HudRevealRules key
        private readonly Dictionary<string, RectTransform> revealTargets = new();
        public void Initialize() {
            Register(UIWidgetId.BottomBar, this);
            revealTargets.Clear();
            ActionPressed = null;
            // A game that unloaded mid swap left the bar wherever it stopped
            swap ??= new BottomBarSwap(Rect, SwapToPending);
            swap.Stop();
            pendingActions = null;
            MiscUtils.DestroyChildren(TempGroup.Rect);
            TempGroup.gameObject.SetActive(false);
            // tools
            MiscUtils.DestroyChildren(MainGroups[0].Rect);

        }

        // starmap & research
        public void LoadSpecialButtons(GameState s) {
            var rect = MainGroups[1].Rect;
            MiscUtils.DestroyChildren(rect);

            string glyph;

            // var btn0 = UISpawn.WidgetPermanent(ButtonG2Prefab, rect);
            // btn0.Item.Load(IconId.CComputing, T.NounAppPlural, () => ShowApps(s));
            // glyph = The.Bindings.GetGlyphFor(ActionType.ShortcutApps);
            // btn0.SetShortcutGlyph(glyph);

            var btn1 = UISpawn.WidgetPermanent(ButtonG2Prefab, rect);
            btn1.Item.Load(IconId.CStarmap, T.Starmap, () => ShowStarmap(s));
            glyph = The.Bindings.GetGlyphFor(ActionType.OpenStarmap);
            btn1.SetShortcutGlyph(glyph);

            var btn2 = UISpawn.WidgetPermanent(ButtonG2Prefab, rect);
            btn2.Item.Load(IconId.CResearch, T.OverlayTipResearch,
                () => ShowResearch(s));
            glyph = The.Bindings.GetGlyphFor(ActionType.OpenResearch);
            btn2.SetShortcutGlyph(glyph);

            revealTargets[HudRevealRules.KeyStarmap] = btn1.Rect;
            revealTargets[HudRevealRules.KeyResearch] = btn2.Rect;
        }

        public bool TryGetRevealTarget(string key, out RectTransform rect) {
            return revealTargets.TryGetValue(key, out rect) && rect != null;
        }

        private void ShowApps(GameState s) {
            ShipComputerComp.ShowApps(s);
        }

        private void ShowResearch(GameState s) {
            s.UIQuery.ShowResearch.Ask(true);
        }

        private void ShowStarmap(GameState s) {
            s.UIQuery.ShowStarmap.Ask(true);
        }

        public void CreateOverlays(GameState s, OverlayInfo[] visualOverlays) {
            OverlaysButton.SetImage(IconId.WEye);
            OverlaysButton.SetText(T.CodexMUiOverlays);
            int idx = 1;
            foreach (var ov in visualOverlays) {
                var btn = UISpawn.WidgetPermanent(ButtonOverlayPrefab,
                    OverlaysButton.Container);
                btn.Load(s, ov, OverlaysButton.Image, idx++);
            }
            OverlaysButton.OnCollapse = () => {
                // The submenu also collapses when the bar hides its groups
                // (entity selection, ship combat). Hiding the button is not the
                // player dismissing the overlay.
                if (!OverlaysButton.gameObject.activeInHierarchy) { return; }
                if (UIShowing.Overlay) {
                    s.Sig.HideOverlay.Send();
                }
            };
        }

        public void ShowOverlaysSubmenu() {
            if (!OverlaysButton.gameObject.activeInHierarchy) {
                return;
            }
            OverlaysButton.ShowSubmenu();
        }
        public void HideOverlaysSubmenu() {
            if (!OverlaysButton.gameObject.activeInHierarchy) {
                return;
            }
            OverlaysButton.HideSubmenu();
        }

        // Where overlay legends dock: above the open overlay buttons (screen space)
        public bool TryGetOverlaysDock(out Vector3 topRight) {
            if (!OverlaysButton.gameObject.activeInHierarchy || !OverlaysButton.IsExpanded) {
                topRight = default;
                return false;
            }
            topRight = OverlaysButton.RestingTopRight();
            return true;
        }

        public void AddToolButtonFor(GameState s, ITool tool) {
            if (tool.IsConfigurable && tool != s.Toolbox.BuildTool) {
                var btn = UISpawn.WidgetPermanent(ButtonToolWithSubmenuPrefab,
                        MainGroups[0].Rect);
                btn.Load(s, tool, ButtonG1Prefab);
                revealTargets[tool.Id] = btn.Rect;
            } else {
                var btn = UISpawn.WidgetPermanent(ButtonToolPrefab,
                        MainGroups[0].Rect);
                btn.Load(s, tool);
                revealTargets[tool.Id] = btn.Rect;
            }
        }

        // animate is for selection changes. Refreshes of the same selection update in
        // place, the player just pressed something there and is looking at it.
        public bool LoadActions(GameState s, List<EntityActionData> actions, bool animate) {
            bool showsActions = actions != null;
            if (swap.IsHiding) {
                // Whatever arrives before the bar is out of view is what slides back in
                pendingActions = actions;
                return showsActions;
            }
            // Toolbar to toolbar changes nothing the player could notice
            if (!animate || (!showsActions && !TempGroup.gameObject.activeSelf)) {
                ApplyActions(actions);
                return showsActions;
            }
            pendingActions = actions;
            // The buttons sliding away belong to the previous selection, their keys
            // must not act on it
            EntityActionHotkeys.ClearVisible();
            swap.Begin();
            return showsActions;
        }

        public void TickSwap(float dt) {
            swap.Tick(dt);
        }

        private void SwapToPending() {
            var actions = pendingActions;
            pendingActions = null;
            ApplyActions(actions);
            // The bar slides back in by its new height, which the layout would only
            // work out at the end of the frame
            LayoutRebuilder.ForceRebuildLayoutImmediate(Rect);
        }

        private void ApplyActions(List<EntityActionData> actions) {
            RebuildCount++;
            if (actionsLife != null) {
                actionsLife.Dispose();
            }
            // The buttons just went back to the pool, their hotkeys go with them
            EntityActionHotkeys.ClearVisible();
            if (actions == null) {
                TempGroup.SetActiveFast(false);
                foreach (var group in MainGroups) {
                    group.SetActiveFast(true);
                }
                if (UIShowing.Overlay) {
                    OverlaysButton.ShowSubmenu();
                }
                return;
            }
            actionsLife = Lifetime.Create("BottomBarWidget_LoadActions");
            for (int i = 0; i < actions.Count; i++) {
                var act = actions[i];
                try {
                    var btn = act.Factory.Invoke(actionsLife, TempGroup.Rect, i);
                    if (btn is WBottomBarButton barButton) {
                        barButton.SetOnPressed(ActionPressed);
                    }
                    EntityActionHotkeys.Register(act.Action, btn);
                } catch (Exception ex) {
                    D.LogEx(ex, "EntityAction failed to spawn the button: {0}", act);
                }
            }
            TempGroup.SetActiveFast(true);
            foreach (var group in MainGroups) {
                group.SetActiveFast(false);
            }
        }
    }
}
