using System;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using Game.Tools;
using KL.Utils;

namespace Game.Systems.Quests {
    /// <summary>
    /// Quest runner instance. All data is in <see cref="QuestData"/>
    /// </summary>
    public sealed class Quest {
        public readonly GameState S;
        public readonly QuestData Data;
        public readonly QuestStep[] Steps;
        public bool IsActive => Data.IsActive;
        public bool IsAvailable => Data.IsAvailable;
        public bool IsOngoing => Data.IsOngoing;
        public bool IsFailed => Data.IsFailed;
        public bool IsEnded => Data.IsEnded;
        public bool HasDeadline => Data.HasDeadline;
        public bool IsMain => Data.Def.IsMain;
        public bool IsAutoAdd => Data.Def.IsTutorial || (Data.Def.IsMain && Data.Def.Flags.HasFlag(QuestFlags.AutoAdd));

        private Quest(GameState s, QuestData data) {
            S = s;
            Data = data;
            Steps = data.Steps;
            data.Quest = this;
        }

        public void Tick(long ticks) {
            for (int i = Steps.Length - 1; i >= 0; i--) {
                QuestStep item = Steps[i];
                if (!item.IsEnded) {
                    item.Tick(ticks);
                    if (!IsActive) {
                        return;
                    }
                }
            }

            if (ticks % Consts.TicksPer30Minutes != 19) {
                return;
            }
            // validate the quest steps
            var activeSteps = 0;
            var completedSteps = 0;
            var failedSteps = 0;
            for (int i = Steps.Length - 1; i >= 0; i--) {
                QuestStep item = Steps[i];
                if (item.IsActive) {
                    activeSteps++;
                } else if (item.IsEnded) {
                    if (item.State == QuestStepState.Completed) {
                        completedSteps++;
                    } else {
                        failedSteps++;
                    }
                }
            }

            if (completedSteps == Steps.Length) {
                D.Err("Quest has all steps completed, but still active! {0}", this);
                Data.SetState(QuestState.Completed);
            } else if (activeSteps == 0) {
                D.Err("Quest has not active steps, while some steps are not completed! {0}", this);
                Data.SetState(QuestState.Failed);
            }
        }

        public static Quest InstanceFor(GameState s, QuestData data) {
            if (data == null) {
                D.Err("Trying to create Quest instance for null QuestData!");
                return null;
            }
            return new Quest(s, data);
        }

        public override string ToString() {
            return $"Quest[{Data}]";
        }

        public void Cleanup() {
            for (int i = Data.Steps.Length - 1; i >= 0; i--) {
                QuestStep step = Data.Steps[i];
                step.Cleanup();
            }
        }
        public void GetProgress(out int done, out int total) {
            done = 0;
            total = 0;
            for (int i = Steps.Length - 1; i >= 0; i--) {
                QuestStep item = Steps[i];
                if (item.IsHidden) { continue; }
                if (item.IsOptional) {
                    if (!item.IsActive && item.State != QuestStepState.Completed) {
                        continue;
                    }
                }
                if (item.State == QuestStepState.Completed) {
                    done++;
                }
                total++;
            }
        }
    }
}
