using System.Collections.Generic;
using Game.CodeGen;
using Game.Data;
using Game.Procgen;
using KL.Randomness;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Story {
    public sealed class DerelictShipWithStorageCapsulesStoryEvent : BaseDerelictShipStoryEvent {
        public const string EventId = "DerelictShipWithStorageCapsules";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new DerelictShipWithStorageCapsulesStoryEvent());
        }

        protected override void AddPostLootStages(ShipFactory factory, Rng rng) {
            var capsules = new List<Def>();
            var count = rng.Range(3, 7);
            var capsuleDef = The.Defs.FastGet(DefIdsH.ObjContainersStorageCapsule);
            for (int i = 0; i < count; i++) {
                capsules.Add(capsuleDef);
            }
            factory.AddStage(new ObjGenStage(capsules));
        }
    }
}
