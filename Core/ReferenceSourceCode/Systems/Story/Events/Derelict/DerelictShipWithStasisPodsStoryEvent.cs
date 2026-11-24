using System.Collections.Generic;
using Game.CodeGen;
using Game.Data;
using Game.Procgen;
using KL.Randomness;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Story {
    public sealed class DerelictShipWithStasisPodsStoryEvent : BaseDerelictShipStoryEvent {
        public const string EventId = "DerelictShipWithStasisPods";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new DerelictShipWithStasisPodsStoryEvent());
        }
        protected override void AddPostLootStages(ShipFactory factory, Rng rng) {
            var objs = new List<Def>();
            var podDef = The.Defs.FastGet(DefIdsH.ObjectsDevicesStasisPod);
            objs.Add(podDef);
            if (rng.Chance(0.1f)) {
                objs.Add(podDef);
                // S.Situation.AddTension(0.2f, "Derelict Stasis Pod #2");
                if (rng.Chance(0.1f)) {
                    objs.Add(podDef);
                    // S.Situation.AddTension(0.2f, "Derelict Stasis Pod #3");
                }
            }

            factory.AddStage(new ObjectsGenStage(objs));
        }
    }
}
