using Game.Constants;
using Game.Data;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.Systems.Drama;

using Game.Utils;
namespace Game.Systems.AI {
    public sealed class AIActionConfessLove_Receive : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionConfessLove_Receive());
        }
        public const string ActId = "ConfessLove_Receive";
        public readonly static int ActIdH = Hashes.S(ActId);
        public override SocialChatContext ChatContext => SocialChatContext.Create(
                SocialContext._LoveConfession,
                faceBuddy: true,
                expectedDuration: Consts.TicksPer30Minutes);
        private AIActionConfessLove_Receive() : base(ActId, () => T.VerbReceivingLoveConfession) {
            Preconditions = AIState.With(AIVarsH.ReceivedLoveConfession, false);
            Outcomes = AIState.With(AIVarsH.ReceivedLoveConfession, true);
            MarkAsWork();
            RequireMentalHealth();
            WithRequiredAbilities(AbilityIdH.Hear);
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.TicksInCurrentAction > Consts.TicksPer30Minutes) {
                goal.WithWorld(AIVarsH.ReceivedLoveConfession, true);
                return Success;
            }

            if (agent.Jobs.TryJob(JobTypeIdH.Social, out var social)) {
                social.UseSkill();
            }

            return InProgress;
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            return 1f;
        }
    }
}
