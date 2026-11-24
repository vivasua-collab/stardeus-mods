using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Components.Upgrades {
    /// <summary>
    /// Turns the Intrusion Suite from a ship-to-ship tool into something that can reach a
    /// whole faction's network at once. Its only consumer is the Sentinel HQ hack - see
    /// Docs/Architecture/Systems/RogueAIRoute.md - so it deliberately has no OnInstall
    /// side effect: presence is checked on demand via IntrusionSuiteComp.HasSignalAmplifier.
    /// </summary>
    public sealed class SignalAmplifierUpgrade : Upgrade {
        public const string Id = "SignalAmplifier";
        public override string Icon => GraphicsId.ObjUpgradesSignalAmplifierUpgrade;
        public override string Title => T.ObjUpgradesSignalamplifierupgrade;
        public override string UpgradeId => Id;
        public override string MatTypeId => MatId.SignalAmplifierUpgrade;

        [SelfInit]
        private static void Register() {
            Add(Id, new SignalAmplifierUpgrade());
        }

        public override bool IsCompatibleWith(Entity entity) {
            return entity.HasComponent<IntrusionSuiteComp>();
        }
    }
}
