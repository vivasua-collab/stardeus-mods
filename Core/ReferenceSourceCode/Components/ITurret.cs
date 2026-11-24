namespace Game.Components {
    public interface ITurret : IComponent {
        bool IsTurretUsable { get; }
        bool TryShootAt(IThreat target, long tick);
        bool CanHandle(IThreat threat);
    }
}
