//#define DEBUG_GEN
using System.Collections.Generic;
using KL.Grid;
using Game.Data;
using Game.Constants;
using KL.Collections;
using Game.Commands;
using KL.Utils;

namespace Game.Procgen {
    public class WallCompactGenStage : BaseShipGenStage {
        private readonly HashSet<int> wallsToRemove = new();
        // Root of a wall cluster is the top-left tile of 4 tile wall group
        private readonly HashSet<int> wallClusterRoots = new();
        private readonly HashSet<int> allWalls = new();
        public override bool RequiresAreas => true;
        public override void Apply() {
            wallsToRemove.Clear();
            wallClusterRoots.Clear();
            allWalls.Clear();
            var wallGrid = S.Map.Grids[WorldLayer.Walls];

            // Grab all the walls of the ship
            spec.ForEachTile((idx) => {
                if (EntityUtils.IsWallAt(idx)) { allWalls.Add(idx); }
            });

            // TODO: more optimal way - grab outlines of all areas! (maybe)

            foreach (var wall in allWalls) {
                // if wall is a part of four wall cluster (::), it will have
                // two neighbors that have one other common cell that is their
                var nEast = Pos.E(wall);
                var nSouth = Pos.S(wall);
                var nSouthEast = Pos.SE(wall);
                if (allWalls.ContainsAll(nEast, nSouth, nSouthEast)) {
                    // we have a cluster
                    wallClusterRoots.Add(wall);
                }
            }

            // Removal algorithm
            // remove a tile with 2 neighbors and no connection to the outside
            foreach (var root in wallClusterRoots) {
                #if DEBUG_GEN
                // D.Warn("{0} considering cluster: {1}", LogPrefix,
                    //Pos.FromIndex(root));
                #endif
                bool removed = false;
                removed = removed || ConsiderRemoving(root, 2);
                removed = removed || ConsiderRemoving(Pos.E(root), 2);
                removed = removed || ConsiderRemoving(Pos.S(root), 2);
                removed = removed || ConsiderRemoving(Pos.SE(root), 2);
                if (!removed) {
                    removed = removed || ConsiderRemoving(root, 3);
                    removed = removed || ConsiderRemoving(Pos.E(root), 3);
                    removed = removed || ConsiderRemoving(Pos.S(root), 3);
                    removed = removed || ConsiderRemoving(Pos.SE(root), 3);
                }
            }

            // At this point we have marked all the walls for removal
            foreach (var wall in wallsToRemove) {
                new CmdRemoveTile(wall, WorldLayer.Walls).Execute(S);
            }

            #if DEBUG_GEN
            // D.Warn("GEN_SHIP {0} removed {1} walls from {2} clusters",
                spec.GenCommand, wallsToRemove.Count, wallClusterRoots.Count);
            #endif
        }

        private bool ConsiderRemoving(int wall, int neighborCount) {
            if (NeighborCount(wall) == neighborCount && !IsOuterWall(wall)) {
                wallsToRemove.Add(wall);
                return true;
            }
            return false;
        }


        private int NeighborCount(int wall) {
            int count = 0;
            if (allWalls.Contains(Pos.N(wall))) { count++; }
            if (allWalls.Contains(Pos.S(wall))) { count++; }
            if (allWalls.Contains(Pos.E(wall))) { count++; }
            if (allWalls.Contains(Pos.W(wall))) { count++; }
            return count;
        }

    }
}
