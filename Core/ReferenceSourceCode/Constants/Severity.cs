using System;
using Game.Input;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using UnityEngine.TextCore;

namespace Game.Constants {
    public enum Severity : byte {
        Irrelevant,
        Mild,
        Moderate,
        Severe,
        Critical,
        // Wound can never heal, part is Destroyed. Used on slots that are NOT required for the
        // being to stay alive (BodyPartSlot.IsRequiredForBeingAlive == false), e.g. Eye.
        Irreparable,
        // Same mechanical effect as Irreparable (unhealable, Destroyed) - only used on slots
        // that ARE required for the being to stay alive (HoldsMind, or Essential+Biological),
        // e.g. Heart. TryAddWound/TryAddOrUpdateWound normalize between the two based on
        // IsRequiredForBeingAlive, so a given slot only ever sees one of the pair - the label
        // just communicates that this wound is also lethal.
        Fatal,
    }
    public static class Severities {
        public static Severity For(float value, bool includeFatal) {
            if (value > 1f) {
                value = 1f;
            }
            if (includeFatal) {
                return (Severity) (1 + Mathf.FloorToInt(value * 5f));
            } else {
                return (Severity) (1 + Mathf.FloorToInt(value * 4f));
            }
        }
        public static bool TryMove(Severity s, sbyte dir, bool canBeFatal, out Severity newS) {
            if (dir == 1) {
                return TryIncrease(s, canBeFatal, out newS);
            } else {
                return TryDecrease(s, out newS);
            }
        }
        public static bool TryIncrease(Severity s, bool canBeFatal, out Severity newS) {
            switch (s) {
                case Severity.Irrelevant: {
                    newS = Severity.Irrelevant;
                    return false;
                }
                case Severity.Mild: {
                    newS = Severity.Moderate;
                    return true;
                }
                case Severity.Moderate: {
                    newS = Severity.Severe;
                    return true;
                }
                case Severity.Severe: {
                    newS = Severity.Critical;
                    return true;
                }
                case Severity.Critical: {
                    if (canBeFatal) {
                        newS = Severity.Fatal;
                    } else {
                        newS = Severity.Irreparable;
                    }
                    return true;
                }
                case Severity.Irreparable: {
                    if (canBeFatal) {
                        newS = Severity.Fatal;
                        return true;
                    }
                    newS = Severity.Irreparable;
                    return false;
                }
                case Severity.Fatal: {
                    if (canBeFatal) {
                        newS = Severity.Fatal;
                        return false;
                    }
                    newS = Severity.Irreparable;
                    return true;
                }
                default: {
                    D.Err("Unhandled Severity: {0}", s);
                    newS = s;
                    return false;
                }
            }
        }
        public static bool TryDecrease(Severity s, out Severity newS) {
            switch (s) {
                case Severity.Irrelevant: {
                    newS = Severity.Irrelevant;
                    return false;
                }
                case Severity.Mild: {
                    newS = Severity.Mild;
                    return false;
                }
                case Severity.Moderate: {
                    newS = Severity.Mild;
                    return true;
                }
                case Severity.Severe: {
                    newS = Severity.Moderate;
                    return true;
                }
                case Severity.Critical: {
                    newS = Severity.Severe;
                    return true;
                }
                case Severity.Irreparable: {
                    newS = Severity.Irreparable;
                    return false;
                }
                case Severity.Fatal: {
                    newS = Severity.Fatal;
                    return false;
                }
                default: {
                    D.Err("Unhandled Severity: {0}", s);
                    newS = s;
                    return false;
                }
            }
        }
        public static string NameFor(Severity severity, bool isPositive) {
            if (isPositive) {
                return NameForPositive(severity);
            } else {
                return NameForNegative(severity);
            }
        }

        public static string NameForPositive(Severity severity) {
            switch (severity) {
                case Severity.Irrelevant: return GlyphMap.SpriteCross;
                case Severity.Mild: return T.SeverityMild;
                case Severity.Moderate: return T.SeverityModerate;
                case Severity.Severe: return T.StatusStrong;
                case Severity.Critical: return T.StatusFull;
                case Severity.Irreparable: return T.StatusPermanent;
                case Severity.Fatal: return T.StatusInnate;
            }
            D.Err("Unhandled Positive Severity: {0}", severity);
            return severity.ToString();
        }
        public static string NameForNegative(Severity severity) {
            switch (severity) {
                case Severity.Irrelevant: return null;
                case Severity.Mild: return T.SeverityMild;
                case Severity.Moderate: return T.SeverityModerate;
                case Severity.Severe: return T.SeveritySevere;
                case Severity.Critical: return T.SeverityCritical;
                case Severity.Irreparable: return T.SeverityIrreparable;
                case Severity.Fatal: return T.SeverityFatal;
            }
            D.Err("Unhandled Severity: {0}", severity);
            return severity.ToString();
        }

        public static Severity Accumulate(Severity current, Severity add, float force, bool canBeFatal) {
            if (add < current) { return current; }
            if (add > current) { return add; }
            if (add == current) {
                if (Rng.UChance(force)) {
                    if (TryIncrease(current, canBeFatal, out var newS)) {
                        return newS;
                    }
                }
            }
            return current;
        }

        public static Severity Bigger(Severity a, Severity b) {
            if (a > b) {
                return a;
            }
            return b;
        }

        public static float AdjustConditionBy(float condition, Severity sev) {
            condition *= 1f - (float) sev / 5f;
            if (condition > 0f) {
                return condition;
            }
            return 0f;
        }

        public static Severity Random(Severity from, Severity to, Rng rng = null) {
            rng ??= Rng.Unseeded;
            return (Severity) rng.Range((int) from, (int) to + 1);
        }

        public static Severity IncreaseBy(Severity severity, byte steps) {
            int newSev = (byte) severity + steps;
            if (newSev >= (int) Severity.Fatal) {
                return Severity.Fatal;
            }
            return (Severity) newSev;
        }
    }
}
