using Game.Data;
using UnityEngine;

namespace Game.Components {
    public interface ICanBeIdle : IComponent {
        string IdleCategory { get; }
        Color ProductColor { get; }
        Def ProductDef { get; }
        Tile DeviceTile { get; }
        string IdleProducts { get; }
        string IdleTooltip { get; }
    }
}
