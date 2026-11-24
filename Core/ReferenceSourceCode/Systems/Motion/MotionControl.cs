#if !PRODUCTION_BUILD
// #define DEBUG_GTFO
// #define DEBUG_ESTIMATES
// #define DEBUG_UNSAFE_MOVES
// #define DEBUG_CLAIMS
// #define DEBUG_TP
// #define DEBUG_COST
// #define DEBUG_MOTION_FLAGS
#endif
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI.Combat;
using Game.Systems.Creatures;
using Game.Systems.Path;
using Game.Utils;
using Game.CodeGen;
using Game.Visuals;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;
using Game.Systems.Stats;
using System.Text;
using Game.Systems.AI;
using Game.Systems.Reg;

namespace Game.Systems.Motion {
    public sealed class MotionControl {
        public Position Position;
        public int EntityId;
        public bool IsGrounded =>
            (Flags & MotionFlags.Grounded) == MotionFlags.Grounded;
        public bool IsFlying =>
            (Flags & MotionFlags.Grounded) == MotionFlags.None;
        public bool IsForcedWalk =>
            (Flags & MotionFlags.ForcedWalk) == MotionFlags.ForcedWalk;
        public bool IsRiding =>
            (Flags & MotionFlags.Riding) == MotionFlags.Riding;

        #if DEBUG_MOTION_FLAGS
        private MotionFlags flags;
        public MotionFlags Flags {
            set => D.SetFieldWithDebug(ref flags, value, "MotionFlags");
            get => flags;
        }
        #else
        public MotionFlags Flags;
        #endif
        public HashSet<int> ImmobilizeReasons;
        private NavPermission permissions;
        public NavPermission Permissions => permissions;
        public bool IsBusy => Progress.IsMoving;
        public bool IsImmobilized => (Flags & MotionFlags.Immobilized) == MotionFlags.Immobilized;
        public readonly List<int> Path = new();
        public PathReq Request;
        public PathRes Result;
        public BeingData Data;
        public Being Being;
        public BeingGraphicsComp Graphics;
        public MotionComp Comp;
        public CombatUnit CombatUnit;
        public int CrowdNow;
        public float Velocity;
        public float MaxVelocity;
        public float MaxTemp;
        public float MinTemp;
        public float MinO2;
        public float WantedPathPct;
        public float WantedDistToTargetSqr;
        public float VelocityPercent;
        public float CombatSlowdown;
        public int TicksStill;
        // ticks spent waiting for a hostile body to clear the next tile (BlockedAt)
        public int TicksBlocked;
        public int BlockedAt = Pos.Invalid;
        public int TicksInCurrentPosition;
        public int TeleporterDelay;
        public float DistCoveredThisTick;
        public Entity Target;
        private TeleporterComp teleporter;
        private readonly PathSys pathSys;
        public readonly GameState S;
        public readonly MotionProgress Progress;
        public PathRes Estimate;
        public readonly List<int> PathEstimate = new();
        /// <summary>
        /// This DOES get cleared after invocation!
        /// </summary>
        public Action<PathRes, List<int>> OnPathEstimate;
        private Guidelines pathViz;
        public bool IsAnimated;
        private bool isShowingPathViz;
        private bool wasSafeBefore;
        private bool wasNotStuck;
        public int PathIdx;
        public int ClaimedPosIdx = Pos.Invalid;
        // where this walk ends, held in PathSys so spot pickers leave it to this being
        public int ReservedDestIdx = Pos.Invalid;
        public event Action<Being, int, int> OnPositionChange;
        private int posIdxOverride;
        private string enqueuedDirt;
        public uint ExpectedReqId;
        private Stat statMoveSpeed;
        public bool IsInQuadTree;
        public DoorComp CurrentDoor;
        /// <summary>
        /// Item1 = Ticks when it happened
        /// Item2 = Ticks when to forget
        /// Public for Save, Load.
        /// <see cref="StoreNavFailureAt(int, int)"/>
        /// <see cref="HadNavFailureAt(int, long)"/>
        /// </summary>
        public readonly Dictionary<int, (long, long)> NavFailureMemory = new();
        public bool WillFloat {
            get {
                if (HasFlags(MotionFlags.Carried)) { return false; }
                if (HasFlags(MotionFlags.Grounded)
                        || HasFlags(MotionFlags.Immobilized)) {
                    return true;
                }
                return false;
            }
        }
        public MotionControl(GameState s) {
            S = s;
            pathSys = s.Sys.Path;
            Progress = new MotionProgress();
        }

        public void TeleportTo(Vector2 pos) {
            int p = Pos.FromVector(pos);
            if (p == Pos.Invalid) {
                D.Err("Trying to teleport a being to an invalid position: {0}", Being);
            }
            if (posIdxOverride != 0) {
                p = posIdxOverride;
            }
            var prevP = Position.Idx;
            Position.Exact = pos;
            #if !PRODUCTION_BUILD
            if (Ready.Gen && Pos.SqrDistance(prevP, p) > 9) {
                if (prevP != 0 && teleporter == null && Being.Carrier == null
                        && Being.IsAlive && !Being.Data.Body.IsIncapacitated
                        && (!IsGrounded || !HasFlags(MotionFlags.InSpace))
                        && (!Being.Graphics.IsRiding || G.ClockSpeed < ClockSpeed.Unlimited)) {
                    D.Err("Teleporting being without teleporter: {0} | AiAct: {1} | {2}",
                        Pos.StringFromTo(prevP, p), Being.AIAgent.CurrentAction, Being);
                }
            }
            #endif
            // Being.Position and Select position will be set in graphics update
            Graphics.Update(pos);
            if (prevP != p) {
                Position.Idx = p;
                Being.PosIdx = p;
                Being.CenterIdx = p;
                OnPositionChange?.Invoke(Being, prevP, p);
                if (S.Sys.Combat.IsAnyCombatInProgress) {
                    Being.AIAgent.CombatUnit.RefreshCover();
                }
                Being.AIAgent.ClearRoamFailure();
                TicksInCurrentPosition = 0;
                #if DEBUG_UNSAFE_MOVES
                ValidateSafety(prevP, p);
                #endif
            }
        }

        #if DEBUG_UNSAFE_MOVES
        private void ValidateSafety(int prevP, int p) {
            var bd = Being.Data;
            if (Being.IsAlive &&
                    bd.Species.IsBiological &&
                    !bd.Body.IsIncapacitated &&
                    bd.StatsBeing.O2Min.Value > 0f &&
                    bd.Safety.IsActuallySafeAt(prevP) &&
                    !bd.Safety.IsActuallySafeAt(p)
                    && !S.Sys.Path.IsDoor(p)) {
                if (Being.Motion.Progress.IsUnsafe) {
                    D.Warn("Being moved from safe to unsafe position (allowed by path request): {0}. {1}. {2} | {3} | {4}",
                        Being, Pos.StringFromTo(prevP, p),
                        Being.AIAgent.CurrentAction, Being.AIAgent.Goal, Being.Motion.Request);
                } else {
                    D.Err("Being moved from safe to unsafe position: {0}. {1}. {2} | {3} | {4}",
                        Being, Pos.StringFromTo(prevP, p),
                        Being.AIAgent.CurrentAction, Being.AIAgent.Goal, Being.Motion.Request);
                }
            }
        }
        #endif

        public bool TryGoTo(Tile obj, PathReqFlags flags = PathReqFlags.None) {
            return TryGoTo(obj, flags, out _);
        }

        public bool TryGoTo(Tile obj, PathReqFlags flags, out MotionProgress prog) {
            if (obj == null || !obj.IsActive) {
                if (obj == null) {
                    D.Err("Trying to go to null object! {0}: {1}", flags, Being);
                } else {
                    D.Warn("Trying to go to inactive / destroyed object! {0}: {1}", flags, Being);
                }
                prog = Error_TargetUnavailable;
                return false;
            }
            if (EntityUtils.IsNearObj(Position.Idx, obj)) {
                Clear(true);
                prog = Progress;
                return true;
            }
            flags |= PathReqFlags.AdjNearby;
            var tr = obj.Transform;
            return TryGoTo(tr.PosIdx, flags, out prog,
                tr.RotatedWidth, tr.RotatedHeight);
        }

        public bool TryGoTo(int posIdx, PathReqFlags flags) {
            return TryGoTo(posIdx, flags, out _);
        }

        public bool TryGoTo(int posIdx, PathReqFlags flags, out MotionProgress prog,
                int goalW = 0, int goalH = 0) {
            return TryStartWalk(posIdx, flags, out prog, goalW, goalH, 0f, null, 0f);
        }

