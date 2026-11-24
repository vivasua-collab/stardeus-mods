using Game.Components;
using Game.Data;
using UnityEngine;

namespace Game.Systems.Turrets {
    public interface ITurretTarget {
        Vector2 Position { get; }
    }
}
