using Game.Constants;
using Game.Systems.Quests;
using Game.Systems.UIUX;
using Game.Utils;

namespace Game.Data.Space {
    public sealed class SOActionQuest : SOAction {
        public const string ActionId = "Quest";
        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionQuest());
        }

        public override bool IsAvailable(SOActionContext ctx) {
            if (ctx.SO.Ship?.IsDerelict == true) { return false; }
            // like hailing, quest interactions reach only as far as the current sector
            if (ctx.SO.Parent != ctx.Player.Parent) { return false; }
            var targets = ctx.S.Query.GetQuestStepTargets.Ask(
                SOUtils.QuestProviderFor(ctx.SO));
            if (targets.Length == 0) { return false; }
            for (int i = 0; i < targets.Length; i++) {
                if (targets[i].Step.Check is IQuestStepWithSOAction soActStep) {
                    return soActStep.HasSOActionFor(ctx.SO);
                }
            }
            return false;
        }

        public override bool AddOptions(QuickChoice qc) {
            var targets = ctx.S.Query.GetQuestStepTargets.Ask(
                SOUtils.QuestProviderFor(ctx.SO));
            if (targets.Length == 0) { return false; }
            for (int i = 0; i < targets.Length; i++) {
                if (targets[i].Step.Check is IQuestStepWithSOAction soActStep) {
                    if (soActStep.TryAddSOActionFor(ctx.SO, qc)) {
                        return true;
                    }
                }
            }
            return true;
        }

        public override string Id => ActionId;

        public override int UIPriority => 50;
    }
}
