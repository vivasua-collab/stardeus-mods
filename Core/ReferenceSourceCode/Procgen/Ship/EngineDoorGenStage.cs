using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Procgen;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Procgen {
    public class EngineDoorGenStage : BaseShipGenStage {
        public EngineDoorGenStage() {
        }

        public override void Apply() {
            var allEngines = S.Components.AllByType<ShipEngineComp>();
            if (allEngines == null) {
                D.ErrSoft("No engines when doing EngineDoorGenStage! Spec: {0}", spec);
                return;
            }
            foreach (ShipEngineComp e in allEngines) {
                CheckInPort(e.Tile);
            }
        }

        private void CheckInPort(Tile engine) {
            if (engine.Transform.HasWorkSpot) {
                var doorDir = Facing.Opposite(engine.Transform.Rotation);
                var doorOffset = Facing.ToOffset(doorDir, 1f);
                var doorPos = Pos.NeighborAt(engine.Transform.InPort,
                    (int) doorOffset.x, (int) doorOffset.y);
                var tile = BuildTile(doorPos,
                    EntityUtils.CreatePrototype<Tile>(DefIds.StructureDoorsDoorAirlock01),
                        allowFailure: true);
                if (tile != null) {
                    D.Warn("Built door at {0}: {1}", Pos.String(doorPos), tile);
                    var aheadPos = Pos.NeighborAt(doorPos,
                        (int) doorOffset.x, (int) doorOffset.y);
                    var wall = EntityUtils.WallAt(aheadPos);
                    if (wall != null) {
                        new CmdRemoveTile(wall).Execute(S);
                    }
                }
            }
        }
    }
}
