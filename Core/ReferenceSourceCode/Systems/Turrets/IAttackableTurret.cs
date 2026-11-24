using Game.Components;
using Game.Data;
using Game.Systems.Combat;

namespace Game.Systems.Turrets {
    public interface IAttackableTurret : IAttackable, IComponent {
        public bool IsScanning { get; }
        public Tile TurretTile { get; }
        // False for ship weapons, which never fire at beings: raiders only go after those for
        // a while (see AICombatActionAttackTurret.MayAttack)
        public bool ShootsBeings { get; }
    }
}
