namespace Game.Data {
    // Electricity of a def in kW, before upgrades, while the device works (idle draw of 0
    // is not a minimum). A max of float.PositiveInfinity means there is no upper bound
    public struct DefPower {
        public bool HasConsumption;
        public float ConsumeMin;
        public float ConsumeMax;
        public bool HasProduction;
        public float ProduceMin;
        public float ProduceMax;
        // In kW per grid cycle, like ElectricNode.BatteryMaxCharge
        public float BatteryCapacity;
        public float BatteryOutput;

        public bool IsEmpty => !HasConsumption && !HasProduction && BatteryCapacity <= 0f;

        // Components of one device share one node, and the node's demand is whatever the
        // last component asked for, so merging keeps the widest range instead of a sum
        public void AddConsumption(float min, float max) {
            if (max <= 0f) { return; }
            if (!HasConsumption) {
                HasConsumption = true;
                ConsumeMin = min;
                ConsumeMax = max;
                return;
            }
            if (min < ConsumeMin) {
                ConsumeMin = min;
            }
            if (max > ConsumeMax) {
                ConsumeMax = max;
            }
        }

        public void AddConsumption(float fixedCost) {
            AddConsumption(fixedCost, fixedCost);
        }

        public void AddProduction(float min, float max) {
            if (max <= 0f) { return; }
            if (!HasProduction) {
                HasProduction = true;
                ProduceMin = min;
                ProduceMax = max;
                return;
            }
            if (min < ProduceMin) {
                ProduceMin = min;
            }
            if (max > ProduceMax) {
                ProduceMax = max;
            }
        }

        public void SetBattery(float capacity, float output) {
            BatteryCapacity = capacity;
            BatteryOutput = output;
        }
    }
}
