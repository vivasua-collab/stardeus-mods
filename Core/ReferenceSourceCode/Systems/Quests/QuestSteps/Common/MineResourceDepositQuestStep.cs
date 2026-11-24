using System;
using System.Data;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Expeditions;
using Game.Utils;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class MineResourceDepositQuestStep : BaseQuestStep,
            IQuestStepWithTarget, IQuestExpeditionObserver {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new MineResourceDepositQuestStep(s, c, l));
        }
        public const string CheckId = "MineResourceDeposit";
        public const string Key_AmtMined = "AmtMined";
        public override bool SaveLoadTexts => true;
        private int amount;
        private int mined;
        private MatType matType;
        private QuestStepTarget target;
        public MineResourceDepositQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            var amtKey = step.Def.Config.GetString(PropertyIdH.DVar_Amount);
            var matTypeIdKey = step.Def.Config.GetString(PropertyIdH.DVar_MatTypeId);
            if (isLoading) {
                mined = step.Data.Data.GetInt(step.Def.DataKey(Key_AmtMined), 0);
                amount = step.Data.Data.GetInt(amtKey, 0);
                var matId = step.Data.Data.GetString(matTypeIdKey);
                matType = MatType.Get(matId);
                if (matType == null) {
                    throw new Exception($"Failed getting mat type by id: {matId}");
                }
            }
            var depositVar = GetQuestVarByKey(PropertyIdH.Var_ResourceDeposit);
            long ticksLeft = Consts.TicksPer3Days;
            if (step.Deadline > 0L) {
                ticksLeft = step.Deadline - s.Ticks;
            }
            var days = ticksLeft / (float) Consts.TicksPerDay;
            if (depositVar is IQuestStepTargetVar targetVar) {
                target = targetVar.GetTargetFor(step);
                target.ExpeditionObserver = this;
                if (target.Target is not SpaceObject targetSO) {
                    D.Throw("Target is not SO: {0} | {1}", target.Target, this);
                    return;
                }
                if (!isLoading) {
                    var miningPower = Expedition.BestPossibleMiningFactor(s, out var maxDepth);
                    if (!s.Sys.Planets.TryEstimateExpeditionMiningRate(targetSO, miningPower, out var rate, out matType)) {
                        D.Throw("No mineable resource at quest step target: {0} | {1}", targetSO, this);
                    }
                    // 1.0 is unachievable, 0.8 may be barely achievable, 0.5 should be easy but not too easy
                    var amountCurve = step.Def.Config.GetFloatSet(PropertyIdH.AmountCurve);
                    var amt = step.SeededRng.Curve(amountCurve);
                    const int MinAmount = 10;
                    var rawAmount = rate * days * amt * s.Difficulty.Reduction;
                    amount = Math.Max(MinAmount, (int) rawAmount);
                    if (rawAmount < MinAmount && rate > 0f && amt > 0f
                            && s.Difficulty.Reduction > 0f && step.Deadline > 0L) {
                        // Mining rate too low for the deadline — extend all pending step deadlines
                        var daysNeeded = MinAmount / (rate * amt * s.Difficulty.Reduction);
                        if (daysNeeded > days) {
                            var extraTicks = (long)((daysNeeded - days) * Consts.TicksPerDay) + 1L;
                            foreach (var qs in step.Data.Steps) {
                                if (qs.Deadline > 0L && !qs.IsEnded) {
                                    qs.Deadline += extraTicks;
                                }
                            }
                        }
                    }
                    // Physical ceiling: a depth-limited deposit can't deliver more than this
                    // before MiningDepth exceeds the best shuttle's reach and mining stalls.
                    if (s.Sys.Planets.TryGetMaxExtractableOutput(targetSO, maxDepth, out var maxOutput)) {
                        var physicalCap = (int) (maxOutput * 0.8f);
                        if (physicalCap > 0 && amount > physicalCap) {
                            amount = physicalCap;
                        }
                    }
                    var shuttleStorageCap = ResourceDepositQuestVar.GetLargestShuttleStorageCapacity(s);
                    if (shuttleStorageCap > 0 && amount > shuttleStorageCap) {
                        amount = shuttleStorageCap;
                    }
                    var d = step.Data.GetOrCreateData();
                    d.SetInt(amtKey, amount);
                }
            } else {
                throw new Exception($"MineResourceDeposit requires IQuestStepTargetVar, but {depositVar} is not that! {this}");
            }
            SetTexts(s, step, isLoading);
            if (isLoading && step.IsActive) {
                s.Query.RegisterQuestStepTarget.Ask(target, true);
            }
        }

        private void SetTexts(GameState s, QuestStep step, bool isLoading) {
            SetInactiveTexts(step);
            if (isLoading) { return; }
            string path = null;
            if (target.Target is SpaceObject so) {
                path = so.ParentPath(fromPlayer: false);
            }
            var depLink = target.Target.StarmapLink;
            var shutLink = Texts.LinkToCodex(DefIdsH.ObjectsDevicesShuttle);
            var titleKey = step.Def.GetTitleKey(step.Data);
            if (titleKey != null) {
                step.Title = titleKey.T(amount, matType.Def.CodexLink, depLink,
                    shutLink);
            } else {
                step.Title = T.QuestStepMineDepositTitle_V4.F(amount, matType.NameT,
                    depLink, shutLink);
            }
            var descKey = step.Def.GetDescKey(step.Data);
            if (descKey != null) {
                step.Desc = descKey.T(path, amount, matType.Def.CodexLink);
            } else {
                step.Desc = T.QuestStepMineDepositDesc_V3.F(path, amount, matType.NameT);
            }
        }

        public override void OnStateChange(QuestStepState before, QuestStepState after) {
            if (after == QuestStepState.Active) {
                if (target?.IsRegistered == false) {
                    S.Query.RegisterQuestStepTarget.Ask(target, true);
                }
            } else if (after == QuestStepState.Completed || after == QuestStepState.Failed) {
                UnregisterTarget();
            }
        }

        private void UnregisterTarget() {
            if (target?.IsRegistered == true) {
                S.Query.RegisterQuestStepTarget.Ask(target, false);
                target = null;
            }
        }

        public override void OnQuestStateChange(QuestState before, QuestState after) {
            if (after == QuestState.Completed || after == QuestState.Failed) {
                UnregisterTarget();
            }
        }

        public QuestStepTarget GetTarget() {
            return target;
        }

        public void OnExpeditionStageChange(Expedition exp,
                IExpeditionStage before, IExpeditionStage after) {
            if (step.IsEnded || after == null
                    || (after.StageType != ExpeditionStageTypeH.Unload
                        && after.StageType != ExpeditionStageTypeH.Done)) {
                return;
            }
            var gotAmt = exp.Shuttle.Tile.MaterialStorage.Available(matType);
            mined += gotAmt;
            if (mined >= amount) {
                CompleteStep();
            } else {
                var d = step.Data.GetOrCreateData();
                d.SetInt(step.Def.DataKey(Key_AmtMined), mined);
            }
        }
    }
}
