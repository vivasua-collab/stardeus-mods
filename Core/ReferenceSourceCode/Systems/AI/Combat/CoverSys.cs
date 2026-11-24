#define SKIP_OVERLAY
#if PRODUCTION_BUILD
#endif
#if UNITY_EDITOR
// #define DEBUG_COMBAT
#endif
using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.Path;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.AI.Combat {
    public sealed class CoverSys : GameSystem
        #if !SKIP_OVERLAY
            ,IOverlayProvider
        #endif
        {
        public const string SysId = "CoverSys";
        public override string Id => SysId;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new CoverSys());
        }

        private readonly List<OverlayInfo> overlays = new();
        public List<OverlayInfo> Overlays => overlays;
        private readonly Queue<int> changes = new();
        private PathSys path;
        private CoverSlot[] coverSlotMap;
        private byte[] coverAmtMap;
        private short[] coverTotalMap;
        private int gridSize;
        private int gridH;

        protected override void OnInitialize() {
            #if !SKIP_OVERLAY
            overlays.Add(new OverlayInfo(OverlayType.Controls,
                OverlayId.Cover, IconId.WCombat));
            S.Sig.ToggleOverlay.AddListener(OnToggleOverlay);
            #endif
            S.Sig.AfterLoadState.AddListener(OnLoadSave);
            S.Query.CoverSlotAt.SetResponder(GetCoverSlotAt);
            S.Query.FindCover.SetResponder(FindCoverFor);
            S.Query.FindCoverNear.SetResponder(FindCoverNear);
            S.Query.GetCoverMap.SetResponder(GetCoverMap);
            S.Query.GetCoverVersion.SetResponder(GetCoverVersion);
            S.Query.GetCoverTotalMap.SetResponder(GetCoverTotalMap);
            S.Sig.WinchEnded.AddListener(OnWinchEnded);
        }

        private void OnWinchEnded(bool moved) {
            if (moved) {
                RebuildCoverMap();
            }
        }

        private void RebuildCoverMap() {
            using (D.Timer("Rebuild cover", 0.001f)) {
                coverVersion++;
                for (int i = gridSize - 1; i >= 0; i--) {
                    coverAmtMap[i] = 0;
                    coverSlotMap[i] = default;
                    coverTotalMap[i] = 0;
                }
                for (int i = 0; i < gridSize; i++) {
                    var wall = EntityUtils.WallAt(i);
                    if (wall == null) {
                        var obj = EntityUtils.ObjAt(i);
                        if (obj != null && !obj.Transform.IsChild
                                && obj.Transform.CoverPercent > 0) {
                            OnCoverChanged(obj, true);
                        }
                    } else if (wall.IsConstructed) {
                        OnCoverChanged(wall, true);
                    }
                }
                EveryFrame();
            }
        }

        private byte[] GetCoverMap() {
            return coverAmtMap;
        }
        // Sight is cached by where the shooter and its target stand. A door that shuts
        // between them changes neither, and a shooter kept firing through it (STA-1747)
        private int coverVersion;
        private int GetCoverVersion() {
            return coverVersion;
        }
        private short[] GetCoverTotalMap() {
            return coverTotalMap;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="unit"></param>
        /// <param name="best">false = closest / any</param>
        /// <returns></returns>
        private CoverSlot FindCoverFor(CombatUnit unit, bool best) {
            var pos = unit.PosIdx;
            CoverSlot bestCover = default;
            var targetPos = unit.AttackTarget?.PosIdx ?? unit.Attackable?.AttackablePosIdx;
            if (targetPos == null || !best || !unit.CanAttack) {
                // Track the best candidate that hasn't been visited recently, and separately
                // the best candidate overall - falling back to the latter keeps a unit from
                // ending up with zero cover just because its only good options are recent,
                // while still steering it away from bouncing between the same couple of spots.
                CoverSlot bestAnyCover = default;
                float bestScore = 0f;
                float bestAnyScore = 0f;
                int found = S.Query.RegionPositionsAt.Ask(pos, Caches.RegPos);
                for (int i = 0; i < found; i++) {
                    var rpos = Caches.RegPos[i];
                    var cov = coverSlotMap[rpos];
                    if (unit.PosIdx == rpos || cov.MaxDir == 0) { continue; }
                    // near cover first, and not beside someone else (CastPosition.SpotMul):
                    // the whole room went for the same few tiles
                    var score = cov.MaxDir * CastPosition.SpotMul(S, unit, rpos);
                    if (score <= bestAnyScore) { continue; }
                    if (!Positions.IsGoodPosFor(S, unit, rpos)) { continue; }
                    bestAnyScore = score;
                    bestAnyCover = cov;
                    if (!unit.WasRecentCover(rpos) && score > bestScore) {
                        bestScore = score;
                        bestCover = cov;
                    }
                }
                bestCover = bestCover.Total > 0 ? bestCover : bestAnyCover;
            } else {
                var distFrom = unit.BestAttack.Stats.RangeMin.Value;
                var distTo = unit.BestAttack.Stats.RangeMax.Value;
                bestCover = FindBestInRegionTowardsEnemy(unit, path,
                    targetPos.Value, distFrom, distTo, pos);
            }
            // D.Warn("Found best cover: {0}", bestCover);
            return bestCover;
        }


        private CoverSlot FindCoverNear(CombatUnit unit, int pos, float dist) {
            CoverSlot bestCover = default;
            CoverSlot bestAnyCover = default;
            float bestScore = 0f;
            float bestAnyScore = 0f;
            var d = dist * dist;
            var ar = S.Sys.Areas;
            var areaAtPos = ar.At(pos);
            int found = S.Query.RegionPositionsAt.Ask(pos, Caches.RegPos);
            for (int i = 0; i < found; i++) {
                var rpos = Caches.RegPos[i];
                var cov = coverSlotMap[rpos];
                if (unit.PosIdx == rpos || cov.MaxDir == 0) { continue; }
                if (ar.At(rpos) != areaAtPos) { continue; }
                if (Pos.SqrDistance(pos, rpos) > d) { continue; }
                var score = cov.MaxDir * CastPosition.SpotMul(S, unit, rpos);
                if (score <= bestAnyScore) { continue; }
                if (!Positions.IsGoodPosFor(S, unit, rpos)) { continue; }
                bestAnyScore = score;
                bestAnyCover = cov;
                if (!unit.WasRecentCover(rpos) && score > bestScore) {
                    bestScore = score;
                    bestCover = cov;
                }
            }
            // D.Warn("Found best cover near: {0}", bestCover);
            return bestCover.Total > 0 ? bestCover : bestAnyCover;
        }

        private CoverSlot FindBestInRegionTowardsEnemy(CombatUnit unit, PathSys path,
                int enemyPos, float distFrom, float distTo, int pos) {
            // ranked by what the dice count (CoverUtils.BlockChance by the angle of the shot),
            // not by the 8 directions of CoverSlot, which the dice no longer use
            float bestCoverTowardsEnemy = 0f;
            float bestAnyCoverTowardsEnemy = 0f;
            if (distFrom < 5f) {
                distFrom = 5f;
            }
            if (distTo < distFrom + 1f) {
                distTo = distFrom + 2f;
            }
            float distFromSqr = distFrom * distFrom;
            float distToSqr = distTo * distTo;
            Pos.ToXY(enemyPos, out var tx, out var ty);
            Vector2 enemyPosV = new Vector2(tx + 0.5f, ty + 0.5f);
            CoverSlot bestCover = default;
            CoverSlot bestAnyCover = default;
            var beingId = unit.Agent.Being.Id;
            int found = S.Query.RegionPositionsAt.Ask(pos, Caches.RegPos);
            for (int i = 0; i < found; i++) {
                var rpos = Caches.RegPos[i];
                var cov = coverSlotMap[rpos];
                if (unit.PosIdx == rpos || cov.Total <= 0) { continue; }
                if (unit.Agent.Motion.HadNavFailureAt(rpos)) { continue; }
                // nobody takes cover on a sandbag or in a doorway
                if (path.IsPassThroughOnly(rpos)) { continue; }
                // someone stands there or walks there to stop
                if (path.IsSpotTaken(rpos, beingId)) { continue; }
                var inDir = CoverUtils.BlockChance(S, rpos, enemyPos);
                // near cover first, and not beside someone else (CastPosition.SpotMul): the
                // whole room went for the same few tiles
                var score = inDir * CastPosition.SpotMul(S, unit, rpos);
                if (score <= bestAnyCoverTowardsEnemy) { continue; }
                var sqrDist = Pos.SqrDistance(rpos, enemyPos);
                if (sqrDist < distFromSqr || sqrDist > distToSqr) {
                    continue;
                }
                if (!EntityUtils.HasLineOfSightCover(Pos.ToVector(rpos),
                        enemyPosV, 2f, distTo, out _)) {
                    // no line of sight towards enemy!
                    continue;
                }
                // enemy has better cover against us from there than we have against it
                if (CoverUtils.BlockChance(S, enemyPos, rpos) > inDir) {
                    continue;
                }
                // Track the best candidate not visited recently, and separately the best
                // overall - falling back to the latter (below) keeps a unit from ending up
                // with zero cover just because its only good options are recent, while still
                // steering it away from bouncing between the same couple of spots.
                bestAnyCoverTowardsEnemy = score;
                bestAnyCover = cov;
                if (!unit.WasRecentCover(rpos) && score > bestCoverTowardsEnemy) {
                    bestCoverTowardsEnemy = score;
                    bestCover = cov;
                }
            }

            #if DEBUG_COMBAT
            D.Warn("Found best cover in region towards enemy: {0}", bestCover);
            #endif
            return bestCover.Total > 0 ? bestCover : bestAnyCover;
        }

        private CoverSlot GetCoverSlotAt(int pos) {
            if (Pos.IsOOB(pos)) {
                return default;
            }
            #if !PRODUCTION_BUILD
            var slot = coverSlotMap[pos];
            if (pos != slot.PosIdx && (slot.PosIdx != 0 || slot.Total != 0)) {
                D.Err("cover does not match position! {0} | {1}",
                        Pos.StringFromTo(pos, slot.PosIdx), slot);
            }
            #endif
            return coverSlotMap[pos];
        }

        private void OnCoverChanged(Tile tile, bool isAdded) {
            byte amt;
            if (isAdded) {
                amt = tile.Transform.CoverPercent;
            } else {
                amt = 0;
            }
            var tt = tile.Transform;
            if (tt.IsMulti) {
                int num = tt.GetSubtilePos(Caches.PosIdx);
                for (int i = 0; i < num; i++) {
                    var p = Caches.PosIdx[i];
                    SetCoverAt(p, amt);
                }
            } else {
                SetCoverAt(tt.PosIdx, amt);
            }
        }

        private void SetCoverAt(int pos, byte amt) {
            if (coverAmtMap[pos] != amt) {
                coverVersion++;
            }
            coverAmtMap[pos] = amt;
            changes.Enqueue(pos);
        }

        #if !SKIP_OVERLAY
        private void OnToggleOverlay(OverlayInfo info, bool on) {
            if (!on || info.Id != OverlayId.Cover) {
                // S.Sig.HideCenterPanel.Send(Director);
            } else {
                // S.Sig.ShowCenterPanel.Send(Director);
            }
        }
        #endif

        private void OnLoadSave(GameState state) {
            path = S.Sys.Path;
            gridSize = S.GridSize;
            gridH = S.GridHeight;
            D.Ass(gridSize != 0, "GridSize is zero when initializing cover sys!");
            coverSlotMap = new CoverSlot[gridSize];
            coverAmtMap = new byte[gridSize];
            coverTotalMap = new short[gridSize];
            S.Sig.CoverChanged.AddListener(OnCoverChanged);
            state.Clock.EveryTickOrFrame.AddListener(EveryFrame);
            Ready.Set(WhenH.CoverSysLoaded, true);
        }

        private void EveryFrame() {
            // int i = 0;
            // float startTime = Time.realtimeSinceStartupAsDouble;
            while (changes.TryDequeue(out var pos)) {
                RecalculateCoverAt(pos);
                RecalculateCoverAt(Pos.N(pos));
                RecalculateCoverAt(Pos.S(pos));
                RecalculateCoverAt(Pos.W(pos));
                RecalculateCoverAt(Pos.E(pos));
                RecalculateCoverAt(Pos.NW(pos));
                RecalculateCoverAt(Pos.SW(pos));
                RecalculateCoverAt(Pos.NE(pos));
                RecalculateCoverAt(Pos.SE(pos));
                // i++;
            }
            // if (i > 0) {
            //     D.Warn("Processed {0} cover changes in {1:0.0000}s", i,
            //         Time.realtimeSinceStartupAsDouble - startTime);
            // }
        }

        private unsafe void RecalculateCoverAt(int pos) {
            Pos.ToXY(pos, out var x, out var y);
            if (x < 1 || x > gridH - 2 || y < 1 || y > gridH - 2) {
                return;
            }
            // no cover by default
            var cover = new CoverSlot();
            cover.PosIdx = pos;
            if (!path.IsPassable(pos)) {
                // there is an obstacle here already
                coverTotalMap[pos] = 0;
                coverSlotMap[pos] = cover;
                return;
            }
            // first calculate direct cover provided in directions

            var vn = coverAmtMap[Pos.N(pos)];
            var ve = coverAmtMap[Pos.E(pos)];
            var vs = coverAmtMap[Pos.S(pos)];
            var vw = coverAmtMap[Pos.W(pos)];
            var vne = coverAmtMap[Pos.NE(pos)];
            var vse = coverAmtMap[Pos.SE(pos)];
            var vnw = coverAmtMap[Pos.NW(pos)];
            var vsw = coverAmtMap[Pos.SW(pos)];

            // right shift by 1 divides byte by 2
            // each direction has value of its direct neighbor or max or half value of side neighbors in that direction
            cover.N = Maths.Max(vn, (byte) (Maths.Max(vne, vnw) >> 1));
            cover.S = Maths.Max(vs, (byte) (Maths.Max(vse, vsw) >> 1));
            cover.E = Maths.Max(ve, (byte) (Maths.Max(vne, vse) >> 1));
            cover.W = Maths.Max(vw, (byte) (Maths.Max(vnw, vsw) >> 1));
            cover.NW = Maths.Max(vnw, (byte) (Maths.Max(vn, vw) >> 1));
            cover.SW = Maths.Max(vsw, (byte) (Maths.Max(vs, vw) >> 1));
            cover.NE = Maths.Max(vne, (byte) (Maths.Max(vn, ve) >> 1));
            cover.SE = Maths.Max(vse, (byte) (Maths.Max(vs, ve) >> 1));
            cover.UpdateTotal();

            coverTotalMap[pos] = cover.Total;
            coverSlotMap[pos] = cover;
        }

        public override void Unload() {
            coverAmtMap = null;
            coverSlotMap = null;
            coverTotalMap = null;
        }

        public string GetName() {
            return Id;
        }
    }
}
