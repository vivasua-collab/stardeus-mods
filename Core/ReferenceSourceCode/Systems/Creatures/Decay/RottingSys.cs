using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Mods;
using Game.Systems.Conditions;
using Game.Systems.BodyParts;
using Game.Systems.Items;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using KL.Utils;
namespace Game.Systems.Creatures.Decay {
    /// <summary>
    /// Tracks dead beings and extracted body parts (ObjData with BodyPart set) to:
    /// 1. Drive development of Rotting (biological) / Corroding (non-biological) conditions.
    /// 2. Apply decay to stored bodies and all extracted body parts.
    /// Runs once per in-game hour to avoid per-tick overhead with many entities.
    /// On-map bodies: ConditionDecay.Tick handles their decay (also hourly).
    /// </summary>
    public sealed class RottingSys : GameSystem {
        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new RottingSys());
        }

        public const string SysId = "RottingSys";
        public override string Id => SysId;

        public override void Unload() { }

        protected override void OnInitialize() {
            S.Sig.AfterLoadState.AddListener(OnLoadState);
        }

        private void OnLoadState(GameState state) {
            state.Clock.OnTick.AddListener(OnTick);
        }


        private void OnTick(long ticks) {
            if (ticks % Consts.TicksPerHour != 0) { return; }

            TickBeings(ticks);
            TickBodyPartObjs();
        }

        private void TickBeings(long ticks) {
            var bd = S.Query.AllBeingData.Ask();
            foreach (var data in bd.Values) {
                var condId = DecayConditionId(data);
                if (data.HasStateFlag(BeingState.Alive)) {
                    if (data.ConditionProgress.ContainsKey(condId) || data.Conditions.ContainsKey(condId)) {
                        data.RemoveCondition(S, condId);
                    }
                    continue;
                }

                bool isOnMap = data.Being != null;
                bool isStored = data.StorageId != 0;

                // Only process beings that are physically present somewhere
                if (!isOnMap && !isStored) { continue; }
                // Don't decay beings in autodocs, repair stations and body mod spots
                if (data.StateFlags.HasFlag(BeingState.InDevice)) { continue; }


                if (data.HasCondition(condId)) {
                    // Condition is active — handle stored bodies since ConditionsSys
                    // won't tick conditions on beings without an active Being entity
                    if (isStored && !isOnMap) {
                        float slowdown = StorageSlowdown(data);
                        ConditionDecay.ApplyDecay(data,
                            QualityLossPerWeek(data) / slowdown,
                            ConditionLossPerWeek(data) / slowdown,
                            condId, Consts.TicksPerHour);
                    }
                    // On-map bodies: ConditionDecay.Tick handles decay
                } else {
                    // Drive development phase: advance a full hour of progress in one call.
                    // The delay includes ConditionDecay.DecayGraceBand's worth of days so the
                    // condition activates exactly at the start of the visible Mild band,
                    // instead of appearing then vanishing again for the grace period.
                    float totalDelayDays = DevelopmentDays(data)
                        + ConditionDecay.GraceBandDays(ConditionLossPerWeek(data));
                    float devRate = Consts.TicksPerHour / (Consts.TicksPerDay * totalDelayDays);
                    if (isStored) {
                        devRate /= StorageSlowdown(data);
                    }
                    if (Condition.Advance(S, ticks, condId, data, 1,
                            devRate, 0f, false, Severity.Mild)) {
                        ConditionDecay.StartDecayAtGraceBand(data);
                        ConditionDecay.SyncConditionState(data, condId);
                    }
                }
            }
        }

        private void TickBodyPartObjs() {
            var allObjs = S.Query.GetAllObjs.Ask();
            foreach (var kvp in allObjs) {
                var obj = kvp.Value;
                if (obj.BodyPart == null) { continue; }
                if (obj.State == ObjState.Destroyed) { continue; }
                // Don't rot parts in devices
                if (obj.State == ObjState.InDevice) { continue; }

                obj.BodyPartState ??= BodyPartState.BrandNew();

                bool isBio = obj.BodyPart.IsBiological;
                float qualityLoss = isBio ? Tunables.RottingQualityLossPerWeek : Tunables.CorrodingQualityLossPerWeek;
                float condLoss = isBio ? Tunables.RottingConditionLossPerWeek : Tunables.CorrodingConditionLossPerWeek;

                if (obj.StorageId != 0) {
                    float slowdown = isBio ? Tunables.RottingStorageSlowdown : Tunables.CorrodingStorageSlowdown;
                    if (slowdown > 0f) {
                        qualityLoss /= slowdown;
                        condLoss /= slowdown;
                    }
                }

                ConditionDecay.ApplyDecayToPartState(
                    obj.BodyPartState, obj.BodyPart,
                    qualityLoss, condLoss, Consts.TicksPerHour);
            }
        }

        private static int DecayConditionId(BeingData data) {
            return Hashes.S(data.Species.IsBiological ? ConditionId.Rotting : ConditionId.Corroding);
        }

        private static float DevelopmentDays(BeingData data) {
            return data.Species.IsBiological
                ? Tunables.RottingDevelopmentDays
                : Tunables.CorrodingDevelopmentDays;
        }

        private static float StorageSlowdown(BeingData data) {
            return data.Species.IsBiological
                ? Tunables.RottingStorageSlowdown
                : Tunables.CorrodingStorageSlowdown;
        }

        private static float QualityLossPerWeek(BeingData data) {
            return data.Species.IsBiological
                ? Tunables.RottingQualityLossPerWeek
                : Tunables.CorrodingQualityLossPerWeek;
        }

        private static float ConditionLossPerWeek(BeingData data) {
            return data.Species.IsBiological
                ? Tunables.RottingConditionLossPerWeek
                : Tunables.CorrodingConditionLossPerWeek;
        }
    }
}
