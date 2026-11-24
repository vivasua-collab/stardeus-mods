using KL.Utils;
using Unity.Mathematics;

namespace Game.Systems.Space {
    public abstract class SpaceAssignable {
        public string Id;
        public int IdH;
        /// <summary>
        /// Chance to get this deposit at region target difficulty
        /// </summary>
        public float[] DifficultyChance;
        /// <summary>
        /// Never consider spawning this assignable for anything with less than this difficulty
        /// </summary>
        public float MinDifficulty;
        private Curve difficultyCurve;
        public Curve DifficultyCurve => difficultyCurve ??= DifficultyChance;
        /// <summary>
        /// How many deposits of this type can appear in a region
        /// </summary>
        public float[] RegionQuantityChance;
        private Curve regionQuantityCurve;
        public Curve RegionQuantityCurve => regionQuantityCurve ??= RegionQuantityChance;
        /// <summary>
        /// How many deposits of this type can appear in the universe
        /// </summary>
        public float[] UniverseQuantityChance;
        private Curve universeQuantityCurve;
        public Curve UniverseQuantityCurve => universeQuantityCurve ??= UniverseQuantityChance;
        /// <summary>
        /// Only regions / sectors that are owned by a faction will have this
        /// </summary>
        public bool RequiresFaction;
        /// <summary>
        /// If true, only one is allowed per sector. Default for deposits.
        /// </summary>
        public bool MaxOnePerSector;
        /// <summary>
        /// When a chance is rolled for a region / sector, the assignable goes
        /// into unavailable list and won't ever be assigned to that region / sector.
        /// Unless this is set to true. Default for Space Effects.
        /// </summary>
        public bool NeverUnavailable;
        /// <summary>
        /// If template is with NoLife, this can't be in that entity
        /// </summary>
        public bool IsBiological;
        /// <summary>
        /// Template Ids this can never be assigned to
        /// </summary>
        public string[] ExcludeTemplates;
        public bool Matches(ISpaceEntity entity, out float diffChance, float diffShift = 0f) {
            if (entity.Biome.Template == null && entity is SpaceRegion) {
                throw new System.Exception(
                    "Trying to assign to region too early! No template!");
            }
            if (IsBiological) {
                if (entity.Biome.Template == null || entity.Biome.Template.NoLife) {
                    diffChance = 0f;
                    return false;
                }
            }
            if (!Arrays.IsEmpty(ExcludeTemplates)) {
                var tpl = entity.Biome.Template;
                if (tpl != null && Arrays.Contains(ExcludeTemplates, tpl.Id)) {
                    diffChance = 0f;
                    return false;
                }
            }
            if (RequiresFaction) {
                if (entity.Biome.Template == null || Arrays.IsEmpty(entity.Biome.FactionsByDominance)) {
                    diffChance = 0f;
                    return false;
                }
            }
            var diff = entity.Biome.Difficulty;
            float diffFrom = diff + diffShift;
            if (diffFrom < 0f) {
                D.Err("Diff from < 0! {0}", entity);
            }
            if (entity is SpaceRegion reg) {
                float diffTo = diffFrom;
                if (reg.Sectors != null) {
                    foreach (var sec in reg.Sectors) {
                        diffFrom = math.min(diffFrom, sec.Biome.Difficulty);
                        diffTo = math.max(diffTo, sec.Biome.Difficulty);
                    }
                }
                var diffChanceMin = DifficultyCurve.Evaluate(diffFrom);
                var diffChanceMax = DifficultyCurve.Evaluate(diffTo);
                diffChance = diffChanceMax;
                if (diffChanceMin < Maths.Epsilon && diffChanceMax < Maths.Epsilon) {
                    return false;
                }
                return true;
            } else if (MaxOnePerSector && entity.Biome.HasAssignable(this, false)) {
                diffChance = 0;
                return false;
            }
            diffChance = DifficultyCurve.Evaluate(diffFrom);
            return diffChance > Maths.Epsilon;
        }
    }
}
