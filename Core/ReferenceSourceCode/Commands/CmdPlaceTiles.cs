using System.Collections;
using System.Collections.Generic;
using Game.Data;
using Game.Commands;
using KL.Grid;
using KL.Utils;
using Game.Constants;
using UnityEngine;

namespace Game.Commands {
    public class CmdPlaceTiles : Command {
        private readonly Def tileDef;
        private readonly List<int> positions;
        private int layerId;
        private Facing.Type rotation;
        private readonly bool instant;

        public CmdPlaceTiles(List<int> pos, Facing.Type rotation, Def def, bool instant) : base("place_tiles") {
            tileDef = def;
            // need a copy
            this.rotation = rotation;
            positions = new List<int>(pos);
            this.instant = instant;
        }

        protected override void Execute() {
            if (!Ready.StateLock) {
                return;
            }
            layerId = tileDef.LayerId;
            var grid = S.Map.Grids[layerId];
            if (FillsGapsOnly(positions, layerId)) {
                for (int i = positions.Count - 1; i >= 0; i--) {
                    int p = positions[i];
                    if (!grid.IsWithinBounds(p) || HasStructAt(p, layerId)) {
                        positions.RemoveAt(i);
                    }
                }
            }
            var pl = positions.Count;
            var s = S;
            List<Tile> placedFloors = layerId == WorldLayer.Floor && !instant ? new() : null;
            for (int i = 0; i < pl; i++) {
                int pos = positions[i];
                if (grid.IsWithinBounds(pos)) {
                    var tile = BuildTile(pos);
                    if (!CmdPlaceTile.CanPlace(pos, tile, layerId, warn: false)) {
                        continue;
                    }
                    CmdPlaceTile.PlaceTileWithChecks(
                        tile, pos, layerId, instant);
                    if (placedFloors != null && tile.IsActive && grid.Get(pos) == tile) {
                        placedFloors.Add(tile);
                    }
                }
            }
            if (placedFloors?.Count > 0) {
                FloorConstructionWavePlanner.AssignAfterPlacement(placedFloors);
            }
        }

        // A floor or wall drag that reaches empty space extends the ship, so it fills only
        // the gaps instead of queueing a rebuild of every tile already built inside the area.
        // The build tool preview asks the same question, so what it marks is what gets ordered.
        public static bool FillsGapsOnly(List<int> positions, int layerId) {
            if (layerId != WorldLayer.Floor && layerId != WorldLayer.Walls) {
                return false;
            }
            for (int i = 0; i < positions.Count; i++) {
                var p = positions[i];
                if (Pos.IsOOB(p)) { continue; }
                if (!HasStructAt(p, layerId)) {
                    return true;
                }
            }
            return false;
        }

        public static bool HasStructAt(int pos, int layerId) {
            if (layerId == WorldLayer.Floor) {
                return EntityUtils.IsFloorAt(pos);
            }
            if (layerId == WorldLayer.Walls) {
                return EntityUtils.IsWallAt(pos);
            }
            return false;
        }

        private Tile BuildTile(int pos) {
            var t = new Tile {
                S = S,
                Definition = tileDef
            };
            The.Defs.BuildComponentsFor(t);
            t.Transform.PosIdx = pos;
            t.Transform.Rotation = rotation;
            return t;
        }
    }
}
