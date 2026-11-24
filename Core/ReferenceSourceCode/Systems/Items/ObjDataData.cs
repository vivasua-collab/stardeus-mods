using System;
using System.Collections.Generic;
using System.Data;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.BodyParts;
using Game.Systems.Conditions;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Systems.Stats;
using KL.Randomness;
using KL.Utils;
using MessagePack;
using UnityEngine;

namespace Game.Systems.Items {
    [MessagePackObject]
    public class ObjDataData {
        [Key("Id")] public uint Id;
        [Key("DefId")] public int DefId;
        [Key("BodyPartDefId")] public int BodyPartDefId;
        [Key("BodyPartState")] public BodyPartState BodyPartState;
        /// <summary>
        /// LEGACY! Only for migrating old saves, do not write
        /// </summary>
        [Key("Mind")] public MindData LegacyMind;
        [Key("MindId")] public uint MindId;
        [Key("ItemId")] public int ItemId;
        [Key("Quality")] public float Quality;
        [Key("DeviceId")] public int DeviceId;
        // [Obsolete("Legacy")]
        [Key("ResGoalId")] public uint LegacyReservedGoalId;
        [Key("Reservation")] public ObjReservationData Reservation;
        [Key("BeingDataId")] public uint BeingDataId;
        // Short keys on purpose: this is the most numerous saved record in the game
        // and MessagePack writes the key string on every one of them.
        [Key("OFlags")] public ObjFlags Flags;
        [Key("LOId")] public uint LastOwnerId;
        [Key("LOMId")] public uint LastOwnerMindId;
        [Key("StorageId")] public int StorageId;
        [Key("State")] public ObjState State;
        [Key("ObjId")] public int ObjId;
        [Key("Color")] public Color Color;
        [Key("CustomString")] public string CustomString;
        [Key("CustomLong")] public long CustomLong;

        public static ObjDataData[] Serialize(Dictionary<uint, ObjData> all) {
            var d = new ObjDataData[all.Count];
            int i = 0;
            foreach (var data in all) {
                if (data.Value.State == ObjState.Destroyed) {
                    D.Err("Trying to save a destroyed obj: {0}", data);
                    continue;
                }
                d[i++] = Serialize(data.Value);
            }
            if (i != d.Length) {
                Array.Resize(ref d, i);
            }
            return d;
        }

        public static ObjDataData Serialize(ObjData data) {
            if (data == null) { return default; }
            var d = new ObjDataData();
            d.Id = data.Id;
            d.State = data.State;
            d.DefId = data.Def.IdH;
            d.DeviceId = data.DeviceId;
            d.Quality = data.Quality;
            d.BodyPartDefId = data.BodyPart?.IdH ?? 0;
            d.BodyPartState = data.BodyPartState;
            d.ItemId = data.Item?.IdH ?? 0;
            d.MindId = data.Mind?.Id ?? 0;
            d.ObjId = data.Obj?.Id ?? 0;
            d.BeingDataId = data.BeingDataId;
            d.Flags = data.Flags;
            d.LastOwnerId = data.LastOwnerId;
            d.LastOwnerMindId = data.LastOwnerMindId;
            // d.ReservedGoalId = data.ReservedForGoal?.Id ?? 0;
            d.Reservation = ObjReservationData.Serialize(data.Reservation);
            d.StorageId = data.StorageId;
            d.Color = data.Color;
            d.CustomString = data.CustomString;
            d.CustomLong = data.CustomLong;
            return d;
        }

        public static bool TryDeserialize(GameState s, ObjDataData data, Obj o,
                out ObjData d, bool expectInitialized) {
            if (expectInitialized) {
                if (data.Id == 0) {
                    D.Err("Trying to deserialize initialized obj with id 0! {0} -> {1}", data, o);
                    d = null;
                    return false;
                }
            } else {
                if (data.Id != 0) {
                    D.Err("Trying to deserialize uninitialized obj with id != 0! {0} -> {1}", data, o);
                    d = null;
                    return false;
                }
            }
            d = new ObjData();
            if (o != null) {
                d.Obj = o;
                o.Data = d;
            }
            d.Id = data.Id;
            d.Def = The.Defs.FastGet(data.DefId);
            if (d.Def == null) {
                D.Err("Failed deserializing object data, def missing: {0}", D.DumpInstance(data));
                d = null;
                return false;
            }
            d.Quality = data.Quality;
            if (d.Quality == 0f) {
                d.Quality = Rng.URangeSmooth(0.7f, 1f);
            } else if (d.Quality < 0f) {
                d.Quality = Rng.URange(0.05f, 0.1f);
            }
            if (data.BodyPartDefId != 0) {
                d.BodyPart = BodyPartDef.Get(data.BodyPartDefId);
                if (d.BodyPart != null) {
                    d.BodyPartState = data.BodyPartState;
                    if (d.BodyPartState.Quality <= 0f) {
                        d.BodyPartState.SetQuality(d.Quality);
                    } else if (d.BodyPartState.Quality != d.Quality) {
                        // force the quality to be dictated by body part state
                        d.Quality = d.BodyPartState.Quality;
                    }
                } else {
                    D.Err("Failed deserializing bodypart obj, no body part by def id H: {0}",
                        data.BodyPartDefId);
                }
            }
            d.Item = InventoryItemDef.FastGet(data.ItemId);
            d.State = data.State;
            d.Color = data.Color;
            d.MindId = data.MindId;
            d.StorageId = data.StorageId;
            d.DeviceId = data.DeviceId;
            d.BeingDataId = data.BeingDataId;
            d.Flags = data.Flags;
            d.LastOwnerId = data.LastOwnerId;
            d.LastOwnerMindId = data.LastOwnerMindId;
            if (expectInitialized && d.State == ObjState.Equipped && d.BeingDataId == 0) {
                D.ErrSoft("Item is equipped but BeingDataId is 0: {0}!", d);
            }
            if (d.Color == default) {
                d.Color = Consts.ColorWhite;
            }
            d.CustomString = data.CustomString;
            d.CustomLong = data.CustomLong;
            // d.LoadGoalId = data.ReservedGoalId;
            d.Reservation = ObjReservationData.Deserialize(data.Reservation, d, data.LegacyReservedGoalId);
            return true;
        }

        public static ObjData DeepCopy(GameState S, ObjData data) {
            // if (data.Id != 0) {
            //     D.Err("Trying to deep copy an initialized objData! This can lead to undefined behaviors: {0}", data);
            // }
            var datadata = Serialize(data);
            datadata.Id = 0;
            datadata.LegacyReservedGoalId = 0;
            // A deep copy mints a brand new object, so it must not inherit the
            // original's ownership history along with its stats.
            datadata.Flags = ObjFlags.None;
            datadata.LastOwnerId = 0;
            datadata.LastOwnerMindId = 0;
            var serialized = MessagePackSerializer.Serialize(datadata,
                MessagePackSerializerOptions.Standard);
            var datadata2 = MessagePackSerializer.Deserialize<ObjDataData>(
                serialized, MessagePackSerializerOptions.Standard);
            if (TryDeserialize(S, datadata2, null, out var copy, false)) {
                return copy;
            }
            D.Err("Failed making a deep copy of obj! If there is more than one gen attempt, saving would be broken! {0}", data);
            return null;
        }
    }
}
