using System.Collections.Generic;
using Game.Systems.Creatures;

namespace Game.Systems.AI.Combat {
    public interface IAttackMethodProvider {
        bool HasRequiredAbilities(CombatUnit unit);
        string ItemName { get; }
        string SlotName { get; }
        float Quality { get; }
        bool IsBodyPart { get; }
    }
}
