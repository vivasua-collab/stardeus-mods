using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using Game.Utils;

namespace Game.Research {
    /// <summary>
    /// Multiplies the energy every Elysium sleeper feeds back into the grid.
    /// Read by StasisArrayMatrixComp.AfterTickGrid.
    /// </summary>
    public sealed class UnlockEffectElysiumOutput : IResearchUnlockEffect {
        public const float DefaultMul = 1f;

        [SelfInit]
        private static void Register() {
            Registries.UnlockEffects.Add("ElysiumOutput_1",
                new UnlockEffectElysiumOutput(1.5f,
                    () => T.ResearchElysiumelectricity,
                    () => T.ResearchElysiumelectricityDesc));
            Registries.UnlockEffects.Add("ElysiumOutput_2",
                new UnlockEffectElysiumOutput(2.25f,
                    () => T.ResearchElysiumefficiency,
                    () => T.ResearchElysiumefficiencyDesc));
        }

        public string Icon => IconId.CEnergy;
        public string Title => title();
        public string Tooltip => tooltip();

        public void OnUnlock(GameState s, bool notify) {
            var current = s.BB.Get(BBKeyH.ElysiumOutputMul, DefaultMul);
            if (mul <= current) { return; }
            s.BB.Set(BBKeyH.ElysiumOutputMul, mul);
            if (!notify) { return; }
            WPopup.Create(false, Icon, T.UpgradeUnlocked, Tooltip).Spawn();
        }

        private readonly float mul;
        private readonly Func<string> title;
        private readonly Func<string> tooltip;

        private UnlockEffectElysiumOutput(float mul, Func<string> title,
                Func<string> tooltip) {
            this.mul = mul;
            this.title = title;
            this.tooltip = tooltip;
        }
    }
}
