using Game.Data;

namespace Game.Components {
    public interface ICanBeUnconfigured : IComponent {
        string DeviceCategory { get; }
        Tile DeviceTile { get; }
    }
}
