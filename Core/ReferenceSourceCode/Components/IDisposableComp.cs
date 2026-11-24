namespace Game.Components {
    public interface IDisposableComp {
        bool CanDisposeNow(out string reason);
    }
}