        // The walk modifiers (path trim, stop near a target) are set only where a walk starts:
        // an early return (goal next to us, blocked, immobilized) used to leave them behind to
        // cut the next, unrelated walk short
        private bool TryStartWalk(int posIdx, PathReqFlags flags, out MotionProgress prog,
                int goalW, int goalH, float pathPct, Entity target, float wantedDistToTargetSqr) {
            // D.Warn("Trying to go to: {0}=>{1}", Pos.StringFromTo(Position.Idx, posIdx), Being);
            if (flags == PathReqFlags.None) {
                D.Err("Path request flags are none for req to go to {0}", Pos.String(posIdx));
                prog = Error_Immobilized;
                return false;
            }
            // D.Warn("Trying to go to {0}. Flags: {1}", Pos.String(posIdx), flags);
            if (HasProgressFlag(MotionProgressFlags.Moving)) {
                prog = Error_LastNavNotDone;
                return false;
            }
            if (HasFlagsAny(MotionFlags.Immobilized | MotionFlags.Carried | MotionFlags.Riding)) {
                prog = Error_Immobilized;
                return false;
            }
            if (Pos.IsOOB(posIdx)) {
                prog = Error_TargetUnavailable;
                return false;
            }
            if (posIdx == 0) {
                D.Err("Will go to 0 coordinate! {0} | {1}", Being, Being.AIAgent.CurrentAction);
            }
            if (Data.HasCondition(ConditionIdH.Sleeping)) {
                D.Err("Trying to move while sleeping! {0} {1}",
                    Pos.StringFromTo(Position.Idx, posIdx), Data);
                prog = Error_Immobilized;
                return false;
            }
            bool onTop = (flags & PathReqFlags.AdjOnTop) == PathReqFlags.AdjOnTop;
            bool nearby = (flags & PathReqFlags.AdjNearby) == PathReqFlags.AdjNearby;

            if ((goalW > 1 || goalH > 1) && !nearby) {
                if (!pathSys.IsPassable(posIdx)) {
                    D.Err("Goal is > 1x1 and is impassable, but there's no Nearby flag! Setting: {0}",
                        Pos.String(posIdx));
                }
                flags |= PathReqFlags.AdjNearby;
                nearby = true;
            }

            if (onTop && posIdx == Position.Idx) {
                prog = Progress;
                return true;
            }
            if (nearby && Pos.AreAdjacent(posIdx, Position.Idx)) {
                prog = Progress;
                return true;
            }

            if (!nearby && ((flags & PathReqFlags.TempDestination) != PathReqFlags.TempDestination)
                    && pathSys.IsClaimed(posIdx, Being.Id)) {
                prog = Error_Blocked;
                return false;
            }

            PathNodeType pathFlags = PathNodeType.Passable;
            if (IsGrounded) {
                pathFlags |= PathNodeType.Walkable;
            }
            if (onTop && !nearby) {
                if (!pathSys.FlagsMatch(posIdx, pathFlags)) {
                    if (IsGrounded) {
                        prog = Error_UnreachableGrounded;
                    } else {
                        prog = Error_UnreachableFlying;
                    }
                    return false;
                }
            }

            if (goalW < 2 && goalH < 2) {
                // only for single tile goals!
                // If it's not on top, we only care if we can reach this from nearby
                bool hasNavigableNeighbor = false;
                for (int i = 0; i < 8; i++) {
                    var np = Pos.NeighborAt(posIdx, i);
                    if (Pos.IsOOB(np)) { continue; }
                    if (pathSys.FlagsMatch(np, pathFlags)) {
                        hasNavigableNeighbor = true;
                        break;
                    }
                }
                if (!hasNavigableNeighbor) {
                    prog = Error_Blocked;
                    return false;
                }
            }

            Progress.GoalPosIdx = posIdx;
            Progress.Flags = MotionProgressFlags.Moving;
            TicksBlocked = 0;
            BlockedAt = Pos.Invalid;
            // before AddReq: an instant request trims the path inside it
            WantedPathPct = pathPct;
            Target = target;
            WantedDistToTargetSqr = wantedDistToTargetSqr;

            // is the current position less safe than the goal? if so, let's allow
            // unsafe path
            if (goalW < 2 && goalH < 2) {
                if (Data.Safety.IsFeelingSaferAt(Position.Idx, posIdx)) {
                    if (!Data.Safety.IsActuallySafeAt(Position.Idx)) {
                        flags |= PathReqFlags.UnsafeOK;
                    } else if (S.Query.RegionDist.Ask(
                            RegPathReq.To(Position.Idx, posIdx, IsGrounded)) < 2) {
                        flags |= PathReqFlags.UnsafeOK;
                    }
                }
            }

            // We have to do actual pathfinding
            Request = CreatePathReq(posIdx, flags, goalW, goalH);
            var reqId = pathSys.AddReq(ref Request, this);
            prog = Progress;
            if ((flags & PathReqFlags.Estimate) != PathReqFlags.Estimate) {
                // only non-estimates will set an expected req id
                ExpectedReqId = reqId;
            }
            return reqId != 0;
        }
        public bool TryGoTowardsTarget(int posIdx, Entity target,
                float wantedDistToTarget) {
            return TryGoTowardsTarget(posIdx, target, wantedDistToTarget, out _);
        }
        public bool TryGoTowardsTarget(int posIdx, Entity target,
                float wantedDistToTarget, out MotionProgress prog) {
            return TryStartWalk(posIdx, PathReqFlags.AdjNearby, out prog, 0, 0, 0f, target,
                wantedDistToTarget * wantedDistToTarget);
        }
        public bool TryGoTowards(int posIdx, float percent) {
            return TryGoTowards(posIdx, percent, out _);
        }

        public bool TryGoTowards(int posIdx, float percent, out MotionProgress prog,
                PathReqFlags flags = PathReqFlags.AdjNearby) {
            // D.Warn("Trying to go towards: {0} | {1}", Pos.StringFromTo(Position.Idx, posIdx), percent);
            return TryStartWalk(posIdx, flags, out prog, 0, 0, percent, null, 0f);
        }

        /// <summary>
        /// This is used for determining if the worker can reach an object where
        /// work has to happen. There is an exception - if we are trying to reach
        /// a floor tile that is underneath a 3x3 or bigger object, we will instead
        /// try to reach the object outline, not the tile itself
        /// </summary>
        public bool EstimatePathTo(Tile t, Action<PathRes, List<int>> onDone,
                PathReqFlags flags = PathReqFlags.AdjNearby) {
            // if (HasPath) {
            //     D.Err("Being has path but trying to estimate! {0} {1}",
            //         Being, Being.AIAgent.CurrentAction);
            //     return false;
            // }
            t = EntityUtils.SelfOrBlockingObjAbove(t);
            if (t == null || !t.IsActive) { return false; }
            int goalW = 1;
            int goalH = 1;
            if (t.Transform.IsMulti) {
                goalW = t.Transform.RotatedWidth;
                goalH = t.Transform.RotatedHeight;
            }
            flags |= PathReqFlags.Estimate;
            flags |= PathReqFlags.TempDestination;
            var estimateReq = CreatePathReq(t.PosIdx, flags, goalW, goalH);
            estimateReq.BeingId = -1;
            #if DEBUG_ESTIMATES
            flags |= PathReqFlags.Instant;
            #endif
            if ((flags & PathReqFlags.Instant) == PathReqFlags.Instant) {
                PathEstimate.Clear();
                var res = pathSys.ExecuteReq(ref estimateReq, PathEstimate);
                if (onDone != null) {
                    onDone(res, PathEstimate);
                }
                return res.IsValid;
            } else {
                if (OnPathEstimate != null) {
                    D.Err("Overwriting OnPathEstimate! {0} with {1}",
                        OnPathEstimate, onDone);
                }
                OnPathEstimate = onDone;
                var reqId = pathSys.AddReq(ref estimateReq, this);
                if (reqId == 0) {
                    onDone(default, PathEstimate);
                }
            }
            return true;
        }

        public bool EstimatePathTo(int p, Action<PathRes, List<int>> onDone,
                PathReqFlags flags = PathReqFlags.AdjNearby) {
            if (Pos.IsOOB(p)) { return false; }
            if (IsGrounded) {
                if (!S.Sys.Areas.CanReachGrounded(Position.Idx, p)) {
                    return false;
                }
            }
            flags |= PathReqFlags.Estimate;
            flags |= PathReqFlags.TempDestination;
            var estimateReq = CreatePathReq(p, flags);
            estimateReq.BeingId = -1;

            #if DEBUG_ESTIMATES
            flags |= PathReqFlags.Instant;
            #endif
            if ((flags & PathReqFlags.Instant) == PathReqFlags.Instant) {
                // D.Warn("Instant path estimate to: {0}", Pos.String(p));
                PathEstimate.Clear();
                var res = pathSys.ExecuteReq(ref estimateReq, PathEstimate);
                if (onDone != null) {
                    onDone(res, PathEstimate);
                }
                return res.IsValid;
            } else {
                if (OnPathEstimate != null) {
                    D.ErrSoft("Overwriting OnPathEstimate! {0} with {1}",
                        OnPathEstimate, onDone);
                }
                OnPathEstimate = onDone;
                var reqId = pathSys.AddReq(ref estimateReq, this);
                if (reqId == 0) {
                    onDone(default, PathEstimate);
                }
            }

            return true;
        }

