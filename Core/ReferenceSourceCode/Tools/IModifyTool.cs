namespace Game.Tools {
    public interface IModifyTool {
        bool CanBuild { get; }
        bool CanRemove { get; }
    }
}
