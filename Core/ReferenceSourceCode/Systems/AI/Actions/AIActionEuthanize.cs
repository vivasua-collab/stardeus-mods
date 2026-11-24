using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Items;
using Game.Systems.Path;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using KL.Grid;
using Unity.Mathematics;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.UI;

namespace Game.Systems.AI {
    public sealed class AIActionEuthanize : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionEuthanize());
        }
        public const string ActId = "Euthanize";
        public readonly static int ActIdH = Hashes.S(ActId);
        private AIActionEuthanize() : base(ActId, () => T.VerbEuthanize) {
            Preconditions = AIState.With(AIVarsH.IsNear, true).And(AIVarsH.IsEuthanized, false);
            Outcomes = AIState.With(AIVarsH.IsEuthanized, true);
            WithRequiredAbilities(AbilityIdH.UseItem, AbilityIdH.DumbLabor);
            MarkAsWork();
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {
            if (goal.Target is not BodyComp body) {
                return Error("Wrong goal target type for euthanize: {0}", goal.Target);
            }

            if (body.Being.IsDead) {
                goal.WithWorld(AIVarsH.IsEuthanized, true);
                return Success;
            }

            if (agent.TicksInCurrentAction > Consts.TicksPer10Minutes) {
                var bodySlot = body.Being?.Data?.Body.BodySlot;
                var euthanizedMsg = T.DeathEuthanizedBy_V1.F(agent.BeingData.Mind.LinkToShortName);
                if (bodySlot != null) {
                    if (!bodySlot.TryAddOrUpdateWound(agent.Being, DamageType.Sharp, Severity.Fatal,
                            ticks, 0f, WoundFlags.CantHeal)) {
                        BeingUtils.Kill(S, body.Being.Data, DamageType.Piercing, agent.Being,
                            euthanizedMsg, null, 2);
                    } else {
                        bodySlot.Body.Bleed(0.5f);
                        bodySlot.Body.Bleed(0.5f);
                        bodySlot.Body.Owner.Mind.OnDeath(euthanizedMsg, ticks);
                    }
                    Sounds.PlaySpikes(agent.Being.Position);
                }
                TryWalkAway(agent);
                goal.WithWorld(AIVarsH.IsEuthanized, true);
                return Success;
            }

            // progress bar and wait
            var opProgress = agent.TicksInCurrentAction / (float) Consts.TicksPer10Minutes;
            agent.ShowInfoBar(Consts.ColorHealthBar, opProgress, T.VerbEuthanize);
            return InProgress;
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.HideInfoBar();
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            return agent.Motion.GoToCost(goal.Target.Entity.PosIdx);
        }
    }
}
