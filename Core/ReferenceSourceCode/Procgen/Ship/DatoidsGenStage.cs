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

namespace Game.Procgen {
    public class DatoidsGenStage : BaseShipGenStage {
        private readonly int tier;
        private readonly Dictionary<int, float> percentCustom;
        private readonly float percent;

        public DatoidsGenStage(int tier, float percent,
                Dictionary<int, float> percentCustom = null) {
            this.tier = tier;
            this.percent = percent;
            this.percentCustom = percentCustom;
        }
        public override void Apply() {
            var CPUPos = spec.CPUPos;

            var datoidsToGive = new Dictionary<MatType, int>();
            foreach (var r in ResearchTree.All) {
                if (r.Tier == tier) {
                    var item = S.Research.Get(r.IdH);
                    foreach (var mat in item.Researchable.DatoidCosts) {
                        if (!datoidsToGive.TryGetValue(mat.Type, out var amt)) {
                            amt = 0;
                        }
                        datoidsToGive[mat.Type] = amt + mat.StackSize;
                    }
                }
            }

            MatStorageComp currentLoader = BuildLoader(CPUPos);

            foreach (var mat in datoidsToGive) {
                var pct = percent;
                var type = mat.Key;
                if (percentCustom != null) {
                    if (percentCustom.TryGetValue(type.IdH, out var pctCustom)) {
                        pct = pctCustom;
                    }
                }
                var amt = Mathf.CeilToInt(mat.Value * pct);
                bool addedMat = false;
                if (currentLoader != null) {
                    amt = currentLoader.Store(type, null, amt, null);
                    if (amt > 0) {
                        currentLoader = BuildLoader(CPUPos);
                        if (currentLoader != null) {
                            amt = currentLoader.Store(type, null, amt, null);
                        }
                        if (amt > 0) {
                            // we can't build a loader
                            currentLoader = null;
                        } else {
                            addedMat = true;
                        }
                    } else {
                        addedMat = true;
                    }
                }
                if (!addedMat) {
                    var storage = S.Components.FindFirstMatching<MatStorageComp>(
                        s => s.Entity.Definition.IdH == DefIdsH.ObjectsDevicesDatoidLoader);
                    // fallback to any storage
                    storage ??= S.Components.FindFirstByType<MatStorageComp>();
                    // Drop excess datoids near the loader or some storage
                    EntityUtils.SpawnRawMaterial(mat.Key, amt,
                        storage.OutPort, 0.2f, true, false);
                }
            }
        }

        private MatStorageComp BuildLoader(int CPUPos) {
            var proto = EntityUtils.CreatePrototype<Tile>(DefIds.ObjectsDevicesDatoidLoader);
            var built = TryBuildDatoidLoaderAt(proto, Pos.S(CPUPos), Facing.Type.Down);
            built ??= TryBuildDatoidLoaderAt(proto, Pos.SE(CPUPos), Facing.Type.Down);
            built ??= TryBuildDatoidLoaderAt(proto, Pos.W(CPUPos), Facing.Type.Left);
            built ??= TryBuildDatoidLoaderAt(proto, Pos.NW(CPUPos), Facing.Type.Left);
            built ??= TryBuildDatoidLoaderAt(proto, Pos.NeighborAt(CPUPos, 2, 0), Facing.Type.Right);
            built ??= TryBuildDatoidLoaderAt(proto, Pos.NeighborAt(CPUPos, 2, 1), Facing.Type.Right);
            built ??= TryBuildDatoidLoaderAt(proto, Pos.NeighborAt(CPUPos, 0, 2), Facing.Type.Up);
            built ??= TryBuildDatoidLoaderAt(proto, Pos.NeighborAt(CPUPos, 1, 2), Facing.Type.Up);
            return built;
        }

        private MatStorageComp TryBuildDatoidLoaderAt(Tile proto, int pos, Facing.Type rot) {
            proto.Transform.Rotation = rot;
            if (CmdPlaceTile.CanPlace(pos, proto,
                    proto.Definition.LayerId, warn: false)) {
                var built = BuildTile(pos, proto, true);
                if (built != null) {
                    D.Warn("Built datoid loader: {0}", built);
                    return built.GetComponent<MatStorageComp>();
                }
            }
            return null;
        }
    }
}
