using UnityEngine;

namespace Game.Components {
    public interface IRanged {
        float Range { get; }
        Color RadiusColor { get; }
        bool RadiusSameArea { get; }
    }
}
