using System.Collections.Generic;
using System.Linq;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.AI.Combat;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using Game.Components;
using Game.Research;
using UnityEngine;
using System.Diagnostics;
using KL.Grid;
using Game.Components.Upgrades;

namespace Game.Procgen {
    public class BridgeGenStage : BaseShipGenStage {
        private readonly bool withAutoPilot;

        public BridgeGenStage(bool withAutoPilot) {
            this.withAutoPilot = withAutoPilot;
        }

        public override void Apply() {
            var CPUPos = spec.CPUPos;

            var bridge = BuildBridge(CPUPos);
            if (bridge != null) {
                if (withAutoPilot) {
                    bridge.Upgrades.Install(0, UpgradeRegistry.Get(AutoPilotUpgrade.Id));
                }
                BuildCaptainsChairFor(bridge);
            } else {
                var fallback = new PlaceTileGenStage(DefIds.ObjectsDevicesBridgeControls, 1, 1, null)
                    .Indoors();
                fallback.Initialize(spec, S);
                fallback.SetRng(rng.Fork());
                fallback.Apply();
                // D.Err("Failed to build bridge controls");
            }
        }

        private Tile BuildBridge(int CPUPos) {
            if (Pos.IsOOB(CPUPos)) {
                D.Err("Trying to build a bridge at OOB CPUPos: {0}", Pos.StringD(CPUPos));
                return null;
            }
            var proto = EntityUtils.CreatePrototype<Tile>(DefIds.ObjectsDevicesBridgeControls);
            var built = TryBuildBridgeAt(proto, Pos.NeighborAt(CPUPos, -1, 5), Facing.Type.Down);
            built ??= TryBuildBridgeAt(proto, Pos.NeighborAt(CPUPos, 0, 5), Facing.Type.Down);
            built ??= TryBuildBridgeAt(proto, Pos.NeighborAt(CPUPos, -1, 4), Facing.Type.Down);
            built ??= TryBuildBridgeAt(proto, Pos.NeighborAt(CPUPos, 0, 4), Facing.Type.Down);
            return built;
        }

        private void BuildCaptainsChairFor(Tile bridge) {
            int pos = bridge.Transform.WorkSpot;
            Facing.Type rot = Facing.Opposite(bridge.Transform.Rotation);
            var proto = EntityUtils.CreatePrototype<Tile>(DefIds.ObjectsFurnitureChairCaptain);
            proto.Transform.Rotation = rot;
            BuildTile(pos, proto, true);
        }

        private Tile TryBuildBridgeAt(Tile proto, int pos, Facing.Type rot) {
            if (Pos.IsOOB(pos)) {
                D.Err("Trying to build a bridge OOB: {0}", Pos.String(pos));
                return null;
            }
            proto.Transform.Rotation = rot;
            int cnt = proto.Transform.OutlineAt(pos, Caches.PosIdx);
            for (int i = 0; i < cnt; i++) {
                if (GenUtils.IsDoor(Caches.PosIdx[i])) {
                    return null;
                }
            }
            if (CmdPlaceTile.CanPlace(pos, proto,
                    proto.Definition.LayerId, warn: false)) {
                return BuildTile(pos, proto, true);
            }
            return null;
        }
    }
}
