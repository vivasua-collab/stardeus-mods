using Game.Data;
using Game.Systems.AI;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using System;
using Game.Systems.Creatures;
using Game.Constants;
using Unity.Mathematics;

namespace Game.Systems.Conditions {
    public abstract class Condition {
        public readonly string Id;
        public readonly int IdH;
        private string nameT;
        protected Func<string> nameTFunc;
        protected Func<string> descTFunc;
        private string descT;
        public string NameT => nameT ??= nameTFunc == null ? $"cond.{Id}".T() : nameTFunc.Invoke();
        public string DescT => descT ??= descTFunc == null ? $"cond.{Id}.desc".TOrNull() : descTFunc.Invoke();
        public readonly string Icon;
        public bool Incapacitates;
        public bool PreventsColonyWork;
        public bool PreventsControl;
        public bool IsPositive;
        public abstract Priority Importance { get; }
        public virtual bool ShowOnHoverFor(Being being) {
            return false;
        }
        protected static void RegisterCondition(Condition cond) {
            Registries.Conditions.Add(cond.Id, cond);
        }
        public virtual void OnStart(ConditionState cond, long ticks) { }
        public virtual void OnLoad(ConditionState cond, long ticks) { }
        public virtual void Tick(GameState s, ConditionState cond, long ticks) { }
        public virtual void OnEnd(ConditionState cond, long ticks) { }

        /// <summary>
        /// MoodSourceIdH of the permanent mood effect this condition keeps on its owner while
        /// it lasts, 0 for none. ConditionsSys removes that effect whenever the condition ends,
        /// so a condition sets the effect but never has to remember to remove it.
        /// </summary>
        public virtual int MoodSourceH => 0;

        /// <summary>
        /// Sets this condition's permanent mood effect. Call it from Tick while the condition
        /// lasts: every call confirms the effect, which keeps the leak backstop away.
        /// </summary>
        protected void SetMood(GameState s, ConditionState cond, string icon, string reason,
                int effect) {
            cond.Owner.Mind.Health?.SetPermanentEffect(s, MoodSourceH, icon, reason, effect);
        }

        public Condition(string id, string icon) {
            Id = id;
            IdH = Hashes.S(id);
            Icon = icon;
        }

        /// <summary>
        /// Increases or reduces the condition
        /// </summary>
        /// <param name="condId">See ConditionIdH</param>
        /// <param name="agent">AI agent</param>
        /// <param name="direction">1 = worsen, -1 = reduce</param>
        /// <param name="developmentRate">1f / Consts.TicksPerSomething</param>
        /// <param name="recoveryRate">1f / Consts.TicksPerSomething</param>
        /// <returns>bool if severity changed</returns>
        public static bool Advance(GameState S, long ticks,
                int condId, BeingData data,
                sbyte direction,
                float developmentRate,
                float recoveryRate,
                bool seekSafety,
                Severity maxSeverity = Severity.Fatal) {
            var levelProgress = ConditionState.GetProgress(data, condId);
            var levelBefore = levelProgress;
            bool changed = false;
            float delta = 0f;

            if (direction == -1) {
                // will start recovering from condition
                if (levelProgress > 0.0001f) {
                    levelProgress -= recoveryRate;
                    delta = -1f;
                    if (levelProgress < 0.0001f) {
                        levelProgress = ChangeConditionLevel(S, data, -1, condId, maxSeverity, out changed);
                    }
                }
            } else if (direction == 1) {
                // we are developing the condition
                levelProgress += developmentRate;
                delta += 1f;
                if (levelProgress > 1f) {
                    levelProgress = ChangeConditionLevel(S, data, 1, condId, maxSeverity, out changed);
                }
                if (seekSafety && !data.HasCondition(ConditionIdH.Sleeping)) {
                    EnsureSafetyGoal(data, ticks, condId, levelProgress);
                }
            }
            if (levelBefore != levelProgress) {
                ConditionState.SetProgress(data, condId, levelProgress);
                if (data.Conditions.TryGetValue(condId, out var state)) {
                    state.LastDelta = delta;
                }
                // D.Warn("Condition: {0} => {1}", ConditionIdH.ResolveName(condId),
                //     levelProgress);
            }
            return changed;
        }

        private static void EnsureSafetyGoal(BeingData data, long ticks, int condId,
                float levelProgress) {
            var agent = data.Being.AIAgent;
            // agent may be insane
            if (agent.IsFeelingSafeNow) {
                return;
            }
            if (!agent.BB.TryNeedGoal(NeedIdH.Safety, out var g)) {
                g = AIGoal.CreatePersonal(JobTypeIdH.Need, IconId.CWarning,
                    T.NeedSafety, agent, ticks)
                    .WithWorldToDesired(AIVarsH.IsInSafeEnvironment, false, true);
                agent.BB.SetNeedGoal(NeedIdH.Safety, g);
            }

            if (g.State == AIGoalState.InProgress) {
                // never touch goal that is in progress
                return;
            }

            if (g.State != AIGoalState.Available) {
                g.ChangeStateTo(AIGoalState.Available, ticks);
                g.ResetWorldState();
            }
            // decide if to increase the importance
            byte minImportance = (byte) math.round(levelProgress);
            if (data.Conditions.TryGetValue(condId, out var cond)) {
                minImportance += (byte) cond.Severity;
            }
            if (g.GoalImportance < minImportance) {
                g.GoalImportance = minImportance;
                g.WithInterrupt(minImportance, 0);
            }
        }

        private static float ChangeConditionLevel(GameState S, BeingData data,
                sbyte direction, int condId, Severity maxSeverity, out bool changed) {
            if (data.Conditions.TryGetValue(condId, out var cond)) {
                bool canBeFatal = maxSeverity == Severity.Fatal;
                if (!canBeFatal) {
                    if (direction == 1 && cond.Severity == maxSeverity) {
                        // reached the limit of our condition, hold just below the ceiling
                        changed = false;
                        return 0.99f;
                    }
                }
                if (Severities.TryMove(cond.Severity, direction, canBeFatal,
                        out var newS)) {
                    changed = true;
                    cond.SetSeverity(newS);
                    // D.Warn("Changing severity of {0} -> {1} for {2}",
                    //     cond.Condition.NameT, newS, data);
                    if (direction == 1) {
                        return 0.1f;
                    } else {
                        return 0.9f;
                    }
                } else {
                    if (cond.Severity == Severity.Mild) {
                        // we're healed
                        data.RemoveCondition(S, cond.Condition.IdH);
                        changed = true;
                        return 0f;
                    } else if (cond.Severity == Severity.Fatal) {
                        // data.Body.Kill(S);
                        // #if !PRODUCTION_BUILD
                        // D.Warn("Being should be dead, fatal condition for need: {0}: {1}", cond, data);
                        // #endif
                        changed = true;
                        return 0f;
                    } else {
                        D.Err("Unexpected severity for condition {0}: {1}",
                            cond.Condition.NameT,
                            cond.Severity);
                    }
                }
            } else {
                if (direction == 1) {
                    // begin being sick
                    var added = ConditionState.CreateFor(S, condId, data, Severity.Mild, false);
                    if (!added) {
                        changed = false;
                        return 0f;
                    }
                    // we're increasing from none to mild
                    changed = true;
                    return 0.1f;
                } else {
                    // We had a small bit of undeveloped condition, but now it's gone
                    changed = true;
                    return 0f;
                }
            }
            changed = false;
            return 0f;
        }
    }
}
