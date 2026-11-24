using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.Quests {
    public sealed class DisposeOfInfectedQuestStep : BaseQuestStep, ITickListener {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(StepId,
                (s, c, l) => new DisposeOfInfectedQuestStep(s, c, l));
        }
        public const string StepId = "DisposeOfInfected";
        public DisposeOfInfectedQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(StepId, s, step, isLoading) {
            step.Title = T.QuestStepDisposeCorpses;
            SetDesc(T.QuestStepDisposeCorpsesTip_V2.F(
                T.CondCrawlerinfected, T.VerbDispose));
        }

        public void Tick(long tick) {
            if (tick % 10 == 3 && CheckCompleted()) {
                step.SetState(QuestStepState.Completed);
            }
        }

        private bool CheckCompleted() {
            return !QuestUtils.HasInfectedCorpse(S);
        }
    }
}
