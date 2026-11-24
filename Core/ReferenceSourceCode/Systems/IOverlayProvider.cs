using System.Collections.Generic;

namespace Game.Systems {
    public interface IOverlayProvider {
        List<OverlayInfo> Overlays { get; }
    }
}
