using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Suggestions {
    public class SuggestionRepairOrDeconstruct : Suggestion {
        [SelfInit]
        static void AutoRegister() {
            Register(new SuggestionRepairOrDeconstruct());
        }
        public const string SuggId = "RepairOrDeconstruct";
        public override string Id => SuggId;
        public override string IconId => Constants.IconId.CWrench;

        public override bool IsAvailable(GameState state) {
            return G.Ticks > Consts.TicksPer5Days && HasAnythingDamaged(state);
        }

        private bool HasAnythingDamaged(GameState s) {
            var damaged = s.Components.CountMatching<DamageableComp>(
                (o) => o.IsHeavilyDamaged, 1);
            return damaged != 0;
        }

        public override bool IsComplete(GameState state) {
            if (!HasAnythingDamaged(state)) { return true; }
            int countAds = 0;

            foreach (var goal in state.Sys.AI.ColonyGoals.Values) {
                if (goal.Target is DamageableComp) {
                    countAds++;
                }
                if (goal.Target is ConstructableComp c && c.IsConstructed && c.HasGoal) {
                    countAds++;
                }
            }
            return countAds > 10;
        }

        public override float Score(GameState state) {
            return 500;
        }
    }
}
