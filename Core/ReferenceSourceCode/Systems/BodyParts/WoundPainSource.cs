using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Stats;
using KL.Utils;

namespace Game.Systems.BodyParts {
    // The wounds as the source of the Pain stat, so the stat tooltip names them next to whatever
    // modifies the pain (a Pain Inhibitor, a trait). A source and not a base the conditions write:
    // Stat.RecalculateBase resets the base from its sources on load and whenever needs change,
    // which would wipe a written base until the next wound update.
    // The Injured and Damaged conditions already walk every wound each tick, so they hand the
    // total over (PainShock.Update) instead of this source walking the body again.
    public sealed class WoundPainSource : IStatSource {
        public string StatSourceNameT => T.StatSourceWounds;
        public float Total { get; private set; }

        public void SetTotal(float total, Stat pain) {
            if (Maths.Approximately(total, Total)) { return; }
            Total = total;
            pain.SourcesChanged();
        }

        public bool GetEffectFor(Stat stat, out float toAdd, out float limit) {
            limit = 0f;
            toAdd = Total;
            return toAdd > 0f;
        }

        public void UpdateStat(Stat stat) {
            if (Total > 0f) {
                stat.UpdateBase(stat.BaseValue + Total);
            }
        }

        // What one wound hurts, by the severity it hurts like (WoundPain.EffectiveSeverity, so
        // a destroyed part's pain settles and small parts hurt less)
        public static float PainOf(Severity effectiveSeverity) {
            if (effectiveSeverity <= Severity.Irrelevant) { return 0f; }
            painCurve ??= CurveDef.FastGet(CurveIdH.Beings_WoundSeverity_Pain);
            if (painCurve == null) {
                D.Err("Wound pain curve missing: {0}", CurveId.Beings_WoundSeverity_Pain);
                return 0f;
            }
            return painCurve.Evaluate((int) effectiveSeverity);
        }
        private static Curve painCurve;

        public override string ToString() {
            return $"WoundPainSource[{Total}]";
        }
    }
}
