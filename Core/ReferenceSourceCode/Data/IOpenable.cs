using Game.Components;

namespace Game.Data {
    public interface IOpenable : IComponent {
        void Open(Being opener);
    }
}
