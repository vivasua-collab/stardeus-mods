using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using System.Linq;
using Game.Data.Space;
using Game.Achievements;
using Game.Systems.Space;

namespace Game.Components {
    public sealed class VoidRipperComp : BaseComponent<VoidRipperComp>, IDefPowerProvider {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new VoidRipperComp());
        }
        private const float DefaultEnergyCost = 1000f;
        private float requiredCharge;
        private float currentCharge;
        private float energyCost;
        private float lastPercentage;
        private ElectricNodeComp elNode;
        private BCallback stateChangedB;
        public ElectricNodeComp ENode => elNode;
        public BCallback StateChangedB => stateChangedB ??= new BCallback();
        public float ChargePercentage => requiredCharge > 0 ? currentCharge / requiredCharge : 0f;
        public bool IsCharged => Maths.Approximately(currentCharge, requiredCharge);

        protected override void OnConfig() {
            energyCost = Config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost);
            // 24 hours charge at 1000 MW/h
            requiredCharge = Config.GetInt(PropertyIdH.RequiredChargeKWH, 1000 * 24);
        }

        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            power.AddConsumption(config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost));
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetFloat("Charge", currentCharge);
        }

        protected override void OnLoad(ComponentData data) {
            currentCharge = data.GetFloat("Charge", 0);
        }

        public override string ToString() {
            return " * VoidRipper";
        }

        public override void OnLateReady(bool wasLoaded) {
            UpdateState(false);
        }

        public void DevFillCharge() {
            SetCurrentCharge(requiredCharge);
        }

        private void SetCurrentCharge(float charge) {
            currentCharge = charge;
            UpdateState(true);
        }

        private void UpdateState(bool wasUpdated) {
            var percentage = ChargePercentage;
            bool changed = !wasUpdated || lastPercentage != percentage;
            if (changed) {
                lastPercentage = percentage;
                var bar = ShowInfoBar(Consts.GreenRectColor, percentage,
                    currentCharge, Mathf.RoundToInt(requiredCharge),
                        T.PercentageChargeBar_V1.F(Units.Percentage(percentage)));
                bar.IsPretranslated = true;
                stateChangedB?.Invoke();
            }
        }

        public bool CanDischarge() {
            return IsCharged && Tile.ENode.IsReachable && IsUsable;
        }

        public bool TryDischarge() {
            if (!CanDischarge()) { return false; }
            SetCurrentCharge(0f);
            S.Clock.ExecuteAfterTicks(1, () => {
                float dmgPct = S.Rng.Curve(Tunables.VoidRipperDamageChance);
                Tile.Health.TakeDamage(Tile.Health.MaxHealth * dmgPct, DamageType.Structural);
            });
            return true;
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp elnode) {
                    this.elNode = elnode;
                    elnode.AddAfterTick(this, AfterTickGrid);
                }
            } else if (message == MsgIdH.HealthWentToZero) {
                OnRipperDestroyed();
            }
        }

        private void OnRipperDestroyed() {
            if (!IsCharged) { return; }
            var curSector = SOUtils.GetPlayerSector(S);
            if (curSector == null) {
                D.Err("Void ripper destroyed without player sector");
                return;
            }
            var targetRegion = PickWarpRegion(S, curSector.Region);
            var targetSector = targetRegion.Sectors.OrderBy(r => S.Rng.Value).First();
            var from = A.Player.Parent;
            S.Clock.ExecuteAfterTicks(1, () => {
                if (SOUtils.WarpTo(S, from, targetSector.SO)) {
                    var msg = $"{T.ObjectsDevicesVoidripper}: {T.WarpedToLocation_V1.F(SOUtils.RegionSectorName(targetSector))}";
                    S.Sig.ShowFanfare.Send(msg);
                    S.Sys.Log.AddLine(msg);
                    The.AchievementTracker.Unlock(AchievementVoidRipper.AchievementId);
                }
            });
        }

        // Until FTL drives are unlocked, a region without a relay would strand the ship there
        public static SpaceRegion PickWarpRegion(GameState s, SpaceRegion from) {
            var map = s.Query.GetSpaceMap.Ask();
            var hasFTL = s.Research.IsDefUnlocked(The.Defs.FastGet(DefIdsH.ObjectsDevicesFTLDrive));
            var others = map.Regions.Where(r => r != from).OrderBy(r => s.Rng.Value).ToList();
            if (hasFTL) {
                return others.First();
            }
            return others.FirstOrDefault(r => SOUtils.HasHyperjumpRelay(r)) ?? others.First();
        }

        private void AfterTickGrid() {
            #if !STARDEUS_DEMO
            if (!elNode.IsPowerable) { return; }
            if (Maths.Approximately(currentCharge, requiredCharge)) {
                elNode.SetConsumerWantedInput(0f);
                return;
            }
            elNode.SetConsumerWantedInput(energyCost);
            if (elNode.IsConsuming) {
                currentCharge += energyCost * 10f / Consts.TicksPerHour;
                if (currentCharge > requiredCharge) {
                    currentCharge = requiredCharge;
                }
                UpdateState(true);
            }
            #endif
        }
    }
}
