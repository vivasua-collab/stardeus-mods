using System;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class UnlockResearchQuestStep : BaseQuestStep, ITickListener,
            IPreCheckedQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new UnlockResearchQuestStep(s, c, l));
        }
        public const string CheckId = "UnlockResearch";
        private readonly Def research;
        public UnlockResearchQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            var researchDef = step.Def.Config.GetString(PropertyIdH.Research);
            research = The.Defs.Get(researchDef);
            if (research == null) {
                D.Throw("Incorrectly configured research step: {0} [Missing def: {1}]",
                    step, researchDef);
            }
            step.Title = T.QuestStepResearch_V1.F(Texts.LinkToResearch(research));
            var tip = T.QuestStepResearchTip_V1.F(s.Research.TreePathFor(research));
            SetDesc(tip);
        }

        public override void OnQuestStateChange(QuestState before, QuestState after) {
            if (after == QuestState.Active) {
                S.Research.DiscoverWithPrerequisites(research.IdH);
            }
        }

        public void Tick(long currentTick) {
            if (S.Research.Get(research.IdH).InternalUnlocked) {
                step.SetState(QuestStepState.Completed);
            }
        }
        public void PreCheck() {
            if (S.Research.Get(research.IdH).InternalUnlocked) {
                step.SetHidden();
                step.SetState(QuestStepState.Completed);
            }
        }
    }
}
