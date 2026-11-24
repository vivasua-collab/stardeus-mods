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
using Game.AI;
using Game.Systems.Mental;
using Game.Systems.AI.Combat;
using Game.Systems.Stats;

namespace Game.Systems.AI {
    public sealed class AIBreakdownActionPanic : AIMentalBreakdownAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIBreakdownActionPanic());
        }
        public const string ActId = "BreakdownPanic";
        public static readonly int ActIdH = Hashes.S(ActId);
        protected override float minSanity => 0f;
        protected override string DescriptionFor(AIAgentComp agent) {
            return T.ActPanicDesc_V1.F(Texts.LinkToBeing(agent.BeingData));
        }
        protected override int AiVar => AIVarsH.MentalPanicked;
        private Curve panicDuration;
        private AIBreakdownActionPanic() : base(ActId, () => T.ActPanic) {
            WithoutRequiredAbilities();
            // WithRequiredAbilities(AbilityIdH.Move);
        }


        public override AIActionResult Execute(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                return InProgress;
            }
            // we were on fire at the start
            if (agent.BB.Bool1) {
                if (!agent.BeingData.HasCondition(ConditionIdH.OnFire)) {
                    // fire was put out, let's not panic anymore
                    return EndBreakdown(agent, goal);
                }
            }
            if (agent.CombatUnit.Army != null) {
                agent.CombatUnit.SetState(CombatUnitState.Fleeing);
            }
            if (agent.CombatUnit.AttackTarget != null) {
                agent.CombatUnit.SetAttackTarget(null, true);
            }

            panicDuration ??= CurveDef.FastGet(CurveIdH.MentalBreakdown_PanicDurationHours);
            var rng = new Rng(Hashes.Quick(agent.Being.Id, Primes.Prime017));
            var duration = rng.Curve(panicDuration);
            if (agent.BB.Bool1) {
                // shorter panic when on fire
                duration *= 0.5f;
            }
            // D.Warn("Ending panic after: {0}h", duration);
            duration *= Consts.TicksPerHour;

            if (agent.TicksInCurrentAction < duration) {
                if (agent.Motion.IsImmobilized) {
                    // panic in place
                    return InProgress;
                }
                agent.BB.ClearMoveTarget();
                if (!AIActionIdleRoam.TryRoam(agent, false, Rng.URange(1, 4))) {
                    var pos = AIActionFindSafety.FindSafePos(S, agent, agent.PosIdx);
                    if (pos != Pos.Invalid) {
                        if (agent.Motion.TryGoTo(pos, PathReqFlags.AdjOnTop | PathReqFlags.UnsafeOK)) {
                            return InProgress;
                        }
                        #if UNITY_EDITOR
                        D.ErrSoft("WTF: Failed to go to safety: {0} -> {1}",
                            Pos.StringFromTo(agent.PosIdx, pos), agent);
                        #endif
                    }
                    // we're panicking, so lets just go anywhere!
                    if (!AIActionIdleRoam.TryRoam(agent, false, 3, true)) {
                        #if UNITY_EDITOR
                        D.ErrSoft("WTF: Failed roaming unsafely away from pos: {0} -> {1}",
                            Pos.String(agent.PosIdx), agent);
                        #endif
                    }
                }
                return InProgress;
            }
            return EndBreakdown(agent, goal);
        }

        // Lazy: Register runs before the language loads, so a label made then stays English
        private static StatModifier panicMod;
        private static StatModifier PanicMod => panicMod ??= StatModifier.AddPercent(0.8f, T.ActPanic);

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearMoveTarget();
            // sanity target
            agent.BB.Float1 = Rng.URange(0.5f, 0.6f);
            // was on fire
            if (agent.BeingData.HasCondition(ConditionIdH.OnFire)) {
                D.Warn("Panicking while on fire! {0}", agent);
                agent.BB.Bool1 = true;
            } else {
                agent.BB.Bool1 = false;

            }
            agent.BeingData.StatsBeing.MoveSpeed.AddModifier(PanicMod, false);
            agent.AddMentalEffect(MentalEffect.Create(agent.PosIdx,
                ticks, Consts.TicksPer4Hours, NameT, -20));
            base.OnActivate(agent, goal, ticks);
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearMoveTarget();
            agent.BB.Float1 = 0f;
            agent.BB.Bool1 = false;
            agent.BeingData.StatsBeing.MoveSpeed.RemoveModifier(PanicMod, false);
            base.OnDeactivate(agent, goal, ticks);
        }
    }
}
