using Game.CodeGen;

namespace Game.Constants {
    [GenerateHashes]
    public static class ShipWeaponTargetTypeId {
        public const string Meteoroid = nameof(Meteoroid);
        public const string Asteroid = nameof(Asteroid);
        public const string BreachCapsule = nameof(BreachCapsule);
        public const string ShipHull = nameof(ShipHull);
        public const string ShipCapacitor = nameof(ShipCapacitor);
        public const string ShipEngine_Hyperdrive = nameof(ShipEngine_Hyperdrive);
        public const string ShipEngine_Thruster = nameof(ShipEngine_Thruster);
        public const string ShipShield = nameof(ShipShield);
        public const string ShipWeapon_Ballistic = nameof(ShipWeapon_Ballistic);
        public const string ShipWeapon_Laser = nameof(ShipWeapon_Laser);
        public const string ShipWeapon_Missile = nameof(ShipWeapon_Missile);
    }
}
