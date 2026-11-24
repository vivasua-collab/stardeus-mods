#if UNITY_EDITOR
//#define DEBUG_GEN
#endif
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Input;
using Game.Rendering;
using Game.Scenarios;
using Game.Systems.Creatures;
using Game.Systems.Naming;
using Game.Systems.UIUX;
using Game.Utils;
using KL.Collections;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Procgen {
    public abstract class BaseShipGenStage : IShipGenStage {
        public virtual bool ForceDebug => false;
        public virtual bool SkipRNG => false;
        protected GameState S;
        public int Attempts { get; set; }
        protected ShipSpec spec;
        public ShipSpec Result => spec;
        protected bool isPreview;
        protected Texture2D previewTex;

        public virtual string Message => GetType().Name;
        public Rng StageRng => stageRngFork;
        protected Rng rng => stageRngFork;
        protected Rng stageRngFork;
        public bool NeedsRepeating { get; protected set; }
        public bool StartOver { get; protected set; }
        public virtual bool StartOverIfNeedsRepeating => false;
        // Requires islands is taken care of
        public virtual bool RequiresAreas => false;
        public virtual bool ProducesIslands => false;
        public virtual bool IsAsync => false;

        public string LogPrefix => string.Format("GEN_SHIP {0} [{1}]",
            spec?.GenCommand, GetType().Name);
        protected virtual void OnInitialize() { }

        public virtual void Apply() { }
        public virtual IEnumerator ApplyAsync() { yield return null; }

        protected void RemoveFloorLights(int p, int radius) {
            if (p < 1) {
                D.Err("Trying to remove floor lights at OOB: {p}");
                return;
            }
            if (radius < 2) {
                TryRemoveLight(p);
                return;
            }
            // Remove floor lights from beneath / nearby
            Pos.ForEachAround(p, radius, true, true, (i) => {
                TryRemoveLight(i);
                return true;
            });
        }

        private void TryRemoveLight(int i) {
            var fl = EntityUtils.FloorAt(i);
            if (fl?.DefinitionId.EndsWith("Lit") == true) {
                ReplaceTile(i, EntityUtils.CreatePrototype<Tile>(
                    fl.DefinitionId.Replace("Lit", "")));
            }
        }

        protected void MakeAreaInvincible(IEnumerable<int> chosen, int expandBy = 0) {
            // expand it to include walls and extra bit of padding
            if (expandBy > 0) {
                var expanded = ExpandArea(new HashSet<int>(chosen), expandBy);
                spec.InvincibleParts.AddRangeInefficient(expanded);
            } else {
                spec.InvincibleParts.AddRange(chosen);
            }
        }
        protected static BeingData GetBeingData(GameState S, Species species, BeingType bType, GameSetupData config) {
            BeingData data = null;
            if (config != null && config.StartingBeings.TryGetValue(species,
                    out var beings)) {
                if (beings.Count > 0) {
                    data = beings[0].DeepCopyFor(S);
                    beings.RemoveAt(0);
                }
            }
            if (data == null) {
                if (config != null) {
                    D.Err("Want to generate a {0} but starting beings has no data!", species);
                }
                var vars = new GenBeingVars(species, bType);
                vars.Body = FactionDef.Player.GenBodyVars;
                data = S.Query.GenBeingFull.Ask(vars, Rng.Unseeded);
            }
            return data;
        }
        protected bool IsWithinGenArea(int pos) {
            return spec.IsWithinGenArea(pos);
        }
        protected void BuildTiles(ICollection<int> list, string def, bool allowFailure = true) {
            foreach (var pos in list) {
                if (pos < 1) {
                    D.Err("Trying to build tile at invalid pos (from a list): {0} {1}", pos, def);
                    return;
                }
                if (isPreview) {
                    Pos.ToXY(pos, out var x, out var y);
                    previewTex.SetPixel(x, y, Consts.ColorWhite);
                } else {
                    // D.Warn("Building tile at {0}: {1}", Pos.String(pos), def);
                    BuildTile(pos, EntityUtils.CreatePrototype<Tile>(def), allowFailure: allowFailure);
                }
            }
        }
        protected bool TryBuildTemplate(int pos, Def def) {
            var proto = EntityUtils.CreatePrototype<Tile>(def);
            var cmd = new CmdPlaceTile(pos, proto, warn: false, true);
            cmd.Execute(S);
            return cmd.IsSuccess;
        }
        protected Tile BuildTile(int pos, Tile prototype, bool replaceFloorLights = false,
                bool allowFailure = true) {
            if (pos < 1) {
                D.Err("Trying to build tile at invalid pos: {0} {1}", pos,
                    prototype.Definition.Id);
                return null;
            }
            if (isPreview) {
                Pos.ToXY(pos, out var x, out var y);
                previewTex.SetPixel(x, y, Consts.ColorWhite);
                return null;
            }
            if (replaceFloorLights) {
                var cnt = prototype.Transform.GetSubtilePos(pos, Caches.PosIdx);
                for (int i = 0; i < cnt; i++) {
                    var sp = Caches.PosIdx[i];
                    RemoveFloorLights(sp, 1);
                }
            }
            var cmd = new CmdPlaceTile(pos, prototype, warn: false, true);
            cmd.Execute(S);
            if (cmd.PlacedTile == null && !allowFailure) {
                D.Err("Failed placing critical tile during generation! {0}: {1}",
                    Pos.String(pos), prototype);
            }
            return cmd.PlacedTile;
        }
        protected bool IsWallFacingOutdoors(int pos) {
            var areas = S.Sys.Areas;
            if (areas.IsOutdoors(Pos.N(pos))) { return true; }
            if (areas.IsOutdoors(Pos.S(pos))) { return true; }
            if (areas.IsOutdoors(Pos.E(pos))) { return true; }
            if (areas.IsOutdoors(Pos.W(pos))) { return true; }
            if (areas.IsOutdoors(Pos.NE(pos))) { return true; }
            if (areas.IsOutdoors(Pos.NW(pos))) { return true; }
            if (areas.IsOutdoors(Pos.SE(pos))) { return true; }
            if (areas.IsOutdoors(Pos.SW(pos))) { return true; }
            return false;
        }
        protected bool IsOuterWall(int pos) {
            // if (EntityUtils.WallAt(wall).Definition.Id ==
            //         DefIds.StructureWallsWallReinforced01) {
            //     return true;
            // }
            var areas = S.Sys.Areas;
            if (areas.IsSpace(Pos.N(pos))) { return true; }
            if (areas.IsSpace(Pos.S(pos))) { return true; }
            if (areas.IsSpace(Pos.E(pos))) { return true; }
            if (areas.IsSpace(Pos.W(pos))) { return true; }
            if (areas.IsSpace(Pos.NE(pos))) { return true; }
            if (areas.IsSpace(Pos.NW(pos))) { return true; }
            if (areas.IsSpace(Pos.SE(pos))) { return true; }
            if (areas.IsSpace(Pos.SW(pos))) { return true; }
            return false;
        }
        protected void ReplaceTile(int pos, Tile prototype) {
            EntityUtils.ReplaceTile(S, pos, prototype);
        }
        protected int RandomX(float from = 0, float to = 1) {
            return RandomOnAxis(from, to, spec.CenterX, spec.Width);
        }

        protected int RandomY(float from = 0, float to = 1) {
            return RandomOnAxis(from, to, spec.CenterY, spec.Height);
        }

        private int RandomOnAxis(float from, float to, int center, int dimension) {
            float min = center - dimension / 2;
            float max = center + dimension / 2;
            min = Mathf.Lerp(min, max, from);
            max = Mathf.Lerp(min, max, to);
            return Mathf.RoundToInt(rng.Range(min, max));
        }
        protected bool IsRoomOddlyShaped(ICollection<int> tiles, PosRect roomRect) {
            return roomRect.MinEdgeLength < 3 ||
                (tiles.Count / (float) roomRect.Size) < 0.4f;
        }

        public void SetRng(Rng rng) {
            D.Ass(rng != null, "Trying to set null RNG!");
            stageRngFork = rng;
        }

        public void Initialize(ShipSpec spec, GameState s) {
            S = s;
            this.spec = spec;
            OnInitialize();
        }
        protected void FindFullNeighbors(int x, int y,
                out bool n, out bool s, out bool e, out bool w) {
            n = !spec.IsEmpty(x, y + 1);
            s = !spec.IsEmpty(x, y - 1);
            e = !spec.IsEmpty(x + 1, y);
            w = !spec.IsEmpty(x - 1, y);
        }
        protected int FindNeighborCount(int x, int y, bool includeDiagonal) {
            var count = 0;
            var n = !spec.IsEmpty(x, y + 1, false, false);
            var s = !spec.IsEmpty(x, y - 1, false, false);
            var e = !spec.IsEmpty(x + 1, y, false, false);
            var w = !spec.IsEmpty(x - 1, y, false, false);
            if (n) { count++; }
            if (y == 0 || s) { count++; }
            if (e) { count++; }
            if (w) { count++; }
            if (includeDiagonal) {
                var ne = !spec.IsEmpty(x + 1, y + 1);
                var se = !spec.IsEmpty(x + 1, y - 1);
                var nw = !spec.IsEmpty(x - 1, y + 1);
                var sw = !spec.IsEmpty(x - 1, y - 1);
                if (y == 0 || sw) { count++; }
                if (y == 0 || se) { count++; }
                if (nw) { count++; }
                if (ne) { count++; }
            }
            return count;
        }
        protected void FindNeighbors(CellType type, int x, int y,
                out bool n, out bool s, out bool e, out bool w) {
            n = spec.IsCell(x, y + 1, type);
            s = spec.IsCell(x, y - 1, type);
            e = spec.IsCell(x + 1, y, type);
            w = spec.IsCell(x - 1, y, type);
        }
        protected void FindDiagonalNeighbors(CellType type, int x, int y,
                out bool ne, out bool se, out bool nw, out bool sw) {
            ne = spec.IsCell(x + 1, y + 1, type);
            se = spec.IsCell(x - 1, y + 1, type);
            nw = spec.IsCell(x + 1, y - 1, type);
            sw = spec.IsCell(x - 1, y - 1, type);
        }

        ///<summary>Will return world position with either top or bottom
        ///of the mirrored half</summary>
        protected int AdjustedPos(int x, int y, bool top) {
            var adjX = spec.CellToWorldX(x);
            var adjY = top ?
                spec.CellToWorldY(spec.TemplateHeight + y) :
                spec.CellToWorldY(spec.TemplateHeight - y - 1);
            return Pos.FromXY(adjX, adjY);
        }

        protected void AppendAdjustedPosition(int x, int y, int trimW, int trimH,
                List<int> list, Func<int, bool> check = null) {
            var adjX = spec.CellToWorldX(x);
            var adjY = spec.CellToWorldY(y);

            // We're getting the bottom left corner of the position
            // and the grid cell is cellWidth x cellHeight
            // so we have to figure out how to center this
            for (int i = trimW; i < spec.CellWidth - trimW; i++) {
                for (int j = trimH; j < spec.CellHeight - trimH; j++) {
                    var pos = Pos.FromXY(adjX + i, adjY + j);
                    if (check?.Invoke(pos) != false) {
                        list.Add(pos);
                    }
                }
            }
        }

        protected void AppendAdjustedPositionMirrored(int x, int y, ICollection<int> list,
                Func<int, bool> check= null) {
            AppendAdjustedPosition(x, spec.TemplateHeight + y, list, check);
            AppendAdjustedPosition(x, spec.TemplateHeight - y - 1, list, check);
        }

        protected void AppendAdjustedPosition(int x, int y, ICollection<int> list,
                Func<int, bool> check = null) {
            var adjX = spec.CellToWorldX(x);
            var adjY = spec.CellToWorldY(y);
            for (int i = 0; i < spec.CellWidth; i++) {
                for (int j = 0; j < spec.CellHeight; j++) {
                    var pos = Pos.FromXY(adjX + i, adjY + j);
                    if (check?.Invoke(pos) != false) {
                        list.Add(pos);
                    }
                }
            }
        }
        protected void AppendAdjustedPositionRowMirrored(int x, int y, int rowY,
                ICollection<int> list, Func<int, bool> check = null) {
            AppendAdjustedPositionRow(x, spec.TemplateHeight + y, rowY, list, check);
            AppendAdjustedPositionRow(x, spec.TemplateHeight - y - 1,
                spec.CellHeight - rowY - 1, list, check);
        }
        protected void AppendAdjustedPositionColumnMirrored(int x, int y, int colX,
                ICollection<int> list, Func<int, bool> check = null) {
            AppendAdjustedPositionColumn(x, spec.TemplateHeight + y, colX, list, check);
            AppendAdjustedPositionColumn(x, spec.TemplateHeight - y - 1, colX, list, check);
        }

        protected void AppendAdjustedPositionRow(int x, int y, int rowY,
                ICollection<int> list, Func<int, bool> check = null) {
            var adjX = spec.CellToWorldX(x);
            var adjY = spec.CellToWorldY(y);
            for (int i = 0; i < spec.CellWidth; i++) {
                var pos = Pos.FromXY(adjX + i, adjY + rowY);
                if (check?.Invoke(pos) != false) {
                    list.Add(pos);
                }
            }
        }

        protected void AppendAdjustedPositionColumn(int x, int y, int colX,
                ICollection<int> list, Func<int, bool> check = null) {
            var adjX = spec.CellToWorldX(x);
            var adjY = spec.CellToWorldY(y);
            for (int i = spec.CellHeight - 1; i >= 0; i--) {
                var pos = Pos.FromXY(adjX + colX, adjY + i);
                if (check?.Invoke(pos) != false) {
                    list.Add(pos);
                }
            }
        }
        protected void PumpAtmosphereAt(IEnumerable<int> area) {
            var temp = Units.CelsiusToKelvin(21);
            foreach (var p in area) {
                S.SetTemp(p, temp);
                S.SetO2(p, 0.21f);
            }
        }


        /// <summary>
        /// Finds a spawn position for a being: within the stage's GenArea if it has one,
        /// otherwise around the ship's CPU. Shared by gen stages that spawn beings
        /// (RandomBeingGenStage, CombatDerelictCrewGenStage).
        /// </summary>
        protected int FindBeingSpawnPos() {
            var pos = 0;
            if (spec.HasGenArea) {
                var subRng = rng.Fork();
                var area = spec.GenArea.Contents();
                area.Sort();
                foreach (var p in area.OrderBy(r => subRng.Value)) {
                    if (EntityUtils.IsEmptyFloorAt(p)) {
                        pos = p;
                        break;
                    }
                }
            } else {
                var shipCpuPos = spec.CPUPos;
                D.Ass(shipCpuPos > 0, "No cpu pos!");
                Pos.ForEachAroundRandom(rng, shipCpuPos, 5, false, true, (p) => {
                    if (EntityUtils.IsEmptyFloorAt(p)) {
                        pos = p;
                        return false;
                    }
                    return true;
                });
            }
            return pos;
        }

        protected bool IsGoodObjectLocation(int p) {
            if (!IsWithinGenArea(p)) { return false; }
            var good = true;
            Pos.ForEachAround(p, 1, true, true, (n) => {
                if (EntityUtils.IsEnergyNodeAt(n)) {
                    good = false;
                    return false;
                }
                return true;
            });
            return good;
        }

        private readonly HashSet<int> fillFootprint = new();
        private readonly HashSet<int> fillNeighbors = new();
        private readonly HashSet<int> fillVisited = new();
        private readonly Stack<int> fillStack = new();

        private bool IsWalkableIn(int p, int areaId) {
            if (p < 0 || p >= Pos.GridW * Pos.GridH) { return false; }
            return S.Sys.Areas.At(p) == areaId && EntityUtils.IsEmptyFloorAt(p);
        }

        private static int Neighbor(int p, int i) {
            switch (i) {
                case 0: return Pos.N(p);
                case 1: return Pos.S(p);
                case 2: return Pos.E(p);
                default: return Pos.W(p);
            }
        }

        // Removing the footprint cannot disconnect anything unless it disconnects the
        // walkable cells touching it: any path that ran through the footprint entered
        // and left through those. So it is enough to show they all stay mutually
        // reachable with the footprint blocked - one fill from the first of them that
        // stops as soon as the rest are seen. Cheap when connected (the common case),
        // and exact: a sealed pocket touches the footprint, so its cell is one of the
        // neighbors and would never be reached. Applies to breached sections (area
        // ids <= 0) too; grounded crew still have to walk those.
        protected bool KeepsRoomConnected(Tile tile, int pos) {
            var areaId = S.Sys.Areas.At(pos);
            fillFootprint.Clear();
            fillNeighbors.Clear();
            int scnt = tile.Transform.GetSubtilePos(pos, Caches.PosIdx);
            for (int i = 0; i < scnt; i++) {
                fillFootprint.Add(Caches.PosIdx[i]);
            }
            foreach (var p in fillFootprint) {
                for (int i = 0; i < 4; i++) {
                    var n = Neighbor(p, i);
                    if (fillFootprint.Contains(n)) { continue; }
                    if (IsWalkableIn(n, areaId)) {
                        fillNeighbors.Add(n);
                    }
                }
            }
            if (fillNeighbors.Count == 0) {
                // Nothing walkable touches the footprint: nobody could work it
                return false;
            }
            if (fillNeighbors.Count == 1) { return true; }
            fillVisited.Clear();
            fillStack.Clear();
            int remaining = fillNeighbors.Count;
            foreach (var seed in fillNeighbors) {
                fillVisited.Add(seed);
                fillStack.Push(seed);
                remaining--;
                break;
            }
            while (fillStack.Count > 0) {
                var p = fillStack.Pop();
                for (int i = 0; i < 4; i++) {
                    var n = Neighbor(p, i);
                    if (fillVisited.Contains(n)) { continue; }
                    if (fillFootprint.Contains(n)) { continue; }
                    if (!IsWalkableIn(n, areaId)) { continue; }
                    fillVisited.Add(n);
                    if (fillNeighbors.Contains(n) && --remaining == 0) {
                        return true;
                    }
                    fillStack.Push(n);
                }
            }
            return false;
        }
        protected IEnumerator Viz(PosRect rect, float waitFor) {
            S.Sig.CameraFocus.Send(rect.Center(), rect.Extents, 0);
            yield return new WaitForSecondsRealtime(waitFor);
        }
        protected IEnumerator Viz(int position, float waitFor) {
            EntityUtils.CameraFocusOn(position);
            S.Sig.CameraShowPosLink.Send(null, EntityUtils.CenterOf(position),
                Consts.V2One, CameraFocus.FlashColor());
            yield return new WaitForSecondsRealtime(waitFor);
        }

        protected IEnumerator Viz(ICollection<int> positions, float waitFor) {
            var rect = Pos.Encapsulate(positions);
            yield return Viz(rect, waitFor);
        }

        protected HashSet<int> ExpandArea(HashSet<int> area, int by = 1) {
            // expand it to include walls and extra bit of padding
            var expanded = new HashSet<int>();
            foreach (var pos in area) {
                expanded.Add(pos);
                Pos.ForEachAround(pos, by, false, true, (p) => {
                    expanded.Add(p);
                    return true;
                });
            }
            return expanded;
        }
        protected void SliceThoughShip(int fromX, int fromY, int toX, int toY,
                Vector2 thicknessRange, Vector2 damageRange) {
            #if DEBUG_GEN
            D.Warn("{0} Slicing through {1}:{2} to {3}:{4}",
                LogPrefix, fromX, toX, fromY, toY);
            #endif
            // 1. pick two points, one on the top edge and one on the bottom
            // of the ship
            var from = new Vector2(fromX, fromY);
            var to = new Vector2(toX, toY);

            var ray = new Ray(from, to - from);

            // 2. go from one point towards the other and apply damage within
            // some small radius
            var rect = new PosRect(fromX, fromY, toX, toY);

            rect.ForEach((point) => {
                var dist = Vectors.DistanceToRay(ray,
                    EntityUtils.ToVector2(point));
                var range = rng.Range(thicknessRange);
                if (dist > range) { return; }
                // Data layers are floor, walls and objects.
                foreach (var layer in RenderLayers.DataLayerIds) {
                    if (spec.InvincibleParts.Contains(point)) {
                        // D.Err("Skipping invincible point: {0}", Pos.String(point));
                    } else {
                        var dmg = rng.Range(damageRange) * (range - dist);
                        dmg = Mathf.Max(dmg, 150f); // 100 is minimum required to destroy simple floors
                        CmdDealDamage.For(S, layer, point, dmg, DamageType.Explosive);
                    }
                }
            });
        }
        public virtual void Reset () {
            Attempts = 0;
        }
    }
}
