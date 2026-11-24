namespace Game.Components {
    public interface IHaulTarget : IComponent {
        bool TryOrderHauling();
    }
}
