using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Tools;
using Game.UI;
using KL.Grid;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;
using Game.Components;
using Game.Input;
using Game.Rendering;
using Game.Systems.AI;
using Game.Utils;

namespace Game.Visuals {
    public sealed class SelectionVFX {
        private readonly GameState S;
        // if anything added here, add to Destroy too!
        private readonly VFXOutlines tiles;
        private readonly VFXOutline hoverTile;
        // Not shared with hoverTile: UpdateHoverTile remembers what it showed, and the
        // build tool moving that outline would leave the free select hover missing
        private readonly VFXOutline buildFootprint;
        private bool isBuildFootprintPlaceable;
        private readonly VFXOutlines beings;
        private readonly VFXOutlines enemies;
        private readonly VFXOutlines obj;
        private readonly VFXTilesBuffer workSpots;
        private readonly VFXTilesBuffer outPorts;
        private readonly VFXTilesBuffer inPorts;
        public readonly VFXTiles MarkedTiles;
        public readonly VFXLines GoalLinks;
        // A drafted shooter's reach. Its own buffer: MarkedTiles belongs
        // to tile selection and ToolChoosePos, which would wipe a ring drawn into it.
        private readonly VFXTiles weaponRange;
        private readonly int[] weaponRangeTiles = new int[1024];
        private int weaponRangeCount;
        private int weaponRangeCenter = -1;
        private float weaponRangeRadius;

        private int lastWorkSpotPos;
        private int lastOutPortPos;
        private int lastInPortPos;
        private readonly float4 bounds;

        // If you ever increase this, also don't forget to increase the
        // VFX particle count
        private const int MaxMarkedTiles = 8192;

        public SelectionVFX(GameState s, Vector4 bounds) {
            S = s;
            this.bounds = bounds;
            tiles = new VFXOutlines("SelectedTiles", 1024,
                Consts.FreeSelectTileColor, bounds);
            hoverTile = new VFXOutline("HoverTile",
                Consts.FreeSelectTileHoverColor, bounds);
            buildFootprint = new VFXOutline("HoverTile",
                Consts.GreenRectColor, bounds);
            beings = new VFXOutlines("SelectedBeings", 1024,
                Consts.FreeSelectBeingColor, bounds);
            enemies = new VFXOutlines("SelectedBeings", 1024,
                Consts.FreeSelectEnemyColor, bounds);
            obj = new VFXOutlines("SelectedObj", 1024,
                Consts.FreeSelectObjColor, bounds);
            workSpots = new VFXTilesBuffer("Ports", 1024,
                Consts.WorkSpotColor, bounds).WithParticleTexture(
                    LinkedResources.Instance.WorkSpotTex);
            outPorts = new VFXTilesBuffer("Ports", 1024,
                Consts.OutPortColor, bounds)
                    .WithParticleTexture(LinkedResources.Instance.OutPortTex);
            inPorts = new VFXTilesBuffer("Ports", 1024,
                Consts.InPortColor, bounds)
                    .WithParticleTexture(LinkedResources.Instance.InPortTex);
            GoalLinks = new VFXLines("AdLink", 1024, CameraFocus.FlashColor(),
                bounds);

            MarkedTiles = new VFXTiles("MarkedTiles", MaxMarkedTiles, bounds);
            weaponRange = new VFXTiles("MarkedTiles", 1024, bounds);

            S.Sig.TilesSelected.AddListener(ShowSelectedTiles);
            S.Sig.MultiSelect.AddListener(OnMultiSelect);
            The.SysSig.UIThemeReloaded.AddListener(OnUIThemeReloaded);
        }

        private void OnUIThemeReloaded() {
            GoalLinks.SetColor(CameraFocus.FlashColor());
        }

        // The color a selected entity is outlined in, by the same rule as the outline groups
        // above. The ring that carries a new player's eye from the selection to its actions
        // (EntityActionsHint) lifts off that outline, so it takes the same color.
        public static Color OutlineColor(Entity entity) {
            if (entity is Being b) {
                return b.Data.IsFriendly ? Consts.FreeSelectBeingColor : Consts.FreeSelectEnemyColor;
            }
            return entity is Obj ? Consts.FreeSelectObjColor : Consts.FreeSelectTileColor;
        }

