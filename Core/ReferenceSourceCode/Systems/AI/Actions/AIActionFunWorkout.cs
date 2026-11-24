using System;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Drama;
using Game.Systems.Mental;
using Game.Systems.Path;
using Game.Systems.Stats;
using Game.Utils;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Systems.AI {
    public sealed class AIActionFunWorkout : AIActionUseSlot {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionFunWorkout());
        }

        public const string ActId = "FunWorkout";
        private static readonly IWorkoutProvider[] workoutProviders = new IWorkoutProvider[32];

        protected override string FailMsgSlotUnavailable => T.AdRejLackingWorkoutEquipment;

        protected override int failReachCooldownFrom => Consts.TicksPer3Hours;
        protected override int failReachCooldownTo => Consts.TicksPer6Hours;

        protected override int SlotDesignation => SlotDesignationH.Workout;
        protected override bool CanBeMakeshift => true;
        protected override int SocialOpportunityId => SocialOpportunityIdH.Workout;
        protected override int SocialOpportunityDuration => Consts.TicksPer4Hours;
        public override SocialChatContext ChatContext => SocialChatContext.Create(
                SocialContext.Workout,
                faceBuddy: false,
                expectedDuration: Consts.TicksPer20Minutes);

        private AIActionFunWorkout() : base(ActId, () => T.WorkingOut) {

            Preconditions = AIState.With(AIVarsH.HadFun, false);
            Outcomes = AIState.With(AIVarsH.HadFun, true);
            WithRequiredNeed(NeedIdH.Fun);
            WithRequiredFlags(StatFlags.Biological);
            WithRequiredAbilities(AbilityIdH.Move);
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.ShowThoughtBubble(IconId.CTreadmill, T.WorkingOut);
            agent.BB.MoveProgress = null;
            FunSession.Clear(agent);
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            base.OnDeactivate(agent, goal, ticks);
            FunSession.Clear(agent);
        }

        protected override bool CheckDone(GameState S, AIAgentComp agent, AIGoal goal) {
            var ticks = G.Ticks;
            // a session at full novelty would have ended by now, even if Fun is not full
            if (agent.BeingData.StatsBeing.Fun.Value > FunSession.SatisfiedAt
                    || FunSession.IsOver(agent, ticks)) {
                FunSession.Finish(agent, ticks);
                goal.WithWorld(AIVarsH.HadFun, true);
                return true;
            }
            return false;
        }
        protected override AIActionResult OnUseSlot(AIAgentComp agent, AIGoal goal, Slot slot, long ticks) {
            var data = agent.BeingData;
            var stats = data.StatsBeing;
            var novelty = FunNovelty.Of(data, FunSubjects.ForWorkout(slot.Parent.Tile), ticks);
            stats.Fun.AddToBaseWithCompoundLimit(FunSession.GainPerTick(stats, novelty, 1f));
            if (slot.Parent is IWorkoutProvider wp) {
                wp.DoWorkout(agent.Being);
            }
            return InProgress;
        }

        protected override AIActionResult OnSlotUnavailable(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.SetCooldown(IdH, ticks, Consts.TicksPerHour, Consts.TicksPer2Hours);
            return Failed(FailMsgSlotUnavailable);
        }

        protected override AIActionResult OnStartUsingSlot(AIAgentComp agent, AIGoal goal, Slot slot, long ticks) {
            if (slot.Parent is IWorkoutProvider wp) {
                wp.BeginWorkout(agent.Being);
            }
            // a session restored from a save keeps its planned end
            if (!FunSession.IsStarted(agent)) {
                FunSession.Begin(agent, ticks,
                    FunSession.FullNetGainPerTick(agent.BeingData.StatsBeing, 1f), 0, long.MaxValue);
            }
            return InProgress;
        }

        protected override void OnEndUsingSlot(AIAgentComp agent, AIGoal goal, Slot slot, long ticks) {
            if (slot.Parent is IWorkoutProvider wp) {
                wp.EndWorkout(agent.Being);
                agent.BB.SetCooldown(IdH, ticks, Consts.TicksPer8Hours, Consts.TicksPer12Hours);
                var time = agent.TicksInCurrentAction;
                if (time > Consts.TicksPer30Minutes) {
                    var src = FunSubjects.ForWorkout(slot.Parent.Tile);
                    var novelty = FunNovelty.Of(agent.BeingData, src, ticks);
                    time = Maths.Max(time, Consts.TicksPer2Hours);
                    if (FunNovelty.TryScaleMood(5, time, novelty, out var effect, out var duration)) {
                        AddMentalEffect(agent, ticks, (int) duration, T.HadAWorkout, effect);
                    }
                    FunNovelty.Record(agent.BeingData, src, ticks);
                }
            }
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            if (!agent.S.Sys.Slots.HasFreeFor(SlotDesignationH.Workout)) {
                return 0f;
            }
            return CachedFunCostWithCoolDown(agent, ticks);
        }

        protected override float PredictFunNovelty(AIAgentComp agent, long ticks) {
            var count = agent.S.Components.FindAllByInterface(workoutProviders);
            var data = agent.BeingData;
            float best = 0f;
            bool any = false;
            for (int i = 0; i < count; i++) {
                var wp = workoutProviders[i];
                if (!wp.Entity.IsActive || !wp.IsAvailable || !wp.HasFreeSlot) { continue; }
                any = true;
                var novelty = FunNovelty.Predict(data, FunSubjects.ForWorkout(wp.Tile), ticks);
                if (novelty > best) {
                    best = novelty;
                    if (best >= 0.999f) { break; }
                }
            }
            Array.Clear(workoutProviders, 0, count);
            return any ? best : 1f;
        }
    }
}
