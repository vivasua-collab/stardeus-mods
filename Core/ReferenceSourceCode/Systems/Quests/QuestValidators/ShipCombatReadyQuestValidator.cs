using Game.Data;
using Game.Utils;

namespace Game.Systems.Quests {
    // Holds back quests that send the player to destroy a ship until the ship can fire:
    // a weapon on a powered battle station, the same bar as joining an NPC battle. Live
    // ship state rather than the MakeShipCombatReady tutorial bool, because scenarios
    // without the tutorial never set that bool, and failing the tutorial sets it too.
    public sealed class ShipCombatReadyQuestValidator : IQuestValidator {
        [SelfInit]
        private static void Register() {
            Registries.QuestValidators.Add(TypeId, new ShipCombatReadyQuestValidator());
        }
        public const string TypeId = "ShipCombatReady";
        public string Type => TypeId;
        public bool CanStartQuest(GameState s, QuestDef def) {
            return s.Universe.Player.Ship?.CombatState.WeaponCombatStrength > 0;
        }
    }
}
