namespace Game.Systems.Ships {
    public enum ShipWeaponPhase {
        Undefined,
        Inactive,
        Windup,
        Charging,
        Ready,
        Firing,
        Disabled,
        OutOfAmmo,
    }
}
