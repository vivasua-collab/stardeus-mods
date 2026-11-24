using System;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Economy;
using Game.Data.Space;
using Game.Input;
using Game.Systems.Trade;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    public abstract class BaseSignalTransactionQuestStep : BaseQuestStep,
            IQuestStepWithTarget {
        private static readonly string[] RetryMessageKeys = {
            "sig.transaction.failed.can.retry.*"
        };

        public override bool SaveLoadTexts => true;
        protected readonly SpaceObject signal;
        private readonly QuestStepTarget target;

        protected BaseSignalTransactionQuestStep(string id, GameState s,
                QuestStep step, bool isLoading)
                    : base(id, s, step, isLoading) {
            var signalVarId = config.GetString(PropertyIdH.Var_Signal, null);
            if (signalVarId != null) {
                var variable = step.Data.GetQuestVar(signalVarId);
                if (variable is not IQuestStepTargetVar targetVar) {
                    throw new Exception(
                        $"Signal transaction Var_Signal is not a target var: {variable} | {this}");
                }
                target = targetVar.GetTargetFor(step);
                signal = target?.Target?.ProviderSO;
            } else {
                signal = step.Data.Provider?.ProviderSO;
                target = new QuestStepTarget {
                    Target = SOUtils.QuestProviderFor(signal),
                    Step = step,
                };
            }
            if (signal == null || target?.Target == null) {
                if (isLoading && step.Data.IsEnded) {
                    return;
                }
                throw new Exception($"Signal transaction has no Signal provider: {step.Data} | {this}");
            }
            target.OnInteract = OnFulfillAttempt;
            target.KeepSOOnInteract = true;

            if (!isLoading) {
                AppendRequestMessage();
            }
            if (isLoading && step.IsActive) {
                RegisterTarget();
            }
        }

        protected abstract void OnFulfillAttempt();
        protected abstract string RequestSummary { get; }

        private void AppendRequestMessage() {
            var keys = config.GetStringSet(PropertyIdH.RequestMsgKeys, null);
            if (Arrays.IsEmpty(keys)) {
                throw new Exception($"Signal transaction has no RequestMsgKeys: {this}");
            }
            var key = Texts.RandomKey(step.SeededRng, keys, this);
            if (key == null) {
                throw new Exception($"Signal transaction request translation is missing: {this}");
            }
            var argTokens = config.GetStringSet(PropertyIdH.RequestMsgArgs, null);
            string message;
            if (Arrays.IsEmpty(argTokens)) {
                message = key.T();
            } else {
                var args = new string[argTokens.Length];
                for (int i = 0; i < argTokens.Length; i++) {
                    args[i] = ResolveRequestArgument(argTokens[i]);
                }
                message = key.T(args);
            }
            if (string.IsNullOrWhiteSpace(step.Data.Desc)) {
                step.Data.Desc = message;
            } else {
                step.Data.Desc = message;
            }
        }

        private string ResolveRequestArgument(string token) {
            var separator = token?.IndexOf(':') ?? -1;
            if (separator <= 0) {
                throw new FormatException($"Invalid Signal request argument: {token} | {this}");
            }
            var type = token.Substring(0, separator);
            var value = token.Substring(separator + 1);
            switch (type) {
                case "Def": {
                    var defId = step.Data.Data?.GetString(value, null);
                    var def = The.Defs.Get(defId);
                    if (def == null) {
                        throw new Exception(
                            $"Signal request argument def not found: {value}={defId} | {this}");
                    }
                    return def.CodexLink;
                }
                case "Money": {
                    if (step.Data.Data == null
                            || !step.Data.Data.TryGetFloat(value, out var money)
                            || !float.IsFinite(money)) {
                        throw new Exception(
                            $"Signal request argument money not found: {value} | {this}");
                    }
                    return Wallet.ToValueString(money);
                }
                case "Int": {
                    if (step.Data.Data == null
                            || !step.Data.Data.TryGetInt(value, out var integer)) {
                        throw new Exception(
                            $"Signal request argument int not found: {value} | {this}");
                    }
                    return integer.ToString();
                }
                case "Text":
                    return value;
                default:
                    throw new FormatException(
                        $"Unknown Signal request argument type: {type} | {this}");
            }
        }

        protected bool TryGetTradePortal(out TradePortalComp tradePortal) {
            tradePortal = TradeUI.FindTradePortal(S);
            if (tradePortal != null) {
                return true;
            }
            var anyTradePortal = S.Components.FindFirstByType<TradePortalComp>();
            string reason;
            if (anyTradePortal != null) {
                reason = $"<br>{Texts.Red(T.NotReady)}: {Texts.LinkToTile(anyTradePortal.Tile)}.";
            } else {
                var portalLink = Texts.LinkToCodex(DefIdsH.ObjectsDevicesTradePortal);
                reason = $"<br>{Texts.Red(T.NotAvailable)}: {portalLink}.";
            }
            ShowRetryFailure(reason);
            return false;
        }

        protected void ShowRetryFailure(string reason) {
            var key = Texts.RandomKey(step.SeededRng, RetryMessageKeys, this);
            var flavor = key?.T();
            var message = $"{T.SigTransactionFailed} {reason}";
            if (!string.IsNullOrWhiteSpace(flavor)) {
                message = $"{message}{GlyphMap.BRHR}{flavor}";
            }
            The.Dialog.ShowSimple(IconId.CSignal, message);
        }

        private void RegisterTarget() {
            if (target != null && !target.IsRegistered) {
                S.Query.RegisterQuestStepTarget.Ask(target, true);
            }
        }

        private void UnregisterTarget() {
            if (target?.IsRegistered == true) {
                S.Query.RegisterQuestStepTarget.Ask(target, false);
            }
        }

        public override void OnStateChange(QuestStepState before, QuestStepState after) {
            if (after == QuestStepState.Active) {
                RegisterTarget();
            } else if (after == QuestStepState.Completed || after == QuestStepState.Failed) {
                UnregisterTarget();
            }
        }

        public override void OnQuestStateChange(QuestState before, QuestState after) {
            if ((after & QuestState.Ended) != QuestState.Undefined) {
                UnregisterTarget();
            }
            if (after == QuestState.Completed || after == QuestState.Failed
                    || after == QuestState.Rejected) {
                if (signal?.Parent != null) {
                    SOUtils.RemoveSO(signal);
                }
            }
        }

        public override void Cleanup() {
            UnregisterTarget();
        }

        public QuestStepTarget GetTarget() {
            return target;
        }

        public string DealSummary() {
            var rewardSummary = "???";
            if (step.Data.Rewards != null) {
                for (int i = 0; i < step.Data.Rewards.Length; i++) {
                    var reward = step.Data.Rewards[i];
                    if (reward.Flags.HasFlag(QuestRewardFlags.Penalty)
                            || reward.Flags.HasFlag(QuestRewardFlags.PackageContent)) {
                        continue;
                    }
                    if (reward.Flags.HasFlag(QuestRewardFlags.Surprise)) {
                        break;
                    }
                    if (reward is Money_FromVarQuestReward moneyReward) {
                        rewardSummary = moneyReward.SignalSummary;
                    } else if (reward is EntityAtTradePortalQuestReward entityReward) {
                        rewardSummary = entityReward.SignalSummary;
                    }
                    break;
                }
            }
            return $"{RequestSummary} {GlyphMap.ArrowRight} {rewardSummary}";
        }
    }
}
