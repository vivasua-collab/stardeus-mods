using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Motion;
using Game.Systems.Stats;
using Game.Utils;
using KL.Grid;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Creatures {
    public sealed class BeingSafety {
        private readonly GameState S;
        private readonly StatsBeing stats;
        public bool IgnoreDanger;
        private static readonly float bioTempLowK = Units.CelsiusToKelvin(0f);
        private static readonly float bioTempHighK = Units.CelsiusToKelvin(40f);
        public BeingSafety(GameState s, BeingData data) {
            S = s;
            stats = data.StatsBeing;
        }
        public float SafetyLevelAt(int pos) {
            var o2min = stats.O2Min.Value;
            var temp = S.GetTemp(pos);
            float safetyLevel = 0f;
            if (o2min > 0f) {
                // biological
                var o2 = S.GetO2(pos);
                if (o2 <= 0.15f) {
                    safetyLevel -= (0.15f - o2) * 10f;
                }
                if (temp < bioTempLowK) {
                    safetyLevel -= (bioTempLowK - temp);
                } else if (temp > bioTempHighK) {
                    safetyLevel -= (temp - bioTempHighK);
                }
            } else {
                var tempMin = stats.TempMin.Value;
                var tempMax = stats.TempMax.Value;
                if (temp < tempMin) {
                    safetyLevel -= (tempMin - temp);
                } else if (temp > tempMax) {
                    safetyLevel -= (temp - tempMax);
                }
            }
            // D.Warn("Safety at {0} -> {1}", Pos.String(pos), safetyLevel);
            return safetyLevel;
        }

        public bool IsActuallySafeAtWithoutEquipment(int pos) {
            var tempMin = stats.TempMin.BaseValue;
            var temp = S.GetTemp(pos);
            if (temp < tempMin) {
                // D.Warn("Not safe at pos {0}. Temp {1} < tempMin {2}",
                //     Pos.String(pos), temp, tempMin);
                return false;
            }
            var tempMax = stats.TempMax.BaseValue;
            if (temp > tempMax) {
                // D.Warn("Not safe at pos {0}. Temp {1} > tempMin {2}",
                //     Pos.String(pos), temp, tempMax);
                return false;
            }
            var o2min = stats.O2Min.BaseValue;
            if (o2min > 0f) {
                var o2 = S.GetO2(pos);
                if (o2 < o2min) {
                    // D.Warn("Not safe at pos {0}. O2 {1} < o2Min {2}",
                    //     Pos.String(pos), o2, o2min);
                    return false;
                }
            }
            return true;
        }
        public bool IsActuallySafeAt(int pos) {
            var tempMin = stats.TempMin.Value;
            var temp = S.GetTemp(pos);
            if (temp < tempMin) {
                // D.Warn("Not safe at pos {0}. Temp {1} < tempMin {2}",
                //     Pos.String(pos), temp, tempMin);
                return false;
            }
            var tempMax = stats.TempMax.Value;
            if (temp > tempMax) {
                // D.Warn("Not safe at pos {0}. Temp {1} > tempMin {2}",
                //     Pos.String(pos), temp, tempMax);
                return false;
            }
            var o2min = stats.O2Min.Value;
            if (o2min > 0f) {
                var o2 = S.GetO2(pos);
                if (o2 < o2min) {
                    // D.Warn("Not safe at pos {0}. O2 {1} < o2Min {2}",
                    //     Pos.String(pos), o2, o2min);
                    return false;
                }
            }
            return true;
        }
        public bool IsActuallySafeAt(int pos, out string threatT) {
            var tempMin = stats.TempMin.Value;
            var temp = S.GetTemp(pos);
            if (temp < tempMin) {
                threatT = T.UnsafeTempTooCold;
                return false;
            }
            var tempMax = stats.TempMax.Value;
            if (temp > tempMax) {
                threatT = T.UnsafeTempTooHot;
                return false;
            }
            var o2min = stats.O2Min.Value;
            if (o2min > 0f) {
                if (S.GetO2(pos) < o2min) {
                    threatT = T.UnsafeOxygenLow;
                    return false;
                }
            }
            threatT = null;
            return true;
        }

        // For conditions that are not safe but not deadly either
        // used for firefighting

        public bool IsActuallySurvivableAt(int pos) {
            var tempMin = stats.TempMin.Value;
            var temp = S.GetTemp(pos);

            // allow -100 degrees temporarily
            if (tempMin > 0f && temp < tempMin - 100) {
                return false;
            }
            var tempMax = stats.TempMax.Value;

            // allow + 200 degrees temporarily
            if (temp > tempMax + 200) { return false; }

            // oxygen is still required, we only add extra temperature to this
            var o2min = stats.O2Min.Value;
            if (o2min > 0f) {
                if (S.GetO2(pos) < o2min) {
                    return false;
                }
            }
            return true;
        }

        public bool IsFeelingSaferAt(int curPosIdx, int posIdx) {
            if (curPosIdx == posIdx) { return false; }
            var curSafety = SafetyLevelAt(curPosIdx);
            if (curSafety == 0f) {
                // we are perfectly safe, won't be safer
                return false;
            }
            return SafetyLevelAt(posIdx) > curSafety;
        }
    }
}
