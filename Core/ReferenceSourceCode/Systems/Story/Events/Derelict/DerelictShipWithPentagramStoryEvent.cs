using System.Collections.Generic;
using Game.CodeGen;
using Game.Data;
using Game.Procgen;
using KL.Randomness;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Story {
    public sealed class DerelictShipWithPentagramStoryEvent : BaseDerelictShipStoryEvent {
        public const string EventId = "DerelictShipWithPentagram";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new DerelictShipWithPentagramStoryEvent());
        }
        // Derelicts are generated into the map at the moment the player approaches them
        // (see CmdSpawnDerelictShip), so this is the "found it" moment, not spawn time.
        public override void AfterGenerate(GameState s, ShipFactory factory, Rng rng) {
            base.AfterGenerate(s, factory, rng);
            The.AchievementTracker.Unlock(
                Achievements.AchievementPentagramFound.AchievementId);
        }

        protected override void AddPostLootStages(ShipFactory factory, Rng rng) {
            factory.AddStage(new ObjectsGenStage(new List<Def> {
                The.Defs.FastGet(DefIdsH.ObjectsDevicesPentagramStatue)
            }));
            factory.AddStage(new SlaughterSpotGenStage());
            factory.AddStage(new SlaughterSpotGenStage());
            factory.AddStage(new SlaughterSpotGenStage());
            factory.AddStage(new SlaughterSpotGenStage());
        }
    }
}
