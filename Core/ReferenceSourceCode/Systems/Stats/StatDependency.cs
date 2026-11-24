using System.Text;
using Game.Constants;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using Game.Input;
using Unity.Mathematics;

namespace Game.Systems.Stats {
    public sealed class StatDependency {
        public readonly Stat DependsOn;
        public readonly float Multiplier;
        /// <summary>
        /// This is influence curve. If depends on value is x, curve will evaluate X and return
        /// influence.
        /// </summary>
        public readonly Curve Curve;
        public bool IsInverse;
        public bool IgnoreZero;
        // Adds the curve's value to the stat instead of scaling it (Addition), for a stat that
        // starts at 0 such as a dodge chance
        public bool IsAdditive;
        // The stat this dependency changes, so its tooltip line can show an added amount in
        // that stat's units
        public Stat Influenced;
        /// <summary>
        /// Percent we add to the value that depends on DependsOn stat
        /// If DependsOn.value is 1f and multiplier is 0.5f, we will add -50%
        /// If DependsOn.value is 1f and multiplier is 1f, we will add 0%
        /// If DependsOn.value is 0.5f and multiplier is 1f, we will add -50%
        /// If DependsOn.value is 0.1f and multiplier is 1f, we will add -90%
        /// We do addsPercent += (DependsOn.value * Influence) - 1f;
        /// </summary>
        public float Influence {
            get {
                if (IsAdditive) { return 1f; }
                if (DependsOn.IsUnused) { return 1f; }
                var v = DependsOn.Value;
                if (IgnoreZero && v == 0f) { return 1f; }
                if (Curve == null) {
                    if (IsInverse) {
                        return (2f - v) * Multiplier;
                    } else {
                        return v * Multiplier;
                    }
                }
                if (IsInverse) {
                    return Curve.Evaluate(2f - v);
                }
                return Curve.Evaluate(v);
            }
        }
        public float Addition {
            get {
                if (!IsAdditive || DependsOn.IsUnused) { return 0f; }
                var v = DependsOn.Value;
                if (IgnoreZero && v == 0f) { return 0f; }
                return Curve.Evaluate(v);
            }
        }
        private StatDependency(Stat dep, float multiplier, bool inverse, bool ignoreZero) {
            DependsOn = dep;
            Multiplier = multiplier;
            Curve = null;
            IsInverse = inverse;
            IgnoreZero = ignoreZero;
            if (multiplier == 0f) {
                D.Err("Stat dependency has 0 multiplier and no curve! {0}", dep);
            }
        }
        private StatDependency(Stat dep, Curve curve, bool inverse, bool ignoreZero) {
            DependsOn = dep;
            Curve = curve;
            Multiplier = 0f;
            IsInverse = inverse;
            IgnoreZero = ignoreZero;
        }

        public static void CreateFor(Stat stat, Stat dependsOn, float multiplier,
                bool inverse = false, bool ignoreZero = false) {
            stat.InfluencedBy ??= new();
            stat.InfluencedBy.Add(new StatDependency(dependsOn, multiplier,
                inverse, ignoreZero) { Influenced = stat });
            dependsOn.Influences ??= new();
            dependsOn.Influences.Add(stat);
            stat.IsDirty = true;
        }

        public static void CreateForCurve(Stat stat, Stat dependsOn, Curve c,
                bool inverse = false, bool ignoreZero = true) {
            stat.InfluencedBy ??= new();
            stat.InfluencedBy.Add(new StatDependency(dependsOn, c, inverse, ignoreZero) {
                Influenced = stat
            });
            dependsOn.Influences ??= new();
            dependsOn.Influences.Add(stat);
            stat.IsDirty = true;
        }

        public static void CreateForCurveAdditive(Stat stat, Stat dependsOn, Curve c,
                bool ignoreZero = false) {
            stat.InfluencedBy ??= new();
            stat.InfluencedBy.Add(new StatDependency(dependsOn, c, false, ignoreZero) {
                IsAdditive = true,
                Influenced = stat
            });
            dependsOn.Influences ??= new();
            dependsOn.Influences.Add(stat);
            stat.IsDirty = true;
        }

        public bool AppendInfo(StringBuilder sb, bool needsLineBreak) {
            if (DependsOn.IsUnused) { return needsLineBreak; }
            if (IgnoreZero && DependsOn.Value == 0f) { return needsLineBreak; }
            if (needsLineBreak) {
                sb.Append('\n');
            }
            sb.Append(Stat.NameFor(DependsOn));
            AppendEffect(sb);
            return true;
        }

        public bool AppendInfluenceInfo(StringBuilder sb, Stat influencedStat, bool needsLineBreak) {
            if (DependsOn.IsUnused) { return needsLineBreak; }
            if (IgnoreZero && DependsOn.Value == 0f) { return needsLineBreak; }
            if (needsLineBreak) {
                sb.Append('\n');
            } else {
                sb.Append(GlyphMap.BRHR);
                sb.AppendLine(Texts.Accented(T.StatInfluences));
            }
            sb.Append(Stat.NameFor(influencedStat));
            AppendEffect(sb);
            return true;
        }

        private void AppendEffect(StringBuilder sb) {
            sb.Append(": ");
            if (IsAdditive) {
                var add = Addition;
                // an effect that rounds to 0% read as a red "0%"
                if (math.abs(add) < 0.005f) {
                    sb.Append(Texts.Grey(T.StatNoEffect));
                } else {
                    var shown = Influenced.DisplaySigned(add);
                    sb.Append(add > 0f ? Texts.Green(shown) : Texts.Red(shown));
                }
                return;
            }
            var inf = Influence;
            if (math.abs(inf - 1f) < 0.005f) {
                sb.Append(Texts.Grey(T.StatNoEffect));
            } else {
                if (inf > 1f) {
                    if (IsInverse) {
                        sb.Append(Texts.Red(Units.PercentageSigned(inf - 1f)));
                    } else {
                        sb.Append(Texts.Green(Units.PercentageSigned(inf - 1f)));
                    }
                } else {
                    if (IsInverse) {
                        sb.Append(Texts.Green(Units.PercentageSigned(inf - 1f)));
                    } else {
                        sb.Append(Texts.Red(Units.PercentageSigned(inf - 1f)));
                    }
                }
            }
        }

        public override string ToString() {
            return $"StatDep[{DependsOn.IdStr}|Mul:{Multiplier}|Inv:{IsInverse}|Curve:{Curve}]";
        }
    }
}
