#if false
using Game.Components;
using Game.Constants;
using Game.Data;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Suggestions {
    public class SuggestionBuildMiniMatterReactor : Suggestion {
        [SelfInit]
        static void AutoRegister() {
            Register(new SuggestionBuildMiniMatterReactor());
        }

        public override string Id => "BuildMiniMatterReactor";
        public override string IconId => Constants.IconId.CEnergy;
        public override bool IsAvailable(GameState state) {
            return true;
        }

        public override bool IsComplete(GameState state) {
            var mr = state.Components.FindFirstByType<MatterReactorComp>();
            if (mr != null && mr.IsConstructed) {
                return true;
            }
            return false;
        }

        public override float Score(GameState state) {
            return float.MaxValue;
        }
    }
}
#endif
