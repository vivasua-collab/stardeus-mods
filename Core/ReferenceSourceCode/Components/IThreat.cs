using Game.Data;

namespace Game.Components {
    public interface IThreat /*: IComponent*/ {
        Entity Entity { get; }
        int ThreatType { get; }
        bool IsThreatActive { get; }
        /// <summary>
        /// If this is true, soundtrack mood will be switched to "Danger"
        /// </summary>
        bool AffectsSoundtrack { get; }
    }
}
