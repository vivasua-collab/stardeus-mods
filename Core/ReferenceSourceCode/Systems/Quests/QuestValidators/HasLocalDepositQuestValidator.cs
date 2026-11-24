using Game.Data;
using Game.Utils;

namespace Game.Systems.Quests {
    public sealed class HasLocalDepositQuestValidator : IQuestValidator {
        [SelfInit]
        private static void Register() {
            Registries.QuestValidators.Add(TypeId, new HasLocalDepositQuestValidator());
        }
        public const string TypeId = "HasLocalDeposit";
        public string Type => TypeId;
        public bool CanStartQuest(GameState s, QuestDef def) {
            if (A.IsInHyperspace) {
                return false;
            }
            return A.PlayerSector.HasAnyMats;
        }
    }
}
