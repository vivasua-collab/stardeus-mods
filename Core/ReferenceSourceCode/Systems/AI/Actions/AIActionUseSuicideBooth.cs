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
using Game.Systems.Stats;
using Game.Systems.Conditions;
using Game.Systems.Creatures;

namespace Game.Systems.AI {
    public sealed class AIActionUseSuicideBooth : AIActionUseSlot {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionUseSuicideBooth());
        }
        public const string ActId = "UseSuicideBooth";
        public static int ActIdH;
        protected override string FailMsgSlotUnavailable =>
            $"{T.RequiredDeviceIsNotAvailable}: {Texts.LinkToCodex(DefIdsH.ObjectsDevicesSuicideBooth)}";
        protected override int SlotDesignation => SlotDesignationH.Suicide;
        protected override bool CanBeMakeshift => false;
        protected override int failReachCooldownFrom => Consts.TicksPerDay;
        protected override int failReachCooldownTo => Consts.TicksPer2Days;
        protected override bool CheckDone(GameState S, AIAgentComp agent, AIGoal goal) {
            bool hasUsedSuicideBooth = false;
            if (agent.BeingData.Species.IsBiological) {
                hasUsedSuicideBooth = agent.BeingData.StatsBeing.BloodAmount.Value <= 0f
                        || agent.BeingData.HasStateFlag(BeingState.Dead);
            } else {
                hasUsedSuicideBooth = agent.BeingData.HasStateFlag(BeingState.Dead)
                    || agent.TicksInCurrentAction > Consts.TicksPerHour;
            }
            if (hasUsedSuicideBooth) {
                var msg = Texts.Red(T.SuicideBoothWarn_V1.F(agent.BeingData.Name));
                S.Sig.ShowFanfare.Send(msg);
                S.Sys.Log.AddLine(agent.Being, msg);
                goal.WithWorld(AIVarsH.HasCommittedSuicide, true);
                agent.BB.Bool2NotSaved = true;
                return true;
            } else {
                return false;
            }
        }

        protected override AIActionResult OnStartUsingSlot(AIAgentComp agent, AIGoal goal, Slot slot, long ticks) {
            agent.HideThoughtBubble();
            // bool1notsaved already used
            agent.BB.Bool2NotSaved = false;
            return InProgress;
        }

        protected override void OnEndUsingSlot(AIAgentComp agent, AIGoal goal, Slot slot, long ticks) {
            // D.Warn("Ending using suicide booth: {0} | {1}", agent, goal);
            if (slot.Parent is SuicideBoothComp booth) {
                booth.RemoveModifiers(agent.BeingData.StatsBeing);
            }
            if (agent.BB.Bool2NotSaved) {
                agent.BB.Bool2NotSaved = false;
                agent.S.Wallet.Add(25);
                if (!agent.BeingData.Species.IsBiological) {
                    // kill the core
                    agent.BeingData.Body.MindSlot.TryAddOrUpdateWound(slot.Parent.Tile,
                        DamageType.Piercing, Severity.Fatal, ticks, 1f);
                }
                BeingUtils.RemoveAllBodyParts(agent.Being,
                    slot.Parent.Tile.Transform.WorkSpotOrPos);
                BeingUtils.Eliminate(agent.S, agent.BeingData, T.ActUseSuicideBooth, ticks);
            }
        }

        protected override AIActionResult OnUseSlotFail(AIAgentComp agent, AIGoal goal, Slot slot, long ticks) {
            if (slot.Parent is SuicideBoothComp booth) {
                booth.RemoveModifiers(agent.BeingData.StatsBeing);
            }
            return base.OnUseSlotFail(agent, goal, slot, ticks);
        }

        private AIActionUseSuicideBooth() : base(ActId, () => T.ActUseSuicideBooth) {
            Preconditions = AIState.With(AIVarsH.HasCommittedSuicide, false);
            Outcomes = AIState.With(AIVarsH.HasCommittedSuicide, true);
            // We can force this action for an immobilized being
            WithRequiredAbilities(AbilityIdH.Move);
            WithForbiddenFlags(StatFlags.Wild);
            MarkAsRest();
            ActIdH = IdH;
        }

        protected override AIActionResult OnSlotUnavailable(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.SetCooldown(IdH, ticks, Consts.TicksPer10Minutes, Consts.TicksPer15Minutes);
            return base.OnSlotUnavailable(agent, goal, ticks);
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.BB.IsCoolingDown(IdH, ticks)) {
                return 0f;
            }
            if (!agent.S.Sys.Slots.HasFor(SlotDesignation)) {
                agent.BB.SetCooldown(IdH, ticks,
                    Consts.TicksPer10Minutes, Consts.TicksPer15Minutes);
                return 0f;
            }
            if (!agent.BeingData.HasCondition(ConditionIdH.Depression)) {
                agent.BB.SetCooldown(IdH, ticks,
                    Consts.TicksPer30Minutes, Consts.TicksPer60Minutes);
                return 0f;
            }
            return 0.5f;
        }

        public static void EnsureGoalFor(BeingData owner, long ticks) {
            if (owner.Being == null) {
                return;
            }
            var agent = owner.Being.AIAgent;
            if (!agent.BB.TryNeedGoal(ActIdH, out var g)) {
                g = AIGoal.CreatePersonal(JobTypeIdH.Need,
                    IconId.CSkull, T.ActUseSuicideBooth, agent, ticks)
                    .WithWorldToDesired(AIVarsH.HasCommittedSuicide, false, true)
                    .WriteInitialState();
                // never interrupt
                g.WithInterrupt(5, 0);
                agent.BB.SetNeedGoal(ActIdH, g);
            }

            if (g.State == AIGoalState.InProgress) {
                // never touch goal that is in progress
                return;
            }

            if (g.State != AIGoalState.Available) {
                g.ChangeStateTo(AIGoalState.Available, ticks);
                g.CreatedAt = ticks;
                g.ResetWorldState();
            }
            g.GoalPriority = 3;
            g.GoalImportance = 1;
            g.WithInterrupt(5, 0);
        }
    }
}
