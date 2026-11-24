using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Economy;
using Game.Utils;

namespace Game.Systems.Quests {
    public sealed class PayMoneyViaTradePortalQuestStep : BaseSignalTransactionQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new PayMoneyViaTradePortalQuestStep(s, c, l));
        }

        public const string CheckId = "PayMoneyViaTradePortal";
        private readonly float money;
        protected override string RequestSummary => Wallet.ToValueString(money);

        public PayMoneyViaTradePortalQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            var moneyKey = config.GetString(PropertyIdH.DVar_Money, null);
            if (string.IsNullOrWhiteSpace(moneyKey)) {
                throw new Exception($"PayMoneyViaTradePortal requires DVar_Money: {this}");
            }
            money = step.Data.Data?.GetFloat(moneyKey, 0f) ?? 0f;
            if (!float.IsFinite(money) || money <= 0f) {
                throw new Exception($"PayMoneyViaTradePortal has invalid amount {money}: {this}");
            }
            SetTexts(isLoading);
        }

        private void SetTexts(bool isLoading) {
            SetInactiveTexts(step);
            if (isLoading) { return; }
            var moneyText = Wallet.ToValueString(money);
            var signalLink = signal.StarmapLink;
            var titleKey = step.Def.GetTitleKey(step.Data);
            step.Title = titleKey != null
                ? titleKey.T(moneyText, signalLink)
                : T.QuestStepPaySignalTitle_V1.F(moneyText);
            var descKey = step.Def.GetDescKey(step.Data);
            step.Desc = descKey != null
                ? descKey.T(moneyText, signalLink)
                : T.QuestStepPaySignalDesc_V2.F(moneyText, signalLink);
        }

        protected override void OnFulfillAttempt() {
            if (!TryGetTradePortal(out _)) { return; }
            if (!S.Wallet.TryPay(money, null)) {
                ShowRetryFailure(T.TradeNotEnoughCash);
                return;
            }
            CompleteStep();
        }
    }
}
