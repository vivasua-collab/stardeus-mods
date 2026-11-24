using System.Collections.Generic;
using Game.Systems.UIUX;
using KL.Utils;

namespace Game.Systems.Crafting {
    public sealed class CrafterConfig {
        public int MaxBatchSize;
        public string CraftActionNameT;
        public string UIItemIcon;
        public readonly BInt TaskPriority = new();
        public bool IsOperatorRequired;
        public float SpeedMultiplier;
        public bool IsIdleWarnOn;
        public bool IsIgnoreUnconfiguredOn;
        public HashSet<string> CompatibleTypes;
        public bool IsAutoHaulProduct;
        public string ProgressBarKey;
        public int ProgressBarKeyHash;
        public Curve NoOperatorQualityCurve;
        public Curve NoOperatorQualityCurveResearched;
        public int NoOperatorQualityResearch;

    }
}
