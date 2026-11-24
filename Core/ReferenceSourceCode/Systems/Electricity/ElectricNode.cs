using System;
using KL.Utils;
using MessagePack;
using UnityEngine;

namespace Game.Systems.Electricity {
    [MessagePackObject]
    // FIXME: Change bools to flags
    public struct ElectricNode {
        public void RestoreFromSaved(ElectricNode saved) {
            Idx = saved.Idx;
            GridId = saved.GridId;
            EntityId = saved.EntityId;
            IsOn = saved.IsOn;
            Produced = saved.Produced;
            Consumed = saved.Consumed;
            Deficit = saved.Deficit;
            NumConnections = saved.NumConnections;
            HasWorkerLink = saved.HasWorkerLink;
            BatteryCharge = saved.BatteryCharge;

            IsReachable = saved.IsReachable;
            ProducerMaxOutput = saved.ProducerMaxOutput;
            ConsumerWantedInput = saved.ConsumerWantedInput;
        }

        [IgnoreMember] public bool IsInitialized => EntityId != 0;
        [IgnoreMember] public bool IsValid => Idx != -1;
        // WARNING if adding new stuff, that is saved - update RestoreFromSaved!
        [Key("Idx")] public int Idx;
        [Key("GridId")] public int GridId;
        [Key("IsReachable")] public bool IsReachable;
        [Key("EntityId")] public int EntityId;
        [Key("IsOn")] public bool IsOn;
        [Key("Produced")] public float Produced;
        [Key("Consumed")] public float Consumed;
        [Key("Deficit")] public float Deficit;
        [Key("NumCon")] public int NumConnections;
        [Key("BatCharge")] public float BatteryCharge;
        // Maximum burn we could reach with the producer
        [Key("ProdMaxOut")] public float ProducerMaxOutput;
        [Key("WantInput")] public float ConsumerWantedInput;
        [Key("HasWorkerLink")] public bool HasWorkerLink;
        // WARNING if adding new stuff, that is saved - update RestoreFromSaved!
        [IgnoreMember] public float BatteryMaxCharge;
        [IgnoreMember] public float AdjustedProducerMaxOutput => HasMalfunction ? 0f : ProducerMaxOutput;
        [IgnoreMember] public bool HasMalfunction;
        [IgnoreMember] public int PosIdx;
        [IgnoreMember] public Vector2 Position;
        [IgnoreMember] public bool IsShipComputer;
        [IgnoreMember] public int ProvidesDisk;
        [IgnoreMember] public int ProvidesMem;
        [IgnoreMember] public int ProvidesCPU;
        [IgnoreMember] public int ProvidesGPU;
        [IgnoreMember] public bool IsConnector;
        [IgnoreMember] public bool IsProducer;
        // Smart producer will take some of the output back
        [IgnoreMember] public bool IsProducerSmart;
        [IgnoreMember] public bool IsProducing => Produced > 0.00001f;
        [IgnoreMember] public bool IsConsuming => Consumed > 0.00001f;
        [IgnoreMember] public bool IsPowerable => IsOn && (NumConnections > 0 || HasWorkerLink);
        [IgnoreMember] public bool HasDeficit => Deficit > 0.00001f;
        [IgnoreMember] public bool IsConsumerHighPriority;
        [IgnoreMember] public bool IsConsumer; // regular priority
        [IgnoreMember] public bool IsConsumerLowPriority;
        [IgnoreMember] public bool IsBattery;
        [IgnoreMember] public int FreeConnections => MaxConnections - NumConnections;
        [IgnoreMember] public int MaxConnections;
        [IgnoreMember] public int ConnectorRange;
        [IgnoreMember] public int ConnectorRangeSqr;
        [IgnoreMember] public bool NeedsSync;

        /// <returns>deficit</returns>
        public float SetConsumed(float consumed, float deficit) {
            // D.Warn("Setting consumed: {0}->{1}, Def: {2}->{3}. FOr {4}",
            //     Consumed, consumed, Deficit, deficit, Idx);
            if (HasMalfunction && consumed > deficit) {
                deficit = consumed;
                consumed = 0f;
            }
            if (consumed != Consumed || Deficit != deficit) {
                Consumed = consumed;
                Deficit = deficit;
                NeedsSync = true;
            }
            return deficit;
        }

        public void SetProduced(float produced) {
            if (produced != Produced) {
                Produced = produced;
                NeedsSync = true;
            }
        }

        public void SetMalfunction(bool isMalfunction)  {
            if (HasMalfunction != isMalfunction) {
                HasMalfunction = isMalfunction;
                NeedsSync = true;
            }
        }

        public void UpdateBattery(float produced, float consumed, float chargeDelta) {
            bool chargeChanged = chargeDelta != 0f;
            if (chargeChanged || produced != Produced || consumed != Consumed) {
                Produced = produced;
                Consumed = consumed;
                if (chargeChanged) {
                    BatteryCharge = Maths.Clamp(
                        BatteryCharge + chargeDelta, 0f, BatteryMaxCharge);
                }
                NeedsSync = true;
            }
        }

        public void SetDeficit(float deficit) {
            if (deficit != Deficit) {
                Deficit = deficit;
                NeedsSync = true;
            }
        }

        public void SetBatteryCharge(float charge) {
            if (charge != BatteryCharge) {
                BatteryCharge = charge;
                NeedsSync = true;
            }
        }

        public override string ToString() {
            #if !PRODUCTION_BUILD
            return D.DumpInstance(this, forLog: true);
            #else
            return $"ElectricNode[{Idx}/Pos:{Position}]";
            #endif
        }
    }
}
