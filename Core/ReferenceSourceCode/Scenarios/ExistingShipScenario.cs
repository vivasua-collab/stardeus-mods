using System.Collections;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.UIUX;
using UnityEngine;

using Game.Utils;
namespace Game.Scenarios {
    public sealed class ExistingShipScenario : Scenario {
        [SelfInit]
        private static void Load() {
            Register(new ExistingShipScenario());
        }
        public static string ScenarioId = "ExistingShip";
        public override string Id => "ExistingShip";
        public override bool IsInternal => true;
        public override string NameT => T.NounShip;
        public override string DescT => null;
        public override int Priority => 0;
        public override int StartingPetCount => 0;

        public override IEnumerator DoSetup(GameState state, GameSetupData config) {
            yield break;
        }

        public override IEnumerator GeneratePreview(Texture2D tex, GameSetupData config) {
            yield break;
        }

        public override (Species, int)[] GetStartingBeingCounts() {
            // Deliberately not DefaultScenario: that is now the human-free onboarding
            // scenario, and an imported / cloned ship should still default to a crew
            // that includes people.
            return Find(WreckedEmptyShipScenario.ScenarioId).GetStartingBeingCounts();
        }
    }
}
