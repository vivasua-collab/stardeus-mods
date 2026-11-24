using System;
using System.Collections.Generic;
using System.Text;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Input;
using Game.Systems.Ships;
using Game.Utils;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class QuestData {
        public static uint LastId;
        public uint Id;
        public QuestStep[] Steps;
        public QuestDef Def;
        /// <summary>
        /// Who has this quest available? For non-ships only!
        /// </summary>
        public IQuestProvider Provider;
        public QuestState State;
        public bool IsActive => State == QuestState.Active;
        public bool IsAvailable => State == QuestState.Available;
        public bool IsOngoing => (State & QuestState.Ongoing) != QuestState.Undefined;
        public bool IsFailed => (State & QuestState.Unsuccessful) != QuestState.Undefined;
        public bool IsEnded => (State & QuestState.Ended) != QuestState.Undefined;
        public string Title;
        public string Desc;
        public int TextVariantIndex = -1;
        public long AvailableTick;
        public long StartTick;
        public long EndTick;
        public QuestBools Bools;
        /// <summary>
        /// Any persistent state for ongoing quest
        /// </summary>
        public ComponentData Data;
        public ComponentData GetOrCreateData() => Data ??= new ComponentData();
        /// <summary>
        /// Any non-persistent state for ongoing quest
        /// </summary>
        private ComponentData temp;
        public ComponentData Temp => temp ??= new ComponentData();

        public bool HasDeadline {
            get {
                for (int i = Steps.Length - 1; i >= 0; i--) {
                    QuestStep step = Steps[i];
                    if (step.IsEnded) { continue; }
                    if (step.Deadline != 0) {
                        return true;
                    }
                }
                return false;
            }
        }

        public Quest Quest;
        public Dictionary<int, QuestVar> Variables;
        public QuestReward[] Rewards;
        public QuestReward FindReward(string id, bool required = true) {
            if (Rewards != null) {
                foreach (var rew in Rewards) {
                    if (rew.Def.Id == id) {
                        return rew;
                    }
                }
            }
            if (required) {
                D.Throw("Couldn't find required quest reward: {0}", id);
            }
            return null;
        }
        /// <summary>
        /// For creating NEW quest, not for loading
        /// </summary>
        /// <returns>null if quest creation has failed</returns>
        public static QuestData CreateFor(GameState s, QuestDef def, Rng rng,
                IQuestProvider provider = null) {

            if (provider is SpaceObject so) {
                if (so.IsShip) {
                    D.Err("Creating QuestProvider from NPCShip.SO! Use NPCShip instead! {0} | {1}",
                        provider, def);
                    provider = so.Ship;
                }
            }

            var qd = new QuestData();
            qd.Id = ++LastId;
            qd.Def = def;
            qd.Provider = provider;
            if (!Arrays.IsEmpty(def.TitleKeys)) {
                qd.TextVariantIndex = s.Query.GetQuestVariantIndex.Ask(def, rng);
            }
            qd.Title = def.GetTitleT(qd.TextVariantIndex);
            qd.Desc = def.GetDescT(qd.TextVariantIndex);
            // if (provider is ColonistQuestProvider cp) {
            //     var giverLink = cp.Mind.LinkToShortName;
            //     if (qd.Title != null) { qd.Title = qd.Title.F(giverLink); }
            //     if (qd.Desc != null) { qd.Desc = qd.Desc.F(giverLink); }
            // }
            qd.AvailableTick = s.Ticks;
            // vars must go first!
            if (!qd.InitVars(s, rng.Int)) {
                return null;
            }
            // steps must go after vars!
            if (!qd.InitSteps(s)) {
                return null;
            }
            // rewards must go last!
            qd.Rewards = QuestRewardDef.CreateRewardsFor(s, def.Rewards, qd,
                rng.Int, isLoading: false);

            // After rewards, in case a skip is what completes the quest
            SkipStepsForOtherScenarios(s, qd);
            PreCheckSteps(qd);
            // Mark the chosen variant as seen only after all failure points have passed.
            if (qd.TextVariantIndex >= 0) {
                s.Query.MarkQuestVariantSeen.Ask(def, qd.TextVariantIndex);
            }
            return qd;
        }

        // Not in InitSteps: completing a step sets bools, which walks every step, and the
        // Steps array is only partly filled there
        private static void SkipStepsForOtherScenarios(GameState s, QuestData qd) {
            var scenarioId = s.Scenario?.Id;
            foreach (var step in qd.Steps) {
                var only = step.Def.ForScenarios;
                if (only == null || Arrays.Contains(only, scenarioId)) { continue; }
                if (step.IsEnded) { continue; }
                step.SetHidden();
                step.SetState(QuestStepState.Completed);
            }
        }

        private static void PreCheckSteps(QuestData qd) {
            foreach (var step in qd.Steps) {
                if (!step.IsHidden && !step.IsEnded) {
                    if (step.Check is IPreCheckedQuestStep pre) {
                        pre.PreCheck();
                    }
                }
            }
        }

        public void SetBools(QuestBools bools) {
            if (bools == null) { return; }
            Bools ??= QuestBools.Create();
            if (Bools.SetAll(bools)) {
                OnBoolsChanged();
            }
        }

        private void OnBoolsChanged() {
            for (int i = Steps.Length - 1; i >= 0; i--) {
                QuestStep step = Steps[i];
                step.OnBoolsChanged(Bools);
            }
            // A step reacting above can set more bools, which re-enters this method and may
            // already have ended the quest through CompleteWhen / FailWhen (two build steps
            // both pre-satisfied at once do exactly that). Ending it again is an error.
            if (IsEnded) {
                return;
            }
            if (QuestBools.ContainsAll(Bools, Def.FailWhen)) {
                SetState(QuestState.Failed);
            } else if (QuestBools.ContainsAll(Bools, Def.CompleteWhen)) {
                SetState(QuestState.Completed);
            }
        }

        private bool InitVars(GameState s, int seed) {
            if (Arrays.IsEmpty(Def.Variables)) {
                return true;
            }
            Variables = new Dictionary<int, QuestVar>(Def.Variables.Length);
            // CANNOT BE REVERSE LOOP because of var dependencies from previous
            // vars, such as location chain
            for (int i = 0; i < Def.Variables.Length; i++) {
                QuestVarDef vd = Def.Variables[i];
                var v = vd.CreateFor(s, this, seed, isLoading: false);
                if (v == null) {
                    return false;
                }
                if (!Variables.TryAdd(vd.IdH, v)) {
                    D.Err("QuestVar with same ID {0} already exists for quest: {1}",
                        vd.Id, this);
                    return false;
                }
            }
            return true;
        }


        private bool InitSteps(GameState s) {
            Steps = new QuestStep[Def.Steps.Length];
            // must be forward loop, because each step can write variables for
            // the next step
            for (int i = 0; i < Def.Steps.Length; i++) {
                QuestStepDef stepDef = Def.Steps[i];
                var step = QuestStep.CreateFor(s, stepDef, this);
                if (step == null) {
                    return false;
                }
                Steps[i] = step;
            }
            return true;
        }

        public bool SetState(QuestState newState, bool warnIfSame = true) {
            if (State == newState) {
                if (warnIfSame) {
                    D.Err("Setting quest state to same: {0} | {1}", newState, this);
                }
                return false;
            }
            D.Warn("Setting quest state: {0} -> {1} | {2}", State, newState, this);
            var lastState = State;
            if (newState == QuestState.Active) {
                // we're switching from available to active, which may have
                // a progress bar, need to reset the old UDB type
                // Quest?.UI.ResetUDB();
                PreCheckSteps(this);
                // A pre-check can complete, fail or delete the quest re-entrantly (its
                // CompletesQuest/FailsQuest step was already satisfied before the quest
                // was accepted). That inner SetState call already did everything - don't
                // overwrite its result by forcing State back to Active below.
                if (State != lastState) {
                    return true;
                }
            // } else {
                // if (A.S.Query.IsTrackedQuest.Ask(Quest)) {
                //     A.S.Query.TrackQuest.Ask(Quest, false);
                // }
            }
            State = newState;
            bool needsRebuildQuestsUI = false;
            if (newState == QuestState.Active) {
                StartTick = G.Ticks;
                needsRebuildQuestsUI = true;
            }
            for (int i = Steps.Length - 1; i >= 0; i--) {
                QuestStep step = Steps[i];
                step.OnQuestStateChange(lastState, newState);
            }

            string questEndMsg = null;
            StringBuilder sb = null;
            bool isQuestOver = false;
            if (newState == QuestState.Completed) {
                questEndMsg = $"{T.QuestEndCompleted}: {Title}";
                if (Rewards != null) {
                    foreach (var r in Rewards) {
                        if (r.Flags.HasFlag(QuestRewardFlags.PackageContent)) {
                            continue;
                        }
                        if (!r.Flags.HasFlag(QuestRewardFlags.Penalty)) {
                            var msg = r.Apply();
                            sb = CollectRewardMsg(sb, msg, T.QuestRewards, questEndMsg);
                        }
                    }
                }
                needsRebuildQuestsUI = true;
                isQuestOver = true;
            } else if (newState == QuestState.Failed) {
                questEndMsg = $"{T.QuestEndFailed}: {Title}";
                if (Rewards != null) {
                    foreach (var r in Rewards) {
                        if (r.Flags.HasFlag(QuestRewardFlags.PackageContent)) {
                            continue;
                        }
                        if (r.Flags.HasFlag(QuestRewardFlags.Penalty)) {
                            var msg = r.Apply();
                            sb = CollectRewardMsg(sb, msg, T.NounPenalties, questEndMsg);
                        }
                    }
                }
                needsRebuildQuestsUI = true;
                isQuestOver = true;
            } else if (newState == QuestState.Rejected) {
                needsRebuildQuestsUI = true;
                isQuestOver = true;
            }

            if (isQuestOver) {
                EndTick = G.Ticks;
                if (Provider is NPCShip ship) {
                    A.S.Query.DelayNextNPCShipQuest.Ask(ship);
                }
            }

            // No Quest wrapper means the quest ended while it was still being created: a
            // step pre-check in CreateFor found its objective already met (e.g. an intact
            // ship already has the device an auto-added tutorial asks for). The player never
            // saw the quest, so there is nothing to announce - the bools still get set via
            // QuestStateChanged, which is what moves the chain on.
            if (questEndMsg != null && Quest != null) {
                if (sb != null) {
                    questEndMsg = sb.ToString();
                }
                The.Dialog.ShowSimple(QuestUtils.IconFor(Quest, false), questEndMsg);
            }

            if (needsRebuildQuestsUI) {
                // Quest?.UI.ResetUDB();
                A.S.UISig.ReloadQuests.Send();
            }

            A.S.Sig.QuestStateChanged.Send(this, lastState, newState);
            return true;
        }

        private static StringBuilder CollectRewardMsg(StringBuilder sb, string msg,
                string title, string prefix) {
            if (msg != null) {
                if (sb == null) {
                    sb = Caches.StringBuilder4;
                    sb.Clear();
                    sb.Append(prefix);
                    sb.Append(GlyphMap.BRHR);
                    sb.AppendFormat("{0}:", title);
                }
                // A.S.Sig.ShowFanfare.Send(msg);
                sb.AppendFormat("<br>{0} {1}",
                    Texts.Accented(GlyphMap.ArrowRight), msg);
            }

            return sb;
        }

        public bool GetNextDeadline(out long deadline, out QuestStep step) {
            deadline = long.MaxValue;
            step = null;
            for (int i = Steps.Length - 1; i >= 0; i--) {
                QuestStep st = Steps[i];
                if (st.IsEnded) { continue; }
                if (st.Deadline > 0 && st.Deadline < deadline) {
                    step = st;
                    deadline = st.Deadline;
                }
            }
            return step != null;
        }

        public QuestVar GetQuestVar(string id) {
            if (Variables.TryGetValue(Hashes.S(id), out var qVar)) {
                return qVar;
            }
            D.Throw("No QuestVar with id: {0} | {1}", id, this);
            return null;
        }

        public override string ToString() {
            return $"QuestData[{Id}|{Def.Id}|{State}]";
        }

    }
}
