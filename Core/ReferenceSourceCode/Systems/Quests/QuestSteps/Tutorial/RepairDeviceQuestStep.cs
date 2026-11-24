using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    /// <summary>
    /// Asks the player to repair a device that is already on the ship. Completes as soon as one
    /// constructed, undamaged instance exists - a spare that is still dented doesn't matter,
    /// since only one working device is ever needed.
    /// Hides itself when the requirement is already met, so quests can carry this step for a
    /// situation (a wrecked start) that most ships never hit.
    /// </summary>
    public sealed class RepairDeviceQuestStep : BaseQuestStep, ITickListener,
            IPreCheckedQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(StepId,
                (s, c, l) => new RepairDeviceQuestStep(s, c, l));
        }
        public const string StepId = "RepairDevice";
        private readonly Def def;
        private bool isSubscribed;
        private bool needsCheckOnTick;

        public RepairDeviceQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(StepId, s, step, isLoading) {
            var defId = config.GetString(PropertyIdH.Definition);
            def = The.Defs.Get(defId);
            if (def == null) {
                D.Throw("Misconfigured step, no def by id: {0} | {1}", defId, step);
            }
            if (!def.HasComponent(ComponentIdH.Damageable)) {
                D.Throw("Def cannot be damaged, so it cannot be repaired: {0} | {1}",
                    defId, step);
            }
            RefreshTitle();
            SetDesc(T.QuestStepRepairTip);
            if (isLoading && step.IsActive) {
                needsCheckOnTick = true;
                Subscribe();
            }
        }

        public void Tick(long ticks) {
            if (needsCheckOnTick) {
                needsCheckOnTick = false;
                if (step.IsActive && CheckCompleted()) {
                    CompleteStep();
                }
            }
        }

        // Point the player at the damaged device itself - on a wrecked ship its location is
        // the missing information, not what the device is.
        private string DeviceLink() {
            if (S.AllByDef(def, out var items)) {
                foreach (Tile item in items.Values) {
                    if (!item.IsConstructed) { continue; }
                    if (item.Health.IsDamaged) {
                        return Texts.LinkToTile(item);
                    }
                }
            }
            return def.CodexLink;
        }

        private void RefreshTitle() {
            step.Title = T.QuestStepRepair_V1.F(DeviceLink());
        }

        public bool CheckCompleted() {
            if (!S.AllByDef(def, out var items)) { return false; }
            foreach (Tile item in items.Values) {
                if (!item.IsConstructed) { continue; }
                if (!item.Health.IsDamaged) { return true; }
            }
            return false;
        }

        private void OnTileRepaired(Tile tile) {
            if (!step.IsActive) { return; }
            if (tile.Definition.IdH != def.IdH) { return; }
            if (!tile.IsConstructed || tile.Health.IsDamaged) { return; }
            CompleteStep();
        }

        public override void OnStateChange(QuestStepState before, QuestStepState after) {
            if (after == QuestStepState.Active) {
                // Activating mid-quest only starts listening for future repairs - re-check now
                // in case the device was already patched up.
                needsCheckOnTick = true;
                // The step is built while the quest is still Available - the instance it points
                // at can have been destroyed and rebuilt before the quest actually starts.
                RefreshTitle();
                Subscribe();
            }
        }

        private void Subscribe() {
            if (isSubscribed) { return; }
            isSubscribed = true;
            S.Sig.TileRepaired.AddListener(OnTileRepaired);
        }

        public override void Cleanup() {
            if (!isSubscribed) { return; }
            isSubscribed = false;
            S.Sig?.TileRepaired.RemoveListener(OnTileRepaired);
        }

        public void PreCheck() {
            if (CheckCompleted()) {
                step.SetHidden();
                CompleteStep();
            }
        }
    }
}
