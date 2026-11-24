using System;
using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Data.Planets;
using Game.Data.Space;
using Game.Utils;
using Game.CodeGen;
using Kl.Algos;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;
using Game.Systems.Mental;
using Game.Systems.Space;

namespace Game.Procgen.Space {
    public class GenSpaceObjectSectorStar : GenSpaceObject {
        private const float maxDist = 50f;
        public override string TypeId => SpaceObjectTypeId.Star;
        [SelfInit]
        private static void Register() {
            GenUniverse.RegisterGenerator(SpaceObjectTypeId.Star,
                new GenSpaceObjectSectorStar());
        }

        public override void GenerateSubObjects(Universe u, SpaceObject parent) {
            // 1 - big copy of the planet itself
            var closeupType = SpaceObjectType.Find(SpaceObjectTypeId.StarCloseup);
            var closeup = Generate(u, UniverseLayer.SectorContents, closeupType, parent);
            closeup.Name = $"{parent.Name} (Closeup)";
            closeup.Location = Consts.V2Zero;
            parent.AddChild(closeup);

            // D.Warn("Done generating star contents for {0}. ExitDist: {1}",
            //     parent, parent.ExitDist);
        }
    }
}
