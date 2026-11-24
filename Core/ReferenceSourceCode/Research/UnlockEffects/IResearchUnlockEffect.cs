using Game.Data;

namespace Game.Research {
    public interface IResearchUnlockEffect {
        string Title { get; }
        string Tooltip { get; }
        string Icon { get; }
        void OnUnlock(GameState s, bool notify);
    }
}
