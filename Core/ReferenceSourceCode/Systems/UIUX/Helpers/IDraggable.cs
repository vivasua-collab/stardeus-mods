using UnityEngine;

namespace Game.Systems.UIUX {
    public interface IDraggable {
        bool IsDestroyed { get; }
        void TryDrag(Vector2 delta);
        void OnEndDrag();
    }
}
