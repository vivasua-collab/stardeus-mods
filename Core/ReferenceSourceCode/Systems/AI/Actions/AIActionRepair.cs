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
using Game.Systems.Mental;

namespace Game.Systems.AI {
    public sealed class AIActionRepair : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionRepair());
        }
        public const string ActId = "Repair";
        private AIActionRepair() : base(ActId, () => T.ActRepair) {
            Preconditions = AIState
                .With(AIVarsH.IsNear, true)
                .And(AIVarsH.IsDamaged, true);

            Outcomes = AIState.With(AIVarsH.IsDamaged, false);
            WithRequiredAbilities(AbilityIdH.Build, AbilityIdH.SkilledLabor);
            WithRequiredJobType(JobTypeIdH.Repairs);
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearFX();
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (goal.Target is DamageableComp d) {
                if (!d.Tile.IsActive) {
                    return Failed(T.AdRejTargetUnavailable);
                }
                if (!EntityUtils.IsNearObj(agent.PosIdx, d.Tile)) {
                    D.ErrSoft("Executing AIActionRepair while worker is not near! {0} ({1})",
                        goal, goal.WorldState);
                    goal.WithWorld(AIVarsH.IsNear, false);
                    return NeedsReplan(UnreachableBy(agent));
                }
                if (agent.Jobs.TryJob(JobTypeIdH.Repairs, out var slot)) {
                    float workAmount = slot.UseSkillAtWorkSpeed(SkillXp.Construction);
                    return PerformWork(S, d, agent, goal, workAmount);
                }
            }
            return Error("Wrong target in repair goal {0}", goal.Target);
        }

        private AIActionResult PerformWork(GameState S, DamageableComp c,
                AIAgentComp agent, AIGoal goal, float workAmount) {
            FaceTarget(c, agent);
            var worker = agent.Being;
            if (agent.TicksInCurrentAction >= Tunables.RepairWarmupTicks) {
                c.Repair(workAmount);
            }
            if (c.IsDamaged) {
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

            c.Tile.ENode?.AutoConnectIfEnabled();
            goal.WithWorld(AIVarsH.IsDamaged, false);
            TryStreamline(agent, goal);
            return Success;
        }
    }
}
