using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Stats;

namespace Game.Systems.AI {
    /// <summary>
    /// <see cref="AINeedHealth.ForceStartHealing(AIGoal, Being, Slot, long)"/>
    /// </summary>
    public sealed class AIActionHealInBed : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionHealInBed());
        }
        public const string ActId = "HealInBed";
        public readonly static int ActIdH = Hashes.S(ActId);
        private AIActionHealInBed() : base(ActId, () => T.JobTypeSleeping) {
            Preconditions = AIState.With(AIVarsH.IsInjured, true)
                .And(AIVarsH.TriedSleepingInBed, true);
            Outcomes = AIState.With(AIVarsH.IsInjured, false);
            MarkAsQuiet();
            WithRequiredFlags(StatFlags.Biological);
            WithoutRequiredAbilities();
            MarkAsRest();
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            var body = agent.BeingData.Body;
            if (!body.CanHeal && !body.IsIncapacitated) {
                agent.BB.SetCooldown(NeedIdH.Health, ticks, Consts.TicksPer15Minutes,
                    Consts.TicksPer30Minutes);
                return Failed(T.CantHeal);
            }
            goal.WorldState.Set(AIVarsH.IsInjured, false);
            return Success;
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.BB.IsCoolingDown(NeedIdH.Health, ticks)) {
                return 0f;
            }
            // don't heal mid combat with enemies trying to hit you
            if (agent.CombatUnit.EnemiesTargetingThisUnit.Count > 0) {
                return 0f;
            }
            return 1f;
        }
    }
}
