using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using Game.Visuals;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.AI {
    public sealed class AIActionCleanDevice : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionCleanDevice());
        }
        public const string ActId = "CleanDevice";
        private AIActionCleanDevice() : base(ActId, () => T.ActCleandevice) {
            Preconditions = AIState
                .With(AIVarsH.IsNear, true)
                .And(AIVarsH.IsClean, false);

            Outcomes = AIState.With(AIVarsH.IsClean, true);
            WithRequiredAbilities(AbilityIdH.CleanDevice, AbilityIdH.DumbLabor);
            WithRequiredJobType(JobTypeIdH.Cleaning);
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearFX();
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {
            if (goal.Target is CleanableComp c) {
                if (!c.Tile.IsActive) {
                    return Failed(T.AdRejTargetUnavailable);
                }
                if (!EntityUtils.IsNearObj(agent.PosIdx, c.Tile)) {
                    D.Err("Executing AIActionCleanDevice while worker is not near! {0} ({1})",
                        goal, goal.WorldState);
                    goal.WithWorld(AIVarsH.IsNear, false);
                    return NeedsReplan(UnreachableBy(agent));
                }
                if (agent.Jobs.TryJob(JobTypeIdH.Cleaning, out var slot)) {
                    float workAmount = slot.UseSkill() * slot.WorkSpeedMul();
                    return PerformWork(S, c, agent, goal, workAmount);
                }
            }
            return Error("Wrong target in clean goal");
        }


        private AIActionResult PerformWork(GameState S, CleanableComp c,
                AIAgentComp agent, AIGoal goal, float workAmount) {
            FaceTarget(c, agent);
            var worker = agent.Being;
            if (c.RemoveDirt(c.DirtCapacity * 0.01f * workAmount, agent.BeingData)) {
                goal.WithWorld(AIVarsH.IsClean, true);
                TryStreamline(agent, goal);
                return Success;
            }

            var tile = c.Tile;
            var punchDir = tile.Position - worker.Position;
            worker.Graphics.Punch(punchDir + Rng.UInsideUnitCircle() * 0.05f,
                0.1f, Rng.URange(0.7f, 1.3f), Rng.URange(0.7f, 1.3f));

            if (agent.BB.FX == null) {
                agent.BB.FX = ParticlesVFX.CreateWithBudget(S,
                    "RepairFX", worker.Position, BBKeyH.SwarmPlayingVFX,
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
                Sounds.PlayRepairs(worker.Position);
            }

            return InProgress;
        }
    }
}
