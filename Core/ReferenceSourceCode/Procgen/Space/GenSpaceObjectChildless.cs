using Game.Constants;
using Game.Data.Space;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Space;

using Game.Utils;
namespace Game.Procgen.Space {
    public class GenSpaceObjectChildless : GenSpaceObject {
        public override string TypeId => SpaceObjectTypeId.Childless;
        [SelfInit]
        private static void Register() {
            GenUniverse.RegisterGenerator(SpaceObjectTypeId.Childless,
                new GenSpaceObjectChildless());
        }

        public override void GenerateSubObjects(Universe u, SpaceObject parent) {
        }
    }
}
