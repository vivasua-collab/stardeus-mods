using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Quests {
    public sealed class DeadlineQuestVar : QuestVar, IQuestVar<long> {
        [SelfInit]
        private static void Register() {
            Registries.QuestVars.Add(VarId,
                (s, def, data, seed, l) => new DeadlineQuestVar(s, def, data, seed, l));
        }

        public const string VarId = "Deadline";
        private long deadline;
        public long Value => deadline;

        public DeadlineQuestVar(GameState s, QuestVarDef def,
                QuestData data, int seed, bool isLoading)
                    : base(s, def, data, seed, isLoading) {
            var days = ResolveDays(def, seed);
            deadline = data.AvailableTick + days * Consts.TicksPerDay;
        }

        private int ResolveDays(QuestVarDef def, int seed) {
            var configuredDays = def.Config.GetInt(PropertyIdH.Days, 0);
            if (configuredDays != 0) {
                return configuredDays;
            }
            Curve daysCurve = def.Config.GetFloatSet(PropertyIdH.DaysCurve);
            var difficultyInput = def.Config.GetString(PropertyIdH.DifficultyInput, null);
            var rng = new Rng(seed);
            float days;
            if (difficultyInput == null) {
                days = rng.Curve(daysCurve);
            } else {
                float input;
                switch (difficultyInput) {
                    case "Sector": {
                        var providerSO = data.Provider?.ProviderSO;
                        input = providerSO != null
                            ? SOUtils.SectorDifficultyOf(providerSO, null)
                            : 0.5f;
                        break;
                    }
                    case "Global":
                        input = Mathf.Clamp01(S.Difficulty.Level / 5f);
                        break;
                    default:
                        throw new Exception(
                            $"Unknown Deadline DifficultyInput: {difficultyInput} | {data}");
                }
                days = daysCurve.Evaluate(input);
            }
            var multRange = def.Config.GetFloatSet(PropertyIdH.DeadlineMult, null);
            if (multRange != null) {
                days *= rng.Range(multRange);
            }
            return Mathf.RoundToInt(days);
        }
    }
}
