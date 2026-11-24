using System;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Systems.Ships;
using Game.Systems.Space;
using Game.UI;
using Game.Utils;
using KL.Collections;
using KL.Pool;
using KL.UI;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class StarmapWidget : BaseWidget {
        [SerializeField] private WGroup container;
        [SerializeField] private WStarmapContent viewport;
        [SerializeField] private StarmapMenuWidget menu;
        [SerializeField] private UIScrollNav scrollNav;
        public StarmapMenuWidget MenuWidget => menu;
        private Lifetime lifetime;
        private UISpaceObjectSelected objHoverPrefab;
        private readonly Vector2 scrollLimitsSector = new Vector2(0.01f, 0.6f);
        private readonly Vector2 scrollLimitsRegion = new Vector2(0.01f, 0.1f);
        private readonly Vector2 scrollLimitsUniverse = new Vector2(0.01f, 0.03f);
        private readonly List<UISpaceObject> closeUICache = new();
        private readonly StarmapIconDeclutter iconDeclutter = new();
        private UISpaceObject player;
        private UISpaceObject playerParent;
        private HyperjumpQueueViz hyperQViz;
        private HyperjumpQueueViz pathPreviewViz;
        private NavLineViz navLineViz;
        private UISpaceObject prefab;

        private float lastScale = -1f;
        private float scaleLastFrame = -1f;
        private UILine linkPrefab;
        private SpaceLink warpLink;
        private UIPlayerMarker playerMarkerPrefab;
        private UIPlayerMarker playerMarker;
        public UIPlayerMarker PlayerMarker => playerMarker;
        private SpaceObjectLooks looks;
        private StarmapContext ctx;
        public string ShowingLayer;
        private UISpaceObjectSelected objHover;
        private bool isHoveringPlayerSO;
        private readonly Dictionary<SpaceObject, UISpaceObject> instances = new();
        public Dictionary<SpaceObject, UISpaceObject> Instances => instances;
        private readonly List<UILine> lines = new();
        private Vector2 centerPos;
        private UISpaceObject center;
        private UIDirectionMarker directionMarker;
        private bool hasDirectionMarker;
        private GameState S;
        private bool isRebuilding;
        private bool rebuildPending;
        private bool rebuildPendingRefocus;
        public void Initialize(GameState s, StarmapContext ctx) {
            this.ctx = ctx;
            ctx.Container = container.Container;
            viewport.Initialize(ctx);
            S = s;
            Register(UIWidgetId.Starmap, this);
            menu.Initialize(s, ctx);
            Toggle(false);

            hyperQViz = new HyperjumpQueueViz("norm", container.Container, 32f,
                Consts.HyperQueuePathColorActive,
                Consts.HyperQueuePathColorPassive);
            pathPreviewViz = new HyperjumpQueueViz("pre", container.Container, 32f,
                Consts.HyperQueuePreviewPathColorActive,
                Consts.HyperQueuePreviewPathColorPassive);

            prefab = ResourceLibrary.FindPrefab<UISpaceObject>(UIPrefabId.UISpaceObject);
            linkPrefab = ResourceLibrary.FindPrefab<UILine>(UIPrefabId.UILineSpaceLink);
            playerMarkerPrefab = ResourceLibrary.FindPrefab<UIPlayerMarker>(UIPrefabId.UIPlayerMarker);
            navLineViz = new NavLineViz(container.Container, The.UITheme.GetColor("StarmapMarker_Hyperspace", ctx));
            objHoverPrefab = ResourceLibrary.FindPrefab<UISpaceObjectSelected>();
            looks = new SpaceObjectLooks();
        }


        public override void Toggle(bool show) {
            if (show) {
                OnOpen();
            } else {
                OnClose();
            }
            base.Toggle(show);
        }

        public void UpdateWidget() {
            if (!gameObject.activeSelf) { return; }
            UpdateLines();
            iconDeclutter.Apply(container.Container.localScale.x, instances, false);
            RenderTargetLine();
            MovePlayerMarker(false);
            RenderDirectionMarker();
            menu.RefreshHyperspaceTabs();
            if (ShowingLayer != ctx.Layer) {
                D.Warn("Rebuilding starmap due to showing a different layer than wanted");
                Rebuild(true);
            }
        }

        private void UpdateLines() {
            var scale = container.Container.localScale.x;
            if (lastScale == scale) { return; }
            if (scaleLastFrame != scale && scaleLastFrame > 0f) {
                scaleLastFrame = scale;
                return;
            }
            scaleLastFrame = scale;
            lastScale = scale;
            var th = UniversePreviewWidget.LineThickness / scale;
            hyperQViz.UpdateLine(scale);
            pathPreviewViz.UpdateLine(scale);
            foreach (var line in lines) {
                line.SetThickness(th);
            }
        }

        private void RenderDirectionMarker() {
            if (player == null) {
                if (hasDirectionMarker) {
                    PrefabPool.Despawn(directionMarker.gameObject);
                    directionMarker = null;
                    hasDirectionMarker = false;
                }
                return;
            }
            if (!hasDirectionMarker) {
                hasDirectionMarker = true;
                // no lifetime on purpose
                var prefab = ResourceLibrary.FindPrefab<UIDirectionMarker>(
                    "UIDirectionMarker");
                directionMarker = PrefabPool.Spawn(prefab, container.Container);
            }
            var tr = player.transform;
            directionMarker.UpdateFor(tr.localPosition, player.StarmapSize,
                S.Sys.Ship.Rotation);
        }
        private Vector2 SpaceToStarmapPos(Vector2 pos) {
            var spread = Tunables.StarmapSpaceObjectSpread;
            pos *= spread;
            pos += centerPos;//(Vector2) center.transform.localPosition;
            return pos;
        }
        private void RenderTargetLine() {
            if (S.Sys.Ship.HasDestination(out var dLoc) && ctx.Layer == UniverseLayer.Sector) {
                dLoc = SpaceToStarmapPos(dLoc);
                Vector2 shipLoc = player.transform.localPosition;
                int siblingIdx = 0;
                if (!navLineViz.HasLine) {
                    foreach (var obj in instances) {
                        if (!obj.Key.Type.IsDynamic) {
                            siblingIdx++;
                        }
                    }
                }
                navLineViz.Draw(shipLoc, dLoc, siblingIdx, lastScale);
            } else if (navLineViz.HasLine) {
                navLineViz.Clear();
            }
        }

        private void OnOpen() {
            EntityUtils.HideEntityLink();
            Rebuild(refocus: ctx.Layer == null);
            var nextJumpTarget = S.Query.GetNextHyperjumpTarget.Ask();
            if (nextJumpTarget != null) {
                if (nextJumpTarget.SO.Parent == ctx.ParentSO) {
                    OnHoverUISpaceObject(null, false);
                }
            }
            MovePlayerMarker(true);
            var q = S.Query.GetHyperjumpQueue.Ask();
            RedrawHyperjumpQueue(q);
        }
        public void RedrawHyperjumpQueue(HyperjumpQueue queue) {
            // D.Warn("redrawing q");
            pathPreviewViz.Clear();
            if (ctx.ParentSO == null) { return; }
            if (ctx.ParentSO.Layer != queue.QueueLayer) {
                // The queue is for a different layer than what's currently shown (e.g. an
                // enqueue happened via Quick Search/context action rather than clicking a
                // node in the currently displayed container) - clear any stale path from a
                // previous layer instead of leaving it drawn.
                hyperQViz.Clear();
                return;
            }
            var q = queue.Queue;
            var warpTarget = queue.WarpTarget;
            if (q.Count > 0) {
                var jumps = S.Sys.ShipNav.PathJumpsFor(ctx.ParentSO);
                var start = A.PlayerSector;
                if (queue.QueueLayer == UniverseLayer.Universe) {
                    start = start?.Parent;
                }
                if (start != null) {
                    hyperQViz.Draw(jumps, start.SpaceEntity, q, queue.QueueLayer);
                } else {
                    D.Err("Start.parent is null! {0}", start);
                    hyperQViz.Clear();
                }
            } else {
                hyperQViz.Clear();
            }
            EnsureWarpTargetLinkCurrent(warpTarget);
            lastScale = -1f;
            scaleLastFrame = -1f;
            UpdateLines();
        }

        // Keeps the marker's line matching the current hop's destination, hiding it if there's
        // none. While actively moving, that's Progress.To regardless of mode - a queued
        // multi-hop journey has no warpTarget at all, but should still show where the ship is
        // currently heading; warpTarget only applies as a pre-departure indicator once nothing
        // is in motion. Cheap to call every frame (reference comparisons); only calls into the
        // heavier ShowWarpTargetLink/ShowLink (which disposes+recreates a child Lifetime) when
        // the link is actually missing or stale.
        private void EnsureWarpTargetLinkCurrent(ISpaceEntity warpTarget) {
            var hp = ctx.HyperjumpProgress;
            var target = (hp.IsMoving && hp.MovingInLayer == ctx.Layer) ? hp.To : warpTarget;
            if (warpLink != null && target == null) {
                playerMarker.HidePath();
                warpLink = null;
            } else if (target != null && (warpLink == null || !warpLink.Contains(target))) {
                ShowWarpTargetLink(target);
            }
        }

        private void MovePlayerMarker(bool force) {
            if (playerMarker == null) { return; }
            var hp = ctx.HyperjumpProgress;
            // No discrete signal fires when a hop actually starts moving, so keep the
            // warp-target line in sync here too (this runs every frame) rather than relying
            // solely on discrete HyperjumpQueueChanged events.
            EnsureWarpTargetLinkCurrent(S.Query.GetWarpTarget.Ask());
            // Layer type matching isn't enough - From/To must also actually be
            // instantiated in the currently displayed container, or UIPosition falls
            // back to the map origin and the marker snaps to an empty spot.
            if (hp.IsMoving && hp.MovingInLayer == ctx.Layer
                    && hp.From?.SO?.UI != null && hp.To?.SO?.UI != null) {
                if (force || !S.Clock.IsStopped) {
                    var pos = hp.PositionAt(
                        G.Ticks, S.Clock.TickProgress, out float prog);
                    playerMarker.HyperspaceMoveProgress = prog;
                    playerMarker.MoveTo(pos);
                }
            } else if (force) {
                playerMarker.HyperspaceMoveProgress = 0f;
                playerMarker.Load(playerParent);
            }
        }


        private void OnClose() {
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
            ctx.MainLifetime = null;
            ClearHover(true);
            menu.Cleanup();
        }


        public void Rebuild(bool refocus) {
            if (isRebuilding) {
                // A nested rebuild request (e.g. a signal fired synchronously as a side
                // effect of the rebuild in progress) would dispose lifetimes still in use
                // by the outer call. Coalesce into a single deferred rebuild instead of
                // re-entering.
                rebuildPending = true;
                rebuildPendingRefocus |= refocus;
                return;
            }
            isRebuilding = true;
            try {
                RebuildNow(refocus);
            } finally {
                isRebuilding = false;
            }
            if (rebuildPending) {
                rebuildPending = false;
                var pendingRefocus = rebuildPendingRefocus;
                rebuildPendingRefocus = false;
                Coroutines.OnNextFrame(() => Rebuild(pendingRefocus));
            }
        }

        private void RebuildNow(bool refocus) {
            if (lifetime != null) {
                lifetime.Dispose();
            }
            ctx.MainLifetime = lifetime = Lifetime.Create("Starmap");

            D.Log("Rebuilding starmap with refocus: {0}", refocus);
            lastScale = -1f;
            // scaleLastFrame = -1f;
            instances.Clear();
            lines.Clear();
            var player = ctx.Player;
            if (ctx.IsCrossRegionHyperjump) {
                // A cross-region hop spans two regions; only the Universe layer can represent
                // it. Resolve this before ParentSO is derived so the tail menu.Load() ->
                // RefreshHyperspaceTabs() finds the layer already correct instead of triggering
                // a nested SwitchLayer rebuild, which nulls ParentSO and defers (isRebuilding),
                // leaving this load to run against a null ParentSO.
                ctx.Layer = UniverseLayer.Universe;
            } else if (ctx.Layer == UniverseLayer.Sector && A.IsInHyperspace) {
                ctx.Layer = null;
            }
            if (ctx.Layer != null) {
                switch (ctx.Layer) {
                    case UniverseLayer.Universe: {
                        ctx.ParentSO = S.Universe.Root;
                        break;
                    }
                    case UniverseLayer.Region: {
                        ctx.ParentSO = SOUtils.GetPlayerSector(A.S).Region.SO;
                        break;
                    }
                    case UniverseLayer.Sector: {
                        ctx.ParentSO = player.Parent;
                        ctx.ParentSO.MarkVisited();
                        break;
                    }
                }
            }
            if (ctx.ParentSO == null) {
                if (ctx.IsInHyperspace) {
                    ctx.ParentSO = S.Universe.HyperspaceLocation.Parent;
                } else {
                    ctx.ParentSO = player.Parent;
                    ctx.ParentSO.MarkVisited();
                }
            }

            ShowingLayer = ctx.Layer = ctx.ParentSO.Layer;

            var siblings = ctx.ParentSO.Children;
            // sanity check
            for (int i = 0; i < siblings.Count; i++) {
                SpaceObject child = siblings[i];
                if (child.Parent != ctx.ParentSO) {
                    D.Err("Starmap trying to show something with wrong parent! {0} != {1}",
                        child.Parent, ctx.ParentSO);
                    var actualParent = child.Parent;
                    ctx.ParentSO.RemoveChild(child);
                    S.Sig.SpaceObjectRemoved.Send(child);
                    actualParent?.AddChild(child);
                    continue;
                }
            }

            // 5. Resize the content panel to fit the whole thing

            // 5.1 figure out the physical bounds of all siblings to get
            //     the size of bounding rectangle
            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minY = float.MaxValue;
            float maxY = float.MinValue;

            int boundSamples = 0;
            for (int i = 0; i < siblings.Count; i++) {
                var obj = siblings[i];
                if (obj == player) { continue; }
                var l = obj.Location;
                boundSamples++;
                if (l.x > maxX) { maxX = l.x; }
                if (l.x < minX) { minX = l.x; }
                if (l.y > maxY) { maxY = l.y; }
                if (l.y < minY) { minY = l.y; }
            }

            if (boundSamples == 0) {
                minX = -5;
                maxX = 5;
                minY = -5;
                maxY = 5;
            }

            var spread = Tunables.StarmapSpaceObjectSpread;

            // Make sure maxX and maxY are even, we want starmap size to be square
            maxX = maxY = Math.Max(maxX, maxY);
            minX = minY = Math.Min(minX, minY);

            var exitRadMul = 1.25f;
            if (ctx.ParentSO.Type.IdH == SpaceObjectTypeIdH.Universe) {
                exitRadMul = Tunables.StarmapPositionMultiplier_Region;
            } else if (ctx.ParentSO.Type.IdH == SpaceObjectTypeIdH.Region) {
                exitRadMul = Tunables.StarmapPositionMultiplier_Sector;
            }

            var exitRadius = Maths.Max(math.abs(maxX), math.abs(minX)) * exitRadMul * 1.25f;
            exitRadius = Mathf.Max(exitRadius, ctx.ParentSO.ExitDist);

            if (exitRadius < 1f) {
                D.Err("Exit radius too small! {0} for {1}", exitRadius, ctx.ParentSO);
                exitRadius = 30f;
            }
            S.Sys.Ship.ExitRadius = exitRadius;
            exitRadius *= spread;

            container.Container.sizeDelta = Consts.V2One * exitRadius * 2.25f;
            centerPos = container.Container.sizeDelta * new Vector2(0.5f, -0.5f);

            if (!AddExitRing(exitRadius)) {
                S.Sys.Ship.ExitRadius = 0f;
            }

            // 6. Get space object prefabs from the pool and fill them up with
            //    actual space object data
            this.player = null;
            this.playerParent = null;
            center = null;
            var addedPlayer = false;

            for (int i = 0; i < siblings.Count; i++) {
                var obj = siblings[i];
                if (obj.Type.IdH == SpaceObjectTypeIdH.Player) {
                    if (addedPlayer) {
                        D.Err("Player is added more than once to starmap siblings! {0} | {1}",
                            ctx.ParentSO, D.DumpCollection(siblings));
                        continue;
                    } else {
                        addedPlayer = true;
                    }
                }
                AddObject(obj, false);
            }
            if (!addedPlayer && ctx.Layer == UniverseLayer.Sector) {
                D.Err("Showing sector layer that didn't contain player SO! {0}",
                    ctx.ParentSO);
                // RebuildStarmap(true, true, "OnBlockUpdate-noplayer");
                // DoCloseStarmap("OnBlockUpdate-noplayer");
                return;
            }

            switch (ctx.ParentSO.Type.IdH) {
                case SpaceObjectTypeIdH.Region: {
                        scrollNav.SetLimits(scrollLimitsRegion);
                        break;
                    }
                case SpaceObjectTypeIdH.Universe: {
                        scrollNav.SetLimits(scrollLimitsUniverse);
                        break;
                    }
                default: {
                        scrollNav.SetLimits(scrollLimitsSector);
                        break;
                    }
            }

            if (this.player == null) {
                var parent = ctx.PlayerLocationInChildren;
                if (parent != null) {
                    playerParent = parent.UI;
                    if (playerMarker == null) {
                        // no lifetime on purpose
                        playerMarker = PrefabPool.Spawn(playerMarkerPrefab,
                            container.Container);
                    }
                    playerMarker.Load(playerParent);
                    playerMarker.SetInHyperspace(ctx.IsInHyperspace);
                }
            } else {
                if (playerMarker != null) {
                    PrefabPool.Despawn(playerMarker.gameObject);
                    playerMarker = null;
                }
            }

            int idx = 0;
            // Smaller items will always be in front of larger ones if they are on top
            // of each other (i.e. points of interest above the moon)
            foreach (var o in siblings.OrderByDescending(o => o.Size)) {
                instances[o].transform.SetSiblingIndex(idx++);
            }
            // But dynamic shit will float up
            foreach (var o in siblings) {
                if (o.Type.IsDynamic) {
                    instances[o].transform.SetAsLastSibling();
                } else if (o.UI.IsStarmapDecoration) {
                    instances[o].transform.SetAsFirstSibling();
                }
            }

            DrawLinks();

            // why was this here?
            // exitRing?.transform.SetAsLastSibling();
            // Prevent other space objects above player
            this.player?.transform.SetAsLastSibling();

            // RectTransform.Maximize();
            // if (!wasFocused) {
            //     wasFocused = true;
            //     wantsRefocus = true;
            // }
            // if (wantsRefocus) {
            //     wantsRefocus = false;
            //     RefocusWidget("OnBlockUpdate (wanted refocus)");
            // }
            if (refocus) {
                RefocusWidget("Rebuild");
            }
            // Universe view only, icons in the other views always show
            iconDeclutter.Measure(instances, ctx.Layer == UniverseLayer.Universe);
            iconDeclutter.Apply(container.Container.localScale.x, instances, true);
            if (hasDirectionMarker) {
                directionMarker.BringToFront();
            }


            // ensure that we've scanned the underlying sector after switching
            if (ctx.Layer == UniverseLayer.Region && A.IsInHyperspace) {
                var hasRadar = S.Sys.Sec.IsRadarActive() || A.InstantBuild;
                SOUtils.ScanWithRadar(S, S.Universe.HyperspaceLocation, hasRadar);
            }
            // RebuildMenu();

            // foreach (var tr in alwaysOnTop) {
            //     tr.SetAsLastSibling();
            // }

            // Move all UI circles to the back
            var rekts = UnityEngine.Pool.ListPool<Transform>.Get();
            MiscUtils.ForEachChild(container.Container, (t) => {
                if (t.TryGetComponent<UICircle>(out var circle)) {
                    rekts.Add(t);
                }
            });
            foreach (var r in rekts) {
                r.SetAsFirstSibling();
            }
            rekts.Clear();
            UnityEngine.Pool.ListPool<Transform>.Release(rekts);

            ClearHover(false);

            var queue = S.Query.GetHyperjumpQueue.Ask();
            if (ctx.Layer == queue.QueueLayer) {
                var q = queue.Queue;
                if (q.Count > 0) {
                    var jumps = S.Sys.ShipNav.PathJumpsFor(ctx.ParentSO);
                    var start = A.PlayerSector;
                    if (ctx.Layer == UniverseLayer.Universe) {
                        start = start.Parent;
                    }
                    hyperQViz.Draw(jumps, start.SpaceEntity, q, queue.QueueLayer);
                    var scale = container.Container.localScale.x;
                    hyperQViz.UpdateLine(scale);
                } else {
                    hyperQViz.Clear();
                }
            } else {
                hyperQViz.Clear();
            }

            if (playerMarker != null) {
                playerMarker?.ForceUpdate();
                playerMarker.transform.SetAsLastSibling();
            }
            // playerMarker.Load(playerParent) above snaps to the static departure position,
            // which is wrong mid-hyperjump. Force a correct reposition now instead of relying
            // on the next frame's MovePlayerMarker(false), which skips the animated branch
            // entirely while the clock is paused.
            MovePlayerMarker(force: true);
            menu.Load();

            D.Log("Children count in starmap: {0}", container.Container.childCount);
        }

        public void ClearHover(bool force) {
            if (objHover != null) {
                PrefabPool.Despawn(objHover.gameObject);
                objHover = null;
            }
            if (playerMarker != null) {
                var warpTarget = S?.Query?.GetWarpTarget.Ask();
                if (force || warpTarget == null || ctx.PlayerLocationInChildren == null) {
                    playerMarker.HidePath();
                } else {
                    ShowWarpTargetLink(warpTarget);
                }
            }
        }
        private void ShowWarpTargetLink(ISpaceEntity warpTarget) {
            if (playerMarker == null) { return; }
            if (warpTarget?.SO == null || ctx.PlayerLocationInChildren == null) {
                playerMarker.HidePath();
                return;
            }
            if (ctx.HyperjumpProgress == null) { return; }
            if (A.IsInHyperspace && ctx.Layer == ctx.HyperjumpProgress.MovingInLayer) {
                warpLink = SpaceLink.ForWarp(warpLink,
                    ctx.HyperjumpProgress.From,
                    ctx.HyperjumpProgress.To);
            } else if (warpTarget.SO.Parent == ctx.PlayerLocationInChildren.Parent) {
                warpLink = SpaceLink.ForWarp(warpLink,
                    ctx.PlayerLocationInChildren.SpaceEntity,
                    warpTarget);
            }
            playerMarker.ShowLink(lifetime, warpLink);
        }

        private bool AddExitRing(float exitRadius) {
            if (ctx.ParentSO.Type.IdH == SpaceObjectTypeIdH.Sector ||
                    ctx.ParentSO.Type.IdH == SpaceObjectTypeIdH.Region ||
                    ctx.ParentSO.Type.IdH == SpaceObjectTypeIdH.Universe) {
                // if (exitRing != null) {
                //     PrefabPool.Despawn(exitRing.gameObject);
                //     exitRing = null;
                // }
                return false;
            }
            var prefab = ResourceLibrary.FindPrefab<UICircle>("UICircleDashed");
            var exitRing = UISpawn.Widget(lifetime, prefab, container.Container, -1);
            UIRect.CopyRT(container.Container, exitRing.Rect);
            exitRing.Rect.localScale = Consts.V3One;
            exitRing.Load(exitRadius,
                Tunables.StarmapExitRingThickness, 1.25f, true,
                    The.UITheme.GetColor("StarmapExitRing", this));
            exitRing.Rect.SetAsFirstSibling();
            // D.Warn("Radius: {0}, Scale: {1}/{2}", exitRadius, exitRing.transform.localScale, container.localScale);
            exitRing.Rect.anchoredPosition = centerPos;
            return true;
        }
        public void SetPlayerAsLast() {
            player?.transform.SetAsLastSibling();
        }

        public void UpdateUISpaceObject(SpaceObject obj,
                Action<UISpaceObject, SpaceObjectLooks> act) {
            var uiso = instances.Get(obj, null);
            act(uiso, looks);
        }


        public void RemoveObject(SpaceObject so) {
            var soUI = so.UI;
            if (instances.TryGetValue(so, out var uiso)) {
                instances.Remove(so);
                soUI ??= uiso;
            }
            if (soUI != null) {
                PrefabPool.Despawn(soUI.gameObject);
            }
            so.SetUI(null);
        }
        public void AddObject(SpaceObject obj, bool fullRebuild) {
            if (instances.TryGetValue(obj, out var ui)) {
                if (!ui.gameObject.activeSelf) {
                    D.Err("Adding object to starmap, but it has an inactive UI (will rebuild): {0}->{1}",
                        obj, ui);
                    instances.Remove(obj);
                } else {
                    return;
                }
            }
            if (UIShowing.Starmap) {
                scrollNav.SavePosition();
            }
            var spread = Tunables.StarmapSpaceObjectSpread;
            var w = UISpawn.Widget(lifetime, prefab, container.Container, -1);
            if (this.center == null && obj.Location.sqrMagnitude < 0.00000001f) {
                this.center = w;
            }
            w.Load(container.Container, obj, looks, centerPos, spread);
            w.ShowIcons(iconDeclutter.IsShowing);
            if (obj.Type.Id == SpaceObjectTypeId.Player) {
                player = w;
            }
            instances.Add(obj, w);
            if (!fullRebuild && obj.Type.IsDynamic) {
                w.transform.SetAsLastSibling();
            }
            obj.SetUI(w);
            if (UIShowing.Starmap) {
                scrollNav.LoadPosition();
            }
            if (hasDirectionMarker) {
                directionMarker.BringToFront();
            }
            S.Sys.Ship.RotateStarfield();
        }
        private void DrawLinks() {
            var so = ctx.ParentSO;
            HashSet<SpaceLink> links = null;
            if (so.Type.IdH == SpaceObjectTypeIdH.Region) {
                links ??= new();
                foreach (var sec in so.Region.Sectors) {
                    foreach (var link in sec.Links) {
                        links.Add(link);
                    }
                }
            } else if (so.Type.IdH == SpaceObjectTypeIdH.Universe) {
                links ??= new();
                var map = S.Query.GetSpaceMap.Ask();
                foreach (var reg in map.Regions) {
                    foreach (var link in reg.Links) {
                        links.Add(link);
                    }
                }
            } else {
                return;
            }
            // D.Warn("Links for {0}: {1}", so, links.Count);
            foreach (var link in links) {
                var line = UISpawn.Widget(lifetime, linkPrefab, container.Container, -1);

                bool isIsolated = SpaceLink.IsIsolatedRegion(link);

                var colFrom = link.ColorFrom;
                var colTo = link.ColorTo;
                if (isIsolated) {
                    colFrom = colTo = Colors.WithA(Consts.SelectionGreyColor, 0.75f);
                }
                line.Load(
                    link.From.UIPosition,
                    link.To.UIPosition,
                    UniversePreviewWidget.LineThickness,
                colFrom, colTo);
                line.Rect.anchoredPosition = Consts.V3Zero;

                link.UpdateName(line.gameObject);

                var li = LinkedResources.Instance;
                if (isIsolated) {
                    // line.SetMaterial(li.spaceMapLinkIsolatedMat);
                    line.SetSprite(li.SpaceMapLinkIsolatedSprite, link.Length * 4f);
                } else {
                    // line.SetMaterial(li.spaceMapLinkMat);
                    line.SetSprite(li.SpaceMapLinkSprite, 0f);
                }

                lines.Add(line);
                line.transform.SetAsFirstSibling();
            }
        }
        public void RefocusWidget(string why) {
            if (player != null) {
                // D.Warn("Focusing on {0}", player);
                #if DEBUG_STARMAP
                D.Warn("Refocusing starmap widget on player: {0} | {1}", player, why);
                #endif
                scrollNav.FocusOn(player.GetComponent<RectTransform>(), 0.05f);
            } else if (playerParent != null) {
                #if DEBUG_STARMAP
                D.Warn("Refocusing starmap widget on player parent: {0} | {1}",
                    playerParent, why);
                #endif
                scrollNav.FocusOn(playerParent.GetComponent<RectTransform>(), 0.02f);
            } else if (center != null) {
                // D.Warn("Focusing on {0}", center);
                #if DEBUG_STARMAP
                D.Warn("Refocusing starmap widget on center: {0} | {1}", center, why);
                #endif
                scrollNav.FocusOn(center.GetComponent<RectTransform>(), 0.1f);
            } else {
                D.Err("Nothing to focus the widget on! ParentSO: {0} | {1}",
                    ctx.ParentSO, why);
            }
        }
        public void OnEnterSpaceObject(SpaceObject obj, SpaceObject prev) {
            if (obj.Type.IdH == SpaceObjectTypeIdH.Hyperspace) {
                playerMarker?.SetInHyperspace(true);
            }
        }
        public void OnHoverUISpaceObject(UISpaceObject obj, bool isHovering) {
            // Tearing down `lifetime` despawns the pooled UISpaceObjects it owns, and
            // deactivating their GameObjects makes Unity fire OnDisable -> OnPointerExit ->
            // this signal synchronously, from inside Lifetime.Dispose. Everything downstream
            // here spawns from `lifetime` (ShowLink -> CreateChild, UISpawn.Widget), which
            // throws on a disposed lifetime and would abort the teardown half-finished.
            if (lifetime == null || lifetime.IsDisposed) { return; }
            if (obj == null) {
                ShowHoverFor(null);
                return;
            }
            isHoveringPlayerSO = isHovering && obj.SpaceObject == S.Universe.Player;
            if (isHovering) {
                ShowHoverFor(obj);
            } else {
                ShowHoverFor(null);
            }
        }

        private void ShowHoverFor(UISpaceObject obj) {
            ClearHover(false);
            ShowPreviewPathTo(null);
            if (obj == null) { return; }
            var so = obj.SpaceObject;
            // stale ui
            if (so == null) { return; }
            // no lifetime on purpose
            objHover = PrefabPool.Spawn(objHoverPrefab, obj.UIPosition,
                Consts.QId, container.Container);
            objHover.LoadFor(obj);
            // Don't let a hover preview mutate the marker's line while a warp is actively in
            // flight - warpLink is shared, mutable state (SpaceLink.ForWarp writes into it in
            // place), so this would otherwise redirect the real in-flight line to point at
            // whatever's being hovered.
            if (!isHoveringPlayerSO && !ctx.HyperjumpProgress.IsMoving && playerMarker != null &&
                    SOUtils.CanJumpTo(so, out var isWarp)) {
                if (isWarp) {
                    var loc = ctx.PlayerLocationInChildren;
                    if (loc == null) {
                        D.Err("Player location in children is null when trying to show hover for: {0}", so);
                        return;
                    }
                    var link = SpaceLink.Between(loc.SpaceEntity, so.SpaceEntity);
                    if (link == null && isWarp) {
                        // assuming a warp
                        warpLink = SpaceLink.ForWarp(warpLink, loc.SpaceEntity, so.SpaceEntity);
                        link = warpLink;
                    }
                    playerMarker.ShowLink(lifetime, link);
                }
            }
            ShowPreviewPathTo(so);
        }

        private void ShowPreviewPathTo(SpaceObject so) {
            if (ctx.HyperjumpProgress.IsMoving) {
                // A hover preview doesn't make sense while a hop is actually in flight - the
                // ship is already committed to its current move.
                pathPreviewViz.Clear();
                return;
            }
            var se = so?.SpaceEntity;
            var playerSE = A.PlayerSector.SpaceEntity;
            if (ctx.Layer == UniverseLayer.Universe) {
                playerSE = playerSE.SO.Parent.SpaceEntity;
            }
            if (se == null || se == playerSE) {
                pathPreviewViz.Clear();
            } else {
                var queue = S.Query.GetHyperjumpQueue.Ask();
                var q = queue.Queue;
                var qLast = q.Last?.Value;
                var qCount = q.Count;
                if (so.Parent.Layer != queue.QueueLayer) {
                    qLast = null;
                    qCount = 0;
                } else if (q.Contains(se)) {
                    pathPreviewViz.Clear();
                    return;
                }
                var path = S.Query.GetHyperjumpPathPreviewFor.Ask(se);
                var jumps = S.Sys.ShipNav.PathJumpsFor(so.Parent);
                if (qCount > jumps) {
                    jumps = 0;
                } else {
                    jumps -= qCount;
                }
                pathPreviewViz.Draw(jumps, qLast ?? playerSE, path, queue.QueueLayer);
                var scale = container.Container.localScale.x;
                pathPreviewViz.UpdateLine(scale);
            }
        }
        public void FocusOn(Vector2 pos, string why) {
            var pos2 = SpaceToStarmapPos(pos);
            #if DEBUG_STARMAP
            D.Warn("Focusing starmap coord: {0}->{1} | {2}", pos, pos2, why);
            #endif
            scrollNav.FocusOn(pos2);
        }

        public bool FocusOn(SpaceObject ob) {
            var ui = instances.Get(ob, null);
            if (ui == null) {
                return false;
            }
            scrollNav.FocusOn(ui.GetComponent<RectTransform>(), zoomScale: 0.03f);
            // S.Clock.ExecuteAfterFramesForUI(2, () => {
            //     if (!UIShowing.Starmap) { return; }
            //     var ui = instances.Get(ob, null);
            //     if (ui != null) {
            //         #if DEBUG_STARMAP
            //         D.Warn("Will hover on Ui space object {0} | {1}", ob, why);
            //         #endif
                    S.UISig.OnHoverUISpaceObject.Send(ui, true);
                // }
            // });
            return true;
        }

        public List<UISpaceObject> CloseTo(UISpaceObject uISpaceObject) {
            closeUICache.Clear();
            // always include the clicked object itself (matches legacy)
            closeUICache.Add(uISpaceObject);
            var targetRect = ImageWorldRect(uISpaceObject);
            // only objects twice as large will be skipped if uiSpaceObject is in front
            var targetArea = targetRect.width * targetRect.height * 2f;
            var targetIndex = uISpaceObject.Rekt.GetSiblingIndex();
            foreach (var inst in instances) {
                var other = inst.Value;
                if (other == uISpaceObject) { continue; }
                if (other.IsStarmapDecoration) { continue; }
                // Lower sibling index = drawn behind. Objects in front of the clicked
                // one can already be clicked directly, so skip them.
                if (other.Rekt.GetSiblingIndex() > targetIndex) { continue; }
                var otherRect = ImageWorldRect(other);
                // Larger objects extend past the clicked one and stay clickable
                // in their uncovered area, so they don't need to be surfaced.
                if (otherRect.width * otherRect.height >= targetArea) { continue; }
                // Smaller + behind: only worth surfacing if it's actually hidden,
                // i.e. the clicked image covers its center.
                if (targetRect.Contains(otherRect.center)) {
                    closeUICache.Add(other);
                }
            }
            return closeUICache;
        }

        // World-space (screen-pixel) bounds of an object's rendered image.
        // GetWorldCorners reflects the full transform chain (zoom + autoscale),
        // so constant-size and zoom-scaled objects are both measured as actually drawn.
        private static Rect ImageWorldRect(UISpaceObject so) {
            so.Image.rectTransform.GetWorldCorners(Caches.Corners);
            // Corners: 0 = bottom-left, 2 = top-right
            var bl = Caches.Corners[0];
            var tr = Caches.Corners[2];
            return new Rect(bl.x, bl.y, tr.x - bl.x, tr.y - bl.y);
        }

        public void Cleanup() {
            ctx = null;
            S = null;
        }
        private Vector2 MouseStarmapPos {
            get {
                var pos = scrollNav.MousePos();
                var spread = Tunables.StarmapSpaceObjectSpread;
                pos -= centerPos;
                pos /= spread;
                return pos;
            }
        }

        public bool IsNearMouse(Vector2 starmapPos) {
            var mousePos = MouseStarmapPos;
            var dist = Vectors.Distance(starmapPos, mousePos);
            var zoom = container.Container.localScale.x;
            dist /= zoom;
            return dist < 4f;
        }

        public bool TryTravelToMouse() {
            // if (!S.Sys.ShipNav.IsFlightReady) {
            //     return false;
            // }
            // Sublight travel-on-RMB only makes sense in the Sector layer - Region/Universe
            // RMB is enqueue/dequeue on the HyperjumpQueue, handled by UISpaceObject.OnClick.
            if (ctx.Layer != UniverseLayer.Sector) {
                return false;
            }
            if (objHover != null && objHover.gameObject.activeSelf) {
                var hoverSo = objHover.UISO;
                if (hoverSo != null) {
                    SOActionTravelTo.BeginTravel(S, hoverSo.SpaceObject);
                    return true;
                }
            }
            var pos = MouseStarmapPos;
            var canTravelToPoint = pos.magnitude < S.Sys.Ship.ExitRadius;
            if (!canTravelToPoint) {
                return false;
            }
            ConfirmTravelTo(pos);
            return true;
        }

        private void ConfirmTravelTo(Vector2 pos) {
            SOUtils.CheckAbandonDerelicts(() => {
                if (S.Sys.Ship.SetDestination(pos, ctx.Player.Parent)) {
                    UISounds.PlayButtonClick();
                }
            }, skipCheckIfHasQueue: false, isHyperjump: false);
        }
    }
}
