using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.UI.Research;
using Game.Utils;
using KL.Randomness;
using KL.UI;
using KL.Utils;
using Microsoft.Msagl.Core.Layout;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class ResearchWidget : BaseWidget {
        private struct ItemPosition {
            public float Height;
            public Vector2 Position;
            public float Scale;
        }
        private struct ResearchLine {
            public WImage Line;
            public int SourceIdH;
            public int TargetIdH;
        }
        private const float LineThickness = 16f;
        // Size of an open card on the canvas: readable at any zoom, without zooming in
        private const float CardScale = 1f;
        // Crossing a border between card and tile must not close the card
        private const float CardCloseDelay = 0.25f;
        // Zoomed in this far, a card in place reads like the hover card
        private const float MaxZoom = 1.2f;
        private float FocusZoom => Tunables.ResearchItemCollapseScale + 0.1f;
        private const float FitMargin = 40f;
        [SerializeField] private CanvasGroup cg;
        [SerializeField] private Canvas canvas;
        [SerializeField] private WGroup container;
        [SerializeField] private UIScrollNav scrollNav;
        [SerializeField] private ResearchMenuWidget menu;
        private UIResearchTierMarker tierMarkerPrefab;
        private WResearchNodeLoader nodePrefab;
        private WImage linePrefab;
        private Material lineMaterial;
        private float lineSpeed;
        private bool areLinesAnimated = true;
        private ResearchContext ctx;
        private Dictionary<string, ResearchTreeNode> layout;
        private readonly Dictionary<int, UIResearchTierMarker> tierMarkers = new(8);
        private readonly List<string> listedItems = new();
        private readonly Dictionary<string, ItemPosition> itemPositions = new();
        private readonly List<WImage> lines = new();
        private readonly List<ResearchLine> researchLines = new();
        private readonly Dictionary<int, WResearchNodeLoader> spawnedNodes = new();
        private Lifetime lifetime;
        private bool isMoving;
        private bool didScaleIncreaseDuringMove;
        private bool didScaleChangeDuringMove;
        private float lastContainerScale;
        private Vector2 lastPos;
        private bool wasLoaded;
        private Color lineColorLocked;
        private Color lineColorUnlocked;
        private Color lineColorUnlockable;
        private Color colorLocked;
        private Color colorUnlocked;
        private Color colorAvailable;
        private Color colorNotDiscovered;
        public bool IsLoadingNow;
        private Def wantFocusOnDef;
        private Vector2 nodeSize;
        private WResearchNodeLoader openCard;
        private float cardLeftAt = -1f;
        // Zoomed in (ResearchItemCollapseScale and up), every research shows its full card
        private bool isCardMode;
        private GameState S;
        public WResearchNodeLoader OpenCardNode => openCard;
        public bool IsCardMode => isCardMode;

        public void Initialize(GameState s, ResearchContext ctx) {
            S = s;
            this.ctx = ctx;
            tierMarkerPrefab = ResourceLibrary.FindPrefab<UIResearchTierMarker>();
            nodePrefab = ResourceLibrary.FindPrefab<WResearchNodeLoader>();
            // Lines run between tile centers
            nodeSize = ((RectTransform) nodePrefab.transform).sizeDelta;
            linePrefab = ResourceLibrary.FindPrefab<WImage>(UIPrefabId.UILineDashedAnimated);
            // Own instance, because the starmap player marker shares the prefab material
            // and must keep scrolling while research lines freeze when zoomed out.
            lineMaterial = new Material(linePrefab.Material);
            lineSpeed = lineMaterial.GetFloat(ShaderVars._Speed);
            SetThemeColors();

            Register(UIWidgetId.Research, this);
            menu.Initialize(s, ctx);
            gameObject.SetActiveFast(false);
        }

        private void SetThemeColors() {
            lineColorLocked = The.UITheme.GetColor("ResearchLockedLine", this);
            lineColorUnlocked = The.UITheme.GetColor("ResearchUnlockedLine", this);
            lineColorUnlockable = The.UITheme.GetColor("ResearchUnlockableLine", this);
            colorLocked = The.UITheme.GetColor("ResearchLocked", this);
            colorUnlocked = The.UITheme.GetColor("ResearchUnlocked", this);
            colorAvailable = The.UITheme.GetColor("ResearchAvailable", this);
            colorNotDiscovered = The.UITheme.GetColor("ResearchNotDiscovered", this);
            WResearchNode.ColorAvailable = colorAvailable;
            WResearchNode.ColorLocked = colorLocked;
            WResearchNode.ColorUnlocked = colorUnlocked;
            WResearchNode.ColorNotDiscovered = colorNotDiscovered;
        }

        public override void Toggle(bool show) {
            if (show) {
                OnOpen();
                canvas.enabled = true;
                UIUtils.ToggleCG(cg, true);
            } else {
                CloseCard();
                gameObject.SetActiveFast(false);
                container.SetActiveFast(false);
                OnClose();
                UIUtils.ToggleCG(cg, false);
                canvas.enabled = false;
            }
            foreach (var node in spawnedNodes.Values) {
                node.OnShowResearch(show);
            }
            if (show) {
                Coroutines.OnNextFrame(() => {
                    container.SetActiveFast(true);
                });
                gameObject.SetActiveFast(true);
            }
        }

        private void OnClose() {
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
            ctx.MainLifetime = null;
            menu.Cleanup();
        }

        private void OnOpen() {
            EntityUtils.HideEntityLink();
            if (lifetime != null) {
                lifetime.Dispose();
            }
            ctx.MainLifetime = lifetime = Lifetime.Create("Research");

            if (!wasLoaded) {
                IsLoadingNow = true;
                Coroutines.Start(LayoutAll(), this, "Layout Research", false, false);
            } else {
                var en = A.Grid;
                OnResourcesUpdated(S.Research.DatoidCounts, en.TotalDisk);
                RefreshTierMarkers();
                SetLifetime();
                menu.Load();
            }
        }

        private void RefreshTierMarkers() {
            if (lifetime == null || IsLoadingNow) { return; }
            foreach (var kvp in tierMarkers) {
                kvp.Value.Load(lifetime, S, kvp.Key);
            }
        }

        private void SetLifetime() {
            foreach (var node in spawnedNodes.Values) {
                node.SetLifetime(lifetime);
            }
        }

        [ContextMenu("Rebuild Tree")]
        private void Rebuild() {
            CloseCard();
            wasLoaded = false;
            foreach (var spawned in spawnedNodes.Values) {
                GameObject.Destroy(spawned.gameObject);
            }
            spawnedNodes.Clear();
            foreach (var tierMarker in tierMarkers.Values) {
                GameObject.Destroy(tierMarker.gameObject);
            }
            tierMarkers.Clear();
            foreach (var line in lines) {
                GameObject.Destroy(line.gameObject);
            }
            lines.Clear();
            researchLines.Clear();
            OnOpen();
        }

        private IEnumerator LayoutAll() {
            Ready.SetGen(false, "Research");
            wasLoaded = true;
            tierMarkers.Clear();
            spawnedNodes.Clear();
            itemPositions.Clear();
            lines.Clear();
            researchLines.Clear();

            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minY = float.MaxValue;
            float maxY = float.MinValue;

            layout = S.UIQuery.GetResearchLayout.Ask();
            foreach (var item in layout) {
                var pos = item.Value.GraphNode.Center;
                var x = (float) pos.X;
                var y = (float) pos.Y;
                if (x < minX) { minX = x; }
                if (x > maxX) { maxX = x; }
                if (y < minY) { minY = y; }
                if (y > maxY) { maxY = y; }
            }

            float offX = -minX;
            float offY = -minY;

            var mulX = Tunables.Research_UI_Node_MulX;
            var mulY = Tunables.Research_UI_Node_MulY;

            // var leftShift = 1500f / scale;
            var width = maxX - minX;
            var height = maxY - minY;
            width *= mulX;
            width += nodeSize.x;
            height *= mulY;
            height += nodeSize.y;
            // D.Warn("MinX: {0}, MaxX: {1}, MinY: {2}, MaxY: {3}. OffX: {4}, OffY: {5}, Width: {6}, Height: {7}",
            //     minX, maxX, minY, maxY, offX, offY, width, height);
            container.Rect.sizeDelta = new Vector2(width, height);
            yield return null;
            FitTreeToScreen();

            int i = 0;
            foreach (var item in layout) {
                var node = item.Value;
                var tier = node.Def.Tier;
                if (node.TierNode != null) {
                    if (!tierMarkers.ContainsKey(tier)) {
                        var c = node.TierNode.Center;
                        AddTierMarker(tier, c.X, c.Y, offX, offY, width, height);
                    }
                }
                CreateItem(item.Value, offX, offY, width, height);
                if (++i % 5 == 0) {
                    yield return null;
                }
                UISounds.PlayHover(UISounds.HoverType.Lowest);
            }
            foreach (var line in lines) {
                line.SetSiblingIdx(0);
            }
            IsLoadingNow = false;
            Ready.SetGen(true, "Research");

            var en = A.Grid;
            S.Sys.Research.CheckResearch();
            S.Research.UpdateUsedDisk();
            OnResourcesUpdated(S.Research.DatoidCounts, en.TotalDisk);

            if (wantFocusOnDef != null) {
                D.Warn("Focusing on def after load: {0}", wantFocusOnDef.Id);

                FocusOnDef(wantFocusOnDef);
                wantFocusOnDef = null;
            }

            menu.Load();
        }
        private void AddTierMarker(int tier, double x, double y, float offX, float offY, float width, float height) {
            var marker = UISpawn.WidgetPermanent(tierMarkerPrefab, container.Container);
            marker.Load(lifetime, S, tier);
            tierMarkers[tier] = marker;
            var pos = TransposeGraph(x, y, offX, offY, width, height);
            // will lower the tier marker by -100, since it's a little too close
            // to the top
            marker.Reposition(-1500f, pos.y - 100, width + 1500f + nodeSize.x);
        }

        private ItemPosition CreateItem(ResearchTreeNode node, float offX, float offY, float width, float height) {
            var item = node.Def;

            var inst = UISpawn.WidgetPermanent(nodePrefab, container.Container);
            inst.SetWidget(this);
            inst.SetZoom(container.Rect.localScale.x);
            inst.SetCardMode(isCardMode);
            inst.Load(node.Def, S.Research.Get(node.Def.IdH));
            inst.SetLifetime(lifetime);
            if (node.LaneWidth > 0) {
                var mulX = Tunables.Research_UI_Node_MulX;
                inst.SetLane(node.LaneWidth * mulX, node.LaneCenterOffset * mulX);
            }
            var rekt = inst.Rect;
            // inst.ToggleLayoutGroups(false);
            // inst.CreateUnlocks(S.Research, item);
            // scale = rekt.localScale.x;

            // D.Warn("Scale: {0}", scale);
            var center = node.GraphNode.Center;
            var pos = TransposeGraph(center.X, center.Y, offX, offY, width, height);

            node.Pos = pos;
            rekt.RepositionTopLeft(pos.x, pos.y);
            // inst.AnchoredPos = rekt.anchoredPosition;
            // inst.Id = item.Id;
            spawnedNodes.Add(item.IdH, inst);
            inst.name = $"WResearchNode_{item.Id}_{node.Pos}";
            //_{height}_{currentHeight}_{thisHeight}_{posY}";

            var rng = new Rng(Hashes.S(item.Id));

            foreach (var edge in node.GraphNode.OutEdges) {
                DrawEdge(rng, item.IdH, node.Id, edge, offX, offY, width, height);
            }

            return new ItemPosition() {
                Position = new Vector2(pos.x, pos.y),
            };
        }
        private void DrawEdge(Rng rng, int sourceIdH, string nodeId, Edge edge,
                float offX, float offY, float width, float height) {
            if (((string) edge.UserData) == "ignore") { return; }
            var points = new List<Vector2>();
            var targetIdH = TargetIdHFor(edge);

            var shift = rng.InsideUnitCircle().normalized *
                Tunables.Research_UI_LineShift;

            //var pi = 0;
            var pointCount = edge.UnderlyingPolyline.Count();
            foreach (var p in edge.UnderlyingPolyline) {
                var point = TransposeGraph(p.X, p.Y, offX, offY, width, height);
                point += new Vector2(nodeSize.x, -nodeSize.y) * 0.5f;
                //if (pi > 0 && pi < pointCount - 1) {
                    //Add a random shift to split the lines apart

                if (pointCount > 2) {
                    point += shift;
                }
                //}
                //pi++;
                points.Add(point);
            }
            //D.Log("Edge Hue: {0}", edgeHue);

            // var color = rng.Color(edgeHue, edgeHue, 55, 65, 75, 85);

            for (int i = 0; i < points.Count - 1; i++) {
                // permanent life
                var line = UIDraw.SpawnLineTL(null, linePrefab, container.Container,
                    LineThickness, points[i], points[i + 1]);
                line.Material = lineMaterial;
                FitDashesToThickness(line);

                line.name = string.Format("UILine_{0}_{1}_{2}_{3}",
                    nodeId, points[i], points[i + 1], i);
                line.SetTint(TintForLine(sourceIdH, targetIdH));
                lines.Add(line);
                researchLines.Add(new ResearchLine() {
                    Line = line,
                    SourceIdH = sourceIdH,
                    TargetIdH = targetIdH,
                });
            }
        }

        // The dash sprite is tiled at its own size, so a line thinner than the sprite cut every
        // arrow in half. Scaling the tiles to the line keeps whole, smaller arrows.
        private static void FitDashesToThickness(WImage line) {
            var img = line.GetComponent<UnityEngine.UI.Image>();
            if (img == null || img.sprite == null) { return; }
            var tileHeight = img.sprite.rect.height / img.pixelsPerUnit;
            if (tileHeight <= 0f) { return; }
            img.pixelsPerUnitMultiplier = tileHeight / LineThickness;
        }

        private static int TargetIdHFor(Edge edge) {
            if (edge.Target?.UserData is ResearchLayout.NodeData data && data.Def != null) {
                return data.Def.IdH;
            }
            return 0;
        }

        private Color TintForLine(int sourceIdH, int targetIdH) {
            var target = targetIdH == 0 ? null : S.Research.Get(targetIdH);
            if (target != null && S.Research.IsDiscovered(targetIdH)
                    && !target.IsUnlocked && target.CanBeUnlockedNow) {
                return lineColorUnlockable;
            }
            return S.Research.IsUnlocked(sourceIdH)
                ? lineColorUnlocked
                : lineColorLocked;
        }

        private Vector2 TransposeGraph(double x, double y,
                float offX, float offY, float width, float height) {
            var mulX = Tunables.Research_UI_Node_MulX;
            var mulY = Tunables.Research_UI_Node_MulY;
            var fx = (float) x;
            var fy = (float) y;

            var posX = fx + offX;
            var posY = fy + offY;

            return new Vector2(width - posX * mulX, posY * mulY - height);

            // var posX = Pad / 2 + (Padding + mulX * fx) * scale;
            // var posY = Pad / 2 + (-Padding + mulY * fy) * scale;
            // return new Vector2(posX, posY - Pad * 2f);
        }

        public void UpdateWidget() {
            UpdateOpenCard();
            var rekt = container.Rect;
            var curScale = rekt.localScale.x;
            Vector2 curPos = rekt.localPosition;
            var scaleChanged = !Maths.Approximately(lastContainerScale, curScale);
            if (scaleChanged || Vectors.SqrDistance(lastPos, curPos) > 0.1f) {
                if (scaleChanged) {
                    didScaleChangeDuringMove = true;
                    if (curScale > lastContainerScale) {
                        didScaleIncreaseDuringMove = true;
                    }
                    UpdateLineAnimation(curScale);
                    foreach (var inst in spawnedNodes.Values) {
                        inst.SetZoom(curScale);
                    }
                    UpdateCardMode(curScale);
                }
                lastContainerScale = curScale;
                lastPos = curPos;
                if (!isMoving) {
                    isMoving = true;
                    CloseCard();
                    foreach (var inst in spawnedNodes.Values) {
                        inst.MoveStarted();
                    }
                }
            } else {
                if (isMoving) {
                    isMoving = false;
                    var collapseScale = Tunables.ResearchItemCollapseScale;
                    foreach (var inst in spawnedNodes.Values) {
                        inst.MoveEnded(curScale, didScaleIncreaseDuringMove,
                            collapseScale);
                    }
                    didScaleChangeDuringMove = false;
                    didScaleIncreaseDuringMove = false;
                }
            }
            //S.Research.UpdateUI();
            // menuView.OnUpdate();
            // if (menuView.NeedsRebuild) {
            //     RebuildMenu();
            // }


        }

        // Switches as soon as the zoom crosses the threshold, so the cards are there when it stops
        private void UpdateCardMode(float scale) {
            var cardMode = scale >= Tunables.ResearchItemCollapseScale;
            if (cardMode == isCardMode) { return; }
            isCardMode = cardMode;
            CloseCard();
            foreach (var inst in spawnedNodes.Values) {
                inst.SetCardMode(cardMode);
            }
        }

        // A hovered tile opens its card just above itself, one card at a time. Unity only sends
        // pointer events when the pointer moves, so the card closes on where the mouse is.
        public void OpenCard(WResearchNodeLoader node) {
            if (isCardMode || isMoving || IsLoadingNow || node == openCard || !node.CanOpenCard) {
                return;
            }
            CloseCard();
            openCard = node;
            cardLeftAt = -1f;
            var scale = container.Rect.localScale.x;
            node.ShowCard(scale, CardScale);
            GetViewCorners(out var viewMin, out var viewMax);
            node.CardRect.GetWorldCorners(Caches.Corners);
            if (Caches.Corners[2].y > viewMax.y || OverlapsMenu(Caches.Corners[0], Caches.Corners[2])) {
                node.PlaceCard(true, scale);
            }
            KeepInView(node.CardRect, viewMin, viewMax);
        }

        public void CloseCard() {
            if (openCard == null) { return; }
            openCard.HideCard();
            openCard = null;
        }

        private void UpdateOpenCard() {
            if (openCard == null) { return; }
            if (IsMouseOver(openCard.CardRect) || IsMouseOver(openCard.Rect)) {
                cardLeftAt = -1f;
            } else if (cardLeftAt < 0f) {
                cardLeftAt = Time.unscaledTime;
            } else if (Time.unscaledTime - cardLeftAt > CardCloseDelay) {
                CloseCard();
            }
        }

        private bool IsMouseOver(RectTransform rt) {
            var root = canvas.rootCanvas;
            var cam = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
            return RectTransformUtility.RectangleContainsScreenPoint(rt,
                The.Bindings.Mouse.ScreenPosition, cam);
        }

        // The side menu draws over the tree, so a card above its tile that would reach under the
        // menu opens below the tile instead
        private bool OverlapsMenu(Vector3 cardMin, Vector3 cardMax) {
            ((RectTransform) menu.transform).GetWorldCorners(Caches.Corners);
            var menuMin = Caches.Corners[0];
            var menuMax = Caches.Corners[2];
            return cardMin.x < menuMax.x && cardMax.x > menuMin.x
                && cardMin.y < menuMax.y && cardMax.y > menuMin.y;
        }

        // The tree's visible area in world space. The side menu is ignored: a card may open over it
        private void GetViewCorners(out Vector3 viewMin, out Vector3 viewMax) {
            ((RectTransform) transform).GetWorldCorners(Caches.Corners);
            viewMin = Caches.Corners[0];
            viewMax = Caches.Corners[2];
        }

        // A card at the edge of the screen moves back into view. The card
        // is wider than its tile, so a sideways move keeps it touching the tile.
        private void KeepInView(RectTransform card, Vector3 viewMin, Vector3 viewMax) {
            card.GetWorldCorners(Caches.Corners);
            var min = Caches.Corners[0];
            var max = Caches.Corners[2];
            var shift = Vector3.zero;
            if (max.x > viewMax.x) { shift.x = viewMax.x - max.x; }
            if (min.x + shift.x < viewMin.x) { shift.x = viewMin.x - min.x; }
            if (max.y > viewMax.y) { shift.y = viewMax.y - max.y; }
            if (min.y + shift.y < viewMin.y) { shift.y = viewMin.y - min.y; }
            card.position += shift;
        }

        // Opens on the whole tree, right of the side menu, and never zooms out past it
        private void FitTreeToScreen() {
            var view = ((RectTransform) transform).rect.size;
            var menuWidth = ((RectTransform) menu.transform).rect.width;
            var size = container.Rect.sizeDelta;
            var width = size.x;
            var fitX = (view.x - menuWidth - 2 * FitMargin) / Mathf.Max(1f, width);
            var fitY = (view.y - 2 * FitMargin) / Mathf.Max(1f, size.y);
            var fit = Mathf.Min(MaxZoom, Mathf.Min(fitX, fitY));
            scrollNav.SetLimits(new Vector2(fit * 0.9f, MaxZoom));
            var center = new Vector2(width / 2 - menuWidth / 2 / fit, -size.y / 2);
            scrollNav.FocusOn(center, fit);
        }

        // Zoomed out, the dash period shrinks to a few screen pixels and the scrolling
        // texture aliases into shimmer that seems to run backwards.
        private void UpdateLineAnimation(float scale) {
            var animate = scale >= Tunables.ResearchItemCollapseScale;
            if (animate == areLinesAnimated) { return; }
            areLinesAnimated = animate;
            lineMaterial.SetFloat(ShaderVars._Speed, animate ? lineSpeed : 0f);
        }

        private void OnDestroy() {
            if (lineMaterial != null) {
                Destroy(lineMaterial);
            }
        }

        public bool FocusOnDef(Def def) {
            D.Warn("Wants to focus on def: {0}", def.Id);
            if (def == null) { return false; }
            if (!def.Researchable.IsTreeNode) {
                var prereq = def.Researchable.Prerequisites[0];
                def = The.Defs.Get(prereq);
                if (def == null) {
                    return false;
                }
            }
            if (!wasLoaded || IsLoadingNow) {
                wantFocusOnDef = def;
                return true;
            }
            if (!spawnedNodes.TryGetValue(def.IdH, out var node)) {
                D.Err("Trying to focus on research def {0} which is not in the tree",
                    def.Id);
                return false;
            }
            D.Warn("Actually focusing on def: {0}", def.Id);
            scrollNav.FocusOn(node.Rect, FocusZoom);
            node.Flash();
            return true;
        }

        // Tests and screenshots frame a research at a chosen zoom; the mode follows next frame
        public void DebugFocusOn(Def def, float zoom) {
            if (spawnedNodes.TryGetValue(def.IdH, out var node)) {
                scrollNav.FocusOn(node.Rect, zoom);
            }
        }

        public void OnResourcesUpdated(Dictionary<MatType, BInt> datoidCounts, int storage) {
            var curStoreUse = S.Research.UsedDisk;
            storage -= curStoreUse;
            foreach (var w in spawnedNodes.Values) {
                w.UpdateResources(datoidCounts, storage);
            }
            RefreshEdgeTints();
            menu.Refresh();
        }

        public void RefreshTheme() {
            SetThemeColors();
            foreach (var w in spawnedNodes.Values) {
                w.UpdateResearchable();
            }
            if (UIShowing.Research) {
                Rebuild();
            }
        }

        public void OnResearchChanged(Def def) {
            // Unlocking a research can satisfy prerequisites of downstream nodes and
            // free up datoids/disk, so refresh every node's resource + availability
            // state, mirroring what reopening the tree does (OnOpen -> OnResourcesUpdated).
            OnResourcesUpdated(S.Research.DatoidCounts, A.Grid.TotalDisk);
            RefreshTierMarkers();
        }

        private void RefreshEdgeTints() {
            for (int i = 0; i < researchLines.Count; i++) {
                var researchLine = researchLines[i];
                researchLine.Line.SetTint(TintForLine(researchLine.SourceIdH,
                    researchLine.TargetIdH));
            }
        }

        public void RebuildMenu() {
            if (wasLoaded) {
                menu.Load();
            }
        }
    }
}
