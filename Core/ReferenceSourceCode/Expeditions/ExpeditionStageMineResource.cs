using Game.Constants;
using Game.Data;
using Game.Story.Events;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Creatures;
using Game.Systems.UIUX;
using Game.Systems.Mental;

namespace Game.Expeditions {
    public sealed class ExpeditionStageMineResource : BaseExpeditionStage {
        [SelfInit]
        private static void Register() {
            ExpeditionStages.Register(ExpeditionStageTypeH.MineResource, () => {
                return new ExpeditionStageMineResource();
            });
        }

        public override int StageType => ExpeditionStageTypeH.MineResource;
        public override bool IsStageCancellable => true;
        public override string MiniWidgetStatusIcon => IconId.CPickaxe;
        public override bool MiniWidgetShowsStorage => true;

        private string title;
        private string titleNotification;
        private string titleTooltip;
        public override string Title => title;
        public override string TitleNotification => titleNotification ?? title;
        public override string TitleTooltip => titleTooltip;
        private long lastTick;
        private readonly B<string> titleB = new(null);
        public override B<string> TitleB => titleB;

        public override bool CanCallBack(Expedition exp) {
            return true;
        }

        public override bool TryCancelStage(out int nextStage) {
            nextStage = ExpeditionStageTypeH.PrepForReturn;
            return true;
        }

        public override IExpeditionStage Tick(Expedition exp, long ticks) {
            if (lastTick == 0) {
                lastTick = ticks;
                return this;
            }
            var remainingStorage = exp.Shuttle.Tile.MaterialStorage.AvailableCapacity;
            if (remainingStorage < 1) {
                exp.IsUnloading = exp.MiningOrders.IsAutoUnload;
                return ExpeditionStages.Find(ExpeditionStageTypeH.PrepForReturn);
            }

            // stop quest selection from re-offering a deposit we've actually mined
            exp.Target.MarkLocalVisited();

            if (exp.S.Sys.Planets.TryManualMine(exp.Target, exp, remainingStorage,
                    out int amt, out MatType type, out var rateWithOutputMul, out var difficulty,
                    out var depth)) {
                if (exp.Shuttle.Store(type, amt) != 0) {
                    // Storage is full
                    exp.IsUnloading = exp.MiningOrders.IsAutoUnload;
                    return ExpeditionStages.Find(ExpeditionStageTypeH.PrepForReturn);
                }
                // Level up worker mining skill
                for (int i = exp.Crew.Count - 1; i >= 0; i--) {
                    BeingData w = exp.Crew[i];
                    // If expedition tick will change, level up rate should be adjusted accordingly
                    if (w.Mind.Jobs.TryJob(JobTypeIdH.Mining, out var job)) {
                        if (job.Incapable) {
                            continue;
                        }
                        // w.Skills.Advance(SkillId.Mining, false, lvlUpRate);
                        job.TrainSkill(Tunables.MiningLevelUpRate * SkillXp.Default, false);
                        // D.Warn("Increasing mining skill with lvlup rate: {0}: {1} -> {2}",
                        //     lvlUpRate, job.LevelProgress, w);
                    }
                }
            } else if (rateWithOutputMul <= exp.MiningOrders.MinRate) {
                // mining too slowly
                exp.IsUnloading = false;
                return ExpeditionStages.Find(ExpeditionStageTypeH.PrepForReturn);
            }
            if (type != null) {
                var depthText = Units.Depth(depth);
                var storage = exp.Shuttle.Tile.MaterialStorage;
                MiniWidgetText.Set(Units.Num(storage.UsedCapacity));
                var storageInfo = Units.XOutOfY(storage.UsedCapacity, storage.MaxCapacity);
                float storageFillPct = storage.FillPercent;
                if (storageFillPct < 0.5f) {
                    storageInfo = Texts.Green(storageInfo);
                } else if (storageFillPct < 0.8f) {
                    storageInfo = Texts.Yellow(storageInfo);
                } else {
                    storageInfo = Texts.Red(storageInfo);
                }
                title = type.Deposit.MineText(rateWithOutputMul, depthText);
                titleB.Set(title);
                titleNotification = type.Deposit.MineShuttleText(rateWithOutputMul, storageInfo);
                subtitle.Set(T.EstMiningRate_V1.F(rateWithOutputMul));
                //title = T.ExpMineResourceTitle_V2.F(type.NameT, rate);
                titleTooltip = string.Format("{0}: <b>{1:0.0}</b>\n{2}: <b>{3}</b>\n{4}: {5}",
                    T.MiningDifficulty, difficulty, T.NounDepth, depthText,
                        T.Storage, storageInfo);
            }
            return this;
        }
    }
}
