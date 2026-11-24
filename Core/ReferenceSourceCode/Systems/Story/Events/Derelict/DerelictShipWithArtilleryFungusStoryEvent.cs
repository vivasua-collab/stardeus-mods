using Game.CodeGen;
using Game.Commands;
using Game.Data;
using Game.Procgen;
using KL.Randomness;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Story {
    public sealed class DerelictShipWithArtilleryFungusStoryEvent : BaseDerelictShipStoryEvent {
        public const string EventId = "DerelictShipWithArtilleryFungus";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new DerelictShipWithArtilleryFungusStoryEvent());
        }

        public override void AfterGenerate(GameState s, ShipFactory factory, Rng rng) {
            var diff = s.Difficulty.Level;
            int spawnedFungus = 0;
            var wt = 1f; //WantedTension;
            int wantedFungus = Mathf.RoundToInt(wt * 20f);
            if (wantedFungus < 30) {
                wantedFungus = 30;
            }

            var fungusDef = The.Defs.FastGet(DefIdsH.ObjPlantsArtilleryFungus);

            factory.ConfinementArea.ForEach((t) => {
                if (spawnedFungus < wantedFungus) {
                    if (EntityUtils.IsEmptyFloorWithIntegrityAt(t)) {
                        if (factory.Spec.IsWithinGenArea(t)) {
                            if (rng.Chance(0.05f * diff)) {
                                s.CmdQ.Enqueue(new CmdSpawnObj(
                                    EntityUtils.CenterOf(t), fungusDef, true, false));
                                spawnedFungus++;
                            }
                        }
                    }
                }
            });
        }
    }
}
