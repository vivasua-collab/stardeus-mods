using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using Game.Systems.Quests;

namespace Game.Expeditions {
    public sealed class ExpeditionStageQuest : BaseExpeditionStage {
        [SelfInit]
        private static void Register() {
            ExpeditionStages.Register(ExpeditionStageTypeH.Quest , () => {
                return new ExpeditionStageQuest();
            });
        }
        public override int StageType => ExpeditionStageTypeH.Quest;
        public override bool IsStageCancellable => handler?.IsExpeditionStageCancellable == true;
        public override string Title => T.ExpStageExploring;

        public override string MiniWidgetStatusIcon => IconId.CMagnifier;

        public ExpeditionStageQuest() {
            MiniWidgetText.Set(T.ExpStageExploring);
        }
        private IExpeditionStage nextStage;
        private int nextStageType;
        private IQuestExpeditionHandler handler;

        public override bool CanCallBack(Expedition exp) {
            return IsStageCancellable;
        }

        public override IExpeditionStage Tick(Expedition exp, long ticks) {
            if (nextStage != null) {
                var ns = nextStage;
                nextStage = null;
                return ns;
            }

            if (!Ready.Get(WhenH.AllSystemsLoaded)) {
                D.Warn("Ticking ExpeditionPOI while systems not loaded!");
                return this;
            }

            if (handler == null) {
                var questStepTargets = exp.S.Query.GetQuestStepTargets.Ask(exp.Target);
                if (questStepTargets.Length != 1) {
                    D.Err("QuestStepTargets for quest expedition is != 1");
                    if (questStepTargets.Length == 0) {
                        ReturnToBase(exp.S);
                        return this;
                    }
                }
                handler = questStepTargets.First.ExpeditionHandler;
                if (handler == null) {
                    D.Err("QuestStepTarget.ExpeditionHandler is null for quest expedition! {0}",
                        questStepTargets.First);
                    ReturnToBase(exp.S);
                    return this;
                }
            }

            return handler.RunExpedition(exp, this, ticks);
        }

        public void RemovePOI(Expedition exp) {
            SOUtils.RemoveSO(exp.Target);
            // target needs to exist or it will go wrong in save / load
            exp.SetTarget(exp.S.Universe.Player);
        }

        public IExpeditionStage ReturnToBase(GameState s) {
            nextStageType = ExpeditionStageTypeH.PrepForReturn;
            nextStage = ExpeditionStages.Find(nextStageType);
            return nextStage;
        }
    }
}
