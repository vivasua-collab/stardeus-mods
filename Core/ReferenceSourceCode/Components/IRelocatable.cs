using Game.Data;

namespace Game.Components {
    public interface IRelocatable : IComponent {
        void RelocateTo(Entity target);
    }
}
