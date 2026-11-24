using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine.Pool;

namespace Game.Systems.Quests {
    public sealed class QuestStep {
        private readonly GameState S;
        public readonly QuestData Data;
        public readonly QuestStepDef Def;
        public string Title;
        public string Desc;
        /// <summary>
        /// For tooltips where all steps are listed
        /// </summary>
        public string DescShort;
        public bool IsHidden { get; private set; }
        public string TitleInactive;
        /// <summary>
        /// You can use "quest.details.will.reveal.on.accept" as a common message
        /// </summary>
        public string DescInactive;
        public QuestStepState State;
        public readonly QuestStepFlags Flags;
        public bool IsActive => State == QuestStepState.Active;
        public bool IsEnded => State == QuestStepState.Completed || State == QuestStepState.Failed;
        public readonly bool IsOptional;
        public readonly bool FailsQuest;
        public readonly bool CompletesQuest;
        public long Deadline;
        public BaseQuestStep Check;
        public bool SaveLoadTexts => Check.SaveLoadTexts;
        public Rng SeededRng => new Rng(Hashes.Quick(
            (int) Data.Id, Primes.Prime022, Hashes.S(Def.Id)));


        [SkipDumpInstance]
        public string Debug {
            get {
                var sb = Caches.StringBuilder4;
                sb.Clear();
                sb.AppendLine(D.DumpInstance(this));
                sb.AppendLine(D.DumpInstance(Check, flags: BindingFlags.Instance
                    | BindingFlags.Public | BindingFlags.NonPublic));
                return sb.ToString();
            }
        }
        public QuestStep(GameState s, QuestStepDef def, QuestData data) {
            S = s;
            Data = data;
            Def = def;
            Title = def.Id;
            Flags = def.Flags;
            IsOptional = Flags.HasFlag(QuestStepFlags.Optional);
            CompletesQuest = Flags.HasFlag(QuestStepFlags.CompletesQuest);
            FailsQuest = Flags.HasFlag(QuestStepFlags.FailsQuest);
        }
        public void Tick(long ticks) {
            // D.Warn("Ticking quest step: {0}", this);
            try {
                if (Check is ITickListener tl) {
                    tl.Tick(ticks);
                }
                if (Deadline > 0 && ticks > Deadline
                        && (Data.State & QuestState.Ongoing) != QuestState.Undefined) {
                    D.Warn("Quest step deadline hit: {0}", this);
                    SetState(QuestStepState.Failed);
                }
            } catch (Exception ex) {
                D.LogEx(ex, "Quest step failed: Check: {0} | {1}",
                    Check, this);
            }
        }

        public static QuestStep CreateFor(GameState s, QuestStepDef def, QuestData data) {
            var item = new QuestStep(s, def, data);
            item.State = QuestStepState.Inactive;
            if (def.Flags.HasFlag(QuestStepFlags.Secret)) {
                item.IsHidden = true;
            }
            item.Check = def.CreateCheck(s, item, isLoading: false);
            if (item.Check == null) {
                return null;
            }
            return item;
        }

        public bool SetState(QuestStepState newState, bool warnIfSame = true) {
            if (State == newState) {
                if (warnIfSame) {
                    D.Err("Setting same state for step: {0} -> {1}", newState, this);
                }
                return false;
            }
            D.Warn("Quest step state changed: {0} -> {1} | {2}", State, newState, this);
            var oldState = State;
            Check.OnStateChange(oldState, newState);
            if (State != oldState) {
                // OnStateChange triggered a re-entrant SetState (e.g. CompleteStep inside
                // OnStateChange(Active)); the inner call already handled everything.
                return true;
            }
            State = newState;
            // Cleanup unsubscribes signal listeners, and SetState is regularly
            // reached from inside a signal dispatch (a quest step listener
            // completing its own step). Signal.Send iterates listeners by
            // index, so removing more than the currently running listener
            // while a Send is in flight breaks that loop - defer cleanup.
            if (newState == QuestStepState.Completed) {
                Deadline = 0L;
                Data.SetBools(Def.SetOnComplete);
                if (Def.Flags.HasFlag(QuestStepFlags.CompletesQuest)) {
                    Data.SetState(QuestState.Completed, warnIfSame: false);
                }
                S.ActQ.Enqueue(Check.Cleanup);
            } else if (newState == QuestStepState.Failed) {
                Deadline = 0L;
                Data.SetBools(Def.SetOnFail);
                if (Def.Flags.HasFlag(QuestStepFlags.FailsQuest)) {
                    Data.SetState(QuestState.Failed, warnIfSame: false);
                }
                S.ActQ.Enqueue(Check.Cleanup);
            }
            Def.OnStateChange(S, newState, this);
            // UI.UpdateUIBlock(true);
            // Data.Quest?.UI.Rebuild();
            S.Sig.QuestStepStateChanged.Send(this, oldState, newState);
            return true;
        }

        public void OnQuestStateChange(QuestState before, QuestState after) {
            Check.OnQuestStateChange(before, after);
            if (after == QuestState.Active
                    && Flags.HasFlag(QuestStepFlags.ActiveOnStart)
                    && !IsEnded) {
                // A step can already be Completed/Failed here if a pre-check resolved it
                // while the quest was still Available - don't resurrect it back to Active.
                SetState(QuestStepState.Active);
            } else if (State == QuestStepState.Active) {
                if ((after & QuestState.Ended) != QuestState.Undefined) {
                    // continue here - see what quest steps look like after quests are abandoned
                    SetState(QuestStepState.Inactive);
                }
            }
        }

        public void OnBoolsChanged(QuestBools bools) {
            if (QuestBools.ContainsAll(bools, Def.FailWhen)) {
                if (State != QuestStepState.Completed) {
                    SetState(QuestStepState.Failed, warnIfSame: false);
                }
            } else if (QuestBools.ContainsAll(bools, Def.CompleteAndHideWhen)) {
                SetHidden();
                SetState(QuestStepState.Completed, warnIfSame: false);
            } else if (QuestBools.ContainsAll(bools, Def.CompleteWhen)) {
                if (State != QuestStepState.Failed) {
                    SetState(QuestStepState.Completed, warnIfSame: false);
                }
            } else if (State == QuestStepState.Inactive) {
                if (QuestBools.ContainsAll(bools, Def.ActivateWhen)) {
                    SetState(QuestStepState.Active);
                }
            }
        }
        public string RandomMsg(string keysVar, Rng rng) {
            var keys = Def.Config.GetStringSet(Hashes.S(keysVar), null);
            if (keys == null) {
                D.Err("No message keys found for var {0} | {1}",
                    keysVar, this);
                return "...";
            }
            return Texts.RandomKey(rng, keys, this)?.T() ?? "...";
        }


        public void Cleanup() {
            Check.Cleanup();
        }

        public void OnLoad() {
            Check = Def.CreateCheck(S, this, isLoading: true);
        }
        public void SetHidden() {
            if (!IsHidden) {
                IsHidden = true;
                Data.SetBools(Def.SetOnHide);
            }
        }
        public void LoadHidden(bool hidden) {
            IsHidden = hidden;
        }

        public override string ToString() {
            return $"QuestStep[{Def.Id}|{State}|{Def}]";
        }

    }
}
