using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;

namespace Game.Systems.AI {
    /// <summary>
    /// Rogue AI alternative to the whole find food -> find table -> eat chain.
    /// The colonist walks to a Tube Feeder and is pumped full of nutrient paste.
    /// No cooking, no hauling of meals, no dining room - and no dignity.
    /// </summary>
    public sealed class AIActionUseTubeFeeder : AIActionUseSlot {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionUseTubeFeeder());
        }
        public const string ActId = "UseTubeFeeder";
        public static int ActIdH = Hashes.S(ActId);

        // Feeding is done once the colonist is comfortably full, matching the
        // threshold AINeedHunger uses to consider the hunger goal satisfied.
        private const float doneThreshold = 0.65f;
        // Cost handed to the planner when there is no usable feeder anywhere,
        // so the regular food chain always wins instead.
        private const float noFeederCost = 10000f;

        protected override string FailMsgSlotUnavailable => T.NoTubeFeeder;
        protected override int SlotDesignation => SlotDesignationH.TubeFeeder;
        protected override bool CanBeMakeshift => false;
        // Longer than the hunger goal's retry delay (at most an hour), so a feeder that
        // turned out unusable is not planned again before the colonist has eaten normally.
        // Reachability here is only a floor island check, which walls and locked doors pass.
        protected override int failReachCooldownFrom => Consts.TicksPer3Hours;
        protected override int failReachCooldownTo => Consts.TicksPer6Hours;

        private AIActionUseTubeFeeder() : base(ActId, () => T.ActUsetubefeeder) {
            Preconditions = AIState.With(AIVarsH.IsHungry, true);
            Outcomes = AIState.With(AIVarsH.IsHungry, false);
            WithRequiredNeed(NeedIdH.Hunger);
            // Deliberately no Chew - the whole point is that the feeder does
            // not need the colonist to cooperate.
            WithRequiredAbilities(AbilityIdH.Eat, AbilityIdH.Move);
            MarkAsRest();
        }

        /// <summary>
        /// The planner has no way to ask "does a feeder exist", so we answer that
        /// through cost. Without this a colony with no feeder would keep planning
        /// this action, fail, and never fall back to normal food.
        /// </summary>
        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            // Asked per being, with the same checks the slot search uses. A colony-wide
            // "is any feeder available" made everyone who could not reach it, or found it
            // occupied, keep planning the feeder and never eat (STA-1370).
            if (!agent.S.Sys.Slots.HasUsableFor(SlotDesignation, agent.BeingData)) {
                return noFeederCost;
            }
            // Cheaper than the find food -> find table -> eat chain, which is
            // the entire point of building one.
            return 0.25f;
        }

        // The feeder was taken, or turned out unreachable, between planning and arrival.
        // Without a cooldown the next hunger plan would pick it again right away.
        protected override AIActionResult OnSlotUnavailable(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.SetCooldown(IdH, ticks, failReachCooldownFrom, failReachCooldownTo);
            return base.OnSlotUnavailable(agent, goal, ticks);
        }

        protected override bool CheckDone(GameState S, AIAgentComp agent, AIGoal goal) {
            var stats = agent.BeingData.StatsBeing;
            if (AINeedHunger.CombinedHungerFor(stats) < doneThreshold) {
                return false;
            }
            goal.WithWorld(AIVarsH.IsHungry, false);
            return true;
        }

        protected override AIActionResult OnUseSlot(AIAgentComp agent, AIGoal goal,
                Slot slot, long ticks) {
            if (slot.Parent is not TubeFeederComp feeder) {
                return Failed(FailMsgSlotUnavailable);
            }
            var stats = agent.BeingData.StatsBeing;
            var eatRate = stats.HungerSatisfyRate.Value;
            if (!feeder.Consume(eatRate)) {
                // Ran dry mid-feed. Bail out so the planner can fall back to
                // regular food rather than standing here starving.
                return Failed(T.AdRejLackingEdible);
            }
            stats.Hunger.TryIncrease(eatRate);
            // A full stomach pushes CombinedHungerFor above the done threshold,
            // so CheckDone ends the action on the next tick through the same
            // path as a normal finish - keeping slot release in one place.
            stats.StomachContents.TryIncrease(eatRate);
            agent.ShowInfoBar(Consts.ColorFuelBar,
                AINeedHunger.CombinedHungerFor(stats), T.Eating);
            return InProgress;
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.BB.Bool1NotSaved) {
                ApplyTubeFedPenalty(agent, ticks);
            }
            agent.HideInfoBar();
            base.OnDeactivate(agent, goal, ticks);
        }

        private void ApplyTubeFedPenalty(AIAgentComp agent, long ticks) {
            if (!agent.BeingData.Species.IsHumanoid) { return; }
            AddMentalEffect(agent, ticks, Consts.TicksPer8Hours, T.TubeFed, -20);
        }
    }
}
