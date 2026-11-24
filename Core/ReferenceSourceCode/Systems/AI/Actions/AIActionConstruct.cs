using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Tools;
using Game.Utils;
using Game.CodeGen;
using Game.Visuals;
using KL.Collections;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using KL.Grid;
using Game.Systems.Mental;

namespace Game.Systems.AI {
    public sealed class AIActionConstruct : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionConstruct());
        }
        public const string ActId = "Construct";
        private AIActionConstruct() : base(ActId, () => T.ActConstruct) {
            Preconditions = AIState
                .With(AIVarsH.IsNear, true)
                .And(AIVarsH.IsConstructed, false);
            Outcomes = AIState.With(AIVarsH.IsConstructed, true);
            WithRequiredAbilities(AbilityIdH.Build, AbilityIdH.DumbLabor);
            WithRequiredJobType(JobTypeIdH.Construction);
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.Bool1NotSaved = false;
        }
        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.Bool1NotSaved = false;
            //checked for junk
            agent.BB.Bool1 = false;
            agent.BB.ClearFX();
        }


        public override AIActionResult Execute(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {
            if (goal.Target is IConstructable c) {
                if (!agent.BB.Bool1NotSaved) {
                    agent.BB.Bool1NotSaved = true;
                    if (!EntityUtils.CheckIfConstructionIsPossible(S, agent.Being,
                            (Tile) c.Entity, true, out var fail)) {
                        return Failed(fail);
                    }
                }
                if (!EntityUtils.IsNearObj(agent.PosIdx, c.ConstructableTile)) {
                    D.Err("Executing AIActionConstruct while worker is not near! {0} ({1})",
                        goal, goal.WorldState);
                    goal.WithWorld(AIVarsH.IsNear, false);
                    return NeedsReplan(UnreachableBy(agent));
                }
                if (agent.Jobs.TryJob(JobTypeIdH.Construction, out var slot)) {
                    float workAmount = slot.UseSkillAtWorkSpeed(SkillXp.Construction);
                    return PerformWork(S, c, agent, goal, workAmount);
                }
            }
            return Error("Target is not IConstructable: {0}", goal.Target);
        }

        private AIActionResult PerformWork(GameState S, IConstructable c, AIAgentComp agent,
                AIGoal goal, float workAmount) {
            FaceTarget(c, agent);
            var tile = c.ConstructableTile;
            var worker = agent.Being;
            if (!CheckMatJunk(S, agent, c)) {
                return Failed(T.AdRejConstructionSiteObstructed);
            }
            // TODO floor check
            if (c.AddWork(agent, workAmount)) {
                Sounds.PlayConstructionComplete(tile.Position);
                goal.WithWorld(AIVarsH.IsConstructed, true);
                TryStreamline(agent, goal);
                return Success;
            } else {
                var punchDir = tile.Position - worker.Position;
                worker.Graphics.Punch(punchDir + Rng.UInsideUnitCircle() * 0.05f,
                    0.1f, Rng.URange(0.7f, 1.3f), Rng.URange(0.7f, 1.3f));

                if (agent.BB.FX == null) {
                    agent.BB.FX = ParticlesVFX.CreateWithBudget(S,
                        "ConstructVFX", worker.Position, BBKeyH.SwarmPlayingVFX,
                            Consts.SwarmMaxVFX).WithStaticOwnership(worker);
                    if (agent.BB.FX.TryGetFX(out var fx)) {
                        fx.SetVector3(ShaderVars.Target, punchDir);
                        var size = tile.Transform.Size.magnitude * 0.25f;
                        fx.SetFloat(ShaderVars.Size, size);
                        fx.SetFloat(ShaderVars.EmitRate, 200f * size);
                    }
                    agent.BB.FX.SetClock(S.Clock);
                }

                if (agent.BB.NextSoundTime < GameTimer.realtimeSinceStartupAsDouble) {
                    agent.BB.NextSoundTime = GameTimer.realtimeSinceStartupAsDouble
                        + 5f / S.Clock.FXTimeScale;
                    Sounds.PlayConstruct(worker.Position);
                }
            }
            return InProgress;
        }

        private bool CheckMatJunk(GameState S, AIAgentComp agent, IConstructable c) {
            if (agent.BB.Bool1) {
                return true;
            }
            agent.BB.Bool1 = true;
            if (c.LayerId == WorldLayer.Floor) {
                return true;
            }

            var tile = c.ConstructableTile;
            MoveObstructions(agent, tile);

            // this sucked because things could get stuck and unbuilt
            // if (hasMatJunk) {
            // move all junk to worker position and continue
            // ToolPlaceTiles.ClearDebrisFor(tile, tile.PosIdx, agent);
            // return false;
            // }
            return true;
        }

        public static void MoveObstructions(AIAgentComp agent, Tile tile) {
            var cnt = tile.Transform.GetSubtilePos(Caches.PosIdx);
            for (int i = 0; i < cnt; i++) {
                var p = Caches.PosIdx[i];
                var obInP = agent.S.Objs.FindFirstInPos(p,
                    (o) => o.Carrier == null);
                if (obInP != null) {
                    obInP.Transform.TeleportTo(agent.PosExact);
                    obInP.Graphics.TeleportTo(agent.PosExact);
                }
                if (tile.S.Beings.HasDeadBeingsInPos(p, agent.PosExact)) {
                    // D.Warn("Moved dead bodies from {0} to {1} for construction of {2}",
                    //     Pos.String(p), agent.PosExact, tile);
                }
            }
        }
    }
}
