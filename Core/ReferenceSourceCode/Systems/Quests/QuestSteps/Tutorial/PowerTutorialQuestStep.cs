using Game.Constants;
using Game.Data;

namespace Game.Systems.Quests {
    // Base for the ProvideChargeStation power steps. They used to react only to the one
    // event that completes them in the expected order (this connector built, that link
    // made), so any other order - a reactor built before the quest, a reactor configured
    // before it was connected, a connector placed next to an existing reactor - left a step
    // waiting for an event that had already happened. The tutorial could not be finished.
    //
    // Now every step is a check of the world state, repeated after anything that could have
    // changed it, plus a periodic backstop. Signals only raise a flag, and the check runs on
    // the next tick: completing a step from inside a grid rebuild's signal would unwind back
    // through the rebuild (see HaveOnGridQuestStep, which this generalises).
    public abstract class PowerTutorialQuestStep : BaseQuestStep, ITickListener,
            IPreCheckedQuestStep {
        private bool needsCheck;
        private bool isSubscribed;

        protected PowerTutorialQuestStep(string id, GameState s, QuestStep step,
                bool isLoading) : base(id, s, step, isLoading) {
        }

        // Called last in the subclass constructor, after its defs are resolved
        protected void Init(bool isLoading) {
            if (isLoading && step.IsActive) {
                UpdateTexts(true);
                Subscribe();
                needsCheck = true;
            } else {
                UpdateTexts(false);
            }
        }

        protected abstract bool IsDone();
        protected virtual void UpdateTexts(bool onActive) { }
        protected virtual void OnDone() { }
        protected virtual void OnSubscribe() { }
        protected virtual void OnUnsubscribe() { }

        protected void RequestCheck() {
            needsCheck = true;
        }

        public void Tick(long ticks) {
            if (!step.IsActive || !Ready.Energy) { return; }
            var worldChanged = needsCheck;
            if (!worldChanged && ticks % Consts.TicksPer5Minutes != 0) { return; }
            needsCheck = false;
            if (!TryComplete() && worldChanged) {
                // The thing the text links to may have been built or removed. Not on the
                // periodic backstop, which would format the same title over and over.
                UpdateTexts(true);
            }
        }

        public void PreCheck() {
            TryComplete();
        }

        private bool TryComplete() {
            if (S?.Map == null) {
                // A deferred check can outlive the game it was scheduled for
                return false;
            }
            if (!IsDone()) { return false; }
            OnDone();
            CompleteStep();
            return true;
        }

        public override void OnStateChange(QuestStepState before, QuestStepState after) {
            if (after == QuestStepState.Active) {
                UpdateTexts(true);
                Subscribe();
                // Activation only starts listening for future changes, the world may
                // already satisfy the step
                needsCheck = true;
            }
        }

        private void OnWorldChanged() {
            needsCheck = true;
        }

        private void OnConstructionCompleted(Tile tile) {
            needsCheck = true;
        }

        private void Subscribe() {
            if (isSubscribed) { return; }
            isSubscribed = true;
            S.Sig.ElectricityGridChanged.AddListener(OnWorldChanged);
            S.Sig.ConstructionCompleted.AddListener(OnConstructionCompleted);
            OnSubscribe();
        }

        public override void Cleanup() {
            if (!isSubscribed) { return; }
            isSubscribed = false;
            S.Sig?.ElectricityGridChanged.RemoveListener(OnWorldChanged);
            S.Sig?.ConstructionCompleted.RemoveListener(OnConstructionCompleted);
            OnUnsubscribe();
        }
    }
}
