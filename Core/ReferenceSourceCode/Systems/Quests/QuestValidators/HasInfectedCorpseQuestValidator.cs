using Game.Data;
using Game.Utils;

namespace Game.Systems.Quests {
    // Holds Tutorial/DisposeOfInfectedBodies until an infected humanoid corpse is actually
    // aboard. Bodies can arrive in any scenario (wrecked ship generation, a dropped stasis
    // pod's dead travelers), so a scenario filter would be wrong in both directions.
    public sealed class HasInfectedCorpseQuestValidator : IQuestValidator {
        [SelfInit]
        private static void Register() {
            Registries.QuestValidators.Add(TypeId, new HasInfectedCorpseQuestValidator());
        }
        public const string TypeId = "HasInfectedCorpse";
        public string Type => TypeId;
        public bool CanStartQuest(GameState s, QuestDef def) {
            return QuestUtils.HasInfectedCorpse(s);
        }
    }
}
