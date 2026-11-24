using Game.Components.Upgrades;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;

namespace Game.Components {
    /// <summary>
    /// The ship side of Signal Intrusion: a powered Intrusion Suite is what lets the
    /// Hack option appear when hailing an NPC ship. Rogue AI tech.
    /// </summary>
    public sealed class IntrusionSuiteComp : BaseComponent<IntrusionSuiteComp>,
            IDefPowerProvider {
        [SelfInit]
        private static void Register() {
            notAvailable = null;
            AddComponentPrototype(new IntrusionSuiteComp());
        }

        private const float DefaultEnergyCost = 120f;
        private float energyCost;

        private static string notAvailable;
        public static string NotAvailable {
            get {
                return notAvailable ??= Texts.AutoLink(Texts.Red(T.IntrusionSuiteNotAvailable),
                    The.Defs.FastGet(DefIdsH.ObjectsDevicesIntrusionSuite));
            }
        }

        public static IntrusionSuiteComp Find(GameState s) {
            return s.Components.FindFirstMatching<IntrusionSuiteComp>(
                (c) => c.Tile.IsConstructed && c.Tile.ENode.IsConsuming);
        }

        /// <summary>
        /// True when a built and powered suite carries the Signal Amplifier, which is what
        /// lets it reach a whole faction's network instead of a single ship.
        /// </summary>
        public static bool HasSignalAmplifier(GameState s) {
            return s.Components.FindFirstMatching<IntrusionSuiteComp>(
                (c) => c.Tile.IsConstructed && c.Tile.ENode.IsConsuming
                    && HasAmplifierInstalled(c)) != null;
        }

        private static bool HasAmplifierInstalled(IntrusionSuiteComp suite) {
            var upgrades = suite.Tile.Upgrades;
            if (upgrades?.Upgrades == null) { return false; }
            for (int i = upgrades.Upgrades.Length - 1; i >= 0; i--) {
                if (upgrades.Upgrades[i] == SignalAmplifierUpgrade.Id) {
                    return true;
                }
            }
            return false;
        }

        protected override void OnConfig() {
            energyCost = Config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost);
        }

        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            power.AddConsumption(config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost));
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp node) {
                    node.SetConsumerWantedInput(energyCost);
                }
            }
        }

        public override string ToString() {
            return " * IntrusionSuite";
        }
    }
}
