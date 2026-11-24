using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using KL.Randomness;
using KL.Utils;
using MessagePack;
using UnityEngine;

namespace Game.Systems.BodyParts {
    [MessagePackObject]
    public sealed class BodyPartState {
        /// <summary>
        /// 0.1f = 90 year old's heart, 1f = 18 year old's heart
        /// For performance reasons, Quality and Condition are simple fields in production, but never set them directly, use SetQuality / SetCondition methods!
        /// </summary>
        [Key("Quality")] public float Quality;
        /// <summary>
        /// 0 - destroyed, 1 - 100% healthy
        /// For performance reasons, Quality and Condition are simple fields in production, but never set them directly, use SetQuality / SetCondition methods!
        /// </summary>
        [Key("Condition")] public float Condition;
        [Key("Decay")] public float Decay;
        [Key("Wounds")] public List<Wound> Wounds;
        [Key("Scars")] public List<Scar> Scars;
        [Key("Flags")] public BodyPartStateFlags Flags;
        [Key("Vars")] public ComponentData Vars;
        [Key("WoundSeverity")] public Severity WoundSeverity;
        private BCallback onChange;
        [IgnoreMember] public BCallback OnChange => onChange ??= new BCallback();
        [IgnoreMember] public bool IsEthicallyRemovable => Flags != BodyPartStateFlags.None;
        [IgnoreMember] public bool HasModerateDamage => WoundSeverity >= Severity.Moderate || !IsUsableSelf;
        [IgnoreMember] public bool HasCriticalDamage => WoundSeverity >= Severity.Critical || !IsUsableSelf;
        [IgnoreMember] public bool IsUsableSelf =>
            (Flags & (BodyPartStateFlags.Incapacitated | BodyPartStateFlags.Destroyed)) == BodyPartStateFlags.None;
        [IgnoreMember] public bool IsUsableWithParent =>
            (Flags & (BodyPartStateFlags.Incapacitated | BodyPartStateFlags.Destroyed | BodyPartStateFlags.ParentUnusable)) == BodyPartStateFlags.None;
        [IgnoreMember] public bool IsUsableParent =>
            (Flags & BodyPartStateFlags.ParentUnusable) == BodyPartStateFlags.None;
        [IgnoreMember] public bool IsDestroyed =>
            (Flags & BodyPartStateFlags.Destroyed) == BodyPartStateFlags.Destroyed;
        [IgnoreMember] public bool IsInfected =>
            (Flags & BodyPartStateFlags.Infected) == BodyPartStateFlags.Infected;

        [IgnoreMember] public bool IsWelded =>
            (Flags & BodyPartStateFlags.Welded) == BodyPartStateFlags.Welded;

        public void SetQuality(float q) {
            if (q != Quality) {
                Quality = q;
                onChange?.Invoke();
            }
        }
        public void SetCondition(float c) {
            if (c != Condition) {
                Condition = c;
                onChange?.Invoke();
            }
        }
        public void Load(float condition, float quality) {
            if (condition != Condition || quality != Quality) {
                Quality = quality;
                Condition = condition;
                onChange?.Invoke();
            }
        }

        /// <summary>For parts from cloning pod / factory only!</summary>
        public static BodyPartState BrandNew() {
            return new BodyPartState {
                Quality = 1f,
                Condition = 1f,
                Decay = 0f,
            };
        }

        private static Curve ageQualityCurve;
        private static Curve qualityFlucCurve;

        /// <summary>
        /// Generated used state
        /// <param name="lifeCycleStage">0.1f - baby, 0.9f - old man</param>
        /// </summary>
        public static BodyPartState Used(float lifeCycleStage, Rng rng = null) {
            ageQualityCurve ??= CurveDef.FastGet(CurveIdH.Beings_AgePercent_BodyPartQuality);
            qualityFlucCurve ??= CurveDef.FastGet(CurveIdH.Beings_BodyPartQualityFluctuation);
            rng ??= Rng.Unseeded;
            var q = ageQualityCurve.Evaluate(lifeCycleStage);
            var fluc = rng.Curve(qualityFlucCurve);
            q += rng.Range(-fluc, fluc);

            // never generate beings with body part quality less than 25% or they might die
            q = Mathf.Clamp(q, 0.25f, 1f);

            return new BodyPartState {
                Quality = q,
                Condition = 1f,
            };
        }

