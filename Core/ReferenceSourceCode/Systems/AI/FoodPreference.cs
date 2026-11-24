using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Traits;

namespace Game.Systems.AI {
    public static class FoodPreference {
        public static bool TryGetPreferredVegan(BeingData being, out bool prefersVegan) {
            if (being.HasTrait(TraitIdH.Vegan)) {
                prefersVegan = true;
                return true;
            }
            if (being.HasTrait(TraitIdH.Carnivore)) {
                prefersVegan = false;
                return true;
            }
            prefersVegan = false;
            return false;
        }

        public static bool IsPreferred(MatType type, bool prefersVegan) {
            return EdibleComp.IsVeganFood(type) == prefersVegan;
        }

        public static bool StorageHasPreferredFood(IMatStorage storage,
                int group, bool prefersVegan) {
            var foodOptions = Caches.MatList;
            foodOptions.Clear();
            if (!storage.AvailableForGroup(group, foodOptions)) {
                return false;
            }
            for (int i = 0; i < foodOptions.Count; i++) {
                if (IsPreferred(foodOptions[i].Type, prefersVegan)) {
                    return true;
                }
            }
            return false;
        }

        public static void KeepPreferredFood(List<Mat> foodOptions, bool prefersVegan) {
            for (int i = foodOptions.Count - 1; i >= 0; i--) {
                if (!IsPreferred(foodOptions[i].Type, prefersVegan)) {
                    foodOptions.RemoveAt(i);
                }
            }
        }

        public static void ApplyMentalEffect(AIAgentComp agent, MatType foodType,
                long ticks) {
            if (foodType == null ||
                    !TryGetPreferredVegan(agent.BeingData, out var prefersVegan)) {
                return;
            }
            var preferred = IsPreferred(foodType, prefersVegan);
            var reason = TraitDef.MoodReason(preferred ? T.AteFavoriteFood : T.AteDislikedFood,
                prefersVegan ? TraitIdH.Vegan : TraitIdH.Carnivore);
            AIAction.AddMentalEffect(agent, ticks, Consts.TicksPer4Hours, reason,
                preferred ? 3 : -5);
        }
    }
}
