using System.Collections.Generic;
using Game.Data;
using Game.Systems.Ships;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Ships {
    [MessagePackObject]
    public class ShipViewSysData {
        [Key("Slots")] public List<ShipViewSlotData> Slots;

        public static ShipViewSysData Serialize(ShipViewSys sys) {
            var slots = new List<ShipViewSlotData>();
            foreach (var slot in sys.Slots) {
                var sd = ShipViewSlotData.Serialize(slot);
                if (sd != null) {
                    slots.Add(sd);
                }
            }

            return new ShipViewSysData {
                Slots = slots,
            };
        }

        public void Restore(GameState s) {
            foreach (var slot in Slots) {
                var ship = s.Query.NPCShipById.Ask(slot.ShipId);
                if (ship == null) { continue; }
                if (!IsCombatant(s, ship)) {
                    D.Warn("Skipping restored ship view, {0} is no longer in combat", ship);
                    continue;
                }
                if (ship.View != null) {
                    D.Err("Skipping duplicate restored ship view slot for {0}", ship);
                    continue;
                }
                s.Query.SpawnShipViewAtPos.Ask(
                    ship, slot.Edge, slot.Position);
            }
        }

        /// <summary>
        /// Only ships that ended up on one of the reconciled combat sides get a view
        /// </summary>
        public static bool IsCombatant(GameState s, NPCShip ship) {
            return s.Query.GetEnemyShips.Ask().Contains(ship)
                || s.Query.GetAllyShips.Ask().Contains(ship);
        }
    }
}
