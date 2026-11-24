using Game.Data;
using Game.Systems.Turrets;
using UnityEngine;

namespace Game.Systems.Ships {
    public interface IShipWeaponTarget {
        uint Id { get; }
        public Def Def { get; }
        Vector2 Position { get; }
        Vector2 TargetSize { get; }
        string TargetName { get; }
        NPCShip Ship { get; }
        ShipWeaponTargetTypeDef TargetType { get; }
        ITurretTarget TurretTarget { get; }
        bool IsDestroyed { get; }
        int HP { get; }
        int MaxHP { get; }
        void OnHit(GameState s, ShipWeaponShot shot);
    }
}