        public void UpdateSelectedBeings(List<Being> beings) {
            ShowSelectedBeings(beings);
        }
        public void UpdateSelectedObj(List<Obj> obj) {
            ShowSelectedObj(obj);
        }

        private void OnMultiSelect(List<Being> b, List<Obj> o, List<Tile> t) {
            ShowSelectedTiles(t);
        }

        private Guidelines previewPath;

        private readonly HashSet<int> selectedBeingIds = new();
        private readonly HashSet<int> selectedEntityIds = new();

        public void CreateManyShiftedLines(List<Vector2> from, Vector2 shift) {
            if (shift.sqrMagnitude < 1f) {
                GoalLinks.Update(0);
                return;
            }
            var count = 0;
            foreach (var src in from) {
                count = GoalLinks.AddLink(src, src + shift, count);
            }
            GoalLinks.Update(count);
        }

        public void CreateManyShiftedLines(List<Vector2> from,
                List<Vector2> shiftedBase, Vector2 shift) {
            if (shift.sqrMagnitude < 1f) {
                GoalLinks.Update(0);
                return;
            }
            int count = 0;
            int total = Mathf.Min(from.Count, shiftedBase.Count);
            for (int i = 0; i < total; i++) {
                count = GoalLinks.AddLink(from[i], shiftedBase[i] + shift, count);
            }
            GoalLinks.Update(count);
        }

        public void CreateManyToOneLines(List<Vector2> from, Vector2 to) {
            var count = 0;
            foreach (var src in from) {
                count = GoalLinks.AddLink(src, to, count);
            }
            GoalLinks.Update(count);
        }
        public void CreateLine(Vector2 from, Vector2 to) {
            GoalLinks.AddLink(from, to, 0);
            GoalLinks.Update(1);
        }

        public void UpdateLinks(List<Tile> tiles, List<Obj> objs, List<Being> beings) {
            var count = 0;

            var showAll = UIShowing.Overlay && UIShowing.OverlayType == OverlayId.Tasks;

            if (!showAll) {
                selectedBeingIds.Clear();
                selectedEntityIds.Clear();
                count = UpdateLinksTiles(tiles, count);
                count = UpdateLinksObjs(objs, count);
                count = UpdateLinksBeings(beings, count);
            }

            count = CreateDirectLines(count, showAll);

            GoalLinks.Update(count);
        }

        /// <summary>
        /// Ended up not using it because these are part of UI, not VFX
        /// </summary>
        private bool StopGoalLinksIfDisabled() {
            if (!RenderUtils.DisableAllVFX) { return false; }
            GoalLinks.Stop();
            return true;
        }

        private int CreateDirectLines(int count, bool showAll) {
            if (!showAll) {
                if (selectedBeingIds.Count == 0 || selectedEntityIds.Count == 0) {
                    return count;
                }
            }
            foreach (var goal in S.Sys.AI.ColonyGoals.Values) {
                if (goal.State != AIGoalState.InProgress) { continue; }
                if (goal.Agent == null) { continue; }
                if (goal.Target?.Entity == null) { continue; }
                if (showAll || selectedBeingIds.Contains(goal.Agent.EntityId)
                        || selectedEntityIds.Contains(goal.Target.EntityId)) {
                    Vector2 from = goal.Agent.Motion.Position.Exact;
                    // This is not good, because when we haul a pile, target
                    // entity may not exist anymore
                    Vector2 to = goal.Target.Entity.Position;
                    count = GoalLinks.AddLink(from, to, count);
                }
            }

            return count;
        }

        private int UpdateLinksBeings(List<Being> beings, int count) {
            var bc = beings.Count;
            for (int i = 0; i < bc; i++) {
                var b = beings[i];
                if (b.Movable?.IsPendingMove == true) {
                    count = GoalLinks.AddLink(b.Position,
                        b.Movable.MoveTarget, count);
                }
                if (!b.Data.IsFriendly) { continue; }
                selectedBeingIds.Add(b.Id);
                selectedEntityIds.Add(b.Id);
                var cu = b.AIAgent.CombatUnit;
                if (cu.Army != null && cu.AttackTarget != null) {
                    count = GoalLinks.AddLink(cu.Position, cu.AttackTarget.Position, count);
                }
            }

            return count;
        }

