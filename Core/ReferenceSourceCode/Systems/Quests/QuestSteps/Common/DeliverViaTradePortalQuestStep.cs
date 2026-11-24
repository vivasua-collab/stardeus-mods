using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Trade;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class DeliverViaTradePortalQuestStep : BaseSignalTransactionQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new DeliverViaTradePortalQuestStep(s, c, l));
        }

        public const string CheckId = "DeliverViaTradePortal";
        private readonly Def requestDef;
        private readonly int amount;
        protected override string RequestSummary => $"{requestDef.NameT} x{amount}";

        public DeliverViaTradePortalQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            var defIdKey = config.GetString(PropertyIdH.DVar_DefId, null);
            var amountKey = config.GetString(PropertyIdH.DVar_Amount, null);
            if (string.IsNullOrWhiteSpace(defIdKey)
                    || string.IsNullOrWhiteSpace(amountKey)) {
                throw new Exception(
                    $"DeliverViaTradePortal requires def and amount DVars: {this}");
            }
            var defId = step.Data.Data?.GetString(defIdKey, null);
            requestDef = The.Defs.Get(defId);
            amount = step.Data.Data?.GetInt(amountKey, 0) ?? 0;
            if (requestDef == null || amount < 1) {
                throw new Exception(
                    $"DeliverViaTradePortal has invalid request {defId} x{amount}: {this}");
            }
            SetTexts(isLoading);
        }

        private void SetTexts(bool isLoading) {
            SetInactiveTexts(step);
            if (isLoading) { return; }
            var itemText = $"{requestDef.CodexLink} x{amount}";
            var signalLink = signal.StarmapLink;
            var titleKey = step.Def.GetTitleKey(step.Data);
            step.Title = titleKey != null
                ? titleKey.T(itemText, signalLink)
                : T.QuestStepDeliverItemTitle_V1.F(itemText);
            var descKey = step.Def.GetDescKey(step.Data);
            step.Desc = descKey != null
                ? descKey.T(itemText, signalLink)
                : T.QuestStepDeliverItemDesc_V2.F(itemText, signalLink);
        }

        protected override void OnFulfillAttempt() {
            if (!TryGetTradePortal(out _)) { return; }
            if (requestDef.MatType != null) {
                if (!S.Sys.Inventory.TrySell(requestDef.MatType, amount, true)) {
                    ShowRetryFailure(T.SigTransactionFailedLackItems);
                    return;
                }
                CompleteStep();
                return;
            }

            var objects = S.Query.GetAllObjs.Ask();
            Caches.ObjDataList.Clear();
            foreach (var obj in objects.Values) {
                if (obj.Def == requestDef && obj.CanSell) {
                    Caches.ObjDataList.Add(obj);
                    if (Caches.ObjDataList.Count == amount) {
                        break;
                    }
                }
            }
            if (Caches.ObjDataList.Count < amount) {
                ShowRetryFailure(T.SigTransactionFailedLackItems);
                return;
            }
            foreach (var obj in Caches.ObjDataList) {
                TradeUI.RemoveObjData(S, obj, this);
            }
            CompleteStep();
        }
    }
}
