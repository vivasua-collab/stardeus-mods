using Game.Constants;
using Game.Data.Space;
using KL.Utils;
using UnityEngine;
using Game.Systems.Ships;
using Game.CodeGen;
using Game.Data;

using Game.Utils;
namespace Game.Procgen.Space {
    public class GenSpaceObjectSpaceStation : GenSpaceObject {
        public override string TypeId => SpaceObjectTypeId.SpaceStation;
        [SelfInit]
        private static void Register() {
            GenUniverse.RegisterGenerator(SpaceObjectTypeId.SpaceStation,
                new GenSpaceObjectSpaceStation());
        }

        public override bool ProducesShip => true;

        public override SpaceObject Generate(Universe u, string layer,
                SpaceObjectType type, SpaceObject parent) {
            var tpl = new NPCShip();
            tpl.Class = ShipClassDef.FastGet(ShipClassIdH.SpaceStation);
            var spaceStation = u.S.Query.SpawnShipTplAt.Ask(parent, tpl);
            return spaceStation.SO;
        }

        public override void GenerateSubObjects(Universe u, SpaceObject parent) {
        }
    }
}