        public int PosOrPathAhead(int maxSteps) {
            if (Progress.HasPath) {
                var pathIdx = PathIdx + maxSteps;
                if (pathIdx >= Path.Count) {
                    pathIdx = Path.Count - 1;
                }
                return Path[pathIdx];
            }
            return Position.Idx;
        }


        /*
        path = 123, 124, 125
        pathIdx = 0 -> 1 -> 2

        */

        public bool AdvancePath(bool initial) {
            if (initial) {
                // to prevent going from cur position to cur pos
                // teleporter check will revert this if needed
                PathIdx = 1;
            }
            if (Path.Count <= PathIdx) {
                return false;
            }
            int nextIdx;
            bool isLastStep = PathIdx == Path.Count - 1;

            nextIdx = Path[PathIdx++];

            var tryTeleport = LookAheadForTeleporter(initial, out var tpTo, ref nextIdx);

            #if DEBUG_TP
            D.Warn("Advancing path: idx:{0} {1}. Try tp: {2}->{3}",
                PathIdx, Pos.StringFromTo(Position.Idx, nextIdx), tryTeleport, Pos.String(tpTo));
            #endif

            if (!Progress.IsUnsafe) {
                var isNextSafe = Data.Safety.IsActuallySafeAt(nextIdx, out var threatT);
                if (!isNextSafe && Being.AIAgent.Goal?.IsNonInterruptable != true
                        && !(Request.HasFlag(PathReqFlags.SurvivableOK)
                            && Data.Safety.IsActuallySurvivableAt(nextIdx))) {
                    if (!S.Sys.Path.IsDoor(nextIdx)) {
                        D.Warn("Aborting move due to unsafe position ahead! {0} | {1} | {2}",
                            Pos.String(nextIdx), Being, threatT);
                        // #if !PRODUCTION_BUILD
                        // if (!The.TestsAreRunning) {
                        //     S.Clock.SetSpeed(ClockSpeed.Stopped);
                        //     EntityUtils.CameraFocusOn(Being);
                        // }
                        // #endif
                        Fail(threatT);
                        return false;
                    }
                }
            }


            if (!tryTeleport && nextIdx != Position.Idx && IsBlockedByBodyAt(nextIdx)) {
                // A fighter of a hostile army stands on or steps into the next tile: wait for it
                // to clear (RW: a pawn never enters a blocker's cell). HasReachedNextNode asks
                // again every frame, and MotionSys repaths or gives up after a while (TickBlocked)
                PathIdx--;
                if (initial) {
                    Position.NextIdx = Position.Idx;
                    Position.Next = Position.Exact;
                }
                BlockedAt = nextIdx;
                Progress.Flags |= MotionProgressFlags.Blocked;
                return true;
            }
            if (BlockedAt != Pos.Invalid) {
                BlockedAt = Pos.Invalid;
                TicksBlocked = 0;
                Progress.Flags &= ~MotionProgressFlags.Blocked;
            }

            // we know that if next idx is not adjacent to current pos, we must
            // be using a teleporter
            if (tryTeleport) {
                // we want to use this teleporter!
                #if DEBUG_TP
                if (initial) {
                    D.Warn("Attempting to teleport (initial): {0}",
                        Pos.StringFromTo(nextIdx, tpTo));
                }
                #endif
                if (AttemptToTeleport(nextIdx, tpTo)) {
                    nextIdx = tpTo;
                } else {
                    Fail(T.PathNotPassable);
                    return false;
                }
            }

            Position.NextIdx = nextIdx;
            MarkBodyAt(nextIdx);
            var next = EntityUtils.CenterOf(Position.NextIdx);

            if (PathIdx < 3) {
                // First two cells, but we always start at idx 1
                Progress.Flags |= MotionProgressFlags.Accelerating;
            } else {
                Progress.Flags &= ~MotionProgressFlags.Accelerating;
                if (nextIdx == Result.Goal) {
                    // Last cell
                    Progress.Flags |= MotionProgressFlags.Decelerating;
                }
            }

            // we may be going nearby, that way let's go all the way to the
            // edge of the goal
            if (Request.HasFlag(PathReqFlags.AdjNearby)) {
                var reqTo = Request.PosTo;
                var goalTo = Result.Goal;
                if (reqTo != goalTo && nextIdx == goalTo) {
                    next = Vector2.Lerp(next,
                        EntityUtils.CenterOf(reqTo), 0.5f);
                }
            } else {
                next += Rng.UInsideUnitCircle() * 0.25f;
            }
            Position.Next = next;

            // Stop short of a closed door at once. MotionSys.CheckDoor asks it to open on the
            // next tick, and a being left walking until then can get past the leaf (STA-1796)
            var doors = S.Sys.Areas.DoorCheck?.DoorsByPos;
            if (doors != null && doors.TryGetValue(nextIdx, out var aheadDoor)) {
                if (!aheadDoor.IsOpen) {
                    CurrentDoor = aheadDoor;
                }
            } else if (IsCrossingClosedDoor(out var inDoor)) {
                CurrentDoor = inDoor;
            }

            // was the floor removed, was a wall built there
            if (pathSys.IsNavigable(nextIdx, IsGrounded)) {
                wasNotStuck = true;
            } else {
                // if we were stuck at the start of our path, we will try to get unstuck first
                if (wasNotStuck) {
                    Fail(T.AdRejInterrupted);
                } else {
                    // we need to know if this thing is passable, ignoring the walkability check
                    if (S.Sys.Path.IsPassable(nextIdx)) {
                        Fail(T.AdRejInterrupted);
                    }
                }
            }

            if (isLastStep && !Request.HasFlag(PathReqFlags.TempDestination)
                    && !pathSys.MayStopAt(nextIdx, Being)) {
                // a sandbag or a door built on the path's end after it was found: the claim
                // below would wait for it forever, so replan to stop beside it
                Fail(T.AdRejInterrupted);
                return false;
            }
            if (isLastStep) {
                // if we only move by one step, we can't release claimed pos
                // in OnTeleport because it will be too late, let's do it now
                ReleaseClaimedPos();
                if (!TryClaimPos(nextIdx)) {
                    // try claim recently release position back
                    TryClaimPos(Position.Idx);
                    // we won't fail, but we also won't go
                    if (initial) {
                        Fail("Congested");
                    }
                    return false;
                }
            } else if (initial || ClaimedPosIdx != Pos.Invalid) {
                // a walk that waited at its start for a blocker still holds its first tile
                ReleaseClaimedPos();
            }

            return true;
        }

        private bool IsBlockedByBodyAt(int posIdx) {
            var mask = HostileBodies.MaskFor(CombatUnit);
            return mask != 0 && (pathSys.BodyBitsAt(posIdx) & mask) != 0;
        }

        // A step takes its tile at once, so a hostile walker does not step into it too before
        // the next rebuild (HostileBodies)
        private void MarkBodyAt(int posIdx) {
            var bit = CombatUnit?.Army?.BodyBit ?? 0;
            if (bit != 0 && posIdx > 0) {
                pathSys.MarkBody(posIdx, bit);
            }
        }

        // ticks to wait for a blocker before looking for a way around it, and before giving
        // up: RW repaths a blocked walk and gives up after 180 ticks (3 s), 9 of ours at 1x
        public const int BlockedRepathTicks = 2;
        public const int BlockedGiveUpTicks = 9;
        #if !PRODUCTION_BUILD
        public static int BlockedRepaths;
        public static int BlockedGiveUps;
        #endif

        // A tick of waiting for a blocker (MotionSys)
        public void TickBlocked() {
            TicksBlocked++;
            if (TicksBlocked == BlockedRepathTicks) {
                RepathAroundBlocker();
            } else if (TicksBlocked >= BlockedGiveUpTicks) {
                GiveUpOnBlocker();
            }
        }

        // The path search now counts the blocker's tile (FastPathfindJob.DifficultyOf): a
        // short way around it if there is one, else the same way and more waiting
        private void RepathAroundBlocker() {
            #if !PRODUCTION_BUILD
            BlockedRepaths++;
            #endif
            var req = Request;
            Path.Clear();
            PathIdx = 0;
            Request = CreatePathReq(req.PosTo, req.Flags, req.GoalWidth, req.GoalHeight);
            ExpectedReqId = pathSys.AddReq(ref Request, this);
            if (ExpectedReqId == 0) {
                // refused at once (AddReq marked the walk failed): end it, so the reserved
                // destination and the wait are let go
                Clear(false);
            }
        }

