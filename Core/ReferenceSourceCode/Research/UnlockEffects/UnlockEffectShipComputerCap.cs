using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using Game.Utils;
using Game.CodeGen;

namespace Game.Research {
    public sealed class UnlockEffectShipComputerCap : IResearchUnlockEffect {
        [SelfInit]
        private static void Register() {
            Registries.UnlockEffects.Add("ShipComputerCap_2",
                new UnlockEffectShipComputerCap(2));
            Registries.UnlockEffects.Add("ShipComputerCap_4",
                new UnlockEffectShipComputerCap(4));
            Registries.UnlockEffects.Add("ShipComputerCap_6",
                new UnlockEffectShipComputerCap(6));
            Registries.UnlockEffects.Add("ShipComputerCap_8",
                new UnlockEffectShipComputerCap(8));
        }
        public string Icon => IconId.CUpgrade;
        public string Title => T.ResearchCpuCap_V1.F(max);
        public string Tooltip => T.ResearchCpuCapDesc_V1.F(max);

        public void OnUnlock(GameState s, bool notify) {
            var curCap = s.BB.Get(BBKeyH.CPUCap, 0);
            if (max > curCap) {
                s.BB.Set(BBKeyH.CPUCap, max);
                if (notify) {
                    WPopup.Create(false, IconId.CComputing, T.UpgradeUnlocked, T.ShipComputerCapUnlock_V1.F(max)).Spawn();
                }
            }
        }

        private readonly int max;

        private UnlockEffectShipComputerCap(int max) {
            this.max = max;
        }
    }
}
