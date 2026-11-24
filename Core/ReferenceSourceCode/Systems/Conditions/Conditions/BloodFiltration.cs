using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Utils;
using KL.Utils;
using Unity.Mathematics;

namespace Game.Systems.Conditions {
    // Kidneys filter the blood. Poor filtration lets BloodToxicity rise toward an
    // equilibrium (Beings/BloodFiltration_Toxicity), and the toxicity drives ConditionToxemia.
    // See Docs/Architecture/Systems/ConditionsSystem.md, "Toxemia"
    public static class BloodFiltration {
        public const int TickInterval = Consts.TicksPer10Minutes;
        // Lower bound of each Toxemia severity, Mild to Fatal
        public const float MildAt = 0.2f;
        public const float ModerateAt = 0.4f;
        public const float SevereAt = 0.6f;
        public const float CriticalAt = 0.8f;
        public const float FatalAt = 0.95f;

        // Species id -> BloodFilterRate of its default body, 0 when it has no kidneys
        private static readonly Dictionary<int, float> normalRates = new();
        private static Curve equilibriumCurve;

        [SelfInit]
        private static void Init() {
            normalRates.Clear();
            equilibriumCurve = null;
        }

        public static void Tick(GameState s, BeingData data, long ticks) {
            if (ticks % TickInterval != 0) { return; }
            Step(s, data, TickInterval / (float) Consts.TicksPerHour);
        }

        // Moves toxicity by the given number of hours and syncs the condition and its wounds
        public static void Step(GameState s, BeingData data, float hours) {
            if (!data.Body.IsAlive || data.IsInStasis) { return; }
            if (!TryGetFiltration(data, out var filtration)) { return; }
            var tox = data.StatsBeing.BloodToxicity;
            var current = tox.BaseValue;
            var target = EquilibriumFor(filtration);
            if (current < target) {
                current = math.min(target, current + Tunables.BloodToxicityRisePerDay * hours / 24f);
            } else if (current > target) {
                current = math.max(target, current - Tunables.BloodToxicityFallPerDay * hours / 24f);
            }
            tox.UpdateBase(current);
            // modifiers (a crawler's facehug) add on top of what the kidneys failed to clear
            var severity = SeverityFor(math.saturate(tox.ValueFresh));
            var cond = SyncCondition(s, data, severity);
            if (cond != null) {
                ConditionToxemia.SyncWounds(cond, G.Ticks);
            }
        }

        // Filtration as a share of what the species' own healthy kidneys give: small kidneys
        // are healthy for a cat. False for beings the model does not apply to.
        public static bool TryGetFiltration(BeingData data, out float filtration) {
            filtration = 0f;
            var stats = data.StatsBeing;
            if (stats.BloodToxicity.IsUnused) { return false; }
            var normal = NormalRateFor(data.Species);
            if (normal <= 0f) { return false; }
            filtration = stats.BloodFilterRate.ValueFresh / normal;
            return true;
        }

        public static float EquilibriumFor(float filtration) {
            equilibriumCurve ??= CurveDef.FastGet(CurveIdH.Beings_BloodFiltration_Toxicity);
            if (equilibriumCurve == null) {
                D.Err("Missing curve {0}", CurveId.Beings_BloodFiltration_Toxicity);
                return 0f;
            }
            return math.saturate(equilibriumCurve.Evaluate(math.saturate(filtration)));
        }

        public static Severity SeverityFor(float toxicity) {
            if (toxicity >= FatalAt) { return Severity.Fatal; }
            if (toxicity >= CriticalAt) { return Severity.Critical; }
            if (toxicity >= SevereAt) { return Severity.Severe; }
            if (toxicity >= ModerateAt) { return Severity.Moderate; }
            if (toxicity >= MildAt) { return Severity.Mild; }
            return Severity.Irrelevant;
        }

        // Derived from the toxicity on every step, so the condition can never drift from it
        private static ConditionState SyncCondition(GameState s, BeingData data, Severity severity) {
            var idH = ConditionIdH.Toxemia;
            data.Conditions.TryGetValue(idH, out var cond);
            if (severity == Severity.Irrelevant) {
                if (cond != null) {
                    data.RemoveCondition(s, idH);
                }
                return null;
            }
            if (cond == null) {
                if (!ConditionState.CreateFor(s, idH, data, severity, true)) { return null; }
                data.Conditions.TryGetValue(idH, out cond);
                return cond;
            }
            if (cond.Severity != severity) {
                cond.SetSeverity(severity);
                data.NotifyConditionsListChanged();
            }
            return cond;
        }

        private static float NormalRateFor(Species species) {
            if (normalRates.TryGetValue(species.IdH, out var rate)) { return rate; }
            rate = 0f;
            var config = BodyConfiguration.ForSpecies(species);
            if (config?.SlotAssignments != null) {
                foreach (var part in config.SlotAssignments.Values) {
                    if (part?.StatSources == null) { continue; }
                    foreach (var src in part.StatSources) {
                        if (src.StatId == StatIdH.BloodFilterRate) {
                            rate += src.Amount;
                        }
                    }
                }
            }
            normalRates[species.IdH] = rate;
            return rate;
        }
    }
}
