using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using Game.Systems.Creatures;

namespace Game.Components {
    public sealed class ChargeStationComp : BaseSlotsComp<ChargeStationComp>,
            IDefPowerProvider {
        private const float DefaultChargeCost = 5f;
        public int Uses { get; private set; }

        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            power.AddConsumption(config.GetFloat(PropertyIdH.ChargeCost, DefaultChargeCost));
        }
        private float chargePerHour;
        private float chargeCost;
        private Being currentBeing;
        public override bool IsAvailable => base.IsAvailable && Tile.ENode.IsPowerable;
        public override int SlotDesignation => SlotDesignationH.Charging;

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new ChargeStationComp());
        }

        private ElectricNodeComp elNode;
        private float chargePerGridTick;

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp enode) {
                    elNode = enode;
                    enode.AddAfterTick(this, AfterTickGrid);
                }
            } else if (message == MsgIdH.ConstructionFinished) {
                CreateBeingSlots(IconId.CEnergy, T.ObjectsDevicesChargestation);
            }
        }
        public bool CanCharge() {
            return elNode.IsPowerable && !elNode.HasDeficit && IsUsable;
        }

        // AIActionUseSlot also puts a bot into the slot from the next tile (after the winch
        // cut its flight short, for one), and the bot is then drawn on the station. Going by
        // the tile alone left it "charging" with no charge until its battery ran out, with
        // the station reserved for nobody else (STA-1594).
        private bool IsServing(Being b) {
            if (b.PosIdx == Tile.PosIdx || IsInSlot(b)) {
                return BeingUtils.NeedsCharge(b.Data);
            }
            // D.Warn("Being too far away from charge station! {0}, {1}, {2}",
            //     Pos.StringFromTo(b.PosIdx, Tile.PosIdx), b, Tile);
            return false;
        }

        private bool IsInSlot(Being b) {
            if (Arrays.IsEmpty(slots)) { return false; }
            for (int i = 0; i < slots.Count; i++) {
                if (slots[i].ContainedBeing == b) {
                    return true;
                }
            }
            return false;
        }

        private void AfterTickGrid() {
            // Before any power check: a bot that left or filled up during a deficit must
            // drop the station's draw, or the grid stays in deficit for nobody
            if (currentBeing != null) {
                if (!IsServing(currentBeing)) {
                    StopCharging();
                    return;
                }
                // Charge only for a grid tick that delivered the station's draw. The draw
                // is registered first, so a grid with nothing to give never hands out charge
                if (IsUsable && elNode.IsConsuming
                        && !BeingUtils.TryCharge(currentBeing.Data, chargePerGridTick)) {
                    StopCharging();
                }
                return;
            }
            if (CanCharge()) {
                if (!Arrays.IsEmpty(slots)) {
                    for (int i = 0; i < slots.Count; i++) {
                        var occupant = slots[i].ContainedBeing;
                        if (occupant != null && IsServing(occupant)) {
                            StartCharging(occupant);
                            return;
                        }
                    }
                }
                if (S.Beings.ByPos.TryGetValue(Tile.PosIdx, out var beings)) {
                    for (int i = beings.Count - 1; i >= 0; i--) {
                        Being b = beings[i];
                        if (b.Motion.IsBusy) { continue; }
                        if (IsServing(b)) {
                            StartCharging(b);
                            return;
                        }
                    }
                }
            }
            // Also drops a draw restored from a save made mid-charge (currentBeing is not
            // saved)
            if (elNode.Node.ConsumerWantedInput > 0f) {
                StopCharging();
            }
        }

        protected override void OnConfig() {
            OnConfigSlots();
            chargePerHour = Config.GetFloat(PropertyIdH.ChargePerHour, 25f);
            chargePerGridTick = chargePerHour / Consts.TicksPerHour * 10f;
            chargeCost = Config.GetFloat(PropertyIdH.ChargeCost, DefaultChargeCost);
        }

        public override void OnLateReady(bool wasLoaded) {
            if (wasLoaded && Tile.IsConstructed) {
                CreateBeingSlots(IconId.CEnergy, T.ObjectsDevicesChargestation);
            }
        }

        public override bool IsCompatibleWith(BeingData data) {
            return data.StatsBeing.BatteryCapacity.BaseValue > 0f;
        }

        public override void OnRemove() {
            RemoveSlots();
        }

        public override void OnSave() {
            GetOrCreateData().SetInt("Uses", Uses);
        }


        protected override void OnLoad(ComponentData data) {
            Uses = data.GetInt("Uses", 0);
        }

        public override string ToString() {
            return string.Format(" * ChargeStation({1}) (Used: x{0})", Uses,
                GetHashCode());
        }

        // No power check: a bot waiting at a station on a grid that produces nothing has to
        // show up as a deficit, or the player sees no reason why it is not charging
        private bool StartCharging(Being being) {
            if (currentBeing != null) {
                D.Err("Someone else trying to charge! {0}. Already charging: {1}",
                    being, currentBeing);
                return false;
            }
            currentBeing = being;
            elNode.SetConsumerWantedInput(chargeCost);
            return true;
        }

        public void StopCharging() {
            currentBeing = null;
            elNode.SetConsumerWantedInput(0f);
        }
    }
}
