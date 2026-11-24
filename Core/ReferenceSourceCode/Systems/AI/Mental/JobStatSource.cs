using System;
using Game.Data;
using KL.Utils;

namespace Game.Systems.Mental {
    // One being stat a job's skill level adds to, through a curve over the level
    // (JobType.ProvidesStats, read by Stats.SkillStatSource)
    [Serializable]
    public sealed class JobStatSource {
        public string Stat;
        // CurveDef id: skill level -> amount added to the stat
        public string CurveId;
        [NonSerialized] public int StatIdH;
        [NonSerialized] public int CurveIdH;
        [NonSerialized] private Curve curve;

        // Resolved on first use: job types load before curve defs
        public Curve Curve => curve ??= CurveDef.FastGet(CurveIdH) ?? 0f;
    }
}
