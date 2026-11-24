using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using Game.Systems.Stats;
using Game.Systems.Creatures;

namespace Game.Components {
    public sealed class AutoDocComp : BaseSlotsComp<AutoDocComp>, IDefPowerProvider {
        public float MaxConsumption => energyCost;

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new AutoDocComp());
        }
        private const float DefaultEnergyCost = 25f;

        // Each occupied slot draws the full cost
        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            var cost = config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost);
            power.AddConsumption(cost, cost * NumSlotsIn(config));
        }

        public override int SlotDesignation => SlotDesignationH.Healing;

        private float energyCost;
        private float woundHealRateAddPctPerHour;
        private float bloodClotRateAddPctPerHour;
        private float bloodRegenRateAddPctPerHour;
        private float intestinesIncreasePerTick;
        private float hygieneIncreasePerTick;
        private float toiletIncreasePerTick;
        private float minSleepIncreasePerTick;
        private static StatModifier autoDocMod_WoundHealRate;
        private static StatModifier autoDocMod_BloodClotRate;
        private static StatModifier autoDocMod_BloodRegenRate;
        private static StatModifier autoDocMod_O2;
        private static StatModifier autoDocMod_Temp;

        protected override void OnConfig() {
            OnConfigSlots();
            energyCost = Config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost);
            woundHealRateAddPctPerHour = Config.GetFloat(PropertyIdH.WoundHealRateAddPctPerHour, 1f);
            bloodClotRateAddPctPerHour = Config.GetFloat(PropertyIdH.BloodClotRateAddPctPerHour, 1f);
            bloodRegenRateAddPctPerHour = Config.GetFloat(PropertyIdH.BloodRegenRateAddPctPerHour, 1f);
            intestinesIncreasePerTick = Config.GetFloat(PropertyIdH.IntestinesIncreasePerHour, 1f) / Consts.TicksPerHour;
            toiletIncreasePerTick = Config.GetFloat(PropertyIdH.ToiletIncreasePerHour, 1f) / Consts.TicksPerHour;
            hygieneIncreasePerTick = Config.GetFloat(PropertyIdH.HygieneIncreasePerHour, 1f) / Consts.TicksPerHour;
            minSleepIncreasePerTick = Config.GetFloat(PropertyIdH.MinSleepIncreasePerHour, 0.1f) / Consts.TicksPerHour;
        }

        public override void OnLateReady(bool wasLoaded) {
            if (wasLoaded && Tile.IsConstructed) {
                CreateBeingSlots(IconId.CHealth, T.MedicalSlots);
            }
        }

        public override void OnRemove() {
            RemoveSlots();
        }

        private ElectricNodeComp elNode;

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp el) {
                    elNode = el;
                    el.AddAfterTick(this, AfterTickGrid);
                }
            } if (message == MsgIdH.ConstructionFinished) {
                CreateBeingSlots(IconId.CHealth, T.MedicalSlots);
            }
        }


        public void ApplyModifiersTo(StatsBeing stats) {
            if (autoDocMod_BloodClotRate == null) {
                autoDocMod_BloodClotRate = StatModifier.Add(
                    bloodClotRateAddPctPerHour / Consts.TicksPerHour,
                        T.ObjectsDevicesAutodoc);
                autoDocMod_BloodRegenRate = StatModifier.Add(
                    bloodRegenRateAddPctPerHour / Consts.TicksPerHour,
                        T.ObjectsDevicesAutodoc);
                autoDocMod_WoundHealRate = StatModifier.Add(
                    woundHealRateAddPctPerHour / Consts.TicksPerHour,
                        T.ObjectsDevicesAutodoc);
                autoDocMod_O2 = StatModifier.Sets(0.21f, T.ObjectsDevicesAutodoc);
                autoDocMod_Temp = StatModifier.Sets(Units.CelsiusToKelvin(22f),
                    T.ObjectsDevicesAutodoc);
            }
            stats.WoundHealRate.TryAddModifier(autoDocMod_WoundHealRate);
            stats.BloodClotRate.TryAddModifier(autoDocMod_BloodClotRate);
            stats.BloodRegenRate.TryAddModifier(autoDocMod_BloodRegenRate);
            stats.O2.TryAddModifier(autoDocMod_O2);
            stats.Temp.TryAddModifier(autoDocMod_Temp);
        }

        public void RemoveModifiers(StatsBeing stats) {
            if (autoDocMod_BloodClotRate == null) { return; }
            stats.WoundHealRate.RemoveModifier(autoDocMod_WoundHealRate, false);
            stats.BloodClotRate.RemoveModifier(autoDocMod_BloodClotRate, false);
            stats.BloodRegenRate.RemoveModifier(autoDocMod_BloodRegenRate, false);
            stats.O2.RemoveModifier(autoDocMod_O2, false);
            stats.Temp.RemoveModifier(autoDocMod_Temp, false);
        }

        public override bool Use(BeingData b, Slot s) {
            // inject food in to intestines
            var stats = b.StatsBeing;
            if (b.HasNeed(NeedIdH.Hunger)) {
                var intestines = stats.IntestinesContents;
                intestines.TryIncrease(intestinesIncreasePerTick);
            }
            if (b.HasNeed(NeedIdH.Toilet)) {
                var toilet = stats.Toilet;
                toilet.TryIncrease(toiletIncreasePerTick);
            }
            if (b.HasNeed(NeedIdH.Hygiene)) {
                var hygiene = stats.Hygiene;
                hygiene.TryIncrease(hygieneIncreasePerTick);
            }
            if (b.HasNeed(NeedIdH.Sleep)) {
                var sleep = stats.Sleep;
                var sleepRate = stats.SleepRate.Value;
                if (sleepRate <= 0f) {
                    sleepRate = minSleepIncreasePerTick;
                }
                sleep.TryIncrease(sleepRate);
            }

            return true;
        }

        private void AfterTickGrid() {
            if (!elNode.IsPowerable) { return; }
            var powerUse = 0f;
            for (int i = slots.Count - 1; i >= 0; i--) {
                Slot slot = slots[i];
                if (slot.ContainedBeing == null) { continue; }
                var data = slot.ContainedBeing.Data;
                if (slot.ContainedBeing.IsDead) {
                    slot.Remove(slot.ContainedBeing);
                    RemoveModifiers(data.StatsBeing);
                    continue;
                }
                powerUse += energyCost;
                if (elNode.IsConsuming) {
                    ApplyModifiersTo(data.StatsBeing);
                } else {
                    RemoveModifiers(data.StatsBeing);
                }
            }
            elNode.SetConsumerWantedInput(powerUse);
        }

        public override bool IsCompatibleWith(BeingData data) {
            return data.Species.IsBiological;
        }

        public override string ToString() {
            return " * AutoDoc";
        }

        public override Facing.Type SpotRotation => Tile.Transform.Rotation;
    }
}
