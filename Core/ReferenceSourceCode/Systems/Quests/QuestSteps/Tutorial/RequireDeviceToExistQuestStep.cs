using System;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Utils;
using Unity.Mathematics;

namespace Game.Systems.Quests {
    public sealed class RequireDeviceToExistQuestStep : BaseQuestStep, ITickListener {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(StepId,
                (s, c, l) => new RequireDeviceToExistQuestStep(s, c, l));
        }
        public const string StepId = "RequireDeviceToExist";
        private Def def;
        public RequireDeviceToExistQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(StepId, s, step, isLoading) {
            if (!step.Def.Flags.HasFlag(QuestStepFlags.Secret)) {
                D.Throw("This step is supposed to have Secret flag! {0}", step);
            }
            var cfg = step.Def.Config;
            var defId = cfg.GetString(PropertyIdH.Definition);
            def = The.Defs.Get(defId);
            if (def == null) {
                D.Throw("Misconfigured step, no def by id: {0} | {1}", defId, step);
            }
        }

        public void Tick(long tick) {
            if (tick % Consts.TicksPerHour == 47) {
                if (S.CountByDef(def) == 0) {
                    step.SetState(QuestStepState.Failed);
                }
            }
        }
    }
}
