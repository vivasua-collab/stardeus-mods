using Game.Components;
using Game.Constants;
using Game.Data;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Suggestions {
    /// <summary>
    /// Gate suggestion. When this is added, some other suggestions can happen.
    /// It has no text and won't show up since its internal.
    /// </summary>
    public class SuggestionStartWreckedScenario : Suggestion {
        [SelfInit]
        static void AutoRegister() {
            Register(new SuggestionStartWreckedScenario());
        }

        public SuggestionStartWreckedScenario() {
            IsInternal = true;
        }

        public const string SuggId = "StartWreckedScenario";
        public override string Id => SuggId;
        public override string IconId => Constants.IconId.WMaterials;

        public override float Score(GameState state) {
            return -1000;
        }

        public override bool IsAvailable(GameState state) {
            return false;
        }

        public override bool IsComplete(GameState state) {
            return true;
        }
    }
}
