using Game.CodeGen;
using Game.Data;
using KL.Utils;

namespace Game.Systems.Quests {
    // Tutorial steps about the room around a device that is already aboard (the Stasis Array):
    // enclose it, close its corner gaps, make it reachable, pressurize it. They differ only in
    // the test they apply to the device's room, their text and the manual page they open, so
    // the device lookup and completion rules live here once instead of in four copies.
    public abstract class DeviceRoomQuestStep : BaseQuestStep, ITickListener {
        protected readonly Def def;
        private readonly bool isOneEnough;
        private readonly bool failIfNoDevice;

        // Steps are built on load before the map is, so the first tick links the device
        private bool isTitleStale = true;

        // Subclass constructors end with RefreshTitle() and set step.Desc, once their own
        // fields are ready - no virtual calls from here
        protected DeviceRoomQuestStep(string id, GameState s, QuestStep step, bool isLoading)
                : base(id, s, step, isLoading) {
            var buildDef = config.GetString(PropertyIdH.Definition);
            def = The.Defs.Get(buildDef);
            if (def == null) {
                D.Throw("Failure to configure step, def is not found: {0} -> {1}",
                    step, buildDef);
                return;
            }
            failIfNoDevice = config.GetBool(PropertyIdH.FailIfNoDevice, true);
            isOneEnough = config.GetBool(PropertyIdH.IsOneEnough, true);
        }

        protected abstract string BuildTitle();
        protected abstract bool IsRoomDone(Entity device);
        protected virtual void OnActivated() { }

        protected void RefreshTitle() {
            step.Title = BuildTitle();
        }

        public override void OnStateChange(QuestStepState before, QuestStepState after) {
            if (after == QuestStepState.Active) {
                RefreshTitle();
                OnActivated();
            }
        }

        public void Tick(long currentTick) {
            if (isTitleStale) {
                isTitleStale = false;
                RefreshTitle();
            }
            if (step.IsActive && CheckCompleted()) {
                CompleteStep();
            }
        }

        protected virtual bool CheckCompleted() {
            int doneCount = 0;
            int totalCount = 0;
            if (S.Map.TryAllByDef(def, out var found)) {
                totalCount = found.Count;
                foreach (var dev in found) {
                    if (!dev.Value.IsActive) { continue; }
                    if (IsRoomDone(dev.Value)) {
                        if (isOneEnough) {
                            return true;
                        }
                        doneCount++;
                    }
                }
            }

            if (failIfNoDevice && totalCount == 0) {
                FailStep();
                return false;
            }
            return totalCount > 0 && totalCount == doneCount;
        }
    }
}
