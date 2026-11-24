using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Motion;
using Game.Systems.Path;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Systems.AI {
    public sealed class AIActionRescueInSpace : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionRescueInSpace());
        }
        public const string ActId = "RescueInSpace";
        public readonly static int ActIdH = Hashes.S(ActId);
        private AIActionRescueInSpace() : base(ActId, () => T.ActRescueinspace) {
            Preconditions = AIState.With(AIVarsH.IsNear, true)
                        .And(AIVarsH.IsStrandedInSpace, true);
            Outcomes = AIState.With(AIVarsH.IsStrandedInSpace, false);
            WithRequiredAbilities(AbilityIdH.Fly, AbilityIdH.Carry);
            WithRequiredJobType(JobTypeIdH.Rescue);
            MarkAsMobile();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.MoveTargetPos = Pos.Invalid;
            agent.BB.MoveProgress = null;
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.MoveTargetPos = Pos.Invalid;
            agent.BB.MoveProgress = null;
            agent.Being.Graphics.DropIfCarrying();
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            var stuckBeing = goal.Target.Entity as Being;
            // Set this when we start carrying the being
            var startPos = agent.BB.Int1;
            var worker = agent.Being;
            var rescuePos = agent.BB.MoveTargetPos;

            if (rescuePos != Pos.Invalid) {
                var distBetweenStart = Pos.SqrDistance(startPos, worker.PosIdx);
                var distBetweenRescueObj = Pos.SqrDistance(startPos, rescuePos);

                // Moved far away
                if (distBetweenStart > 25 || distBetweenRescueObj <= 25) {
                    var workerIsland = S.Sys.Areas.IslandAt(worker.PosIdx);
                    if (workerIsland != 0) {
                        // check if we are above the same island as rescue obj
                        var objIsland = S.Sys.Areas.IslandAt(rescuePos);
                        if (objIsland == workerIsland) {
                            // success!
                            worker.Graphics.DropIfCarrying();
                            // worker.Motion.Cancel("done rescuing stuck being");
                            //stuckBeing.Navigation.Mobilize();
                            goal.WorldState.Set(AIVarsH.IsStrandedInSpace, false);
                            return Success;
                        }
                    }
                }
            }

            if (worker.Motion.IsBusy) {
                return InProgress;
            }

            if (agent.BB.MoveProgress != null) {
                if (agent.BB.MoveProgress.HasFailed) {
                    return FailedNavToTarget(agent, stuckBeing.PosIdx);
                }
            }

            // 1. reach the target (our stuck being)
            // FIXME maybe use Fast Magnitude?
            // http://www.claysturner.com/dsp/FastMagnitude.pdf
            // fuck this document

            var distToTarget = Pos.SqrDistance(worker.PosIdx, stuckBeing.PosIdx);
            if (distToTarget > 2) {
                if (worker.Motion.TryGoTo(stuckBeing.PosIdx,
                        PathReqFlags.AdjOnTop | PathReqFlags.AdjNearby,
                        out agent.BB.MoveProgress)) {
                    return InProgress;
                } else {
                    return FailedNavToTarget(agent, stuckBeing.PosIdx);
                }
            }

            if (!worker.Graphics.IsCarrying()) {
                if (!worker.Graphics.Carry(stuckBeing)) {
                    return Failed("Failed to carry!");
                }
                agent.BB.Int1 = worker.PosIdx;
            }

            // We didn't return Success after the previous block
            // It means that we reached the rescue obj, but there was no
            // ground beneath it
            if (agent.BB.MoveTargetPos != Pos.Invalid) {
                var newTargetPos = Pos.Invalid;
                Pos.ForEachAroundRandom(S.Rng, worker.PosIdx, 5, false,
                    true, (p) => {
                        if (EntityUtils.IsEmptyFloorAt(p, false)) {
                            newTargetPos = p;
                            return false;
                        }
                        return true;
                    });
                if (newTargetPos == Pos.Invalid) {
                    return Failed(T.AdRejTargetUnavailable);
                }
                if (newTargetPos != worker.PosIdx) {
                    agent.BB.MoveTargetPos = newTargetPos;
                    if (worker.Motion.TryGoTo(newTargetPos,
                            PathReqFlags.AdjOnTop | PathReqFlags.AdjNearby, out agent.BB.MoveProgress)) {
                        return InProgress;
                    } else {
                        return FailedNavToTarget(agent, newTargetPos);
                    }
                }
            }

            // 2. carry the being back to a spot with a floor
            Tile rescueObj;
            if (stuckBeing.Data.Species.IsBiological) {
                rescueObj = FindRescueSpotForMeatbag(S, worker);
            } else {
                rescueObj = FindRescueSpotForRobot(S, worker);
            }
            if (rescueObj == null) {
                return Failed(T.AdRejTargetUnavailable);
            }
            agent.BB.MoveTargetPos = rescueObj.PosIdx;
            if (agent.Motion.TryGoTo(rescueObj,
                    PathReqFlags.AdjOnTop | PathReqFlags.AdjNearby | PathReqFlags.TempDestination,
                    out agent.BB.MoveProgress)) {
                return InProgress;
            } else {
                return FailedNavToTarget(agent, rescueObj.PosIdx);
            }
        }
        private Tile FindRescueSpotForMeatbag(GameState S, Being worker) {
            Caches.TileList.Clear();
            var allPumps = S.Components.AllByType<OxygenProducerComp>();
            if (allPumps != null) {
                foreach (var pump in allPumps) {
                    if (pump.IsConstructed && pump.Entity is Tile t && t.ENode?.IsPowered == true) {
                        Caches.TileList.Add(t);
                    }
                }
            }

            Tile rescueObj = null;
            if (Caches.TileList.Count > 0) {
                rescueObj = Rng.UFrom(Caches.TileList);
            }
            if (rescueObj == null) {
                var stasis = S.Components.FindFirstByType<StasisArrayComp>();
                if (stasis == null) {
                    var elObj = S.Components.FindFirstByInterface<ElectricNodeComp>(
                            s => s.Node.IsConnector
                            && s.Entity.Definition.LayerId == WorldLayer.Objects);
                    if (elObj != null) {
                        rescueObj = elObj.Entity as Tile;
                    }
                } else {
                    rescueObj = stasis.Tile;
                }
            }
            return rescueObj;
        }

        private Tile FindRescueSpotForRobot(GameState S, Being worker) {
            var chargeStation = S.Components.FindClosestMatching<ChargeStationComp>(
                worker.Position, false, null);
            Tile rescueObj = null;
            if (chargeStation == null) {
                var cpu = S.Components.FindFirstByType<ShipComputerComp>();
                if (cpu == null) {
                    var elObj = S.Components.FindFirstByInterface<ElectricNodeComp>(
                            s => s.Node.IsConnector);
                    if (elObj != null) {
                        rescueObj = elObj.Entity as Tile;
                    }
                } else {
                    rescueObj = cpu.Tile;
                }
            } else {
                rescueObj = chargeStation.Tile;
            }
            return rescueObj;
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.Being == goal.Target.Entity) {
                return 0f;
            }
            return agent.Motion.GoToCost(goal.Target.Entity.PosIdx) * 2f;
        }
    }
}
