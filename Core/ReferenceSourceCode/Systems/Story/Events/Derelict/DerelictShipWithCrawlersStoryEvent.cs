using Game.CodeGen;
using Game.Commands;
using Game.Data;
using Game.Procgen;
using Game.Systems.Creatures;
using Game.Systems.Naming;
using KL.Randomness;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Story {
    public sealed class DerelictShipWithCrawlersStoryEvent : BaseDerelictShipStoryEvent {
        public const string EventId = "DerelictShipWithCrawlers";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new DerelictShipWithCrawlersStoryEvent());
        }

        public override void AfterGenerate(GameState s, ShipFactory factory, Rng rng) {
            var diff = s.Difficulty.Level;
            int spawnedCrawlers = 0;
            var wt = 1f; //WantedTension;
            int wantedCrawlers = Mathf.RoundToInt(wt / 0.5f);
            if (wantedCrawlers < 5) {
                wantedCrawlers = 5;
            }

            var crawler = Species.Get(SpeciesIdH.Crawler);

            factory.ConfinementArea.ForEach((t) => {
                if (spawnedCrawlers < wantedCrawlers) {
                    if (EntityUtils.IsEmptyFloorWithIntegrityAt(t)) {
                        if (factory.Spec.IsWithinGenArea(t)) {
                            if (rng.Chance(0.05f * diff)) {
                                s.CmdQ.Enqueue(new CmdSpawnBeing(
                                    new GenBeingVars(crawler, BeingType.Neutral), t));
                                spawnedCrawlers++;
                            }
                        }
                    }
                }
            });
        }
    }
}
