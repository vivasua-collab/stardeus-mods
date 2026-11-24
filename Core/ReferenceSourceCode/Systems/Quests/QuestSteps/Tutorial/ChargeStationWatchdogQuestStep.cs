using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.Quests {
    public sealed class ChargeStationWatchdogQuestStep : BaseQuestStep, ITickListener {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new ChargeStationWatchdogQuestStep(s, c, l));
        }

        public const string CheckId = "ChargeStationWatchdog";

        public ChargeStationWatchdogQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
        }

        public void Tick(long ticks) {
            if (!step.IsActive || ticks % Consts.TicksPer5Minutes != 0) { return; }
            CheckAndComplete();
        }

        private void CheckAndComplete() {
            foreach (var node in A.Grid.NodeComponents.Values) {
                if (node.Node.IsReachable
                        && node.IsPowered
                        && node.Grid.MaxOutputWithoutBat > 0
                        && node.Tile.Definition.IdH == DefIdsH.ObjectsDevicesChargeStation) {

                    CompleteStep();
                    return;
                }
            }
        }
    }
}
