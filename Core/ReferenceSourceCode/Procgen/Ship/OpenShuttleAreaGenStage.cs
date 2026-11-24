//#define DEBUG_GEN
using System.Collections.Generic;
using KL.Grid;
using Game.Data;
using Game.Constants;
using KL.Collections;
using Game.Commands;
using KL.Utils;
using Game.Components;
using Game.CodeGen;

namespace Game.Procgen {
    [MustGoAfterGenStage(typeof(ShuttleGenStage))]
    public class OpenShuttleAreaGenStage : BaseShipGenStage {
        public override bool RequiresAreas => true;
        private List<int> remainingArea;
        public readonly bool tryOnlyNearShuttle;
        public readonly bool onlyIndoors;
        public OpenShuttleAreaGenStage(bool tryOnlyNearShuttle, bool onlyIndoors) {
            this.tryOnlyNearShuttle = tryOnlyNearShuttle;
            this.onlyIndoors = onlyIndoors;
        }

        public override void Apply() {
            var shuttle = S.Components.FindFirstByType<ShuttleComp>();
            if (shuttle == null) {
                D.Err("Shuttle not found in OpenShuttleAreaGenStage");
                return;
            }
            var shutPos = shuttle.Tile.PosIdx;
            if (onlyIndoors && S.Sys.Areas.IsOutside(shutPos)) {
                return;
            }
            var shutArea = S.Sys.Areas.AnyAreaAt(shutPos);
            remainingArea = new List<int>(PosRect.RemoveOutline(shutArea));
            if (TryRemoveNearShuttle(shutPos, shuttle.Tile.Transform.Rotation, shutArea,
                    out var zoneW, out var zoneH)) {
                return;
            }
            if (tryOnlyNearShuttle) {
                return;
            }

            while (remainingArea.Count > 0) {
                var start = remainingArea[0];
                remainingArea.Remove(start);
                if (TryRemoveZone(shutArea, start, zoneW, zoneH)) {
                    return;
                }
            }
            D.Err("Failed to remove a zone for the shuttle! {0}", Pos.String(shutPos));
        }

        private bool TryRemoveNearShuttle(int shutPos, Facing.Type rot,
                HashSet<int> shutArea, out int zoneW, out int zoneH) {
            // we want to remove a patch of floor in the shuttle area
            ResolveShuttleExitZone(shutPos, rot,
                out zoneW, out zoneH, out var zoneStart, false);
            return TryRemoveZone(shutArea, zoneStart, zoneW, zoneH);
        }

        public static void ResolveShuttleExitZone(int shutPos, Facing.Type rot,
                out int zoneW, out int zoneH, out int zoneStart, bool includeOutline) {
            zoneStart = 0;
            zoneW = 0;
            zoneH = 0;
            int outline = includeOutline ? 1 : 0;
            switch (rot) {
                case Facing.Type.Down: {
                        zoneStart = Pos.NeighborAt(shutPos, 0, -4 - outline);
                        zoneW = 3 + outline * 2;
                        zoneH = 4 + outline * 2;
                        break;
                    }
                case Facing.Type.Up: {
                        zoneStart = Pos.NeighborAt(shutPos, 0, 4 + outline);
                        zoneW = 3 + outline * 2;
                        zoneH = 4 + outline * 2;
                        break;
                    }
                case Facing.Type.Left: {
                        zoneStart = Pos.NeighborAt(shutPos, -4 - outline, 0);
                        zoneW = 4 + outline * 2;
                        zoneH = 3 + outline * 2;
                        break;
                    }
                case Facing.Type.Right: {
                        zoneStart = Pos.NeighborAt(shutPos, 4 + outline, 0);
                        zoneW = 4 + outline * 2;
                        zoneH = 3 + outline * 2;
                        break;
                    }
            }
            // D.Warn("Resolving shuttle exit zone for {0}: {1}: {2} ({3}x{4})",
            //     Pos.String(shutPos), rot, Pos.String(zoneStart), zoneW, zoneH);
        }

        private bool TryRemoveZone(HashSet<int> shutArea, int zoneStart, int zoneW, int zoneH) {
            if (!IsZoneEmpty(shutArea, zoneStart, zoneW, zoneH)){
                return false;
            }

            // if we didn't return, it's good to go
            for (int x = 0; x < zoneW; x++) {
                for (int y = 0; y < zoneH; y++) {
                    var p = Pos.NeighborAt(zoneStart, x, y);
                    new CmdRemoveTile(EntityUtils.FloorAt(p)).Execute(S);
                }
            }
            // replace floors with frame on the edge
            for (int x = -1; x <= zoneW; x++) {
                for (int y = -1; y <= zoneH; y++) {
                    var isEdge = x == -1 || y == -1 || x == zoneW || y == zoneH;
                    if (isEdge) {
                        var p = Pos.NeighborAt(zoneStart, x, y);
                        if (EntityUtils.IsEmptyFloorAt(p)) {
                            var newFloor = EntityUtils.CreatePrototype<Tile>(
                                DefIds.StructureFloorsFrame01);
                            ReplaceTile(p, newFloor);
                        }
                    }
                }
            }

            return true;
        }
        public static bool IsZoneEmpty(HashSet<int> shutArea, int zoneStart, int zoneW, int zoneH) {
            // we will expand the zone by +1 to see if there is nothing in the way around
            for (int x = 0; x < zoneW; x++) {
                for (int y = 0; y < zoneH; y++) {
                    var p = Pos.NeighborAt(zoneStart, x, y);
                    if (shutArea?.Contains(p) != false && EntityUtils.IsEmptyFloorAt(p)) {
                        // still ok
                    } else {
                        bool isRim = x == -1 || y == -1 || x == zoneW || y == zoneH;
                        if (isRim) { return false; }
                        if (EntityUtils.GetTopmostParentTile(p)?.Definition.IdH
                                    == DefIdsH.ObjectsDevicesShuttle) {
                            // still ok! we allow the shuttle to be on the edge of the hole
                        } else {
                            return false;
                        }
                    }
                }
            }
            return true;
        }
    }
}