        private int UpdateLinksObjs(List<Obj> objs, int count) {
            var oc = objs.Count;
            bool isRadarActive = false;
            if (oc > 0) {
                isRadarActive = S.Sys.Sec.IsRadarActive();
            }
            for (int i = 0; i < oc; i++) {
                var obj = objs[i];
                selectedEntityIds.Add(obj.Id);
                if (obj.Movable?.IsPendingMove == true) {
                    count = GoalLinks.AddLink(obj.Position,
                        obj.Movable.MoveTarget, count);
                } else if (isRadarActive && obj.TryGetComponent<IWithTrajectory>(
                        out var trajectory)) {
                    if (trajectory.TryGetHitPoint(out var hitPoint, out var velocity)) {
                        count = GoalLinks.AddLink(obj.Position, hitPoint, count);
                    } else {
                        var pos = obj.Position;
                        count = GoalLinks.AddLink(pos, pos + (velocity * 25f), count);
                    }
                }
            }

            return count;
        }

        private int UpdateLinksTiles(List<Tile> tiles, int count) {
            var tc = tiles.Count;
            for (int i = 0; i < tc; i++) {
                var t = EntityUtils.ParentObj(tiles[i]);
                selectedEntityIds.Add(t.Id);
                // Reinstallable
                if (t.Reinstallable?.IsPending == true) {
                    var src = t.Reinstallable.Source;
                    var tgt = t.Reinstallable.Target;
                    if (src == null && tgt == null) {
                        D.Err("Reinstallable pending but no src or tgt: {0}", t);
                    } else {
                        var to = src == null ? tgt.Tile.Position : src.Tile.Position;
                        count = GoalLinks.AddLink(t.Position, to, count);
                    }
                }
            }

            return count;
        }

        private bool isShowingPathViz;

        public void ShowPreviewPath(List<int> path, Color color) {
            previewPath ??= Guidelines.Create("PathGuideline");
            previewPath.ShowPath(path, 0, Consts.V2Zero, color);
            isShowingPathViz = true;
        }

        public void HidePreviewPath() {
            if (isShowingPathViz) {
                isShowingPathViz = false;
                previewPath.Dispose();
            }
        }

        public void ShowSelectedBeings(List<Being> entities) {
            var stc = (entities?.Count) ?? 0;
            var count = 0;
            var countEnemy = 0;
            for (int i = 0; i < stc; i++) {
                var b = entities[i];
                if (!b.IsActive) {
                    continue;
                }
                if (b.Data.IsFriendly) {
                    b.Motion.ShowPathViz(updateOnly: false);
                    count = beings.AddOutline(b.SelectPosition, b.Size, count);
                } else {
                    countEnemy = enemies.AddOutline(b.SelectPosition, b.Size, countEnemy);
                }
            }
            if (count > 0) {
                beings.Update(count);
            } else {
                beings.Stop();
            }
            if (countEnemy > 0) {
                enemies.Update(countEnemy);
            } else {
                enemies.Stop();
            }
            ShowWeaponRange(stc == 1 ? entities[0] : null);
        }

        // A drafted shooter's reach: without it a refused out-of-range order (CombatOrders) is
        // guesswork. One selected unit only.
        private void ShowWeaponRange(Being b) {
            float radius = 0f;
            if (b != null && b.IsActive && b.Data.IsFriendly
                    && b.AIAgent.DirectControl.IsUnderDirectControl) {
                var attack = b.AIAgent.CombatUnit.PreviewBestAttack();
                if (attack != null && !attack.IsMelee) {
                    radius = attack.Stats.RangeMax.Value;
                }
            }
            if (radius <= 0f) {
                if (weaponRangeCenter != -1) {
                    weaponRangeCenter = -1;
                    weaponRange.Stop();
                }
                return;
            }
            var center = b.PosIdx;
            if (center == weaponRangeCenter && radius == weaponRangeRadius) { return; }
            weaponRangeCenter = center;
            weaponRangeRadius = radius;
            weaponRangeCount = Pos.OuterRadius(center, radius - 1f, radius, weaponRangeTiles);
            var col = Colors.ToF4(Consts.ColorWeaponRange);
            int count = 0;
            for (int i = 0; i < weaponRangeCount; i++) {
                count = weaponRange.AddTile(weaponRangeTiles[i], col, 0.9f, count);
            }
            weaponRange.Show(count);
        }

