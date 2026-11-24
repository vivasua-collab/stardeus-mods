using System;
using System.Collections;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Research;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.UI;
using KL.Utils;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    // One research in the tree. Zoomed out: its tile, and its card just above the tile while the
    // tile is hovered (ResearchWidget opens and closes the card). Zoomed in: its full card in
    // place of the tile, so nearby costs can be compared without hovering.
    [DisallowMultipleComponent]
    public class WResearchNodeLoader : WBase {
        [SerializeField] private WResearchNode nodePrefab;
        [SerializeField] private WResearchNodeRoot nodeRootPrefab;
        [SerializeField] private WText hugeTitle;
        [SerializeField] private WResearchTile tile;
        [SerializeField] private UIHoverCallback hoverCallback;
        override protected bool IsPoolable => false;
        private Lifetime lifetime;
        private WResearchNode node;
        private WResearchNodeRoot nodeRoot;
        private ResearchWidget widget;
        private bool isLoaded;
        private Def def;
        private ResearchProgress progress;
        private bool isRoot;
        private bool isCardOpen;
        private bool isCardMode;
        private float laneWidth;
        private float laneCenterOffset;

        // The hover card's edge reaches this far (canvas px) into its tile, so the mouse never
        // crosses a seam that belongs to neither on its way from the tile to the card
        private const float CardOverlap = 4f;
        public bool IsCardOpen => isCardOpen;
        public bool IsCardInPlace => isCardMode && isLoaded && !isRoot;
        public RectTransform CardRect => node != null ? node.Rect : null;
        public WResearchTile Tile => tile;
        public string DebugTileTitle => hugeTitle.Text.text;

        public void OnShowResearch(bool show) {
            if (!isLoaded) { return; }
            if (!isRoot) {
                node.OnShowResearch(show);
            }
        }
        private void Load() {
            var btn = hugeTitle.GetComponent<Button>();
            hoverCallback.SetCallback(OnHoverTile, null, false);
            if (isRoot) {
                tile.SetActiveFast(false);
                nodeRoot = UISpawn.WidgetPermanent(nodeRootPrefab, Rect);
                nodeRoot.Rect.localPosition = Consts.V3Zero;
                btn.enabled = false;
                nodeRoot.Load(def.Researchable);
                nodeRoot.SetLane(laneWidth, laneCenterOffset);
                isLoaded = true;
            } else {
                node = UISpawn.WidgetPermanent(nodePrefab, Rect);
                node.Rect.localPosition = Consts.V3Zero;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => {
                    A.S.UIQuery.ShowInResearchTree.Ask(def);
                });
                btn.enabled = true;
                node.Load(def.Researchable, progress, hugeTitle);
                // WResearchNode.Load assigns ChangeCallback, so append after it.
                progress.ChangeCallback += tile.Repaint;
                tile.Load(def, progress, hugeTitle.Text);
                node.SetLifetime(lifetime);
                UIUtils.ToggleCG(node.CanvasGroup, false);
                isLoaded = true;
                ApplyMode();
            }
            lifetime = null;
        }

        private void OnHoverTile(bool hov) {
            if (!hov) {
                The.SysSig.HideTooltip.Send();
                return;
            }
            if (!isLoaded || isRoot || isCardMode) { return; }
            if (!A.S.Research.IsDiscoveredOrSandbox(progress.IdH)) {
                The.SysSig.ShowTooltip.Send(T.UndiscoveredResearch, true);
                return;
            }
            widget?.OpenCard(this);
        }

        public void Load(Def def, ResearchProgress progress) {
            var isDiscovered = A.S.Research.IsDiscoveredOrSandbox(def.IdH);
            hugeTitle.SetText(isDiscovered ? def.NameT : "???");
            this.def = def;
            this.progress = progress;
            isRoot = def.Researchable.IsCostless;
            Coroutines.OnNextFrame(Load);
        }

        public void SetWidget(ResearchWidget widget) {
            this.widget = widget;
        }

        // A category root's title spans its whole category (px, right of the root's middle)
        public void SetLane(float width, float centerOffset) {
            laneWidth = width;
            laneCenterOffset = centerOffset;
            if (isLoaded && isRoot) {
                nodeRoot.SetLane(width, centerOffset);
            }
        }

        public bool CanOpenCard => isLoaded && !isRoot
            && A.S.Research.IsDiscoveredOrSandbox(def.IdH);

        // Shows the card just above the tile at a constant size on the canvas, whatever the zoom,
        // so the tile's neighbors stay in sight
        public void ShowCard(float containerScale, float cardScale) {
            if (!CanOpenCard || isCardMode) { return; }
            isCardOpen = true;
            The.SysSig.HideTooltip.Send();
            transform.SetAsLastSibling();
            var scale = Mathf.Max(1f, cardScale / Mathf.Max(containerScale, 0.0001f));
            node.Rect.localScale = Consts.V3One * scale;
            PlaceCard(false, containerScale);
            // The card just grew: its text has to re-render for the new scale, or it blurs
            node.OnMoveEnded(true);
            UIUtils.ToggleCG(node.CanvasGroup, true);
        }

        // Above the tile, or below it where there is no room above. Either way the card's edge
        // reaches a little into the tile.
        public void PlaceCard(bool below, float containerScale) {
            var cardRect = node.Rect;
            var tileSize = Rect.sizeDelta;
            var cardSize = cardRect.rect.size * cardRect.localScale.x;
            var overlap = CardOverlap / Mathf.Max(containerScale, 0.0001f);
            // Both pivots are top left, so the tile spans y from -tileSize.y to 0
            var top = below ? overlap - tileSize.y : cardSize.y - overlap;
            cardRect.localPosition = new Vector3((tileSize.x - cardSize.x) / 2, top, 0);
        }

        public void HideCard() {
            if (!isCardOpen) { return; }
            isCardOpen = false;
            UIUtils.ToggleCG(node.CanvasGroup, false);
            node.OnPointerExit(null);
            The.SysSig.HideTooltip.Send();
        }

        // Zoomed in, the full card replaces the tile (ResearchWidget closes any hover card first)
        public void SetCardMode(bool on) {
            if (isCardMode == on) { return; }
            isCardMode = on;
            ApplyMode();
        }

        private void ApplyMode() {
            if (!isLoaded || isRoot) { return; }
            isCardOpen = false;
            tile.SetActiveFast(!isCardMode);
            if (isCardMode) {
                var cardRect = node.Rect;
                cardRect.localScale = Consts.V3One;
                cardRect.localPosition = new Vector3((Rect.sizeDelta.x - cardRect.rect.width) / 2,
                    0, 0);
                node.OnMoveEnded(true);
                UIUtils.ToggleCG(node.CanvasGroup, true);
            } else {
                UIUtils.ToggleCG(node.CanvasGroup, false);
                node.OnPointerExit(null);
            }
        }

        public void SetZoom(float zoom) {
            tile.SetZoom(zoom);
        }

        public void MoveStarted() {
            if (!isLoaded || isRoot) { return; }
            node.OnMoveStarted();
        }

        public void MoveEnded(float scale, bool scaleIncreased, float collapseScale) {
            if (isLoaded && !isRoot) {
                node.OnMoveEnded(scaleIncreased);
            }
        }

        public void SetLifetime(Lifetime lifetime) {
            if (isLoaded && !isRoot) {
                node.SetLifetime(lifetime);
            } else {
                this.lifetime = lifetime;
            }
        }

        // Both, because focusing changes the zoom right after, and so which of them shows
        public void Flash() {
            if (isLoaded && !isRoot) {
                tile.Flash();
                node.Flash();
            }
        }

        public void UpdateResources(Dictionary<MatType, BInt> dc, int storage) {
            if (!isLoaded) { return; }
            if (isRoot) {
                // A root can get discovered while the tree exists (the Rogue AI root when
                // the Asimov Override is installed), and this is the refresh that follows
                // every research change.
                nodeRoot.UpdateResearchable();
                return;
            }
            node.UpdateResources(dc, storage);
            tile.Repaint();
        }

        public void UpdateResearchable() {
            if (isLoaded) {
                if (isRoot) {
                    nodeRoot.UpdateResearchable();
                } else {
                    node.UpdateResearchable();
                    tile.Repaint();
                }
            }
        }
    }
}
