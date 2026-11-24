using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.Drama;
using Game.Systems.Notify;
using Game.UI;
using Game.Utils;
using KL.Randomness;

namespace Game.AI {
    public abstract class AIMentalBreakdownAction : AIAction {
        protected abstract string DescriptionFor(AIAgentComp agent);
        protected abstract int AiVar { get; }
        protected virtual float minSanity => A.S.Difficulty.SanityBaseline;

        public override SocialChatContext ChatContext => SocialChatContext.NoChat;
        protected AIMentalBreakdownAction(string id, Func<string> nameT) : base(id, nameT) {
            Preconditions = AIState.With(AiVar, false);
            Outcomes = AIState.With(AiVar, true);
            RequireMentalHealth();
            MarkAsRest();
        }

        protected void RemoveBreakdownNotification(AIAgentComp agent, long ticks) {
            var s = agent.S;
            agent.BB.ClearNotification();
            s.Sys.Log.AddLine(agent.Being, T.BreakdownEnded);
        }

        protected void AddBreakdownNotification(AIAgentComp agent, AIGoal g, long ticks) {
            var tip = DescriptionFor(agent);
            var data = Notification.BuildFor(agent.BeingData, null, NameT, 0, NotificationType.Warning)
                .Group("ment_break", title: T.NounMentalBreakdown, icon: IconId.CStress)
                .Sticky()
                .WithTooltip(tip);

            var s = agent.S;
            s.Sys.Log.AddLine(agent.Being, tip);
            s.Sig.ShowFanfare.Send(Texts.Yellow(tip));
            agent.BB.ShowNotification(data);
        }

        protected AIActionResult EndBreakdown(AIAgentComp agent, AIGoal goal) {
            goal.WithWorld(AiVar, true);
            return Success;
        }

        // Selection-time eligibility for this specific agent right now (world/position
        // aware, beyond the static ability/species gates on the action itself). Override
        // to reject breakdowns that have nothing to target, or no safe way to reach it.
        // Default: anyone who has the action may be assigned it.
        public virtual bool CanBeAssignedTo(AIAgentComp agent) => true;

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            return 1f;
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.BeingData.HasCondition(ConditionIdH.MentalBreakdown)) {
                AddBreakdownNotification(agent, goal, ticks);
            }
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.BeingData.HasCondition(ConditionIdH.MentalBreakdown)) {
                agent.BeingData.Mind.Health?.AfterBreakdown(ticks, minSanity);
                RemoveBreakdownNotification(agent, ticks);
            }
        }
    }
}