        // RW: a pawn blocked by a hostile for 3 s attacks it if it can, else ends its job
        private void GiveUpOnBlocker() {
            #if !PRODUCTION_BUILD
            BlockedGiveUps++;
            #endif
            var cu = CombatUnit;
            var mask = HostileBodies.MaskFor(cu);
            var blocker = mask == 0 ? null
                : HostileBodies.BodyAt(S.Sys.Combat.Armies, BlockedAt, mask);
            Fail(T.NavFailBlockedByEnemy);
            if (blocker != null && MayAttackBlocker(cu) && cu.AttackTarget != blocker) {
                cu.SetAttackTarget(blocker);
            }
        }

        // Not when the player is in control (a drafted unit stands and melees an enemy next
        // to it on its own) or gave it a target, nor for a unit that flees or ignores fights
        private static bool MayAttackBlocker(CombatUnit cu) {
            return cu.CanAttack && !cu.IsFleeing && !cu.IsAttackTargetForced
                && cu.EffectiveConfig != CombatUnitConfig.Ignore
                && !cu.Agent.DirectControl.IsUnderDirectControl;
        }

        // A walk that stops for good holds its last tile until it ends (RW reserves a goto's
        // destination). Passing through (TempDestination) holds nothing.
        private void ReserveDestination(PathReq req) {
            ReleaseDestination();
            if (req.HasFlag(PathReqFlags.TempDestination) || Path.Count == 0) { return; }
            var dest = Path[Path.Count - 1];
            if (dest == Position.Idx || Pos.IsOOB(dest)) { return; }
            if (pathSys.TryReserveDest(dest, Being.Id)) {
                ReservedDestIdx = dest;
            }
        }

        public void ReleaseDestination() {
            if (ReservedDestIdx != Pos.Invalid) {
                pathSys.ReleaseDest(ReservedDestIdx, Being.Id);
                ReservedDestIdx = Pos.Invalid;
            }
        }

        public void ReleaseClaimedPos() {
            if (ClaimedPosIdx != Pos.Invalid) {
                #if DEBUG_CLAIMS
                D.Warn("Releasing claimed pos: {0} | {1}", Pos.String(ClaimedPosIdx), Being);
                #endif
                pathSys.ReleaseClaimedPos(ClaimedPosIdx, Being.Id);
                ClaimedPosIdx = Pos.Invalid;
            }
        }

        public bool TryClaimPos(int posIdx) {
            if ((Request.Flags & PathReqFlags.TempDestination) == PathReqFlags.TempDestination) {
                // not claiming temp destination
                #if DEBUG_CLAIMS
                D.Warn("Skipping claim of {0} due to temp request {1}", Pos.String(posIdx), Being);
                #endif
                return true;
            }
            if (!Being.IsActive || Being.Data.Body.IsIncapacitated
                    || Being.Data.HasStateFlag(BeingState.InDevice)) {
                #if DEBUG_CLAIMS
                D.Warn("Not claiming pos due to inactive / incapacitated: {0} | {1}",
                    Pos.StringFromTo(ClaimedPosIdx, posIdx), Being);
                #endif
                return true;
            }
            #if DEBUG_CLAIMS
            D.Warn("Trying to claim pos: {0} | {1}", Pos.StringFromTo(ClaimedPosIdx, posIdx), Being);
            #endif
            if (ClaimedPosIdx != Pos.Invalid) {
                #if DEBUG_CLAIMS
                D.Err("Trying to claim position while already have one claimed. Will release: {0}. Claiming {1} | {2}",
                    Pos.String(ClaimedPosIdx), Pos.String(posIdx), Being);
                #endif
                ReleaseClaimedPos();
            }
            if (pathSys.TryClaimPos(posIdx, Being)) {
                #if DEBUG_CLAIMS
                D.Warn("Claimed pos: {0} | {1} | {2}", Pos.String(posIdx), Being, Being.AIAgent.Goal);
                #endif
                ClaimedPosIdx = posIdx;
                return true;
            }
            return false;
        }

        private bool LookAheadForTeleporter(bool initial, out int tpTo, ref int nextIdx) {
            bool tryTeleport = false;
            if (initial) {
                // start path from teleporter
                if (pathSys.IsTeleporter(Position.Idx, out tpTo)) {
                    // PathIdx was set to 1 initially, then ++ed, now must revert back to
                    // teleport correctly
                    PathIdx = 1;
                    nextIdx = Path[0];
                    if (Path.Count > PathIdx) {
                        if (Path[PathIdx] == tpTo) {
                            tryTeleport = true;
                        }
                    }
                } else if (pathSys.IsTeleporter(nextIdx, out tpTo)) {
                    // start near the teleporter carefully
                    PathIdx = 1;
                    nextIdx = Path[0];
                }
            } else {
                if (pathSys.IsTeleporter(nextIdx, out tpTo)) {
                    // we are starting our walk from a teleporter, let's not
                    // advance the path idx!
                    if (Path.Count > PathIdx) {
                        if (Path[PathIdx] == tpTo) {
                            tryTeleport = true;
                        }
                    }
                }
            }

            return tryTeleport;
        }

        private bool AttemptToTeleport(int tpIn, int tpOut) {
            var curTeleporter = EntityUtils.ObjAt(tpIn);
            var nextTeleporter = EntityUtils.ObjAt(tpOut);
            bool isTeleporterWorking = false;
            var t1 = curTeleporter?.Teleporter;
            if (t1 != null) {
                var t2 = nextTeleporter?.Teleporter;
                if (t2 != null) {
                    if (t1.TryTeleportTo(Being, t2)) {
                        teleporter = t2;
                        TeleporterDelay = Consts.TicksPer5Minutes;
                        isTeleporterWorking = true;
                    }
                }
            }
            if (!isTeleporterWorking && !Pos.AreAdjacent(tpIn, tpOut)) {
                return false;
            }
            return true;
        }

        public void SetAbleToFly(bool yes) {
            if (yes) {
                Flags &= ~MotionFlags.Grounded;
            } else {
                Flags |= MotionFlags.Grounded;
            }
            SetPermission(NavPermission.Flying, yes);
            SetPermission(NavPermission.Walking,
                (Flags & MotionFlags.Grounded) == MotionFlags.Grounded);
        }

        private bool NeedsCover {
            get {
                if (CombatUnit.State != CombatUnitState.Unaware) {
                    if (CombatUnit.Agent.DirectControl.IsUnderDirectControl) {
                        return false;
                    }
                    return true;
                }
                if (Data.Mind.Traits.HasTrait(TraitIdH.Paranoid)) { return true; }
                return false;
            }
        }
        public PathReq CreatePathReq(int toPos, PathReqFlags flags,
                int posW = 0, int posH = 0) {
            if (NeedsCover) {
                flags |= PathReqFlags.CombatCover;
            }
            // D.Warn("Creating path req for {0} to {1} | {2}", Being, Pos.String(toPos), flags);
            if (HasProgressFlag(MotionProgressFlags.Teleporting)) {
                D.Err("Creating path request when teleporting! {0} | {1} | {2}",
                    flags, Being.AIAgent, Data);
            }
            float maxTemp = MaxTemp;
            float minTemp = MinTemp;
            float minO2 = MinO2;
            if ((flags & PathReqFlags.SurvivableOK) == PathReqFlags.SurvivableOK) {
                minTemp -= 100f;
                maxTemp += 200f;
            }
            // D.Warn("Creating path request from pos: {0} | {1} | {2}",
            //     Pos.StringFromTo(Position.Idx, toPos), Progress.Flags, Data);
            return new PathReq {
                PosFrom = Position.Idx,
                PosTo = toPos,
                Flags = flags,
                GoalWidth = posW,
                GoalHeight = posH,
                BeingId = EntityId,
                Permissions = permissions,
                HostileBodies = HostileBodies.MaskFor(CombatUnit),
                MaxTemp = maxTemp,
                MinTemp = minTemp,
                MinO2 = minO2,
            };
        }
        #if !PRODUCTION_BUILD
        private string permissionsInfo;

        public string PermissionsInfo {
            get {
                if (permissionsInfo != null) { return permissionsInfo; }
                permissionsInfo = PathNode.PermissionStr(permissions);
                return permissionsInfo;
            }
        }
        #endif

        /// <returns>true if there is no overlap between being permissions and deny list</returns>
        public bool CheckPermissions(NavPermission deny) {
            return (deny & permissions) == NavPermission.None;
        }

        public void UpdateNavPermissions() {
            var species = Data.Species;
            if (species.IsBiological) {
                SetPermission(NavPermission.Biological, true);
                if (species.IsAnimal) {
                    SetPermission(NavPermission.Animal, true);
                } else {
                    SetPermission(NavPermission.Humanoid, true);
                }
            } else {
                SetPermission(NavPermission.Robot, true);
            }
        }
        public bool CanGoThrough(NavPermission deny) {
            return (deny & permissions) == NavPermission.None;
        }

        public void SetPermission(NavPermission perm, bool on) {
            if (on) {
                permissions |= perm;
            } else {
                permissions &= ~perm;
            }
            #if !PRODUCTION_BUILD
            permissionsInfo = null;
            #endif
        }

