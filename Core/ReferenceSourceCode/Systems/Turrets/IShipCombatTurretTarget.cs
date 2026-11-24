using Game.Data;
using Game.Systems.Ships;
using UnityEngine;

namespace Game.Systems.Turrets {
    public interface IShipCombatTurretTarget : ITurretTarget {
        bool IsThreatActive { get; }
        ShipWeaponTargetTypeDef TargetType { get; }
        Entity Entity { get; }
        Vector2 PredictPosition(float afterSeconds);
        float TurretEvadePercent { get;}
        float PointDefenseEvadePercent { get;}
    }
}
