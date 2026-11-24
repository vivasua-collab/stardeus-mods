using Game.Data;
using Game.Procgen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Creatures {
    public sealed class GenBeingApplyModifiersDataStep : GenBeingDataStep {
        [SelfInit]
        private static void Register() {
            RegisterStep(new GenBeingApplyModifiersDataStep());
        }
        private const string StepId = "ApplyModifiers";
        public override string Id => StepId;
        // last step, after traits, experience and stats
        public override int Order => 200;
        public override bool Generate(GameState S, BeingData data, Rng rng,
                GenBeingVars vars) {
            if (data.Mind == null) {
                D.Err("Mind is null for being during gen apply modifiers step! {0}", data);
                return true;
            }
            if (data.Mind.IsBlank) {
                return true;
            }
            foreach (var trait in data.Mind.Traits.Slots) {
                trait.ApplyModifiersAfterGenBeing(S, data, rng, vars);
            }
            foreach (var role in data.Mind.Experience.Roles.Values) {
                role.ApplyModifiersAfterGenBeing(S, data, rng, vars);
            }
            return true;
        }
    }
}