        public void Fail(string reason) {
            Progress.FailReason = reason;
            Progress.Flags |= MotionProgressFlags.Failed;
            Progress.Flags &= ~MotionProgressFlags.Moving;
            Clear(false);
        }

        public void Abort(string reason) {
            if (!Progress.IsMoving) { return; }
            Fail(reason);
        }

        public void Clear(bool resetFlags) {
            // D.Warn("Clearing path. Reset flags: {0}", resetFlags);
            Path.Clear();
            Position.NextIdx = Pos.Invalid;
            Position.Next = Consts.V2Zero;
            PathIdx = 0;
            CurrentDoor = null;
            TicksBlocked = 0;
            BlockedAt = Pos.Invalid;
            Progress.Flags &= ~MotionProgressFlags.Blocked;
            ReleaseDestination();
            if (resetFlags) {
                if (HasProgressFlag(MotionProgressFlags.Teleporting)) {
                    // D.Warn("Clearing move progress while teleporting! {0}", Data);
                    Progress.Flags = MotionProgressFlags.Teleporting;
                } else {
                    Progress.Flags = MotionProgressFlags.None;
                }
            } else {
                Progress.Flags &= ~(
                    MotionProgressFlags.Moving
                        | MotionProgressFlags.HasPath
                        | MotionProgressFlags.Accelerating
                        | MotionProgressFlags.Decelerating);
            }
            // clear forced walk flag if we had one
            Flags &= ~MotionFlags.ForcedWalk;
            WantedPathPct = 0f;
            WantedDistToTargetSqr = 0f;
            wasSafeBefore = false;
            wasNotStuck = false;
            // D.Warn("Clearing path for {0}", Being);
            ExpectedReqId = 0;
            Velocity = 0;
            VelocityPercent = 0;
            if (!Data.HasStateFlag(BeingState.Hidden)) {
                Graphics.Update(Position.Exact);
            }
            Target = null;
            if (ClaimedPosIdx == Pos.Invalid) {
                if (!TryClaimPos(Position.Idx)) {
                    // TryStepAside();
                    // We are in someone's place
                    // D.Err("TODO: push out {0}", Pos.String(Position.Idx));
                }
            }
        }

        public void Remove() {
            ReleaseClaimedPos();
            // hack to prevent releasing claimed pos on clear
            ClaimedPosIdx = 0;
            Clear(true);
            ClaimedPosIdx = Pos.Invalid;
            PathEstimate.Clear();
            HidePathViz();
            OnPositionChange = null;
        }

        public bool CanIdle => pathSys.CanIdleAt(Position.Idx)
            && !Being.Graphics.HasRider;

        public bool SkipRunAgent => HasFlagsAny(MotionFlags.ForcedWalk | MotionFlags.Immobilized);
        public bool IsAtClaimedPos => ClaimedPosIdx == Position.Idx;

        public void SetPathRes(PathReq req, PathRes pathRes) {
            // D.Warn("Setting path res: {0} => {1}", req, pathRes);
            if (req.HasFlag(PathReqFlags.Estimate)) {
                if (OnPathEstimate != null) {
                    OnPathEstimate.Invoke(pathRes, PathEstimate);
                    OnPathEstimate = null;
                }
            } else {
                // D.Warn("Setting path res {2}-> {0} | {1}", D.Dump(Path), pathRes, Pos.String(Position.Idx));
                if (pathRes.Start != Position.Idx) {
                    if ((Flags & MotionFlags.InSpace) == MotionFlags.InSpace) {
                        // this can happen if being is floating away
                        D.Warn("Setting path result in space where start != cur pos! {0} | {1}=>{2} | {3}",
                            Pos.StringFromTo(Position.Idx, pathRes.Start), req, pathRes, Data);
                    } else {
                        D.Err("Setting path result where start != cur pos! {0} | {1}=>{2} | {3}",
                            Pos.StringFromTo(Position.Idx, pathRes.Start), req, pathRes, Data);
                    }
                    pathRes.IsValid = false;
                }
                Result = pathRes;
                wasSafeBefore = false;
                wasNotStuck = false;
                Progress.Flags = MotionProgressFlags.HasPath;
                // CrossesUnsafe: an escape planned from a safe tile with no safe neighbor (an
                // open airlock between unsafe rooms), which AdvancePath would refuse at once
                if (!pathRes.IsSafe || pathRes.CrossesUnsafe) {
                    Progress.Flags |= MotionProgressFlags.Unsafe;
                }
                if (!pathRes.IsValid) {
                    Progress.Flags |= MotionProgressFlags.Failed;
                    Progress.Flags &= ~MotionProgressFlags.Moving;
                    Clear(false);
                    TryUnstuck(IsGrounded);

                } else {
                    // The first node of Path is our current position already,
                    // so we will set path idx to 1
                    // NO, you stupid fuck! It can be a teleporter!
                    // PathIdx = 1;
                    Progress.Flags |= MotionProgressFlags.Moving;

                    if (WantedPathPct > 0f) {
                        pathRes = TrimPath(pathRes);
                    }
                    ReserveDestination(req);

                    var isSafe = pathSys.IsPassable(Position.Idx)
                        && Data.Safety.IsActuallySafeAt(Position.Idx);
                    if (!isSafe) {
                        // we start at an unsafe path, let's ignore safety for the
                        // whole trip
                        Progress.Flags |= MotionProgressFlags.Unsafe;
                    }
                    if (!AdvancePath(true)) {
                        //if (Path.Count == 0) {
                            // otherwise the path was aborted due to safety
                            // it can also be due to crowd / claimed positions
                            // D.Err("Got path result but path is 0 length: {0}->{1} | {2}",
                            //     req, pathRes, Data);
                        //}
                        // a walk AdvancePath failed stays failed (see MotionSys.HasReachedNextNode)
                        if (!Progress.HasFailed) {
                            Clear(true);
                        }
                    }
                }
            }
        }

        public void TryUnstuck(bool isGrounded) {
            var pos = Position.Idx;
            // check stuckage
            if (isGrounded) {
                if (pathSys.IsWalkable(pos)) { return; }
            } else {
                if (pathSys.IsPassable(pos)) { return; }
            }
            var obj = EntityUtils.ParentObjAt(pos);
            if (obj != null && obj.IsConstructed && obj.Transform.HasAnyPort) {
                // teleport to workspot to unstuck
                D.ErrSoft("Unstucking {0} on top of {1}", Being, obj);
                TeleportTo(EntityUtils.CenterOf(obj.Transform.AnyPortOutFirst));
                return;
            }
            var wall = obj ?? EntityUtils.WallAt(pos);
            int maxDist = 3;
            if (wall == null || !wall.IsConstructed) {
                if (!isGrounded) {
                    return;
                } else {
                    if (pathSys.IsPassable(pos)) {
                        // we are stuck in space, let's allow grabbing the edge
                        maxDist = 1;
                    }
                }
            }
            // find closest non-wall around this position and drop the being there
            bool gotUnstuck = false;
            // try floors
            for (int i = 1; i <= maxDist; i++) {
                Pos.ForEachAroundRandom(Rng.Unseeded, pos, i, false, true, (p) => {
                    if (pathSys.IsWalkable(p)) {
                        TeleportTo(EntityUtils.CenterOf(p));
                        gotUnstuck = true;
                    }
                    return !gotUnstuck;
                });
                if (gotUnstuck) { return; }
            }

            if (maxDist > 1) {
                // try space, but only if it's not grounded being stuck in a passable position
                for (int i = 1; i <= maxDist; i++) {
                    Pos.ForEachAroundRandom(Rng.Unseeded, pos, i, false, true, (p) => {
                        if (pathSys.IsPassable(p)) {
                            TeleportTo(EntityUtils.CenterOf(p));
                            gotUnstuck = true;
                        }
                        return !gotUnstuck;
                    });
                    if (gotUnstuck) { return; }
                }
            }
        }

        private PathRes TrimPath(PathRes pathRes) {
            // D.Warn("Path before trim: {0} | {1}", EntityUtils.DumpPath(Path), pathRes);
            if (pathRes.Length < 3) {
                WantedPathPct = 0f;
            } else {
                var newLength = (int) math.ceil(pathRes.Length * WantedPathPct);
                if (newLength < 3) {
                    newLength = 3;
                }
                // never cut it short in a doorway or on a sandbag: go on past it
                // (PathSys.MayStopAt), or the walk ends somewhere it must leave again
                while (newLength < Path.Count && pathSys.IsPassThroughOnly(Path[newLength - 1])) {
                    newLength++;
                }
                WantedPathPct = 0f;
                for (int i = Path.Count - 1; i >= 0; i--) {
                    if (i >= newLength) {
                        Path.RemoveAt(i);
                    } else {
                        break;
                    }
                }

                pathRes.Length = newLength;
                // set the new goal
                pathRes.Goal = Path[^1];
            }
            // D.Warn("Path after trim: {0} | {1}", EntityUtils.DumpPath(Path), pathRes);

            return pathRes;
        }

