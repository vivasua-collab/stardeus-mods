using Game.Data;
using Game.Systems.Trade;
using Game.Utils;

namespace Game.Systems.Quests {
    /// <summary>
    /// Passes only when the colony has an active (constructed and powered) trade portal —
    /// required for a delivery quest passenger to arrive. Gates the quest so it is not offered
    /// when arrival would be impossible.
    /// </summary>
    public sealed class HasActiveTradePortalQuestValidator : IQuestValidator {
        [SelfInit]
        private static void Register() {
            Registries.QuestValidators.Add(TypeId, new HasActiveTradePortalQuestValidator());
        }
        public const string TypeId = "HasActiveTradePortal";
        public string Type => TypeId;
        public bool CanStartQuest(GameState s, QuestDef def) {
            return TradeUI.FindTradePortal(s) != null;
        }
    }
}
