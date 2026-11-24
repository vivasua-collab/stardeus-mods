namespace Game.Components {
    // Explosives the player can set off on demand from the bottom bar.
    // Deliberately does not cover arming: C42 arms for proximity triggering,
    // the Resonance Charge has no trigger other than the player.
    public interface IDetonatable : IComponent {
        bool CanTriggerDetonation { get; }
        void TriggerDetonation();
    }
}
