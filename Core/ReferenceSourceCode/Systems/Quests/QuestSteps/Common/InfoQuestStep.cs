using System;
using Game.Data;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class InfoQuestStep : BaseQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(StepId,
                (s, c, l) => new InfoQuestStep(s, c, l));
        }
        public const string StepId = "Info";
        public InfoQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(StepId, s, step, isLoading) {
            var titleKey = step.Def.GetTitleKey(step.Data);
            if (titleKey == null) {
                D.Throw("Misconfigured InfoQuestStep, no TitleKey! {0}", step.Def);
            }
            step.Title = titleKey.T();
            var descKey = step.Def.GetDescKey(step.Data);
            if (descKey != null) {
                step.Desc = descKey.T();
            }
        }
    }
}
