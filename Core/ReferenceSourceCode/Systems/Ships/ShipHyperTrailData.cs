using System.Linq;
using Game.Data;
using Game.Systems.Space;
using KL.Utils;
using MessagePack;
using UnityEngine;

namespace Game.Systems.Ships {
    [MessagePackObject]
    public class ShipHyperTrailData {
        [Key("ShipId")] public uint ShipId;
        [Key("Location")] public Vector2 Location;
        [Key("FromId")] public int FromId;
        [Key("ToId")] public int ToId;
        [Key("SOId")] public int SOId;
        [Key("CreatedAt")] public long CreatedAt;

        public static bool TryDeserialize(GameState s, ShipHyperTrailData data, out ShipHyperTrail trail) {
            var ship = s.Query.NPCShipById.Ask(data.ShipId);
            if (ship == null) {
                trail = null;
                return false;
            }
            var from = s.Universe.Find(data.FromId)?.Sector;
            if (from == null) {
                trail = null;
                return false;
            }
            trail = new ShipHyperTrail(ship, from);
            trail.Location = data.Location;
            trail.CreatedAt = data.CreatedAt;
            trail.To = s.Universe.Find(data.ToId)?.Sector;
            if (data.SOId > 0) {
                trail.SO = s.Universe.Find(data.SOId);
            }
            return trail.From != null;
        }

        public static bool TrySerialize(ShipHyperTrail trail, out ShipHyperTrailData data) {
            data = new ShipHyperTrailData();
            if (trail.Ship.State == ShipStateFlags.None) {
                return false;
            }
            data.ShipId = trail.Ship.Id;
            data.FromId = trail.From.SO.Id;
            data.Location = trail.Location;
            data.ToId = trail.To?.SO.Id ?? 0;
            data.CreatedAt = trail.CreatedAt;
            data.SOId = trail.SO?.Id ?? 0;
            return true;
        }
    }
}
