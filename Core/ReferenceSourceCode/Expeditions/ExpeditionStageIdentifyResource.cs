using Game.Constants;
using Game.Data;
using Game.Data.Planets;
using Game.Data.Space;
using Game.Story.Events;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Mental;

namespace Game.Expeditions {
    public sealed class ExpeditionStageIdentifyResource : BaseExpeditionStage {
        [SelfInit]
        private static void Register() {
            ExpeditionStages.Register(ExpeditionStageTypeH.IdentifyResource, () => {
                return new ExpeditionStageIdentifyResource();
            });
        }

        public override int StageType => ExpeditionStageTypeH.IdentifyResource;
        public override bool IsStageCancellable => true;
        private string title;
        private string titleTooltip;
        public override string Title => title ?? T.ExpStageExploring;
        public override string TitleTooltip => titleTooltip;
        private long identifyTick;

        public override string MiniWidgetStatusIcon => IconId.CMagnifier;

        public ExpeditionStageIdentifyResource() {
            MiniWidgetText.Set(T.VerbScanning);
        }

        public override bool CanCallBack(Expedition exp) {
            return true;
        }

        public override bool TryCancelStage(out int nextStage) {
            nextStage = ExpeditionStageTypeH.PrepForReturn;
            return true;
        }

        public override IExpeditionStage Tick(Expedition exp, long ticks) {
            float expeditionFac = exp.ExpeditionFactor;
            if (expeditionFac < 1f) {
                expeditionFac = 1f;
            }
            if (identifyTick == 0) {
                identifyTick = ticks + Mathf.RoundToInt(exp.S.Rng.Range(
                    Consts.TicksPer3Hours, Consts.TicksPer4Hours) / expeditionFac);
                return this;
            }
            if (identifyTick > ticks) {
                foreach (var mem in exp.Crew) {
                    if (mem.Mind.Jobs.TryJob(JobTypeIdH.Expedition, out var expJob)) {
                        expJob.TrainSkill(15f * SkillXp.Default, false);
                    }
                }
                return this;
            }

            var poiSO = exp.Target;
            SOActionScan.ScanResource(poiSO, out var matType);

            if (matType != null) {
                exp.S.Sys.Log.AddLine(T.ExpFoundResource_V2.F(exp.Shuttle.Name ?? T.Expedition,
                    matType.Def.CodexLink), Texts.TMPColorGreen);
            } else {
                D.Err("SOActionScan.ScanResource returned null mat type for {0}", poiSO);
            }

            exp.Context.RebuildUI();

            return ExpeditionStages.Find(ExpeditionStageTypeH.MineResource);
        }
    }
}
