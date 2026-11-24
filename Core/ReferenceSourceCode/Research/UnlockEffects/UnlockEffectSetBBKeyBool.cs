using Game.Data;

namespace Game.Research {
    public abstract class UnlockEffectSetBBKeyBool : IResearchUnlockEffect {
        public abstract string Icon { get; }
        public abstract string Title { get; }
        public abstract string Tooltip { get; }
        protected abstract void Notify();

        public void OnUnlock(GameState s, bool notify) {
            s.BB.Set(key, true);
            if (notify) {
                Notify();
            }
        }

        protected readonly int key;
        protected UnlockEffectSetBBKeyBool(int key) {
            this.key = key;
        }
    }
}
