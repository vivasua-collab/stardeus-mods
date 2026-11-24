using System;
using System.Collections.Generic;
using System.Linq;
using Game.Data;
using Game.Data.Space;
using Game.Utils;
using Game.CodeGen;
using Kl.Algos;
using KL.Utils;
using UnityEngine;
using Game.Constants;
using Game.Systems.Ships;

namespace Game.Procgen.Space {
    public class GenSpaceObjectShipDerelict : GenSpaceObject {
        public override string TypeId => SpaceObjectTypeId.ShipDerelict;
        [SelfInit]
        private static void Register() {
            GenUniverse.RegisterGenerator(SpaceObjectTypeId.ShipDerelict,
                new GenSpaceObjectShipDerelict());
        }
        public override bool ProducesShip => true;

        public override SpaceObject Generate(Universe u, string layer,
                SpaceObjectType type, SpaceObject parent) {
            var derelict = u.S.Query.SpawnShipAt.Ask(parent);
            derelict.TurnIntoDerelict(null);
            return derelict.SO;
        }

        public override void GenerateSubObjects(Universe u, SpaceObject obj) {
            // TODO - maybe add moons and stuff
        }

        protected override Vector2[] GenerateChildPoints(Universe u, SpaceObject ob, int count) {
            return null;
        }
    }
}
