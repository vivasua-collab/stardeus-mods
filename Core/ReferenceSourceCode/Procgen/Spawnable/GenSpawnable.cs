using System;
using Game.Components;
using KL.Utils;

namespace Game.Procgen {
    [Serializable]
    /// <summary>Must override fully!</summary>
    public class GenSpawnable {
        public float Chance;
        public float ChanceLocked;
        public int TierDiff;
        public string Category;
        public int SkipOwned;
        public ComponentConfig Quantity;
        public GenSpawnable Copy() {
            var res = new GenSpawnable();
            res.Chance = Chance;
            res.Category = Category;
            res.ChanceLocked = ChanceLocked;
            res.TierDiff = TierDiff;
            res.SkipOwned = SkipOwned;
            res.Quantity = Quantity?.Copy();
            return res;
        }

        public static GenSpawnable Merge(GenSpawnable baseS, GenSpawnable s) {
            if (baseS == null) { return s; }
            if (s == null) { return baseS; }
            var hasOverride = s.Chance > 0 || s.ChanceLocked > 0 || s.TierDiff != 0 || s.Category != null || s.SkipOwned > 0;
            if (hasOverride) {
                return s;
            }
            return baseS;
        }
    }
}
