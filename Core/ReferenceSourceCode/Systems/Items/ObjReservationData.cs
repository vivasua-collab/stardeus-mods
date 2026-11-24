using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.Creatures.Surgeries;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Items {
    [MessagePackObject]
    public class ObjReservationData {
        [Key("ObjId")] public uint ObjId;
        [Key("GoalId")] public uint GoalId;
        [Key("SurgeryId")] public int SurgeryId;
        [Key("CreatedAt")] public long CreatedAt;

        public static ObjReservationData Serialize(ObjReservation reservation) {
            if (reservation == null) { return default; }
            var d = new ObjReservationData();
            d.ObjId = reservation.Obj?.Id ?? 0;
            d.GoalId = reservation.Goal?.Id ?? 0;
            d.SurgeryId = reservation.Surgery?.Id ?? 0;
            d.CreatedAt = reservation.CreatedAt;
            return d;
        }

        public static ObjReservation Deserialize(ObjReservationData data, ObjData objData, uint legacyGoalId) {
            if (objData == null) {
                D.Err("Trying to deserialize reservation with null objData! {0}", data);
                return null;
            }
            if (data == null) {
                if (legacyGoalId > 0) {
                    var res = new ObjReservation(objData, G.Ticks);
                    res.LoadGoalId = legacyGoalId;
                    return res;
                }
                return null;
            }
            var reservation = new ObjReservation(objData, data.CreatedAt);
            reservation.LoadGoalId = data.GoalId;
            reservation.LoadSurgeryId = data.SurgeryId;
            return reservation;
        }
    }
}
