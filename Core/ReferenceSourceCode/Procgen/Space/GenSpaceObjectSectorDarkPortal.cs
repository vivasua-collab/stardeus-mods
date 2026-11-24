using System;
using System.Collections.Generic;
using System.Linq;
using Game.Data;
using Game.Data.Space;
using Game.Utils;
using Game.CodeGen;
using Kl.Algos;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Constants;
using Game.Systems.Space;

namespace Game.Procgen.Space {
    public class GenSpaceObjectSectorDarkPortal : GenSpaceObject {
        public override string TypeId => SpaceObjectTypeId.DarkPortal;
        [SelfInit]
        private static void Register() {
            GenUniverse.RegisterGenerator(SpaceObjectTypeId.DarkPortal,
                new GenSpaceObjectSectorDarkPortal());
        }

        public override void GenerateSubObjects(Universe u, SpaceObject obj) {
            var inner = Generate(u, UniverseLayer.SectorContents, obj.Type, obj);
            obj.AddChild(inner);
        }
    }
}
