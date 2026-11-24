using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Commands;
using KL.Grid;
using KL.Utils;
using Game.Utils;
using Game.CodeGen;
using Game.Data.Space;

namespace Game.Commands {
    public class CmdRemoveTile : Command {
        private Tile tileToRemove;
        private Map map;

        private readonly int posIdx;
        private readonly int layer;
        private readonly bool skipDanglingCheck;
        private readonly bool autoRebuild;

        public CmdRemoveTile(Tile tile, bool skipDanglingCheck = false,
                bool autoRebuild = false) : base("remove_tile") {
            if (tile == null) {
                D.Err("Trying to remove null tile!");
                return;
            }
            if (tile.Id == 0) {
                D.Err("Tile id is 0 in CmdRemoveTile! {0}", tile);
            }
            map = tile.Map;
            this.skipDanglingCheck = skipDanglingCheck;
            tileToRemove = tile;
            posIdx = tile.PosIdx;
            layer = tile.Definition.LayerId;
            if (autoRebuild) {
                if (The.R.State.Ticks < Consts.TicksPer6Hours) {
                    // if (Ready.Gen) {
                    //     D.Warn("Skipping auto rebuild due to object destroyed too early in-game (min 6h)");
                    // }
                    autoRebuild = false;
                } else if (!EntityUtils.IsWithinMargin(posIdx, false, false)) {
                    autoRebuild = false;
                } else if (A.InstantBuild) {
                    autoRebuild = false;
                } else if (tile.Goal?.JobIdH == JobTypeIdH.Demolition) {
                    // The player wanted it gone - destruction just finished the job
                    autoRebuild = false;
                }
            }
            this.autoRebuild = autoRebuild;
        }

        public CmdRemoveTile(int idx, int layer) : base("remove_tile") {
            if (idx < 1) {
                D.Err("Removing tile with 0 idx!");
            }
            posIdx = idx;
            this.layer = layer;
        }

        protected override void Execute() {
            if (!GameState.IsCurrent(S)) {
                D.Err("Trying to execute CmdRemoveTile from a different game");
                return;
            }
            if (map == null) {
                map = S.Map;
            }
            if (posIdx < 0 || posIdx >= S.GridSize) {
                // D.Warn("Trying to remove tile at OOB: {0}", posIdx);
                return;
            }
            if (tileToRemove == null && posIdx != 0) {
                tileToRemove = map.Grids[layer].Get(posIdx);
            }
            if (tileToRemove == null) { return; }
            if (tileToRemove.Id == 0) {
                D.Warn("Tile id is 0 in CmdRemoveTile.Execute! Was it removed by something else? {0}",
                    tileToRemove);
                return;
            }
            if (tileToRemove.Transform.IsChild) {
                tileToRemove = tileToRemove.Transform.Parent;
            }
            if (tileToRemove.Transform.IsMulti) {
                var num = EntityUtils.SubtilePositions(posIdx, tileToRemove,
                    Caches.PosIdx);
                for (int i = 0; i < num; i++) {
                    var subTile = map.Grids[layer].Set(Caches.PosIdx[i], null);
                    if (subTile != null) {
                        S.RemoveEntity(subTile, isSubTile: subTile.Transform.IsChild);
                    }
                }
            } else {
                map.Grids[layer].Set(posIdx, null);
                S.RemoveEntity(tileToRemove);
                if (layer == WorldLayer.Floor) {
                    // try to mark objects that were on this tile as in space
                    if (S.Objs.ByPos.TryGetValue(posIdx, out var objs)) {
                        for (int i = objs.Count - 1; i >= 0; i--) {
                            Obj obj = objs[i];
                            obj.Transform.IsInSpace = true;
                        }
                    }
                }
            }

            if (autoRebuild && !A.InstantBuild && EntityUtils.IsWithinMargin(posIdx)) {
                if (S.Research.IsDefUnlocked(tileToRemove.Definition)) {
                    var newProto = EntityUtils.CreatePrototype<Tile>(tileToRemove.Definition);
                    newProto.Transform.Rotation = tileToRemove.Transform.Rotation;
                    S.CmdQ.Enqueue(new CmdPlaceTile(
                        tileToRemove.PosIdx, newProto, warn: false, false,
                            skipValidation: true, afterPlace: (t) => {
                                EntityUtils.CopyConfig(tileToRemove, t);
                                if (t.ENode != null) {
                                    t.ENode.ConnectionsToRestore = tileToRemove.ENode?.ConnectionsToRestore;
                                }
                                var goal = t.Goal;
                                if (goal != null) {
                                    goal.WithPriority(A.AutoRebuildPriority);
                                } else {
                                    D.Err("Rebuild task not found! {0}", t);
                                }
                            }));
                }
            }

            if (layer == WorldLayer.Floor) {
                if (Ready.Gen && !skipDanglingCheck) {
                    S.CmdQ.Enqueue(new CmdCheckDanglingObject(posIdx, autoRebuild));
                }
            }
        }
    }
}
