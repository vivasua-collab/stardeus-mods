using Game.Data;
using Game.Utils;

namespace Game.Systems.Quests {
    public sealed class HasLocalDepositWithSellableMatQuestValidator : IQuestValidator {
        [SelfInit]
        private static void Register() {
            Registries.QuestValidators.Add(TypeId, new HasLocalDepositWithSellableMatQuestValidator());
        }
        public const string TypeId = "HasLocalDepositWithSellableMat";
        public string Type => TypeId;
        public bool CanStartQuest(GameState s, QuestDef def) {
            if (A.IsInHyperspace) { return false; }
            var sector = A.PlayerSector.Sector;
            if (sector == null) { return false; }
            return ResourceDepositQuestVar.CanProvideSellableDeposit(s, sector);
        }
    }
}
