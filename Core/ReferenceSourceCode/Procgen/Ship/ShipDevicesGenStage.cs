using KL.Grid;
using Game.Data;
using System.Collections.Generic;
using Game.Constants;
using KL.Utils;
using Game.Commands;
using System.Linq;
using Game.Components;
using Game.Systems.Ships;
using System.Collections;
using KL.Randomness;

namespace Game.Procgen {
    public class ShipDevicesGenStage : BaseShipGenStage {
        private readonly NPCShip ship;
        public ShipDevicesGenStage(NPCShip ship) {
            this.ship = ship;
        }
        public override bool IsAsync => true;
        public override bool RequiresAreas => true;
        public override IEnumerator ApplyAsync() {

            var subRng = rng.Fork();
            var buildSubRng = subRng.Fork();
            var solidArea = S.Sys.Areas.SolidArea.OrderBy(_ => subRng.Value).ToList();
            foreach (var device in ship.CombatState.Devices) {
                if (device.Device.IsDestroyed) { continue; }
                if (!rng.Chance(Tunables.ShipDeviceDerelictSurvivalChance)) { continue; }
                // see ShipEngineGenStage
                if (device is ShipEngine) { continue; }
                if (device is ShipShield) { continue; }
                while (solidArea.Count > 0) {
                    var posIdx = solidArea[0];
                    solidArea.RemoveAt(0);
                    if (!IsWithinGenArea(posIdx)) { continue; }
                    if (BuildObject(posIdx, device, buildSubRng)) {
                        break;
                    }
                }
                yield return null;
                if (!GameState.IsCurrent(S)) { yield break; }
            }
        }

        private bool BuildObject(int startPos, IShipDevice device, Rng rng) {
            if (Pos.SqrDistance(startPos, spec.CPUPos) < 25 * 25) {
                return false;
            }
            if (!IsGoodObjectLocation(startPos)) {
                return false;
            }

            var d = device.Device;
            var def = d.Def;

            var proto = EntityUtils.CreatePrototype<Tile>(def);
            proto.Transform.Rotation = proto.Transform.RandomValidRotation(rng);
            BuildTile(startPos, proto);
            var obj = EntityUtils.ObjAt(startPos);
            if (obj != null) {
                // This is the wrong object
                if (obj.Transform.IsChild || obj.DefinitionId != def.Id) {
                    return false;
                }
                D.Warn("Built ship device on derelict: {0}", obj);
                if (d.Health < d.HealthMax) {
                    D.Warn("damaging ship device on derelict: {0}: {1}", obj, d.HealthPercent);
                    obj.Health.TakeDamage(d.HealthMax - d.Health, DamageType.Explosive);
                }
                return true;
            }
            return false;
        }
    }
}