        public void SetVelocity(float pct) {
            if (pct < 0.15f) { pct = 0.15f; }
            Velocity = MaxVelocity * pct;
            VelocityPercent = pct;
        }

        /// <param name="targetPos">If this is invalid, distance will be considered as 10</param>
        /// <returns></returns>
        public float GoToCost(int targetPos) {
            float sqrDist;
            #if DEBUG_COST
            float dist;
            #endif
            if (Pos.IsOOB(targetPos)) {
                sqrDist = 100f;
                #if DEBUG_COST
                dist = 10f;
                #endif
            } else {
                sqrDist = Pos.SqrDistance(Position.Idx, targetPos);
                #if DEBUG_COST
                dist = Pos.Distance(Position.Idx, targetPos);
                #endif
            }
            if (sqrDist < 25f) {
                sqrDist = 25f;
                #if DEBUG_COST
                dist = 5f;
                #endif
            }
            if (IsImmobilized || MaxVelocity < 0.0001f) {
                return 10000000f;
            }
            float cost;
            if (IsGrounded) {
                cost = sqrDist * 0.1f / MaxVelocity;// (5f / MaxVelocity) * sqrDist;
                #if DEBUG_COST
                D.Warn("GoTo cost for grounded dist: {0} => {1}", dist, cost);
                #endif
            } else {
                cost = sqrDist * 0.05f / MaxVelocity; //(2f / MaxVelocity) * sqrDist;
                #if DEBUG_COST
                D.Warn("GoTo cost for flying dist: {0} => {1}", dist, cost);
                #endif
            }
            return cost;
        }

        public void ExitTeleporter() {
            teleporter.Exit(Being);
            teleporter = null;
        }

        public void ShowPathViz(bool updateOnly) {
            if (updateOnly && !isShowingPathViz) { return; }
            if (!Progress.IsMoving) {
                // A stop mid-path (a drafted shooter halting once its target is in range)
                // clears the path but leaves the drawn one behind until hidden here
                if (isShowingPathViz) {
                    HidePathViz();
                }
                return;
            }

            if (!Result.IsValid || Data.HasStateFlag(BeingState.Dead)) {
                if (isShowingPathViz) {
                    HidePathViz();
                }
                return;
            }
            if (pathViz == null) {
                pathViz = Guidelines.Create("PathGuideline");
            }
            isShowingPathViz = true;
            if (!pathViz.ShowPath(Path, PathIdx - 1,
                    Being.SelectPosition, Consts.BeingPathColor)) {
                HidePathViz();
            }
        }

        public void HidePathViz() {
            pathViz?.Dispose();
            isShowingPathViz = false;
        }

        public void Animate(float dist) {
            Being.Graphics.ChangeAnimationFrame(dist);
        }
        /// <returns>false if being floated too far and was eliminated</returns>
        public bool UpdateInSpace(bool validateImmobilization) {
            bool wasInSpace = HasFlags(MotionFlags.InSpace);
            if (EntityUtils.IsWithinBounds(Position.Exact)) {
                var isInSpace = EntityUtils.IsEmptySpaceAt(Position.Idx);
                if (isInSpace) {
                    // if (!Flags.HasFlag(MotionFlags.InSpace)) {
                    //     D.Warn("Setting in space! {0}", Being);
                    // }
                    Flags |= MotionFlags.InSpace;
                    if (!wasInSpace && IsGrounded) {
                        if (!TryStepAside()) {
                            // we are now immobilizing in "checkNeedRescue" in motion control
                            // Immobilize(ImmobilizeReasonIdH.GroundedInSpace, true);
                        }
                    }
                } else {
                    Flags &= ~MotionFlags.InSpace;
                    if (wasInSpace && IsGrounded) {
                        // still need to remove the immobilize reason just in case
                        Immobilize(ImmobilizeReasonIdH.GroundedInSpace, false);
                    } else if (validateImmobilization && IsGrounded && ImmobilizeReasons != null) {
                        if (Being.Carrier == null && ImmobilizeReasons.Remove(ImmobilizeReasonIdH.GroundedInSpace)) {
                            D.Err("Incorrectly marked as grounded in space (fixing)! {0}", Being);
                        }
                    }
                }
                return true;
            } else {
                Flags |= MotionFlags.InSpace;
                D.Err("Being is OOB, eliminating: {0}", Being.Data, Being);
                BeingUtils.Eliminate(S, Being.Data, T.DeathAbandonedInSpace, G.Ticks);
                return false;
            }
        }