        public void ShowSelectedObj(List<Obj> entities) {
            var stc = (entities?.Count) ?? 0;
            var count = 0;
            for (int i = 0; i < stc; i++) {
                var o = entities[i];
                count = obj.AddOutline(o.SelectPosition, o.SelectableSize, count);
            }
            if (count > 0) {
                obj.Update(count);
            } else {
                obj.Stop();
            }
        }

        private bool wasShowingHover;
        private bool wasShowingTPHover;
        private Tile lastHoverTile;

        public void UpdateHoverTile(bool isHovering, Tile tile) {
            if (wasShowingHover == isHovering && lastHoverTile == tile) {
                return;
            }
            lastHoverTile = tile;
            wasShowingHover = isHovering;
            if (isHovering) {
                if (tile.Transform.IsChild) {
                    tile = tile.Transform.Parent;
                }
                hoverTile.SetOutline(tile.Position, tile.Size);
                hoverTile.Show();
                if (tile.Teleporter != null) {
                    wasShowingTPHover = A.Grid.UI.ShowTeleporterHover(
                        tile.Teleporter);
                }
            } else {
                hoverTile.Stop();
                if (wasShowingTPHover) {
                    wasShowingTPHover = false;
                    A.Grid.UI.RefreshVFX();
                }
            }
        }

        // Show reinits the effect, so it only happens when the outline changes,
        // or the circling would restart every frame
        public void ShowBuildFootprint(Tile preview, int pos, bool isPlaceable) {
            var tr = preview.Transform;
            var center = tr.RotatedCenter(pos);
            var size = tr.Size;
            var outline = new float4(center.x, center.y, size.x / 2f, size.y / 2f);
            if (buildFootprint.IsPlaying && isBuildFootprintPlaceable == isPlaceable
                    && buildFootprint.Position.Equals(outline)) {
                return;
            }
            isBuildFootprintPlaceable = isPlaceable;
            buildFootprint.SetColor(isPlaceable
                ? Consts.GreenRectColor : Consts.RedRectColor);
            buildFootprint.SetOutline(center, size);
            buildFootprint.Show();
        }

        public void HideBuildFootprint() {
            buildFootprint.Stop();
        }

        public void ShowRange(int rangeCount) {
            MarkedTiles.Show(rangeCount);
        }

        public void ShowSelectedTiles(List<Tile> selectedTiles) {
            var stc = (selectedTiles?.Count) ?? 0;
            var count = 0;
            var wsCount = 0;
            var opCount = 0;
            var ipCount = 0;
            var rangeCount = 0;
            var areas = S.Sys.Areas;
            outlineGroups.Clear();
            for (int i = 0; i < stc; i++) {
                var tile = selectedTiles[i];
                if (tile.Transform.IsChild) {
                    tile = tile.Transform.Parent;
                }
                count = tiles.AddOutline(tile.Position, tile.Size, count);
                if (tile.Transform.HasWorkSpot) {
                    wsCount = workSpots.AddTile(tile.Transform.WorkSpot, 0.8f, wsCount);
                }
                if (tile.Transform.HasOutPort) {
                    opCount = outPorts.AddTile(tile.Transform.OutPort, 1.0f, opCount);
                }
                if (tile.Transform.HasInPort) {
                    ipCount = inPorts.AddTile(tile.Transform.InPort, 1.0f, ipCount);
                }
                if (!outlineGroups.TryAdd(tile, areas)) {
                    rangeCount = AddRange(rangeCount, tile, tile.PosIdx);
                }
            }
            rangeCount = outlineGroups.AddTo(MarkedTiles, areas, outlineTiles, rangeCount);

            // Selecting many ranged devices at once (select similar) can add
            // more tiles than the buffer holds; the extra ones are dropped
            MarkedTiles.Show(Mathf.Min(rangeCount, MarkedTiles.MaxParticles));
            tiles.Show(count);
            workSpots.Show(wsCount);
            outPorts.Show(opCount);
            inPorts.Show(ipCount);
        }

