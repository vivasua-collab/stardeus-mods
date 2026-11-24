using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    /// <summary>
    /// Fails the quest once no usable shuttle can exist any more. Counting tiles by def
    /// (RequireDeviceToExist) is wrong here in both directions: a deserter leaves the tile in
    /// place with IsLost set, and a destroyed shuttle auto-rebuilds into a blueprint that the
    /// player may never fund - both would keep a shuttle quest alive forever.
    /// </summary>
    public sealed class RequireShuttleQuestStep : BaseQuestStep, ITickListener {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(StepId,
                (s, c, l) => new RequireShuttleQuestStep(s, c, l));
        }
        public const string StepId = "RequireShuttle";

        public RequireShuttleQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(StepId, s, step, isLoading) {
            if (!step.Def.Flags.HasFlag(QuestStepFlags.Secret)) {
                D.Throw("This step is supposed to have Secret flag! {0}", step);
            }
        }

        public void Tick(long tick) {
            if (tick % Consts.TicksPerHour == 47) {
                if (S.Components.CountMatching(ShuttleComp.IsUsable, max: 1) == 0) {
                    step.SetState(QuestStepState.Failed);
                }
            }
        }
    }
}
