using System;
using Game.Constants;
using Game.Data;
using Game.Systems.Trade;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class ExecuteTradeQuestStep : BaseQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(StepId,
                (s, c, l) => new ExecuteTradeQuestStep(s, c, l));
        }
        public const string StepId = "ExecuteTrade";
        private bool isSubscribed;
        public ExecuteTradeQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(StepId, s, step, isLoading) {
            step.Title = T.TradeExecute;
            SetDesc(T.QuestStepExecuteTradeTip);
            if (isLoading && step.IsActive) {
                Subscribe();
            }
        }

        public override void OnStateChange(QuestStepState before, QuestStepState after) {
            if (after == QuestStepState.Active) {
                Subscribe();
            }
        }

        private void Subscribe() {
            if (!isSubscribed) {
                isSubscribed = true;
                S.Sig.TradeExecuted.AddListener(OnExecuted);
            }
        }

        private void OnExecuted(TradeUI trade) {
            var soId = trade.Trader.SOId;
            if (soId != 0) {
                var so = S.Universe.Find(soId);
                if (so != null && so.Ship?.Class.IsSpaceStation == true) {
                    CompleteStep();
                }
            }
        }

        public override void Cleanup() {
            if (isSubscribed) {
                isSubscribed = false;
                S.Sig?.TradeExecuted.RemoveListener(OnExecuted);
            }
        }

    }
}
