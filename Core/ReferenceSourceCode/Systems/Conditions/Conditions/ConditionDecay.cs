using Game.Constants;
using Game.Data;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Conditions {
    /// <summary>
    /// Base class for Rotting (biological) and Corroding (non-biological) decay conditions.
    /// Active on dead bodies that are on the map (Being != null). For stored bodies,
    /// RottingSys handles degradation directly since ConditionsSys does not tick
    /// conditions on beings without an active entity.
    /// Ticks once per in-game hour to avoid per-tick overhead with many entities.
    ///
    /// The first <see cref="DecayGraceBand"/> of raw BodyPartState.Decay is a silent grace
    /// band: no quality loss, no wound, nothing shown in the UI - a brand new part or a
    /// freshly dead body should not visibly corrode/rot immediately. Wound severity, part
    /// Condition and the condition's own severity/progress are all derived from
    /// <see cref="VisibleDecay"/>, which rescales the remaining range back to 0..1, so the
    /// existing severity bands and "fully decayed = 100% progress" behavior are unaffected.
    /// </summary>
    public abstract class ConditionDecay : Condition {
        internal const float DecayGraceBand = 0.2f;

        protected abstract float QualityLossPerWeek { get; }
        protected abstract float ConditionLossPerWeek { get; }

        protected ConditionDecay(string id, string icon) : base(id, icon) { }

        public override Priority Importance => Priority.Normal;

        public override void OnStart(ConditionState cond, long ticks) {
            cond.Flags |= ConditionFlags.TickAfterDeath;
            cond.Flags |= ConditionFlags.RemoveOnResurrect;
        }

        public override void OnLoad(ConditionState cond, long ticks) {
            cond.Flags |= ConditionFlags.TickAfterDeath;
            cond.Flags |= ConditionFlags.RemoveOnResurrect;
            SyncConditionState(cond.Owner, cond.Condition.IdH);
        }

        public override void Tick(GameState s, ConditionState cond, long ticks) {
            if (ticks % Consts.TicksPerHour != 0) { return; }
            // repair station, autodoc, body mod spots will stop decay instantly
            if ((cond.Owner.StateFlags & BeingState.InDevice) == BeingState.InDevice) {
                return;
            }
            ApplyDecay(cond.Owner, QualityLossPerWeek, ConditionLossPerWeek, IdH, Consts.TicksPerHour);
        }

        /// <summary>Rescales raw decay past the grace band back to a 0..1 range.</summary>
        internal static float VisibleDecay(float decay) {
            return Mathf.Clamp01((decay - DecayGraceBand) / (1f - DecayGraceBand));
        }

        /// <summary>How many days the grace band itself takes to cross, at a given weekly rate.</summary>
        internal static float GraceBandDays(float conditionLossPerWeek) {
            return DecayGraceBand / conditionLossPerWeek * 7f;
        }

        /// <summary>Raises every installed slot's raw Decay to the grace band boundary. Called
        /// when a decay condition activates, so it starts exactly at Mild/0% with no backlog
        /// of invisible decay to burn through.</summary>
        internal static void StartDecayAtGraceBand(BeingData data) {
            foreach (var slot in data.Body.Slots) {
                if (slot.IsPartRemoved || slot.State == null) { continue; }
                if (slot.State.Decay < DecayGraceBand) {
                    slot.State.Decay = DecayGraceBand;
                }
            }
        }

        internal static void ApplyDecay(BeingData data,
                float qualityLossPerWeek, float conditionLossPerWeek, int conditionId,
                long ticksElapsed = 1) {
            float qualityLoss = qualityLossPerWeek / Consts.TicksPerWeek * ticksElapsed;
            float condLoss = conditionLossPerWeek / Consts.TicksPerWeek * ticksElapsed;
            var source = NamedEntity.For("Decay");
            float maxDecay = 0f;
            foreach (var slot in data.Body.Slots) {
                if (slot.IsPartRemoved || slot.State == null) { continue; }
                float newDecay = Mathf.Min(1f, slot.State.Decay + condLoss);
                maxDecay = Mathf.Max(maxDecay, newDecay);
                if (newDecay < DecayGraceBand) {
                    // Still within the silent grace band: no quality loss, no visible wound.
                    // Clean up a Decay wound left over from a save made before the grace band
                    // existed.
                    if (!Arrays.IsEmpty(slot.State.Wounds) && slot.State.RemoveWound(DamageType.Decay)) {
                        slot.UpdateCondition(false);
                    }
                    slot.State.Decay = newDecay;
                    continue;
                }
                if (slot.State.Quality > 0f) {
                    slot.State.SetQuality(Mathf.Max(0f, slot.State.Quality - qualityLoss));
                }
                slot.TryAddOrUpdateWound(source, DamageType.Decay,
                    ConditionLossToSeverity(newDecay),
                    G.Ticks, 1f, WoundFlags.None, conditionId: conditionId);
                slot.State.Decay = newDecay;
            }
            SyncConditionState(data, conditionId, maxDecay);
        }

        internal static void ApplyDecayToPartState(BodyPartState state, BodyPartDef def,
                float qualityLossPerWeek, float conditionLossPerWeek, long ticksElapsed) {
            float qualityLoss = qualityLossPerWeek / Consts.TicksPerWeek * ticksElapsed;
            float condLoss = conditionLossPerWeek / Consts.TicksPerWeek * ticksElapsed;
            float newDecay = Mathf.Min(1f, state.Decay + condLoss);
            if (newDecay < DecayGraceBand) {
                // Still within the silent grace band: no quality loss, no visible wound.
                // Clean up a Decay wound left over from a save made before the grace band
                // existed.
                if (!Arrays.IsEmpty(state.Wounds) && state.RemoveWound(DamageType.Decay)) {
                    state.UpdateCondition(true, def.SlotType);
                }
                state.Decay = newDecay;
                return;
            }
            if (state.Quality > 0f) {
                state.SetQuality(Mathf.Max(0f, state.Quality - qualityLoss));
            }
            if (newDecay > state.Decay) {
                // D.Warn("Decaying body part: {0} / {1}. {2}", def.Id, state, newDecay);
                var sev = ConditionLossToSeverity(newDecay);
                var w = state.WoundFor(DamageType.Decay);
                if (w.Severity < sev) {
                    w.Severity = sev;
                    state.UpdateWound(w);
                    state.UpdateCondition(true, def.SlotType);
                }
                state.Decay = newDecay;
            }
        }

        /// <summary>Maps raw decay to wound/condition severity. Below the grace band this is
        /// Irrelevant (nothing shown); above it, the existing 20%-wide bands apply to
        /// <see cref="VisibleDecay"/>.</summary>
        internal static Severity ConditionLossToSeverity(float decay) {
            if (decay < DecayGraceBand) { return Severity.Irrelevant; }
            float visible = VisibleDecay(decay);
            if (visible >= 0.8f) { return Severity.Irreparable; }
            if (visible >= 0.6f) { return Severity.Critical; }
            if (visible >= 0.4f) { return Severity.Severe; }
            if (visible >= 0.2f) { return Severity.Moderate; }
            return Severity.Mild;
        }

        internal static void SyncConditionState(BeingData data, int conditionId) {
            float maxDecay = 0f;
            foreach (var slot in data.Body.Slots) {
                if (slot.IsPartRemoved || slot.State == null) { continue; }
                maxDecay = Mathf.Max(maxDecay, slot.State.Decay);
            }
            SyncConditionState(data, conditionId, maxDecay);
        }

        private static void SyncConditionState(BeingData data, int conditionId, float maxDecay) {
            if (!data.Conditions.TryGetValue(conditionId, out var condition)) { return; }

            var rawSeverity = ConditionLossToSeverity(maxDecay);
            // Only reachable from a pre-grace-band save, or a corpse healed back into the
            // grace band: keep the condition at Mild but hide it until decay clears the band.
            bool isUndetected = rawSeverity == Severity.Irrelevant;
            var severity = isUndetected ? Severity.Mild : rawSeverity;
            float progress = isUndetected ? 0f : Mathf.Clamp01(
                (VisibleDecay(maxDecay) - SeverityToVisibleLowerBound(severity)) * 5f);

            bool severityChanged = condition.Severity != severity;
            bool progressChanged = !data.ConditionProgress.TryGetValue(conditionId, out var currentProgress)
                || Maths.Different(currentProgress, progress);
            bool undetectedChanged = condition.IsUndetected != isUndetected;
            if (!severityChanged && !progressChanged && !undetectedChanged) { return; }

            if (isUndetected) {
                condition.Flags |= ConditionFlags.Undetected;
            } else {
                condition.Flags &= ~ConditionFlags.Undetected;
            }
            condition.SetSeverity(severity);
            if (progressChanged) {
                ConditionState.SetProgress(data, conditionId, progress);
            } else {
                data.NotifyConditionsListChanged();
            }
        }

        private static float SeverityToVisibleLowerBound(Severity severity) {
            return severity switch {
                Severity.Fatal => 0.8f,
                Severity.Irreparable => 0.8f,
                Severity.Critical => 0.6f,
                Severity.Severe => 0.4f,
                Severity.Moderate => 0.2f,
                _ => 0f,
            };
        }

        /// <summary>Raw decay at the bottom of a severity band (used by Wound.TryHeal to write
        /// BodyPartState.Decay back after a healed severity step).</summary>
        internal static float SeverityToDecayLowerBound(Severity severity) {
            return DecayGraceBand + SeverityToVisibleLowerBound(severity) * (1f - DecayGraceBand);
        }
    }
}
