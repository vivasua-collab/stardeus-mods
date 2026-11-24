using System.Collections.Generic;
using Game.CodeGen;
using Game.Commands;
using Game.Constants;
using Game.Data;
using KL.Grid;
using KL.Utils;

using Game.Utils;
namespace Game.Components {

    public sealed class TemplateComp : BaseComponent<TemplateComp> {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new TemplateComp());
        }
        public float Chance;
        public float MinInstances;
        public float MaxInstances;
        public bool PlaceInTheMiddle;
        public override void OnLateReady(bool wasLoaded) {
        }

        public bool Spawn(int rootPos) {
            Chance = Config.GetFloat(PropertyIdH.Chance, 0.5f);
            PlaceInTheMiddle = Config.GetBool(PropertyIdH.PlaceInTheMiddle, false);
            MinInstances = Config.GetInt(PropertyIdH.MinInstances, 1);
            MaxInstances = Config.GetInt(PropertyIdH.MaxInstances, 2);
            var definitions = Config.GetStringSet(PropertyIdH.Definitions);
            var positions = Config.GetVector2Set(PropertyIdH.Positions);
            var rotations = Config.GetStringSet(PropertyIdH.Rotations);
            //S.CmdQueue.Enqueue(new CmdRemoveTile(rootPos, entity.Definition.LayerId));
            var inSpace = Tile.Constructable != null && Tile.Constructable.IsBuiltInSpace;

            List<(int, Tile)> toPlace = new();
            // First build all floors
            if (inSpace) {
                for (int i = 0; i < definitions.Length; i++) {
                    var def = definitions[i];
                    var pos = positions[i];
                    var rot = rotations[i];
                    var tile = EntityUtils.CreatePrototype<Tile>(def);
                    if (tile == null) {
                        D.Err("Def not available but in template: {0} -> {1}",
                            entity.Definition.Id, def);
                        continue;
                    }
                    if (tile.Transform.LayerId != WorldLayer.Floor) {
                        continue;
                    }
                    if (tile.Transform.IsRotatable) {
                        tile.Transform.Rotation = Facing.ConvertRotation(rot[0]);
                    }
                    var tilePos = rootPos + EntityUtils.ToPosCorner(pos);
                    if (!CmdPlaceTile.CanPlace(tilePos, tile,
                            tile.Definition.LayerId, warn: false)) {
                        if (!EntityUtils.IsEmptyFloorWithIntegrityAt(tilePos)) {
                            return false;
                        }
                    }
                    toPlace.Add((tilePos, tile));
                }
            }

            for (int i = 0; i < definitions.Length; i++) {
                var def = definitions[i];
                var pos = positions[i];
                var rot = rotations[i];
                var tile = EntityUtils.CreatePrototype<Tile>(def);
                if (tile == null) {
                    D.Err("Def not available but in template: {0} -> {1}",
                        entity.Definition.Id, def);
                    continue;
                }
                if (inSpace) {
                    // we already built the floors
                    if (tile.Transform.LayerId == WorldLayer.Floor) {
                        continue;
                    }
                }
                if (tile.Transform.IsRotatable) {
                    tile.Transform.Rotation = Facing.ConvertRotation(rot[0]);
                }
                var tilePos = rootPos + EntityUtils.ToPosCorner(pos);
                if (inSpace && tile.Definition.LayerId == WorldLayer.Objects) {
                    if (tile.Constructable.Placement == PlacementConstraint.InSpaceAttached) {
                        if (!EntityUtils.HasAdjacentFloor(tile, tilePos)) {
                            // D.Warn("no adjacent floor next to {0} {1}",
                            //    tilePos, tile);
                            continue;
                        }
                    }
                }
                if (!CmdPlaceTile.CanPlace(tilePos, tile, tile.Definition.LayerId, warn: false)) {
                    if (tile.Definition.LayerId == WorldLayer.Floor) {
                        if (!EntityUtils.IsEmptyFloorWithIntegrityAt(tilePos)) {
                            return false;
                        }
                    } else {
                        return false;
                    }
                }
                toPlace.Add((tilePos, tile));
            }

            foreach (var tp in toPlace) {
                var cmd = new CmdPlaceTile(tp.Item1, tp.Item2, warn: false, true);
                cmd.Execute(S);
                if (cmd.PlacedTile == null) {
                    D.ErrSoft("Failed placing tile: {0} at {1}",
                        tp.Item2, Pos.String(tp.Item1));
                }
            }
            return true;
        }

        public override string ToString() {
            return " * Template";
        }
    }
}
