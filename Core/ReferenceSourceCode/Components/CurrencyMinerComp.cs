using Game.Utils;

namespace Game.Components {
    public sealed class CurrencyMinerComp : BaseComponent<CurrencyMinerComp> {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new CurrencyMinerComp());
        }
        public bool IsMinerAdded;

        public void RegisterMiner() {
            // Create wallet entry
            if (!IsMinerAdded) {
                S.Sys.Economy.AddMiner(this);
                IsMinerAdded = true;
            }
        }
        public void UnregisterMiner() {
            if (IsMinerAdded) {
                IsMinerAdded = false;
                S.Sys.Economy.RemoveMiner(this);
            } else {
                S.Sys.Economy.UpdateMiner(this);
            }
        }

        public override string ToString() {
            return " * Currency Miner";
        }
    }
}
