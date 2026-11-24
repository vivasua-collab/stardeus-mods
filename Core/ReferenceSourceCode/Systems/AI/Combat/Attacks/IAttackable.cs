using Game.Data;
using UnityEngine;

namespace Game.Systems.Combat {
    public interface IAttackable : INamedEntity {
        Vector2 AttackablePosition { get; }
        int AttackablePosIdx { get; }
        string[] TakeDamageSounds { get; }
    }
}