        public void SetPosIdxOverride(int pos) {
            if (Being.Motion.IsBusy) {
                D.Err("Setting position override for {0} / {1} while being is going somewhere!",
                    Being, Being.AIAgent.CurrentAction);
            }
            posIdxOverride = pos;
            if (pos != Position.Idx) {
                TeleportTo(Position.Exact);
            }
        }
        public void ClearPosIdxOverride() {
            if (posIdxOverride == 0) { return; }
            posIdxOverride = 0;
            TeleportTo(Position.Exact);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasFlags(MotionFlags flags) {
            return (Flags & flags) == flags;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasFlagsAny(MotionFlags flags) {
            return (Flags & flags) != MotionFlags.None;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasProgressFlag(MotionProgressFlags flags) {
            return (Progress.Flags & flags) == flags;
        }

        public void UpdatePosIdx(int pos, int dx, int dy) {
            if (posIdxOverride != 0) {
                posIdxOverride = Pos.NeighborAt(posIdxOverride, dx, dy);
            }
            TeleportTo(Position.Exact + new Vector2(dx, dy));
        }

        public void EnqueueDirt(string type) {
            enqueuedDirt = type;
        }

        public void SpawnDirt() {
            if (!IsGrounded || (enqueuedDirt == null &&
                    !Rng.UChance(MotionSys.GroundedDirtChance))) {
                return;
            }
            if (!EntityUtils.IsEmptyFloorAt(Position.Idx)) {
                return;
            }
            if (enqueuedDirt == null) {
                enqueuedDirt = Images.RandomMatching(
                    GraphicsId.DirtDirtMark01, 2);
            }
            S.Sys.Dirt.Add(DirtData.Create(Being, enqueuedDirt));
            enqueuedDirt = null;
        }

        public void SetBeingData(BeingData beingData) {
            Data = beingData;
            if (beingData.LastPosition.IsValid) {
                Position = beingData.LastPosition;
                Being.PosIdx = Position.Idx;
                Being.Position = Position.Exact;
                Being.SelectPosition = Position.Exact;
                if (!IsInQuadTree) {
                    S.Beings.AfterMotionInitialized(Being);
                }
            } else {
                D.Err("Setting being data: {0}", beingData.LastPosition);
            }
            if (beingData.StatsBeing != null) {
                statMoveSpeed = Data.StatsBeing.MoveSpeed;
                MaxVelocity = statMoveSpeed.Value;
            }
            UpdateNavPermissions();
            S.Clock.OnNextUpdate(Initialize);
        }

        public void StoreNavFailureAt(int pos,
                int forgetAfterTicks = Consts.TicksPerHour) {
            if (pos < 1) {
                D.Err("Trying to store nav failure at position {0}! {1} / {2}", pos,
                    Comp.Being.AIAgent.Goal, Comp.Being.AIAgent.CurrentAction);
                return;
            }
            if (forgetAfterTicks == 0) {
                D.Err("Trying to store nav failure with forget after ticks == 0!");
            }
            // D.Log("Storing nav failure at {0} => {1}", Pos.String(pos), Being);
            // #if !PRODUCTION_BUILD
            // if (NavFailureMemory.TryGetValue(pos, out var ticks)) {
            //     if (ticks.Item1 == G.Ticks) {
            //         D.Err("Storing nav failure twice while we already stored one to same spot! {0} | {1}",
            //             Pos.String(pos), Being);
            //     }
            // }
            // #endif
            NavFailureMemory[pos] = (G.Ticks, G.Ticks + forgetAfterTicks);
        }
        public bool HadNavFailureAt(int posIdx) {
            if (posIdx < 1) {
                D.Err("Trying to check if had nav failure at position {0}! {1} | {2}",
                    Pos.StringD(posIdx), Data, Being.AIAgent.CurrentAction);
                return false;
            }
            if (NavFailureMemory.TryGetValue(posIdx, out var when)) {
                if (IsFlying && when.Item1 < S.Vars.LastAreaChangeTick) {
                    // D.Warn("Clearing nav fail mem due to area change at {0}", D.Pos(posIdx));
                    NavFailureMemory.Remove(posIdx);
                } else if (G.Ticks > when.Item2) {
                    // D.Warn("Clearing nav fail mem due to timeout at {0}. Start: {1}, End: {2}, Now: {3}",
                    //     D.Pos(posIdx),
                    //         Units.TicksToTime(when.Item1),
                    //         Units.TicksToTime(when.Item2),
                    //         Units.TicksToTime(G.Ticks));
                    NavFailureMemory.Remove(posIdx);
                } else {
                    // D.Log("Checking if had nav failure at {0} => true. {1}",
                    //     Pos.StringD(posIdx), Being);
                    return true;
                }
            }
            // D.Log("Checking if had nav failure at {0} => false. {1}", Pos.String(posIdx), Being);
            return false;
        }

        public void CheckNeedForRescue(long ticks) {
            if (!IsGrounded) { return; }
            if (Being.IsDead) { return; }
            if (HasFlags(MotionFlags.Carried)) {
                return;
            }
            // immobilize only if being is in mid rescue
            if (Being.Goal?.JobIdH == JobTypeIdH.Rescue && Being.Goal.State == AIGoalState.Pending) {
                if (!IsImmobilized && TicksInCurrentPosition > Consts.TicksPer2Hours) {
                    Immobilize(ImmobilizeReasonIdH.GroundedInSpace, true);
                }
                return;
            }
            if (!Being.AIAgent.IsIdle) {
                // agent is busy, perhaps we can check later
                if (!Data.IsFriendly) {
                    if (!IsImmobilized
                            && (Flags & MotionFlags.InSpace) == MotionFlags.InSpace
                            && TicksInCurrentPosition > Consts.TicksPer2Hours) {
                        Immobilize(ImmobilizeReasonIdH.GroundedInSpace, true);
                    }
                }
                return;
            }
            // check for island of size 1
            if ((Flags & MotionFlags.InSpace) == MotionFlags.None) {
                if (Being.Goal == null && Being.Data.IsFriendly
                        && S.Sys.Areas.IslandSizeAt(Position.Idx) == 1) {
                    CreateRescueGoal();
                }
                return;
            }
            if (Progress.HasFailed && Progress.GoalPosIdx > 0) {
                StoreNavFailureAt(Progress.GoalPosIdx, Consts.TicksPer3Hours);
            }
            // Couldn't find a ledge to cling to
            if (!TryStepAside() && Being.Goal == null) {
                if (Data.IsFriendly) {
                    CreateRescueGoal();
                } else {
                    if (!IsImmobilized && TicksInCurrentPosition > Consts.TicksPer2Hours) {
                        Immobilize(ImmobilizeReasonIdH.GroundedInSpace, true);
                    }
                }
            }
        }

        ///<summary>Move within the pos being is in (i.e when eating near the table)
        public void MoveNearbyAndFace(int posIdx) {
            var curPos = Position.Idx;
            var curPosCenter = Pos.ToVector(curPos);
            var facedPosCenter = Pos.ToVector(posIdx);

            // We are above, don't got down or it will look like shit
            if (posIdx == Pos.S(curPos)) {
                //Vector2.Lerp(curPosCenter, facedPosCenter, 0f);
                Position.Exact = curPosCenter;
                Being.Graphics.SetFacing(Facing.Type.Down);
            // We are below, we can move up pretty close
            } else if (posIdx == Pos.N(curPos)) {
                Position.Exact = Vector2.Lerp(curPosCenter, facedPosCenter, 0.6f);
                Being.Graphics.SetFacing(Facing.Type.Up);
            } else {
                // We are on the side (E or W)
                Position.Exact = Vector2.Lerp(curPosCenter, facedPosCenter, 0.25f)
                        + new Vector2(0, 0.25f);
                if (posIdx == Pos.E(curPos)) {
                    Being.Graphics.SetFacing(Facing.Type.Right);
                    Being.Graphics.SetRotation(Rng.URange(-5f, -10f), false);
                } else {
                    Being.Graphics.SetFacing(Facing.Type.Left);
                    Being.Graphics.SetRotation(Rng.URange(5f, 10f), false);
                }
            }
            Being.PosIdx = Position.Idx;
            Being.Position = Position.Exact;
            Graphics.Update(Position.Exact);
        }

        public void SetForcedWalk(bool directOrder = false) {
            if (IsBusy) {
                Flags |= MotionFlags.ForcedWalk;
            }
            if (directOrder) {
                Being.AIAgent.DirectControl.RecordDirectOrderActivity();
            }
        }

        private int lastStepAsidePos;

        public bool TryStepAside() {
            // before creating rescue goal, let's try to grab a ledge and save
            // ourselves
            int found = Pos.Around(Position.Idx, Caches.PosIdx9, false);
            int bId = Being.Id;
            for (int i = 0; i < found; i++) {
                var pos = Caches.PosIdx9[i];
                if (pos == lastStepAsidePos) { continue; }
                if (Data.HasCondition(ConditionIdH.Sleeping) || Data.HasStateFlag(BeingState.InDevice)) {
                    continue;
                }
                if (pathSys.IsPassable(pos) && !pathSys.IsClaimed(pos, bId)
                        && !pathSys.IsPassThroughOnly(pos)
                        && !HadNavFailureAt(pos) && TryGoTo(pos, PathReqFlags.AdjOnTop)) {
                    // if we already tried that one, let's try another
                    lastStepAsidePos = pos;
                    // prevent AI from assigning a task while we are doing it
                    SetForcedWalk();
                    // found a ledge we can climb
                    // D.Warn("Will Step Aside: {0}, {1}", Pos.StringFromTo(Position.Idx, pos), Being);
                    return true;
                }
            }
            return false;
        }
        public void UpdatePermissions() {
            var data = Being.Data;
            data.StatsBeing.AIAlignment.Apply();
            var friendly = data.IsFriendly;
            SetPermission(NavPermission.Enemy, !friendly);
            SetPermission(NavPermission.Friendly, friendly);
            SetPermission(NavPermission.Guest, data.IsGuest);
        }
        public void Initialize() {
            // D.Warn("Initializing motion: {0}", Being);
            if (!Being.IsActive) {
                // A being stashed hidden in the same call it was created in (RaidPayload:
                // raiders carried inside hullpiercers/capsules) is gone again before this
                // deferred init runs. That is the intended lifecycle, not a leak.
                if (Being.HasData && Being.Data.HasStateFlag(BeingState.Hidden)) {
                    return;
                }
                // BeingUtils.Recreate swaps the Being around the same BeingData - losing a mind
                // holding part and getting one back both do it. The discarded instance still
                // carries the init SetBeingData queued for it, and arrives here as a husk the
                // data has already moved on from. Also the intended lifecycle, not a leak.
                if (Being.HasData && Being.Data.Being != Being) {
                    return;
                }
                D.Err("Trying to initialize motion for an inactive being: {0}", Being);
                return;
            }
            statMoveSpeed = Data.StatsBeing.MoveSpeed;
            MaxVelocity = statMoveSpeed.Value;
            TeleportTo(Position.Exact);
            if (ClaimedPosIdx != Position.Idx) {
                // TryClaimPos would make people in proximity scatter on load
                // instead we'll claim a position without actually claiming it
                // Warning: this may have side effects for multiple beings who
                // get spawned in one spot
                // ClaimedPosIdx = Position.Idx;
                // IT HAD SIDE EFFECTS! Don't do it

                // CANNOT DO WITHOUT IT because it fucks up surgeries otherwise
                TryClaimPos(Position.Idx);
            }
            Being.Graphics.RebuildLooks();
            UpdatePermissions();
        }

        private void CreateRescueGoal() {
            Comp.CreateRescueGoal();
        }

        // Position.Idx follows the exact position, so a being that walked up to a closed door
        // can already stand in the door's tile. Re-ordered there, its new path starts beyond the
        // door, and the door ahead is never checked: it still has to get past the leaf at the
        // tile's center (STA-1796). Heading back the way it came crosses nothing.
        public bool IsCrossingClosedDoor(out DoorComp door) {
            var doors = S.Sys.Areas.DoorCheck?.DoorsByPos;
            if (doors == null || !doors.TryGetValue(Position.Idx, out door) || door.IsOpen) {
                door = null;
                return false;
            }
            var leaf = EntityUtils.CenterOf(Position.Idx);
            if (Vector2.Dot(leaf - Position.Exact, Position.Next - leaf) > 0f) {
                return true;
            }
            door = null;
            return false;
        }

        public bool CheckSafety() {
            if (CurrentDoor != null && CurrentDoor.Tile.PosIdx == Position.Idx) {
                // ignore safety inside doors
                return true;
            }
            if ((Data.EnvFlags & BeingEnvFlags.Safe) == BeingEnvFlags.Safe) {
                wasSafeBefore = true;
            } else {
                // A SurvivableOK trip (firefighting) is planned through tiles that are
                // hotter than TempMax on purpose, and never sets Progress.IsUnsafe,
                // so it needs the same tolerance AdvancePath applies per step.
                if (wasSafeBefore && !Progress.IsUnsafe
                        && !(Request.HasFlag(PathReqFlags.SurvivableOK)
                            && Data.Safety.IsActuallySurvivableAt(Position.Idx))) {
                    Fail(T.InfoUnsafeConditions);
                    D.Warn("Failing motion due to Being unsafe at {0}: {1}: {2}",
                        Pos.String(Position.Idx), Data.EnvFlags, Being);
                    return false;
                }
            }
            return true;
        }

        public void ClearNavFailureAt(int gPos) {
            // D.Warn("Removing nav failure at: {0}", gPos);
            NavFailureMemory?.Remove(gPos);
        }

        public void UpdateMaxVelocity() {
            TicksStill = 0;
            MaxVelocity = statMoveSpeed.Value;
            if (MaxVelocity == 0 && !IsImmobilized && !Data.Body.IsInjured) {
                var stats = Data.StatsBeing;
                D.Warn("Non-immobilized being has max velocity of 0! {0} | {1} | {2} | {3} | {4}",
                    statMoveSpeed, stats.Sight, stats.LungEfficiency,
                    Data, Data.Body);
                MaxVelocity = 0.1f;
            }
        }

        public bool UpdateRiding() {
            if (!HasFlags(MotionFlags.Riding)) { return false; }
            if (Being.Graphics.SpriteSet != null) {
                TeleportTo(Being.Graphics.SpriteSet.PositionLerped);
            }
            return true;
        }

        public bool IsImmobilizedBy(int reasonId) {
            return ImmobilizeReasons != null && ImmobilizeReasons.Contains(reasonId);
        }

        public void Immobilize(int reasonId, bool add) {
            // D.Warn("Immobilizing: {0} -> {1} -> {2}", Being, ImmobilizeReasonIdH.ResolveName(reasonId), add);
            bool wasImmobilized = !Arrays.IsEmpty(ImmobilizeReasons);
            if (add) {
                (ImmobilizeReasons ??= new()).Add(reasonId);
            } else {
                ImmobilizeReasons?.Remove(reasonId);
            }
            bool? needParticles = null;
            if (Arrays.IsEmpty(ImmobilizeReasons)) {
                Flags &= ~MotionFlags.Immobilized;
                if (wasImmobilized) {
                    if (Being.IsAlive && !Being.Data.Body.IsIncapacitated) {
                        Comp.Transmit(Comp, MsgIdH.ImmobilizeEnded);
                    }
                    needParticles = true;
                }
            } else {
                Flags |= MotionFlags.Immobilized;
                if (!wasImmobilized) {
                    if (Being.IsAlive && !Being.Data.Body.IsIncapacitated) {
                        Comp.Transmit(Comp, MsgIdH.ImmobilizeStarted);
                    }
                    needParticles = false;
                }
            }
            if (needParticles != null) {
                if (Comp.Being.TryGetComponent<ParticleTrailComp>(out var tr)) {
                    if (needParticles == true) {
                        tr.SetVisible();
                    } else {
                        tr.SetHidden();
                    }
                }
            }
            UpdateInfoIcon();
        }

        public void UpdateInfoIcon() {
            if (IsImmobilized) {
                var body = Data.Body;
                if (body.IsAlive && !body.IsIncapacitated) {
                    int cnt = ImmobilizeReasons.Count;
                    if (cnt > 1 || !ImmobilizeReasons.Contains(ImmobilizeReasonIdH.Waiting)) {
                        // Don't show immobilized icon for those who are waiting for being petted, etc
                        var sb = Caches.StringBuilder2;
                        sb.Clear();
                        sb.Append(T.NavImmobilized);
                        sb.Append(": ");
                        int i = 0;
                        foreach (var r in ImmobilizeReasons) {
                            var name = NameForImmobilizeReason(r);
                            sb.Append(name);
                            if (++i < cnt) {
                                sb.Append(", ");
                            }
                        }
                        Comp.ShowInfoIcon(IconId.CImmobilized, sb.ToString());
                    }
                } else {
                    Comp.HideInfoIcon();
                }
            } else {
                Comp.HideInfoIcon();
            }
        }
        public static string NameForImmobilizeReason(int reasonId) {
            switch (reasonId) {
                case ImmobilizeReasonIdH.NoAbility: return T.AdjIncapable;
                case ImmobilizeReasonIdH.Disabled: return T.BeingStateIncapacitated;
                case ImmobilizeReasonIdH.Dead: return T.Dead;
                case ImmobilizeReasonIdH.OnFire: return T.CondOnfire;
                case ImmobilizeReasonIdH.Carried: return T.CarryingBeing;
                case ImmobilizeReasonIdH.Discharged: return T.BatteryDischarged;
                case ImmobilizeReasonIdH.GroundedInSpace: return T.NavGrounded;
                case ImmobilizeReasonIdH.Waiting: return T.WaitingForRescue;
                case ImmobilizeReasonIdH.Operation: return T.JobTypeMedical;
            }
            var name = ImmobilizeReasonIdH.ResolveName(reasonId);
            D.Err("Immobilize reason id does not have a case in name: {0}", name);
            return name;
        }

        public void DumpNavFailMemory(StringBuilder sb) {
            var t = G.Ticks;
            bool addedHeader = false;
            if (NavFailureMemory == null) {
                sb.AppendLine("No Nav Failures");
                return;
            }
            foreach (var mem in NavFailureMemory) {
                var pos = mem.Key;
                var added = mem.Value.Item1;
                var expires = mem.Value.Item2;
                if (t > expires) {
                    continue;
                }
                if (!addedHeader) {
                    addedHeader = true;
                    sb.Append("Nav Fail Mem:\n");
                }
                sb.AppendFormat(" - {0} (added: {1}, expires in: {2})\n",
                    Pos.String(pos),
                    Units.TicksAgoFromNow(added),
                    Units.TicksToTime(expires - G.Ticks));
            }
        }

        public void CheckClaimedPos() {
            var d = Being.Data;
            if (Being.Carrier == null
                    && d.HasStateFlag(BeingState.Alive)
                    && !d.HasCondition(ConditionIdH.Sleeping)
                    && !d.HasStateFlag(BeingState.InDevice)
                    && !d.Body.IsIncapacitated) {
                if (ClaimedPosIdx != Pos.Invalid && ClaimedPosIdx == Position.Idx
                        && !Progress.IsMoving && !IsImmobilized
                        && !pathSys.MayStopAt(ClaimedPosIdx, Being)) {
                    // done with the task that put it in a doorway or on a sandbag: nobody
                    // lingers there (PathSys.MayStopAt)
                    ReleaseClaimedPos();
                    TryStepAside();
                    return;
                }
                if (ClaimedPosIdx == Pos.Invalid && !Progress.IsMoving) {
                    #if DEBUG_CLAIMS
                    D.ErrSoft("Not moving and not claimed position, trying to claim current: {0} | {1}",
                        Pos.String(Position.Idx), Being);
                    #endif
                    if (!TryClaimPos(Position.Idx)) {
                        if (!IsImmobilized) {
                            #if DEBUG_CLAIMS
                            D.ErrSoft("Failed to claim current pos, need to GTFO ASAP | {0}", Being);
                            #endif
                            if (!TryStepAside()) {
                                // if (!AIActionIdleRoam.TryRoam(Being.AIAgent, false, 2)) {
                                    #if DEBUG_GTFO
                                    if (!The.TestsAreRunning) {
                                        if (!IsGrounded || !EntityUtils.IsEmptySpaceAt(Position.Idx)) {
                                            D.ErrSoft("Failed to GTFO: {0} -> {1}", Pos.String(Position.Idx), Being);
                                        }
                                    }
                                    #endif
                                // }
                            }
                        }
                    }
                }
            } else {
                if (ClaimedPosIdx != Pos.Invalid) {
                    ReleaseClaimedPos();
                }
            }
        }

        public static readonly MotionProgress Error_LastNavNotDone = new MotionProgress() {
            Flags = MotionProgressFlags.Failed,
            FailReason = T.NavFailLastNavNotDone,
        };
        public static readonly MotionProgress Error_Immobilized = new MotionProgress() {
            Flags = MotionProgressFlags.Failed,
            FailReason = T.NavFailBeingImmobilized,
        };
        public static readonly MotionProgress Error_TargetUnavailable = new MotionProgress() {
            Flags = MotionProgressFlags.Failed,
            FailReason = T.AdRejTargetUnavailable,
        };
        public static readonly MotionProgress Error_UnreachableGrounded = new MotionProgress() {
            Flags = MotionProgressFlags.Failed,
            FailReason = T.NavFailUnreachableGrounded,
        };
        public static readonly MotionProgress Error_UnreachableFlying = new MotionProgress() {
            Flags = MotionProgressFlags.Failed,
            FailReason = T.NavFailUnreachableFlying,
        };
        public static readonly MotionProgress Error_Blocked = new MotionProgress() {
            Flags = MotionProgressFlags.Failed,
            FailReason = T.NavFailObjectBlockedByAdjacent,
        };
    }
}
