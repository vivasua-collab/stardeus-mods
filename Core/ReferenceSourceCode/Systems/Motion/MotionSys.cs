#if !PRODUCTION_BUILD
// #define DEBUG_MOTION
#endif
using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.Areas;
using Game.Systems.Creatures;
using Game.Utils;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Systems.Motion {
    public sealed class MotionSys : GameSystem {
        public const string SysId = "MotionSys";
        public override string Id => SysId;
        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new MotionSys());
        }

        public readonly List<MotionControl> Movables = new();
        private DoorConnectionCheck doorCheck;
        private float navSpeedTunable;
        public static float GroundedDirtChance;

        public override void Unload() {
            Movables.Clear();
        }

        protected override void OnInitialize() {
            S.Sig.AfterLoadState.AddListener(OnLoadState);
            S.Query.RegisterMovable.SetResponder(RegisterMovable);
            S.Query.UnregisterMovable.SetResponder(UnregisterMovable);
            navSpeedTunable = Tunables.NavigationSpeed;
            GroundedDirtChance = Tunables.GroundedBeingDirtSpawnChance;
        }

        private bool UnregisterMovable(MotionControl control) {
            return Movables.Remove(control);
        }

        private MotionControl RegisterMovable(MotionComp comp) {
            var movable = new MotionControl(S);
            movable.Comp = comp;
            Movables.Add(movable);
            return movable;
        }

        private void OnLoadState(GameState s) {
            GameTimer.ResetFPS();
            s.Clock.OnTick.AddListener(OnTick);
            s.Clock.OnTickProgress.AddListener(OnTickProgress);
            doorCheck = s.Sys.Areas.DoorCheck;
            if (doorCheck == null) {
                D.Err("Door check is null!");
            }
        }

        private void OnTick(long ticks) {
            bool checkRescue = ticks % Consts.TicksPer15Minutes == 4;
            #if !PRODUCTION_BUILD
            if (The.TestsAreRunning) {
                checkRescue = true;
            }
            #endif
            for (int i = Movables.Count - 1; i >= 0; i--) {
                MotionControl mov = Movables[i];
                mov.CheckClaimedPos();
                if (!mov.Being.IsActive)  {
                    D.Err("Inactive being's movable is registered! {0}. {1}",
                        mov.Being, mov.Being.Data);
                    continue;
                }
                if (!mov.UpdateInSpace(checkRescue)) {
                    continue;
                }
                mov.TicksInCurrentPosition++;
                // 1. validations
                if (mov.Progress.IsMoving) {
                    if (mov.TicksInCurrentPosition < Consts.TicksPer3Minutes) {
                        if (mov.Being.Goal?.JobIdH == JobTypeIdH.Rescue) {
                            AIGoal.Cancel(mov.Being.Goal, ticks);
                        }
                    }
                    mov.Data.Counters.Increment(CounterIdH.TicksMovingTotal);
                    mov.Data.Counters.Increment(CounterIdH.TicksMovingMonth);
                } else {
                    if (mov.HasFlags(MotionFlags.ForcedWalk)) {
                        D.Err("Agent has a ForcedWalk flag while still! Last goal: {0}/{1}",
                            mov.Being.AIAgent.LastCompletedGoal, mov.Being.AIAgent.LastFailedGoal);
                        mov.Flags &= ~MotionFlags.ForcedWalk;
                    }
                    if (checkRescue || mov.Progress.HasFailed) {
                        mov.CheckNeedForRescue(ticks);
                    }
                    mov.TicksStill++;
                    mov.DistCoveredThisTick = 0f;
                    continue;
                }
                CheckDoor(mov);
                if (mov.HasProgressFlag(MotionProgressFlags.Blocked)) {
                    mov.TickBlocked();
                }

                // mov.ShowPathViz(updateOnly: true);
                // 2. velocity change (in progress?)
                mov.UpdateMaxVelocity();
                // mov.ShowPathViz(updateOnly: true);
                // stillness checks
                // teleporter wait delay
                if (mov.TeleporterDelay > 0) {
                    mov.TeleporterDelay--;
                    mov.DistCoveredThisTick = 0f;
                    if (mov.TeleporterDelay == 0) {
                        mov.ExitTeleporter();
                    }
                }
            }
        }

        private void CheckDoor(MotionControl mov) {
            // Do we have a door in front of us?
            var nextIdx = mov.Position.NextIdx;
            if (nextIdx < 1) { return; }
            if (doorCheck.DoorsByPos.TryGetValue(mov.Position.NextIdx, out var door)) {
                if (!door.IsOpen) {
                    mov.CurrentDoor = door;
                    if (!TryOpenDoor(mov, door)) {
                        mov.Fail(T.Door);
                    }
                }
            } else if (mov.IsCrossingClosedDoor(out door)) {
                mov.CurrentDoor = door;
                // Locked or denied: let it out the way it is heading rather than trap it inside
                if (!TryOpenDoor(mov, door)) {
                    mov.CurrentDoor = null;
                }
            } else {
                mov.CurrentDoor = null;
            }
        }

        private static bool TryOpenDoor(MotionControl mov, DoorComp door) {
            if (!door.TryOpen(mov)) { return false; }
            if (!door.IsOperational && Rng.UChance(0.5f)) {
                mov.Being.Graphics.Punch(
                    door.Entity.Position - mov.Position.Exact,
                        0.5f, 0.1f, 0.1f);
            }
            return true;
        }

        /// <summary>
        /// pos idx -> top speed of the unit in that pos
        /// </summary>
        private readonly Dictionary<int, float> motionCongestionPoints = new();

        private void OnTickProgress(float progress) {
            var tpf = S.Clock.TicksPerUpdate;
            // D.FIXME("Implement speed shift when multiple units are in the same position moving in the same direction");
            motionCongestionPoints.Clear();
            for (int i = Movables.Count - 1; i >= 0; i--) {
                MotionControl mov = Movables[i];
                if (UpdateMotion(tpf, mov, out int curPos)) {
                    if (!motionCongestionPoints.TryGetValue(curPos, out var vel)) {
                        vel = 0f;
                    }
                    motionCongestionPoints[curPos] = Math.Max(vel, mov.MaxVelocity);
                }
            }
        }

        /// <param name="tpf">ticks per frame</param>
        /// <param name="mov"></param>
        /// <returns>true if being is moving</returns>
        private bool UpdateMotion(float tpf, MotionControl mov, out int curPosIfMoving) {
            if (mov.UpdateRiding()) {
                curPosIfMoving = Pos.Invalid;
                return false;
            }
            if (mov.Path.Count == 0) {
                curPosIfMoving = Pos.Invalid;
                return false;
            }
            if (mov.TeleporterDelay > 0) {
                curPosIfMoving = Pos.Invalid;
                return false;
            }
            var rm = 1f;
            if (mov.CombatSlowdown > 0f) {
                rm *= mov.CombatSlowdown;
            }

            var pos = mov.Position;
            if (mov.IsGrounded) {
                // walking over a sandbag and the like (PassabilityComp.MoveSpeedMul)
                var obj = EntityUtils.ParentObjAt(pos.Idx);
                if (obj?.Passability != null && obj.Passability.MoveSpeedMul < 1f) {
                    rm *= obj.Passability.MoveSpeedMul;
                }
            }

            float velPct;
            if (mov.HasProgressFlag(MotionProgressFlags.Accelerating)) {
                // dist to goal
                float distToNext = Vectors.Distance(pos.Exact, pos.Next);
                velPct = math.lerp(1f, 0.5f, distToNext * 0.7f);
            } else if (mov.HasProgressFlag(MotionProgressFlags.Decelerating)) {
                float distToNext = Vectors.Distance(pos.Exact, pos.Next);
                velPct = math.lerp(0.5f, 1f, distToNext * 0.7f);
            } else {
                velPct = 1.0f;
            }
            // we are moving through a congestion point, let's vary the speed
            // first person in the congestion will be normal speed, others will yield and wait if they are same speed or slower
            if (motionCongestionPoints.TryGetValue(pos.Idx, out float maxVel)) {
                if (mov.MaxVelocity <= maxVel) {
                    float velPctAdj;
                    if (mov.HasProgressFlag(MotionProgressFlags.IgnoreCongestion)) {
                        var noise = Rng2.ZeroToOne.Noise2D(
                                (int) mov.Being.Position.x,
                                (int) mov.Being.Position.y, Primes.Prime007);
                        noise -= 0.5f;
                        noise *= 0.1f;
                        velPctAdj = 1f + noise;
                    } else {
                        velPctAdj = 0.1f + Rng2.ZeroToOne.Noise1D(mov.Being.Id, Primes.Prime007) * 0.1f;
                        // D.Warn("Adjusting congested velocity at {0}: {1} *= {2} | {3}",
                        //     Pos.String(pos.Idx), velPct, velPctAdj, mov.Being);
                    }
                    velPct *= velPctAdj;
                }
            }
            mov.SetVelocity(velPct);


            // Do we have a door in front of us?
            if (mov.CurrentDoor?.IsOpen == false) {
                // wait for the door to converge
                curPosIfMoving = Pos.Invalid;
                return false;
            }

            // D.Warn("Moving at with tpf {0} | {1} | {2} | TPU ready: {3}",
            //     tpf, G.ClockSpeed, GameTimer.FPS, S.Clock.IsTPUReady);
            var maxMoveDist = rm * mov.Velocity * tpf * navSpeedTunable;
            maxMoveDist -= mov.DistCoveredThisTick;
            if (maxMoveDist <= 0f) {
                mov.DistCoveredThisTick = 0f;
                curPosIfMoving = pos.Idx;
                return true;
            }

            if (Vectors.SqrDistance(pos.Exact, pos.Next) >= 4f * 4f) {
                if (EntityUtils.ObjAt(Pos.FromVector(pos.Exact))?.Teleporter != null
                        || EntityUtils.ObjAt(Pos.FromVector(pos.Next))?.Teleporter != null) {
                    // all good, inactive teleporters
                    #if DEBUG_MOTIONS
                    D.Err("Failing motion due to inactive teleporter! {7} | {0}->{1}. Req: {2} | Res: {3} | W%: {4} | Path: {5} | being: {6}",
                        pos.Exact, pos.Next, mov.Request, mov.Result,
                        mov.WantedPathPct,
                        EntityUtils.DumpPath(mov.Path), mov.Being, mov.Being.AIAgent.CurrentAction);
                    #endif
                    curPosIfMoving = Pos.Invalid;
                    mov.Fail(T.PathNotPassable);
                    return false;
                } else {
                    D.Err("Trying to walk too far (no teleporters)! {7} | {0}->{1}. Req: {2} | Res: {3} | W%: {4} | Path: {5} | being: {6}",
                        pos.Exact, pos.Next, mov.Request, mov.Result,
                        mov.WantedPathPct,
                        EntityUtils.DumpPath(mov.Path), mov.Being, mov.Being.AIAgent.CurrentAction);
                }
                #if !PRODUCTION_BUILD
                // S.Clock.SetSpeed(ClockSpeed.Stopped);
                // EntityUtils.CameraFocusOn(mov.Being);
                // return;
                #endif
                if (S.Sys.Path.IsTeleporter(pos.Idx, out var posTo)) {
                    D.Err("Teleporter found, auto teleporting to {0}",
                        Pos.StringFromTo(pos.Idx, posTo));
                    maxMoveDist = 1000f;
                } else {
                    D.Err("No teleporter at pos: {0}. Failing motion for {1}", Pos.String(pos.Idx), mov.Being);
                    mov.Fail(T.PathNotPassable);
                    curPosIfMoving = Pos.Invalid;
                    return false;
                }
            }

            var next = Vector2.MoveTowards(pos.Exact, pos.Next, maxMoveDist);
            var dist = Vector2.Distance(pos.Exact, next);

            mov.TeleportTo(next);
            if (mov.IsAnimated) {
                mov.Animate(dist);
            }
            if (HasReachedNextNode(mov)) {
                // We may need to recursively execute the move again if
                // we are running at 10x speed and ticks are more frequent
                // than render frames, otherwise there will be problems
                // with speed differing heavily at 10x vs 1x
                if (!mov.IsBusy) {
                    mov.DistCoveredThisTick = 0f;
                    curPosIfMoving = pos.Idx;
                    return true;
                }
                if (mov.HasProgressFlag(MotionProgressFlags.Blocked)) {
                    // standing until the tile ahead clears: nothing left to walk this frame
                    mov.DistCoveredThisTick = 0f;
                    curPosIfMoving = pos.Idx;
                    return true;
                }
                CheckDoor(mov);
                var diff = maxMoveDist - dist;
                // D.Warn("TPF: {0}. Dist this tick: {1}, max dist: {2}. Frame: {3}: Clock: {4}",
                //     tpf, mov.DistCoveredThisTick, maxMoveDist, Time.frameCount, G.ClockSpeed);
                if (diff > 0.0000001f) {
                    if (tpf > 1f) {
                        #if !PRODUCTION_BUILD
                        D.Warn("TPF is too high for recursive move! {0}; {1}", tpf, G.ClockSpeed);
                        #endif
                        tpf = 1f;
                    }
                    // D.Warn("Doing extra update motion this tick: {0}", mov.DistCoveredThisTick);
                    mov.DistCoveredThisTick += dist;
                    return UpdateMotion(tpf, mov, out curPosIfMoving);
                }
            }
            mov.DistCoveredThisTick = 0f;
            curPosIfMoving = pos.Idx;
            return true;
        }

        private bool HasReachedNextNode(MotionControl mov) {
            if (mov.IsImmobilized) {
                mov.Fail(T.NavFailBeingImmobilized);
                // don't continue any logic
                return true;
            }
            var pos = mov.Position;

            bool reachedNextPathPoint = Vectors.SqrDistance(pos.Exact, pos.Next)
                <= 0.001f;

            if (!reachedNextPathPoint) { return false; }
            // D.Warn("Reached next path node {0}->{1} {2} ({3})",
            //     Pos.String(pos.Idx),
            //     Pos.String(pos.NextIdx), G.Ticks, G.ClockSpeed);

            if (mov.WantedDistToTargetSqr > 0f) {
                var distToTargetSqr = Vectors.SqrDistance(pos.Exact, mov.Target.Position);
                if (distToTargetSqr <= mov.WantedDistToTargetSqr) {
                    mov.Clear(true);
                    return false;
                }
            }

            if (!mov.HasProgressFlag(MotionProgressFlags.Blocked)) {
                // a blocked walker reaches its own tile every frame
                mov.SpawnDirt();
            }

            if (!mov.AdvancePath(false)) {
                // AdvancePath failed the walk itself (unsafe tile ahead, a teleporter that would
                // not take us, a stop it may not end on) and Fail already cleared it: resetting
                // the flags here would report the walk as arrived
                if (!mov.Progress.HasFailed) {
                    // we have arrived
                    mov.Clear(true);
                }
                return false;
            }

            // we have advanced the path
            if (!mov.CheckSafety()) {
                return false;
            }
            return true;
        }
    }
}
