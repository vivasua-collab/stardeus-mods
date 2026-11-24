using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Mods;
using Game.Systems.AI;
using Game.Systems.Creatures;
using Game.Tools;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Conditions {
    public sealed class ConditionsSys : GameSystem {
        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new ConditionsSys());
        }


        public const string SysId = "ConditionsSys";
        public override string Id => SysId;

        public override void Unload() {
        }

        protected override void OnInitialize() {
            S.Sig.AfterLoadState.AddListener(OnLoadState);
            S.Sig.ConditionAdded.AddListener(OnAddCondition);
            S.Sig.ConditionRemoved.AddListener(OnRemoveCondition);
            S.Sig.ActivateCondition.AddListener(OnActivateCondition);
        }

        private readonly List<ConditionState> conditionStates = new();
        /// <summary>
        /// Mostly for tests, don't access this directly
        /// </summary>
        public List<ConditionState> ActiveConditions => conditionStates;

        private void OnLoadState(GameState state) {
            state.Clock.OnTick.AddListener(OnTick);
            ticksNow = state.Clock.Ticks;
        }

        private long ticksNow;
        // one condition that threw on every tick stopped every condition after it (bleeding,
        // healing, diseases of all beings) from ticking
        private readonly ErrorThrottle tickErrors = new();

        private void OnTick(long ticks) {
            ticksNow = ticks;
            for (int i = conditionStates.Count - 1; i >= 0; i--) {
                // a tick can end several conditions
                if (i >= conditionStates.Count) { continue; }
                ConditionState cond = conditionStates[i];
                try {
                    TickCondition(cond, ticks);
                } catch (Exception ex) {
                    if (tickErrors.ShouldLog(cond, out var count)) {
                        D.LogEx(ex, "Failed ticking condition {0} ({1} times)", cond, count);
                    }
                }
            }
            // the throttle holds its keys, so ended conditions are let go daily
            if (ticks % Consts.TicksPerDay == 0) {
                tickErrors.Clear();
            }
        }

        private void TickCondition(ConditionState cond, long ticks) {
            // Dead being?
            if (cond.IsDisabled) { return; }
            // Frozen in a pod. Their array's stability is the only thing allowed to harm
            // a sleeper (ManifestSys.HarmHour), so wounds, bleeding, diseases, immunity
            // and every duration stop with them until they are woken - opting in per
            // condition via PauseWhenBeingHidden left most of them running. Same guard,
            // same reason, as the top of BeingsSys.OnTick. Death clears the role, so a
            // corpse in front of the array still rots and bleeds out.
            if (cond.Owner.IsInStasis) { return; }
            if (cond.Duration > 0 && cond.StartTick + cond.Duration <= ticks) {
                cond.Owner.RemoveCondition(S, cond.Condition.IdH);
                // ended: OnEnd already ran and took its mood effect away, a Tick now would
                // act on a condition the being no longer has
                return;
            }
            cond.Condition.Tick(S, cond, ticks);
        }

        private void RemoveState(ConditionState cond, int idx) {
            if ((cond.Flags & ConditionFlags.RegisteredWithSystem) != ConditionFlags.RegisteredWithSystem) {
                D.Err("Removing a condition that is not registered with system! {0}", cond);
            }
            cond.Flags &= ~ConditionFlags.RegisteredWithSystem;
            // ended
            conditionStates.RemoveAt(idx);
            cond.Condition.OnEnd(cond, ticksNow);
            // The one place every condition ends, so no condition can leave its permanent
            // mood effect behind
            var moodSource = cond.Condition.MoodSourceH;
            if (moodSource != 0) {
                cond.Owner.Mind?.Health?.RemovePermanentEffect(moodSource, ticksNow);
            }
        }

        private void OnActivateCondition(ConditionState cond, bool isActive) {
            if (isActive) {
                conditionStates.AddUniqueInefficient(cond);
                cond.Flags |= ConditionFlags.RegisteredWithSystem;
            } else {
                conditionStates.Remove(cond);
                cond.Flags &= ~ConditionFlags.RegisteredWithSystem;
            }
        }

        private void OnAddCondition(ConditionState cond, bool isNew) {
            if ((cond.Flags & ConditionFlags.RegisteredWithSystem) == ConditionFlags.RegisteredWithSystem) {
                D.Err("Trying to register a cond that already has RegisteredWithSystem flag! {0}", cond);
            }
            if (!conditionStates.AddUniqueInefficient(cond)) {
                D.Err("Trying to add same condition more than once! New: {0}, Cond: {1}", isNew, cond);
                return;
            }
            cond.Flags |= ConditionFlags.RegisteredWithSystem;
            if (isNew) {
                // D.Warn("Adding new condition: {0}", cond);
                if (ticksNow == 0) {
                    cond.StartTick = 1;
                } else {
                    cond.StartTick = ticksNow;
                }
                cond.Condition.OnStart(cond, ticksNow);
            } else {
                // D.Warn("Loading condition: {0}", cond);
                cond.Condition.OnLoad(cond, ticksNow);
            }
        }

        private void OnRemoveCondition(BeingData being, int condId) {
            var cond = Registries.Conditions.FastGet(condId);
            if (cond == null) {
                D.Err("Failed to find condition by id: {0} ({1})", condId,
                    ConditionIdH.ResolveName(condId));
                return;
            }
            RemoveConditionFor(being, cond);
        }

        private void RemoveConditionFor(BeingData being, Condition cond) {
            bool removed = false;
            // D.Warn("Removing condition for {0}->{1}", being, cond);
            var countBefore = conditionStates.Count;
            for (int i = countBefore - 1; i >= 0; i--) {
                ConditionState c = conditionStates[i];
                if (c.Owner == being && c.Condition == cond) {
                    RemoveState(c, i);
                    removed = true;
                    break;
                }
            }
            if (!removed) {
                D.Err("Did not find Being/Condition pair in conditionStates for {0}/{1}/{2}",
                    being, cond, being.Body.StateB);
                D.Err("Condition states: {0}", D.Dump(conditionStates));
                foreach (var c in conditionStates) {
                    D.Err("Existing condition: {0}", c);
                }
            } else {
                if (countBefore == conditionStates.Count) {
                    D.Err("Condition removed, but conditions count still the same! {0} | {1}",
                        cond, D.DumpCollection(conditionStates));
                }
            }
        }
    }
}