        public Wound WoundFor(DamageType type) {
            Wound w;
            if (Wounds == null) {
                w = new Wound();
                w.Type = type;
                w.Severity = Severity.Irrelevant;
                Wounds = new List<Wound>() { w };
                return w;
            }

            for (int i = Wounds.Count - 1; i >= 0; i--) {
                w = Wounds[i];
                if (w.Type == type) {
                    return w;
                }
            }

            // We didn't find a wound type
            w = new Wound();
            w.Type = type;
            Wounds.Add(w);
            return w;
        }
        // Non-mutating lookup, unlike WoundFor which adds a wound when none matches.
        public bool TryGetWound(DamageType type, out Wound wound) {
            if (Wounds != null) {
                for (int i = Wounds.Count - 1; i >= 0; i--) {
                    if (Wounds[i].Type == type) {
                        wound = Wounds[i];
                        return true;
                    }
                }
            }
            wound = default;
            return false;
        }

        // Every new scar costs quality. No cap on the count: each scar
        // explains part of the quality the part lost, and scarring is rare enough to stay small.
        public void AddScar(Scar scar) {
            var qualityBefore = Quality;
            SetQuality(Scar.QualityAfter(qualityBefore, scar.Severity));
            scar.QualityLoss = qualityBefore - Quality;
            Scars ??= new();
            Scars.Add(scar);
            // D.Warn("Added scar: {0}", D.DumpInstance(scar));
        }

        /// <summary>
        /// Flags that record permanent history of the part rather than its current derived
        /// condition. UpdateCondition rebuilds Condition and the derived flags (Incapacitated,
        /// ParentUnusable) from Wounds on every call - these survive that rebuild.
        /// </summary>
        public const BodyPartStateFlags PermanentFlags =
            BodyPartStateFlags.Infected | BodyPartStateFlags.Welded
            | BodyPartStateFlags.Destroyed | BodyPartStateFlags.Crippled;

        /// <summary>Updates Condition and IsUsable according to Wounds</summary>
        /// <returns>true if IsUsable changed</returns>
        public bool UpdateCondition(bool isParentUsable, BodyPartType type) {
            var wasUsable = IsUsableSelf;
            var wasUsableParent = IsUsableParent;
            float condBefore = Condition;
            Flags &= PermanentFlags;
            if (!isParentUsable) {
                Flags |= BodyPartStateFlags.ParentUnusable;
            }
            // Destroyed comes only from an Irreparable/Fatal wound (kept by PermanentFlags), never
            // from a low condition: several Critical wounds of different types multiply below 1%
            // and still heal, and treating that as destroyed killed beings on the next update.
            var biggestSeverity = Severity.Irrelevant;
            Condition = 1f;
            if (!Arrays.IsEmpty(Wounds)) {
                for (int i = Wounds.Count - 1; i >= 0; i--) {
                    var sev = Wounds[i].Severity;
                    biggestSeverity = Severities.Bigger(biggestSeverity, sev);
                    Condition = Severities.AdjustConditionBy(Condition, sev);
                }
            }
            if ((Flags & BodyPartStateFlags.Crippled) == BodyPartStateFlags.Crippled
                    && Condition > Tunables.CrippledPartMaxCondition) {
                Condition = Tunables.CrippledPartMaxCondition;
            }
            if (Condition < 0.1f) {
                Flags |= BodyPartStateFlags.Incapacitated;
            }
            if (biggestSeverity >= Severity.Irreparable) {
                Flags |= BodyPartStateFlags.Destroyed;
            }
            WoundSeverity = biggestSeverity;

            return IsUsableSelf != wasUsable || IsUsableParent != wasUsableParent
                || Maths.Different(condBefore, Condition);
        }

        public bool RemoveWound(DamageType type) {
            if (Wounds == null) { return false; }
            for (int i = Wounds.Count - 1; i >= 0; i--) {
                var wound = Wounds[i];
                if (wound.Type == type) {
                    Wounds.RemoveAt(i);
                    if (type == DamageType.Decay) {
                        Decay = 0f;
                    }
                    return true;
                }
            }
            return false;
        }

        public void UpdateWound(in Wound w) {
            if (Wounds == null) {
                Wounds = new List<Wound>() { w };
                return;
            }

            for (int i = Wounds.Count - 1; i >= 0; i--) {
                var wound = Wounds[i];
                if (wound.Type == w.Type) {
                    Wounds[i] = w;
                    return;
                }
            }

            Wounds.Add(w);
        }

        public override string ToString() {
            return $"BodyPartState[C:{Condition}xQ:{Quality},Usable:{IsUsableSelf}.Wounds:{Arrays.Count(Wounds)}]";
        }

    }
}
