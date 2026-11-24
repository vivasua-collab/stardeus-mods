using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    // Asks for one of the Definitions (a Wardrobe) to share an enclosed room with the anchor
    // Definition (the Stasis Array). New Beginning places the first colonist's only space suit
    // next to the array, and a tight room around the array alone leaves it in vacuum.
    public sealed class EntityInSameRoomQuestStep : BaseQuestStep, ITickListener {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(StepId,
                (s, c, l) => new EntityInSameRoomQuestStep(s, c, l));
        }
        public const string StepId = "EntityInSameRoom";
        private readonly Def anchorDef;
        private readonly Def[] targetDefs;

        public EntityInSameRoomQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(StepId, s, step, isLoading) {
            var anchorId = config.GetString(PropertyIdH.Definition);
            anchorDef = The.Defs.Get(anchorId);
            if (anchorDef == null) {
                D.Throw("Failure to configure step, def is not found: {0} -> {1}",
                    step, anchorId);
                return;
            }
            var targetIds = config.GetStringSet(PropertyIdH.Definitions, null);
            if (Arrays.IsEmpty(targetIds)) {
                D.Throw("Failure to configure step, no target Definitions: {0}", step);
                return;
            }
            targetDefs = new Def[targetIds.Length];
            for (int i = 0; i < targetIds.Length; i++) {
                targetDefs[i] = The.Defs.Get(targetIds[i]);
                if (targetDefs[i] == null) {
                    D.Throw("Failure to configure step, def is not found: {0} -> {1}",
                        step, targetIds[i]);
                    return;
                }
            }
            RefreshTitle();
            step.Desc = T.QuestStepEncloseWithTip_V1.F(targetDefs[0].CodexLink);
        }

        // Steps are built on load before the map is, so the first tick links the entities
        private bool isTitleStale = true;

        private void RefreshTitle() {
            step.Title = T.QuestStepEncloseWith_V2.F(
                QuestStepLinks.ClosestLink(S, anchorDef, targetDefs),
                QuestStepLinks.DeviceLink(S, anchorDef));
        }

        public override void OnStateChange(QuestStepState before, QuestStepState after) {
            if (after == QuestStepState.Active) {
                RefreshTitle();
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

        public bool CheckCompleted() {
            // The pod can fail to place its wardrobe, and the player can take it apart. With
            // nothing left to wall in, the step must not hold up the tutorial chain.
            if (!HasAnyTarget()) { return true; }
            if (!S.Map.TryAllByDef(anchorDef, out var anchors)) { return false; }
            foreach (var anchor in anchors.Values) {
                if (!anchor.IsActive) { continue; }
                var areaId = QuestStepRooms.EnclosedAreaOf(S, anchor);
                if (areaId == 0) { continue; }
                if (HasTargetIn(areaId)) {
                    return true;
                }
            }
            return false;
        }

        private bool HasAnyTarget() {
            foreach (var targetDef in targetDefs) {
                if (!S.Map.TryAllByDef(targetDef, out var targets)) { continue; }
                foreach (var target in targets.Values) {
                    if (target.IsActive) { return true; }
                }
            }
            return false;
        }

        private bool HasTargetIn(int areaId) {
            var areas = S.Sys.Areas;
            foreach (var targetDef in targetDefs) {
                if (!S.Map.TryAllByDef(targetDef, out var targets)) { continue; }
                foreach (var target in targets.Values) {
                    if (!target.IsActive) { continue; }
                    if (areas.At(target.PosIdx) == areaId) {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
