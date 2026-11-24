using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;

namespace Game.Expeditions {
    public sealed class ExpeditionStageUnload : BaseExpeditionStage {
        public override bool CanCallBack(Expedition exp) {
            return true;
        }
        public override bool IsReturning => false;

        public override string Title => T.ExpStageUnloading;
        public override int StageType => ExpeditionStageTypeH.Unload;

        public override string MiniWidgetStatusIcon => IconId.CPickaxe;

        public ExpeditionStageUnload() {
            MiniWidgetText.Set(T.VerbUnloading);
        }


        [SelfInit]
        private static void Register() {
            ExpeditionStages.Register(ExpeditionStageTypeH.Unload, () => {
                return new ExpeditionStageUnload();
            });
        }

        private bool hasDumped;

        public override IExpeditionStage Tick(Expedition exp, long ticks) {
            if (!hasDumped) {
                hasDumped = true;
                exp.DumpLoot();
                return this;
            }
            exp.IsUnloading = false;
            return ExpeditionStages.Find(ExpeditionStageTypeH.Prep);
        }
    }
}
