using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Conditions;
using Game.Systems.Mental;

namespace Game.Systems.AI {
    public sealed class AIActionSleepAnywhere : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionSleepAnywhere());
        }
        public const string ActId = "SleepAnywhere";

        public static int ActIdH;

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.Bool1NotSaved = false;
            ConditionState.CreateFor(agent.S, ConditionIdH.Sleeping,
                agent.BeingData, Severity.Irrelevant, true);
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.Bool1NotSaved = false;
            agent.BB.ClearParticles();
            agent.Being.Graphics.SetVertical(true);
            agent.Being.Graphics.SetOffset(Consts.V2Zero);
            agent.BeingData.RemoveCondition(agent.S, ConditionIdH.Sleeping);
            // a night on the floor replaces last night's bed and room moods too
            SleepMoods.EndLastNight(agent.BeingData.Mind.Health, ticks);
            float needValue = agent.BeingData.StatsBeing.Sleep.Value * 0.5f;
            AINeedSleep.MakeThirsty(agent, ticks, needValue);
        }

        private AIActionSleepAnywhere() : base(ActId, () => T.ActSleep) {
            Preconditions = AIState
                    .With(AIVarsH.HasSlept, false)
                    .And(AIVarsH.TriedSleepingInBed, true);
            Outcomes = AIState.With(AIVarsH.HasSlept, true);
            MarkAsQuiet();
            WithRequiredNeed(NeedIdH.Sleep);
            WithRequiredAbilities(AbilityIdH.Sleep);
            ActIdH = IdH;
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            // never go to sleep when someone is targeting you!
            if (agent.CombatUnit.EnemiesTargetingThis > 0) {
                return 0f;
            }
            return (agent.Being.Data.StatsBeing.Sleep.Value + 0.001f) * 10f;
        }

        private bool IsInSleepPosition(AIAgentComp agent)  {
            return agent.BB.Bool1NotSaved;
        }

        private void SetInSleepPosition(AIAgentComp agent) {
            agent.BB.Bool1NotSaved = true;
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            var stats = agent.BeingData.StatsBeing;
            var speed = stats.SleepRate.Value;
            var excess = stats.Sleep.AddToBaseWithCompoundLimit(speed);
            if (excess > 0f || stats.Sleep.Value > 0.7f) {
                AIActionSleepInBed.ApplyNoBedPenalty(agent, ticks);
                AIActionSleepInBed.ApplyPrematureWakeup(agent, ticks);
                goal.WithWorld(AIVarsH.HasSlept, true);
                return Success;
            }
            if (!IsInSleepPosition(agent) || Rng.UChance(0.002f)) {
                var worker = agent.Being;
                worker.Graphics.SetVertical(false);
                worker.Graphics.SetRotation(Rng.URange(-10f, 10f), true);
                if (agent.BB.Particles == null) {
                    var data = new ParticlesSys.Data() {
                        Owner = worker,
                        IsEmitOnly = true,
                        EmitChance = 1,
                        PrefabName = "SleepFX",
                        Play = true,
                    };
                    agent.BB.SetParticles(data);
                    worker.AIAgent.HideAllInfo();
                    SetInSleepPosition(agent);
                }
            }

            if (!agent.BeingData.HasCondition(ConditionIdH.Sleeping)) {
                D.Err("Being is sleeping but has not sleeping condition! Fixing. {0} | {1}",
                    agent, goal);
                ConditionState.CreateFor(agent.S, ConditionIdH.Sleeping,
                    agent.BeingData, Severity.Irrelevant, true);
            }

            return InProgress;
        }
    }
}
