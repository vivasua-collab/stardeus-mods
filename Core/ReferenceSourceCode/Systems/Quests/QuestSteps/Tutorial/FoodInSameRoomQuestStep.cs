using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    // Asks for food to be stored in the same enclosed room as the anchor Definition (the Stasis
    // Array). New Beginning leaves the first colonist's only rations next to the array, and a
    // tight room around the array alone leaves them in vacuum. Any food storage counts, including
    // the unstored ration pile and a fridge built later. The food Definitions (Survival Meal)
    // only pick what the text links to, so the player sees which pile is meant.
    public sealed class FoodInSameRoomQuestStep : BaseQuestStep, ITickListener {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(StepId,
                (s, c, l) => new FoodInSameRoomQuestStep(s, c, l));
        }
        public const string StepId = "FoodInSameRoom";
        private readonly Def anchorDef;
        private readonly Def[] foodDefs;

        public FoodInSameRoomQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(StepId, s, step, isLoading) {
            var anchorId = config.GetString(PropertyIdH.Definition);
            anchorDef = The.Defs.Get(anchorId);
            if (anchorDef == null) {
                D.Throw("Failure to configure step, def is not found: {0} -> {1}",
                    step, anchorId);
                return;
            }
            var foodIds = config.GetStringSet(PropertyIdH.Definitions, null);
            if (Arrays.IsEmpty(foodIds)) {
                D.Throw("Failure to configure step, no food Definitions: {0}", step);
                return;
            }
            foodDefs = new Def[foodIds.Length];
            for (int i = 0; i < foodIds.Length; i++) {
                foodDefs[i] = The.Defs.Get(foodIds[i]);
                if (foodDefs[i] == null) {
                    D.Throw("Failure to configure step, def is not found: {0} -> {1}",
                        step, foodIds[i]);
                    return;
                }
            }
            RefreshTitle();
        }

        // Steps are built on load before the map is, so the first tick links the entities
        private bool isTitleStale = true;

        private void RefreshTitle() {
            var foodLink = QuestStepLinks.ClosestLink(S, anchorDef, foodDefs);
            var deviceLink = QuestStepLinks.DeviceLink(S, anchorDef);
            step.Title = T.QuestStepEncloseRations_V2.F(foodLink, deviceLink);
            step.Desc = T.QuestStepEncloseRationsTip_V2.F(foodLink, deviceLink);
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
            // With no food aboard there is nothing to bring in, and the step must not hold up
            // the tutorial chain
            if (S.Sys.Inventory.CountByGroup(MatGroupIdH.Food) == 0) { return true; }
            if (!S.Map.TryAllByDef(anchorDef, out var anchors)) { return false; }
            foreach (var anchor in anchors.Values) {
                if (!anchor.IsActive) { continue; }
                var areaId = QuestStepRooms.EnclosedAreaOf(S, anchor);
                if (areaId == 0) { continue; }
                if (HasFoodIn(areaId)) {
                    return true;
                }
            }
            return false;
        }

        // Same storage filter AIActionFindFood applies, and OutPort because that is where a
        // colonist goes to take the food
        private bool HasFoodIn(int areaId) {
            var areas = S.Sys.Areas;
            var storages = S.Sys.Inventory.StoragesByGroup(MatGroupIdH.Food);
            if (storages == null) { return false; }
            for (int i = storages.Count - 1; i >= 0; i--) {
                var s = storages[i];
                if (!s.IsPublic) { continue; }
                if (!s.Entity.IsActive || s.Entity.IsCarriedByAny) { continue; }
                var port = s.OutPort;
                if (port < 0 || areas.At(port) != areaId) { continue; }
                if (s.AvailableForGroup(MatGroupIdH.Food)) {
                    return true;
                }
            }
            return false;
        }
    }
}
