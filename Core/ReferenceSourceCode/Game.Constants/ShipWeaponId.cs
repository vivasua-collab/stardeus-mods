using Game.CodeGen;

namespace Game.Constants {
    [GenerateHashes]
    public static class ShipWeaponId {
        public const string ShredderCannon = nameof(ShredderCannon);
        public const string InterceptorTurret = nameof(InterceptorTurret);
        public const string HeavyLaser = nameof(HeavyLaser);
        public const string RocketPod = nameof(RocketPod);
        public const string GaussStriker = nameof(GaussStriker);
    }
}
