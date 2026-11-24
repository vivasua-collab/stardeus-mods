using KL.Grid;
using Game.Data;
using UnityEngine;
using Game.Commands;
using KL.Utils;
using System.Collections;
using Game.Constants;
using Game.Modifiers;
using Game.Components;
using Game.Rendering;
using System.Collections.Generic;
using System;

namespace Game.Procgen {
    public class SplitStasisAndCPUGenStage : BaseShipGenStage {
        public override bool RequiresAreas => true;
        public override bool IsAsync => true;
        public override bool ProducesIslands => true;
        public override IEnumerator ApplyAsync() {
            var stasis = S.Components.FindFirstMatching<StasisArrayComp>(
                s => IsWithinGenArea(s.Tile.CenterIdx));
            int stasisPos = Pos.Invalid;
            if (stasis != null) {
                stasisPos = stasis.Tile.CenterIdx;
            }
            var cpu = S.Components.FindFirstMatching<ShipComputerComp>(
                s => IsWithinGenArea(s.Tile.CenterIdx));
            int cpuPos = Pos.Invalid;
            if (cpu != null) {
                cpuPos = cpu.Tile.CenterIdx;
            }
            if (stasisPos == Pos.Invalid && cpuPos == Pos.Invalid) {
                // where the stasis stage built, and what is there now: never built, or lost
                var built = new List<string>();
                if (spec.StasisPos != null) {
                    foreach (var p in spec.StasisPos) {
                        built.Add($"{Pos.String(p)}: {EntityUtils.ParentObjAt(p)?.Definition?.Id ?? "nothing"}");
                    }
                }
                var arrays = new List<string>();
                var allArrays = S.Components.AllByType<StasisArrayComp>();
                if (allArrays != null) {
                    foreach (StasisArrayComp a in allArrays) {
                        arrays.Add($"{Pos.String(a.Tile.CenterIdx)} in area: {IsWithinGenArea(a.Tile.CenterIdx)}");
                    }
                }
                D.Err("Neither stasis nor CPU not available, skipping split stasis phase. Arrays built: [{0}]. All arrays: [{1}]. Gen area: {2}",
                    string.Join(", ", built), string.Join(", ", arrays), spec.GenArea);
                yield break;
            }
            if (stasisPos == Pos.Invalid) {
                stasisPos = RandomPosAround(cpuPos);
            }
            if (cpuPos == Pos.Invalid) {
                cpuPos = RandomPosAround(stasisPos);
            }
            var stasisV2 = Pos.ToVector(stasisPos);
            var cpuV2 = Pos.ToVector(cpuPos);

            var vec = stasisV2 - cpuV2;

            int attempts = 5;
            var islandsBefore = S.Sys.Areas.IslandCount;
            while (attempts-- > 0 && S.Sys.Areas.IslandCount - islandsBefore == 0) {
                if (!S.Sys.Areas.IsSameIsland(cpuPos, stasisPos)) {
                    yield break;
                }
                Slice(cpuV2, vec, rng.Range(0.45f, 0.55f), rng.Range(85f, 95f));
                S.Sig.ForceAreas.Send();
                //yield return null;
                //yield return Ready.WaitForAreas(this);
            }
        }

        private int RandomPosAround(int pos) {
            var candidates = new List<int>();
            var minDist = Math.Max(spec.Width, spec.Height) * 0.2f;
            minDist *= minDist;
            spec.ForEachTile((p) => {
                if (EntityUtils.IsEmptyFloorWithIntegrityAt(p)
                        && Pos.SqrDistance(pos, p) > minDist) {
                    candidates.Add(p);
                }
            });
            return rng.From(candidates);
        }

        private void Slice(Vector2 cpuV2, Vector2 vec, float dist, float angle) {
            var angled1 = Vectors.Rotate(vec, angle).normalized * spec.Width;
            var angled2 = Vectors.Rotate(vec, -angle).normalized * spec.Width ;
            var midPoint = cpuV2 + vec * dist;
            var damageA = midPoint + angled1;
            var damageB = midPoint + angled2;
            SliceThoughShip(damageA.x, damageA.y, damageB.x, damageB.y,
                new Vector2(2, 7), new Vector2(250, 500));
        }

        private void SliceThoughShip(float fromX, float fromY, float toX, float toY,
                Vector2 thicknessRange, Vector2 damageRange) {
            #if DEBUG_GEN
            // D.Warn("{0} Slicing through {1} to {2}", LogPrefix, from, to);
            #endif
            // 1. pick two points, one on the top edge and one on the bottom
            // of the ship
            var from = new Vector2(fromX, fromY);
            var to = new Vector2(toX, toY);

            var ray = new Ray(from, to - from);

            // 2. go from one point towards the other and apply damage within
            // some small radius
            var rect = new PosRect(
                Mathf.RoundToInt(fromX),
                Mathf.RoundToInt(fromY),
                Mathf.RoundToInt(toX),
                Mathf.RoundToInt(toY));

            rect.ForEach((point) => {
                if (Pos.IsOOB(point)) { return; }
                if (!IsWithinGenArea(point)) { return; }
                var dist = Vectors.DistanceToRay(ray,
                    EntityUtils.ToVector2(point));
                var range = rng.Range(thicknessRange);
                if (dist > range) { return; }
                // Data layers are floor, walls and objects.
                foreach (var layer in RenderLayers.DataLayerIds) {
                    var dmg = rng.Range(damageRange) * (range - dist);
                    if (spec.InvincibleParts.Contains(point)) {
                    } else {
                        CmdDealDamage.For(S, layer, point, dmg, DamageType.Explosive);
                    }
                }
            });
        }
    }
}