        public void ShowPreviewRange(Tile tile, int pos) {
            if (tile == null) {
                MarkedTiles.Show(0);
                return;
            }
            var rangeCount = AddRange(0, tile, pos);
            rangeCount = ShowGroupRanges(tile, pos, rangeCount);

            MarkedTiles.Show(rangeCount);
        }

        private int ShowGroupRanges(Tile tile, int pos, int rangeCount) {
            if (rangeCount <= 0 || !tile.TryGetComponent<IRangedShowOthers>(
                    out var gr, IsRangedNonZero)) {
                return rangeCount;
            }
            if (gr.Range < 1) {
                return rangeCount;
            }
            var tilePos = EntityUtils.CenterOf(pos);
            var found = S.Components.FindAllMatchingByInterface(
                    Caches.RangedGroups, (g) => {
                        if (g.RangeGroupId != gr.RangeGroupId) {
                            if (g.RangeGroupId == 0 || gr.RangeGroupId == 0) {
                                return false;
                            }
                        }
                        if (g.RangeGroupId == 0) {
                            if (g.TypeHash != gr.TypeHash) {
                                return false;
                            }
                        }
                        if (g == gr) { return false; }
                        var maxRange = Maths.Max(
                            g.RangeOthersMaxSqrDist, gr.RangeOthersMaxSqrDist);
                        if (maxRange < 1f) {
                            maxRange = (gr.Range + g.Range) * (gr.Range + g.Range);
                        }
                        return Vectors.SqrDistance(
                            g.Entity.Position, tilePos) < maxRange;
                    });
            for (int i = 0; i < found; i++) {
                var t = Caches.RangedGroups[i];
                if (t.Entity is Tile ti) {
                    rangeCount = AddRange(rangeCount, ti, ti.PosIdx, 0.5f);
                    if (rangeCount >= MaxMarkedTiles) {
                        break;
                    }
                }
            }

            return rangeCount;
        }

        private bool IsRangedNonZero(IRanged ranged) {
            return ranged.Range > 0f;
        }

        private readonly List<int> outlineTiles = new();
        private readonly RangeAreaOutline singleOutline = new();
        private readonly RangeOutlineGroups outlineGroups = new();

        public int AddRange(int rangeCount, Tile tile, int pos, float colorAmt = 1f) {
            if (tile == null) { return rangeCount; }
            var position = tile.Transform.RotatedCenter(pos);
            if (!tile.TryGetComponent<IRanged>(out var ranged, IsRangedNonZero)) {
                return rangeCount;
            }
            return AddRange(rangeCount, pos, colorAmt, position, ranged);
        }

        public int AddRange(int rangeCount, int centerPos, float colorAmt,
                Vector2 center, IRanged ranged) {
            var rcol = Colors.ToF4(ranged.RadiusColor);
            if (colorAmt < 0.999f) {
                // reduce the alpha
                rcol.w *= colorAmt;
            }
            if (ranged.RadiusSameArea) {
                var ar = S.Sys.Areas;
                var area = ar.At(centerPos);
                if (area == -1) {
                    // wall
                    return rangeCount;
                }
                if (ranged is IRangedAreaOutline) {
                    singleOutline.Clear();
                    singleOutline.Add(centerPos, center, ranged.Range);
                    singleOutline.Collect(ar, outlineTiles);
                    for (int i = 0; i < outlineTiles.Count; i++) {
                        rangeCount = MarkedTiles.AddTile(outlineTiles[i],
                            rcol, 0.9f, rangeCount);
                    }
                } else if (ranged is IRangedCached rangedCached) {
                    var cached = rangedCached.GetCachedRange(centerPos);
                    foreach (var p in cached) {
                        if (!ar.IsSameAreaOrOutside(centerPos, p)) {
                            continue;
                        }
                        rangeCount = MarkedTiles.AddTile(p,
                            rcol, 0.9f, rangeCount);
                    }
                } else {
                    Pos.ForEachInOuterRadius(center, ranged.Range - 1, ranged.Range,
                        (c) => {
                            if (!ar.IsSameAreaOrOutside(centerPos, c)) {
                                return;
                            }
                            rangeCount = MarkedTiles.AddTile(c,
                                rcol, 0.9f, rangeCount);
                        });
                }
            } else {
                if (ranged is IRangedCached rangedCached) {
                    var cached = rangedCached.GetCachedRange(centerPos);
                    foreach (var p in cached) {
                        rangeCount = MarkedTiles.AddTile(p, rcol, 0.9f, rangeCount);
                    }
                } else {
                    Pos.ForEachInOuterRadius(center, ranged.Range - 1, ranged.Range,
                        (c) => {
                            rangeCount = MarkedTiles.AddTile(c,
                                rcol, 0.9f, rangeCount);
                        });
                }
            }

            return rangeCount;
        }

