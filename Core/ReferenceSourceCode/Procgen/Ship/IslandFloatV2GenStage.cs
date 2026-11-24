using System.Collections.Generic;
using KL.Grid;
using Game.Data;
using KL.Utils;
using Game.Constants;
using System.Collections;
using KL.Collections;
using UnityEngine.InputSystem;
using UnityEngine;
using Game.Components;
using System.Linq;
using Game.Commands;

namespace Game.Procgen {
    public class IslandFloatV2GenStage : BaseShipGenStage {
        public override bool ProducesIslands => true;
        public override bool IsAsync => true;
        public override bool RequiresAreas => true;

        public override IEnumerator ApplyAsync() {
            if (S.Sys.Areas.IslandCount < 2) { yield break; }
            var islands = new Dictionary<int, HashSet<int>>(S.Sys.Areas.Islands);
            foreach (var island in islands) {
                if (!IsWithinGenArea(island.Value.First())) { continue; }
                if (island.Value.Count < 30) { continue; }
                var isle = new HashSet<int>(island.Value);
                yield return TryFloatApart(isle);
            }
            var cpu = S.Components.FindFirstMatching<ShipComputerComp>(
                c => IsWithinGenArea(c.Tile.Transform.PosIdx));
            if (cpu != null) {
                spec.SetCPUPos(cpu.Tile.Transform.PosIdx);
            }
            spec.ForbidForEachTile = true;
            S.Sig.ResetTileGraphics.Send();
            S.Sig.RebuildMap.Send();
            //yield return null;
            //yield return S.Sys.Path.Recalculate();
            S.Sig.ForceAreas.Send();
        }

        private IEnumerator TryFloatApart(HashSet<int> island) {
            // var shipCpu = S.Components.FindFirstByType<ShipComputerComp>();
            // if (shipCpu != null) {
            //     if (S.Sys.Areas.IsSameIsland(shipCpu.Tile.PosIdx, island.First())) {
            //         yield break;
            //     }
            // }
            var subRng = rng.Fork();
            var dist = subRng.Range(2, 6);
            var islandPosRect = Pos.Encapsulate(island);
            var islandCenter = islandPosRect.CenterPos;
            var shipCenter = Pos.FromXY(spec.CenterX, spec.CenterY);
            Pos.Direction(shipCenter, islandCenter, out var x, out var y);
            var islandList = new List<int>(island);
            islandList.Sort();
            var anchor = subRng.From(islandList);
            var target = Pos.NeighborAt(anchor, x * dist , y * dist);
            while (Pos.IsOOB(target) && dist > 0) {
                dist--;
                target = Pos.NeighborAt(anchor, x * dist , y * dist);
            }
            if (Pos.IsOOB(target)) {
                D.Err("Can't push, target OOB");
                yield break;
            }
            if (spec.HasGenArea) {
                // A confined ship (a derelict in its no-build corner) floats apart inside its
                // area: pushed outward from a corner, a section landed in the player's build
                // space and counted as the player's ship (STA-1924)
                while (dist > 0 && PushesOutOfGenArea(islandPosRect, x * dist, y * dist)) {
                    dist--;
                }
                if (dist == 0) { yield break; }
                target = Pos.NeighborAt(anchor, x * dist, y * dist);
            }
            if (target == islandCenter) {
                yield break;
            }
            var moveSurvivorPos = spec.HasSurvivors && island.Contains(spec.SurvivorPos);
            List<int> affectedStasisPos = null;
            if (spec.HasStasisArray) {
                affectedStasisPos = new();
                foreach (var pos in spec.StasisPos) {
                    if (island.Contains(pos)) {
                        affectedStasisPos.Add(pos);
                    }
                }
            }

            var moveCPUPos = spec.HasCPU && island.Contains(spec.CPUPos);
            var cmd = new CmdPushSection(anchor, anchor,
                EntityUtils.CenterOf(target), null, false);
            cmd.ShipSpec = spec;
            cmd.IsGenStage = true;
            yield return cmd.WithState(S).ExecuteAsync();
            if (cmd.StepsMade > 0) {
                if (moveSurvivorPos) {
                    var p = Pos.NeighborAt(spec.SurvivorPos,
                        (int) cmd.ExecutedMotion.x,
                        (int) cmd.ExecutedMotion.y);
                    spec.SetSurvivorPos(p);
                }
                if (affectedStasisPos != null) {
                    for (int i = affectedStasisPos.Count - 1; i >= 0; i--) {
                        int sp = affectedStasisPos[i];
                        var p = Pos.NeighborAt(sp,
                            (int) cmd.ExecutedMotion.x,
                            (int) cmd.ExecutedMotion.y);
                        var idx = spec.StasisPos.IndexOf(sp);
                        if (idx != -1) {
                            spec.StasisPos[idx] = p;
                        }
                        if (Pos.IsOOB(p)) {
                            D.Err("Invalid stasis pos after float: {0}", Pos.StringD(p));
                        } else {
                            // D.Warn("Moving stasis pos: {0}", Pos.StringFromTo(sp, p));
                            spec.StasisPos[i] = p;
                        }
                    }
                }
            }
        }

        // A hull can already stick out of its area by a few tiles; a push may keep that, but
        // never take it further out
        private bool PushesOutOfGenArea(PosRect r, int dx, int dy) {
            var a = spec.GenArea;
            if (dx < 0 && r.MinX + dx < Mathf.Min(a.MinX, r.MinX)) { return true; }
            if (dx > 0 && r.MaxX + dx > Mathf.Max(a.MaxX, r.MaxX)) { return true; }
            if (dy < 0 && r.MinY + dy < Mathf.Min(a.MinY, r.MinY)) { return true; }
            if (dy > 0 && r.MaxY + dy > Mathf.Max(a.MaxY, r.MaxY)) { return true; }
            return false;
        }
    }
}
