using System;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Utils;
using Game.Systems.Stats;
using Game.Systems.Creatures;
using Game.Systems.Conditions;
using Game.Components.Upgrades;

namespace Game.Components {
    public sealed class RepairStationComp : BaseSlotsComp<RepairStationComp>,
            ISpeedUpgradeable, IDefPowerProvider {
        private const int DefaultRepairCost = 15;

        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            power.AddConsumption(config.GetInt(PropertyIdH.RepairCost, DefaultRepairCost));
        }
        public int Uses { get; private set; }
        public bool IsRepairing { get; private set; }
        public float RepairRate;
        private float upgradeSpeed = 1f;
        private float repairCost;
        private float chargePerHour;
        private float chargePerGridTick;
        private Being currentBeing;
        public override int SlotDesignation => SlotDesignationH.BotRepair;

        public bool IsSpeedUpgradeable => true;

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new RepairStationComp());
        }

        private ElectricNodeComp elNode;

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                elNode = (ElectricNodeComp) sender;
                elNode.AddAfterTick(this, AfterTickGrid);
            } else if (message == MsgIdH.ConstructionFinished) {
                CreateBeingSlots(IconId.CWrench, T.ObjectsDevicesRepairstation);
            }
        }
        public bool CanRepair() {
            return elNode.IsPowerable && !elNode.HasDeficit && IsUsable;
        }

        private void AfterTickGrid() {
            // Must run before the CanRepair() check: an unpowered station still has to
            // release beings it no longer services, or they stay stuck forever.
            if (currentBeing == null) {
                ReleaseStaleOccupants();
            }
            if (!CanRepair()) {
                if (IsRepairing) {
                    StopRepairing();
                }
                return;
            }
            var pos = Tile.PosIdx;
            if (currentBeing != null) {
                // try to keep servicing the last being who was sitting here
                bool stillNeedsService = currentBeing.Data.Body.CanHeal
                    || currentBeing.Data.HasCondition(ConditionIdH.Discharged);
                if (currentBeing.PosIdx != pos || !stillNeedsService) {
                    StopRepairing();
                } else {
                    BeingUtils.TryCharge(currentBeing.Data, chargePerGridTick);
                }
                return;
            }
            if (currentBeing == null) {
                if (S.Beings.ByPos.TryGetValue(pos, out var beings)) {
                    for (int i = beings.Count - 1; i >= 0; i--) {
                        Being b = beings[i];
                        if (b.Motion.IsBusy) { continue; }
                        // allow healing dead bots by relocating them
                        // if (b.IsDead) { continue; }
                        bool damaged = b.Data.Body.CanHeal
                            && b.Data.HasCondition(ConditionIdH.Damaged);
                        bool discharged = b.Data.HasCondition(ConditionIdH.Discharged);
                        if (damaged || discharged) {
                            StartRepairing(b);
                            return;
                        }
                    }
                }
            }
            if (elNode.IsConsuming) {
                // nobody is charging
                StopRepairing();
            }
        }

        protected override void OnConfig() {
            OnConfigSlots();
            RepairRate = Config.GetFloat(PropertyIdH.RepairRate, 5f);
            repairCost = Config.GetInt(PropertyIdH.RepairCost, DefaultRepairCost);
            chargePerHour = Config.GetFloat(PropertyIdH.ChargePerHour, 5f);
            chargePerGridTick = chargePerHour / Consts.TicksPerHour * 10f;
        }

        public override void OnSave() {
            GetOrCreateData().SetInt("Uses", Uses);
        }

        protected override void OnLoad(ComponentData data) {
            Uses = data.GetInt("Uses", 0);
        }

        public override void OnLateReady(bool wasLoaded) {
            if (wasLoaded && Tile.IsConstructed) {
                CreateBeingSlots(IconId.CWrench, T.ObjectsDevicesRepairstation);
            }
        }

        public override string ToString() {
            return string.Format(" * RepairStation({1}) (Used: x{0})", Uses,
                GetHashCode());
        }

        public override void OnRemove() {
            RemoveSlots();
            StopRepairing();
        }

        public bool StartRepairing(Being being) {
            if (!CanRepair()) { return false; }
            if (currentBeing != null && currentBeing != being) {
                // Someone else is claiming the station - hand it over instead of
                // losing track of the previous occupant and its InDevice flag.
                StopRepairing();
            }
            // ensure that maintenance rate mod is set
            elNode.SetConsumerWantedInput(repairCost);
            being.Data.StateFlags |= BeingState.InDevice;
            IsRepairing = true;
            currentBeing = being;
            // if (being.IsDead) {
                if (being.Data.Conditions.TryGetValue(ConditionIdH.Damaged, out var cond)) {
                    if (cond.IsDisabled) {
                        cond.Flags &= ~ConditionFlags.Inactive;
                        cond.Flags |= ConditionFlags.TickAfterDeath;
                    }
                    if ((cond.Flags & ConditionFlags.RegisteredWithSystem) != ConditionFlags.RegisteredWithSystem) {
                        D.Err("Damaged condition was not registered!");
                        S.Sig.ConditionAdded.Send(cond, true);
                    }
                }
            // }
            Use(being.Data, slots[0]);
            return true;
        }

        public override bool Use(BeingData worker, Slot slot) {
            var usable = CanRepair() && worker.Being == currentBeing;
            if (usable) {
                if (maintenanceRateMod == null) {
                    maintenanceRateMod = StatModifier.Add(
                        (RepairRate * upgradeSpeed) / Consts.TicksPerDay,
                        T.ObjectsDevicesRepairstation);
                }
                worker.StatsBeing.MaintenanceRate.
                    TryAddModifier(maintenanceRateMod, true);
            } else {
                StopRepairing();
            }
            return usable;
        }

        public void StopRepairing(bool eject = true) {
            IsRepairing = false;
            if (currentBeing != null) {
                Release(currentBeing, eject);
                currentBeing = null;
            }
            if (Tile.IsConstructed) {
                elNode?.SetConsumerWantedInput(0f);
            }
        }

        /// <summary>
        /// Strips everything this station stamps on a being it services. Ejecting only
        /// makes sense for a being we were actually servicing - a stale occupant is left
        /// where it is, now that the player can relocate it again.
        /// </summary>
        private void Release(Being being, bool eject) {
            being.Data.StatsBeing.MaintenanceRate.
                RemoveModifier(maintenanceRateMod, false);
            being.Conditions.HideInfoBar();
            being.Data.StateFlags &= ~BeingState.InDevice;
            if (!eject || (!being.IsDead && !being.Data.Body.IsIncapacitated)) {
                return;
            }
            // Someone is carrying it away, it must stay in their hands (STA-1724)
            if (being.IsCarriedByAny) { return; }
            bool ejected = false;
            int leastCrowdedPos = Pos.Invalid;
            int leastCrowdedCnt = int.MaxValue;
            Pos.ForEachAround(Tile.PosIdx, 1, false, true, (p) => {
                if (!S.Sys.Path.IsWalkable(p) || S.Sys.Path.IsDoor(p)) {
                    return true;
                }
                if (S.Beings.ByPos.TryGetValue(p, out var atPos)) {
                    var cnt = Arrays.Count(atPos);
                    if (leastCrowdedCnt > cnt) {
                        leastCrowdedCnt = cnt;
                        leastCrowdedPos = p;
                    }
                    if (cnt > 0) { return true; }
                }
                // found pos
                being.Motion.TeleportTo(
                    EntityUtils.PosToV2WithRandOffset(p));
                ejected = true;
                return false;
            });
            if (!ejected && leastCrowdedPos != Pos.Invalid) {
                being.Motion.TeleportTo(
                    EntityUtils.PosToV2WithRandOffset(leastCrowdedPos));
            }
        }

        /// <summary>
        /// The InDevice flag survives a save, but which being this station was servicing
        /// does not, and a second bot claiming the station used to overwrite it. Nothing
        /// else can heal that: the orphan sweep in BeingsSys only covers beings with no
        /// device tile underneath them. So while we service nobody, anyone standing on
        /// the station who isn't in our slot must not be flagged as being in a device.
        /// </summary>
        private void ReleaseStaleOccupants() {
            if (Arrays.IsEmpty(slots)) { return; }
            if (!S.Beings.ByPos.TryGetValue(Tile.PosIdx, out var beings)) { return; }
            for (int i = beings.Count - 1; i >= 0; i--) {
                var b = beings[i];
                if (!b.Data.HasStateFlag(BeingState.InDevice)) { continue; }
                if (b == slots[0].ContainedBeing) { continue; }
                // We only ever flag bots, so we only ever unflag bots
                if (!IsCompatibleWith(b.Data)) { continue; }
                D.Warn("Releasing a stale repair station occupant: {0} | {1}", b, Tile);
                Release(b, eject: false);
            }
        }

        // The slot only knows about bots that were put into it. A bot lying on the station
        // outside the slot - one we cannot service, like a drone with a destroyed battery,
        // or one the player relocated here - leaves the slot looking free, so slot searches
        // would stack another bot on top of it (STA-1577). Passers-by don't count.
        public bool HasOccupantOutsideSlot() {
            if (Arrays.IsEmpty(slots)) { return false; }
            if (!S.Beings.ByPos.TryGetValue(Tile.PosIdx, out var beings)) { return false; }
            var slot = slots[0];
            for (int i = 0; i < beings.Count; i++) {
                var b = beings[i];
                if (b == slot.ContainedBeing || b == slot.ReservedBy) { continue; }
                if (b.Motion.IsBusy) { continue; }
                return true;
            }
            return false;
        }

        public static bool TryGetAt(int pos, out RepairStationComp station) {
            var tile = EntityUtils.GetTopmostParentTile(pos);
            if (tile == null) {
                station = null;
                return false;
            }
            return tile.TryGetComponent<RepairStationComp>(out station);
        }

        // A downed bot on the station can be relocated (STA-1724), so the carrier takes it
        // out of our hands first. Not ejecting: the carrier holds it right after this.
        public void ReleaseForPickup(Being being) {
            if (!Arrays.IsEmpty(slots) && slots[0].ContainedBeing == being) {
                slots[0].Remove(being);
            }
            if (currentBeing == being) {
                StopRepairing(eject: false);
            } else if (being.Data.HasStateFlag(BeingState.InDevice)) {
                Release(being, eject: false);
            }
        }

        // Slot search filter that prefers stations with nothing lying on them
        public static readonly Func<ISlots, bool> IsUnobstructed =
            s => s is not RepairStationComp r || !r.HasOccupantOutsideSlot();

        public override bool IsCompatibleWith(BeingData data) {
            return !data.Species.IsBiological;
        }

        public void ApplySpeedUpgrade(float amt) {
            upgradeSpeed += amt;
        }

        private StatModifier maintenanceRateMod;
    }
}