        public void Destroy() {
            The.SysSig.UIThemeReloaded.RemoveListener(OnUIThemeReloaded);
            tiles.Destroy();
            hoverTile.Destroy();
            buildFootprint.Destroy();
            beings.Destroy();
            enemies.Destroy();
            obj.Destroy();
            workSpots.Destroy();
            outPorts.Destroy();
            inPorts.Destroy();
            MarkedTiles.Destroy();
            weaponRange.Destroy();
            GoalLinks.Destroy();
            previewPath?.Dispose();
        }

        public void ShowWorkSpotsAndPorts(List<int> workSpots, List<int> inPorts, List<int> outPorts) {
            int wsCount = 0;
            int ipCount = 0;
            int opCount = 0;
            for (int i = workSpots.Count - 1; i >= 0; i--) {
                int pos = workSpots[i];
                wsCount = this.workSpots.AddTile(pos, 0.9f, wsCount);
            }
            for (int i = inPorts.Count - 1; i >= 0; i--) {
                int pos = inPorts[i];
                ipCount = this.inPorts.AddTile(pos, 0.9f, ipCount);
            }
            for (int i = outPorts.Count - 1; i >= 0; i--) {
                int pos = outPorts[i];
                opCount = this.outPorts.AddTile(pos, 0.9f, opCount);
            }
            if (wsCount > 0) {
                this.workSpots.Show(wsCount);
            } else {
                this.workSpots.Stop();
            }
            if (ipCount > 0) {
                this.inPorts.Show(ipCount);
            } else {
                this.inPorts.Stop();
            }
            if (opCount > 0) {
                this.outPorts.Show(opCount);
            } else {
                this.outPorts.Stop();
            }
        }

        public void ShowWorkSpot(Tile tilePreview, int hoverPos) {
            if (tilePreview?.Transform.HasWorkSpot == true) {
                var pos = tilePreview.Transform.RotatedWorkSpot(hoverPos);
                if (lastWorkSpotPos != pos) {
                    lastWorkSpotPos = pos;
                    workSpots.AddTile(pos, 0.9f, 0);
                    workSpots.Show(1);
                }
            } else {
                workSpots.Stop();
            }
        }
        public void ShowPorts(Tile tilePreview, int hoverPos) {
            if (tilePreview?.Transform.HasInPort == true) {
                var pos = tilePreview.Transform.RotatedInPort(hoverPos);
                if (lastInPortPos != pos) {
                    lastInPortPos = pos;
                    inPorts.AddTile(pos, 0.9f, 0);
                    inPorts.Show(1);
                }
            } else {
                inPorts.Stop();
            }
            if (tilePreview?.Transform.HasOutPort == true) {
                var pos = tilePreview.Transform.RotatedOutPort(hoverPos);
                if (lastOutPortPos != pos) {
                    lastOutPortPos = pos;
                    outPorts.AddTile(pos, 0.9f, 0);
                    outPorts.Show(1);
                }
            } else {
                outPorts.Stop();
            }
        }
        public void ShowMarkedTiles(int count) {
            MarkedTiles.Show(count);
        }

        public void HideAll() {
            tiles.Stop();
            buildFootprint.Stop();
            workSpots.Stop();
            MarkedTiles.Stop();
            obj.Stop();
            beings.Stop();
            enemies.Stop();
            weaponRange.Stop();
            weaponRangeCenter = -1;
            GoalLinks.Stop();
            HidePreviewPath();
        }
    }
}
