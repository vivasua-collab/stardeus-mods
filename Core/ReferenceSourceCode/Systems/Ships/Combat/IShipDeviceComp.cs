using Game.Components;
using Game.Data;

namespace Game.Systems.Ships {
    public interface IShipDeviceComp : IComponent {
        ShipDeviceDef DeviceDef { get; }
    }
}
