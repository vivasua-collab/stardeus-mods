using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Suggestions {
    public class SuggestionStasisALS : Suggestion {
        [SelfInit]
        static void AutoRegister() {
            Register(new SuggestionStasisALS());
        }
        public const string SuggId = "StasisALS";
        public override string Id => SuggId;
        public override string WaitsForBool => QuestBoolIds.MadeStasisSurvivable;
        public override string IconId => Constants.IconId.CHealth;
        public override bool IsAvailable(GameState state) {
            return state.Situation.GridMaxOutput
                - state.Situation.GridConsumption > 500;
        }

        public override bool IsComplete(GameState state) {
            if (state.Components.CountByType<StasisArrayComp>() == 0) {
                return true;
            }
            var array = state.Components.FindFirstMatching<StasisArrayComp>(
                c => c.IsALSOn);
            return array != null;
        }

        public override float Score(GameState state) {
            // We're low on colonists, may want to enable ALS and request wakeups
            if (state.Situation.Colonists < 3) {
                return int.MaxValue;
            }
            return 1000;
        }
    }
}
