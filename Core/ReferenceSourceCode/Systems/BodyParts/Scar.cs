using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Creatures;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using MessagePack;
using UnityEngine;

namespace Game.Systems.BodyParts {
    [MessagePackObject]
    public struct Scar {
        [Key("Type")] public DamageType Type;
        [Key("Severity")] public Severity Severity;
        [Key("CreatedAt")] public long CreatedAt;
        /// <summary>
        /// Source of this scar. If it's deepened, source could be overwritten.
        /// </summary>
        [Key("Source")] public string Source;
        // Quality this scar took from the part when it formed. 0 on scars from saves made before
        // scars cost quality - those never lowered it.
        [Key("QualityLoss")] public float QualityLoss;

        public override string ToString() {
            return D.DumpInstance(this, true);
            // return $"Wound[{Type}|{Severity}|Bleed:{BleedPercentPerHour}]";
        }

        public static Scar FromWound(Wound w, long ticks) {
            return new Scar {
                Type = w.Type,
                Source = w.Source,
                Severity = w.ScarSeverity,
                CreatedAt = ticks,
            };
        }

        public static string TooltipFor(Scar scar) {
            var sb = Caches.StringBuilder3;
            sb.Clear();
            sb.AppendFormat(" {0} ", GlyphMap.ArrowRight);
            sb.Append(T.ScarInfo_V3.F(
                Texts.Accented(Severities.NameForNegative(scar.Severity)),
                Texts.Accented(DamageTypes.NameFor(scar.Type)),
                Texts.Accented(scar.Source ?? T.Unknown)
            ));
            if (scar.QualityLoss > 0f) {
                sb.Append(' ');
                sb.Append(Texts.GreyParentheses($"{T.NounQuality} -{Units.Percentage(scar.QualityLoss)}"));
            }
            if (scar.CreatedAt != 0L) {
                sb.Append(' ');
                sb.Append(Texts.GreyParentheses(Units.TicksAgoFromNow(scar.CreatedAt)));
            }

            return sb.ToString();
        }

        public static Scar RandomHistorical(Species species, uint age, Rng rng, string source = null) {
            var scar = new Scar();
            float scarAgeRange = rng.Range(0.25f, 0.75f);
            var ticksAgo = (int) (Consts.TicksPerYear * age * scarAgeRange);
            scar.CreatedAt = G.Ticks - ticksAgo;
            scar.Type = DamageTypes.RandomForScar(rng);
            scar.Severity = RandomHistoricalSeverity(rng);
            scar.Source = source ?? RandomSource(species, rng);
            return scar;
        }

        // Generated scars come in bulk (a scarred being rolls for every part), and each one costs
        // quality, so most of them are mild
        private static Severity RandomHistoricalSeverity(Rng rng) {
            var roll = rng.Range(0f, 1f);
            if (roll < 0.6f) {
                return Severity.Mild;
            }
            if (roll < 0.9f) {
                return Severity.Moderate;
            }
            return Severity.Severe;
        }

        // Chance that a fully healed wound leaves a scar of scarSeverity, which is one step milder
        // than the worst severity the wound reached (Wound.ScarSeverity)
        public static float ChanceFor(DamageType type, Severity scarSeverity) {
            float chance;
            switch (scarSeverity) {
                case Severity.Mild: {
                    chance = Tunables.BodyPartScarChance_ModerateWound;
                    break;
                }
                case Severity.Moderate: {
                    chance = Tunables.BodyPartScarChance_SevereWound;
                    break;
                }
                case Severity.Severe: {
                    chance = Tunables.BodyPartScarChance_CriticalWound;
                    break;
                }
                default: return 0f;
            }
            return Mathf.Clamp01(chance * TypeChanceMul(type));
        }

        public static bool CanScar(DamageType type) {
            return TypeChanceMul(type) > 0f;
        }

        // Bruises never scar, since blunt hits leave the skin whole; burns scar more readily than
        // cuts and punctures
        private static float TypeChanceMul(DamageType type) {
            switch (type) {
                case DamageType.Sharp:
                case DamageType.Piercing: return 1f;
                case DamageType.Heat:
                case DamageType.Acid:
                case DamageType.Explosive:
                case DamageType.Energy: return 1.5f;
                default: return 0f;
            }
        }

        // A scar takes a share of the quality above the floor, so scars alone approach the floor
        // but never cross it, and a part already at or below it loses nothing
        public static float QualityAfter(float quality, Severity severity) {
            var floor = Tunables.BodyPartScarQualityFloor;
            if (quality <= floor) {
                return quality;
            }
            return floor + (quality - floor) * (1f - QualityLossFor(severity));
        }

        private static float QualityLossFor(Severity severity) {
            switch (severity) {
                case Severity.Irrelevant: return 0f;
                case Severity.Mild: return Tunables.BodyPartScarQualityLoss_Mild;
                case Severity.Moderate: return Tunables.BodyPartScarQualityLoss_Moderate;
                default: return Tunables.BodyPartScarQualityLoss_Severe;
            }
        }

        // Rolled once, when the wound has fully healed
        public static bool TryFormFrom(BodyPartState state, in Wound wound) {
            var chance = wound.ScarChance;
            if (chance <= 0f || !A.S.Rng.Chance(chance)) {
                return false;
            }
            state.AddScar(FromWound(wound, G.Ticks));
            return true;
        }

        public static string RandomSource(Species species, Rng rng) {
            var list = Caches.StringList;
            list.Clear();
            list.Add(T.ScarRandomSourceGeneric1);
            list.Add(T.ScarRandomSourceGeneric2);
            list.Add(T.ScarRandomSourceGeneric3);
            list.Add(T.ScarRandomSourceGeneric4);
            list.Add(T.ScarRandomSourceGeneric5);
            list.Add(T.ScarRandomSourceGeneric6);
            list.Add(T.ScarRandomSourceGeneric7);
            list.Add(T.ScarRandomSourceGeneric8);
            list.Add(T.ScarRandomSourceGeneric9);
            if (species.IsHumanoid) {
                list.Add(T.ScarRandomSourceHuman1);
                list.Add(T.ScarRandomSourceHuman2);
                list.Add(T.ScarRandomSourceHuman3);
                list.Add(T.ScarRandomSourceHuman4);
                list.Add(T.ScarRandomSourceHuman5);
            }
            return rng.From(list);
        }
    }
}
