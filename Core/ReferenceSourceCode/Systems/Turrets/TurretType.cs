using System;

namespace Game.Systems.Turrets {
    [Flags]
    public enum TurretType : byte {
        Undefined,
        /// <summary>
        /// On-board offensive turret against beings
        /// </summary>
        AntiPersonnel,
        /// <summary>
        /// Offensive turret for ship to ship combat
        /// </summary>
        ShipCombat,
        /// <summary>
        /// Defense against incoming missiles, asteroids, meteoroids
        /// </summary>
        // The values are implicit (0, 1, 2, 3) despite [Flags], so this is 3, the same bits as
        // AntiPersonnel | ShipCombat: a turret tagged PointDefense would pass every
        // "(Type & AntiPersonnel)" check (TurretsSys, RotatingTurretComp.ShootsBeings) and
        // count as an anti-personnel and a ship combat turret at once. Nothing assigns it yet
        // (Interceptors register as ShipCombat); give it its own bit (1 << 2) before using it.
        PointDefense,
    }
}
