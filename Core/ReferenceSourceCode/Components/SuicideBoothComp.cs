using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using Game.Systems.Stats;
using Game.Systems.Creatures;

namespace Game.Components {
    public sealed class SuicideBoothComp : BaseSlotsComp<SuicideBoothComp>, IDefPowerProvider {

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new SuicideBoothComp());
        }
        private const float DefaultEnergyCost = 10f;

        // Each occupied slot draws the full cost
        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            var cost = config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost);
            power.AddConsumption(cost, cost * NumSlotsIn(config));
        }

        public override int SlotDesignation => SlotDesignationH.Suicide;

        private float energyCost;
        public void ApplyModifiersTo(StatsBeing stats) {
            suicideBoothZero ??= StatModifier.Sets(0f,
                T.ObjectsDevicesSuicidebooth);
            stats.BloodRegenRate.TryAddModifier(suicideBoothZero);
            stats.WoundHealRate.TryAddModifier(suicideBoothZero);
        }

        public void RemoveModifiers(StatsBeing stats) {
            if (suicideBoothZero == null) { return; }
            stats.BloodRegenRate.RemoveModifier(suicideBoothZero, false);
            stats.WoundHealRate.RemoveModifier(suicideBoothZero, false);
        }


        protected override void OnConfig() {
            OnConfigSlots();
            energyCost = Config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost);
        }

        public override void OnLateReady(bool wasLoaded) {
            if (wasLoaded && Tile.IsConstructed) {
                CreateBeingSlots(IconId.CSkull, T.ObjectsDevicesSuicidebooth);
            }
        }

        public override void OnRemove() {
            RemoveSlots();
        }

        private ElectricNodeComp elNode;
        private static StatModifier suicideBoothZero;

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp el) {
                    elNode = el;
                    el.AddAfterTick(this, AfterTickGrid);
                }
            } if (message == MsgIdH.ConstructionFinished) {
                CreateBeingSlots(IconId.CSkull, T.ObjectsDevicesSuicidebooth);
            }
        }

        private void AfterTickGrid() {
            if (!elNode.IsPowerable) { return; }
            var powerUse = 0f;
            for (int i = slots.Count - 1; i >= 0; i--) {
                Slot slot = slots[i];
                if (slot.ContainedBeing == null) { continue; }
                var data = slot.ContainedBeing.Data;
                powerUse += energyCost;
                if (elNode.IsConsuming) {
                    if (data.Species.IsBiological) {
                        data.StatsBeing.BloodAmount.TryDrain(0.05f / Consts.TicksPerMinute * 10f);
                    } else {
                        data.Body.MindSlot.TryAddOrUpdateWound(Tile, DamageType.Sharp,
                            Severity.Mild, G.Ticks, 0.5f);
                    }
                    ApplyModifiersTo(data.StatsBeing);
                } else {
                    RemoveModifiers(data.StatsBeing);
                }
            }
            elNode.SetConsumerWantedInput(powerUse);
        }

        public override bool IsCompatibleWith(BeingData data) {
            return true;
        }

        public override string ToString() {
            return " * SuicideBooth";
        }

        public override Facing.Type SpotRotation => Tile.Transform.Rotation;
    }
}
