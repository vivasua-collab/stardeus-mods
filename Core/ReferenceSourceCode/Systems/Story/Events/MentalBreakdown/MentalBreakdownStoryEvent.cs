using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Systems.AI;
using Game.Systems.Conditions;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using KL.Utils;

namespace Game.Systems.Story {
    public abstract class MentalBreakdownStoryEvent : StoryEvent {
        protected abstract int AIVar { get; }
        protected abstract string Name { get; }
        protected abstract MentalBreakdownBand Band { get; }
        public MentalBreakdownBand EventBand => Band;
        protected abstract string DescriptionFor(BeingData being);

        // The MentalBreakdown condition still carries a Severity for display; derive it
        // from the band so the band stays the single source of truth.
        private static Severity SeverityForBand(MentalBreakdownBand band) {
            switch (band) {
                case MentalBreakdownBand.Medium: return Severity.Moderate;
                case MentalBreakdownBand.Heavy: return Severity.Severe;
                default: return Severity.Mild;
            }
        }

        public override void OnTick(StoryContext ctx, StoryEventData data) {
            // A being off the ship ends it too, which also heals saves where a sold being kept it
            if (data.TargetBeing?.Being == null
                    || !data.TargetBeing.HasCondition(ConditionIdH.MentalBreakdown)) {
                data.SetState(ctx.S, StoryEventState.Ending);
            }
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            StartBreakdown(ctx, data, Name, AIVar);
        }

        protected void StartBreakdown(StoryContext ctx, StoryEventData data,
                string name, int aiVar) {
            var target = data.TargetBeing?.Being?.AIAgent;
            var s = ctx.S;
            var ticks = ctx.Ticks;
            if (target == null) {
                data.FailEvent(s, "MentalBreakdown requires a TargetBeing");
                return;
            }
            var g = AIGoal.CreatePersonal(JobTypeIdH.Need, IconId.CStress,
                name, target, ctx.Ticks)
                    .WithWorldToDesired(aiVar, false, true)
                    .WriteInitialState();
            g.Flags |= AIGoalFlags.ForceAssigned;
            g.Flags |= AIGoalFlags.NonInterruptable;
            g.Flags |= AIGoalFlags.NonRetryable;

            var planner = s.Query.GetAIPlanner.Ask();
            if (planner.TryFormulateFor(g, target, ticks, out var plan)) {
                if (target.Goal != null) {
                    AIGoal.Interrupt(target.Goal, ticks);
                }
                if (target.SetGoal(g, plan, ticks)) {
                    var desc = DescriptionFor(data.TargetBeing);
                    data.CreateLogEntry(IconId.CStress, name, desc);
                    if (!ConditionState.CreateFor(s, ConditionIdH.MentalBreakdown,
                            target.BeingData, SeverityForBand(Band), false)) {
                        D.Err("Failed creating mental breakdown condition for being {0} -> {1}",
                            target.BeingData, this);
                    }
                    target.BeingData.Mind.Health.LastBreakdownTime = ticks;
                    Achievements.DramaAchievements.OnBreakdownStarted(s, Id);
                    return;
                } else {
                    D.Err("Failed setting breakdown goal: {0} / {1}", target, g);
                }
            } else {
                D.Err("Failed formulating a plan for breakdown goal: {0} -> {1}. Abilities: {2}",
                    g, target, string.Join(",", target.BeingData.AbilitySources.Select(s => s.Ability.NameT)));
            }
            data.FailEvent(s, "Could not start mental breakdown");
        }
    }
}
