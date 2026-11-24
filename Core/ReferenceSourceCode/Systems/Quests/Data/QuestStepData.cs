using System;
using Game.Data;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Quests {
    [MessagePackObject]
    public sealed class QuestStepData {
        [Key("DefId")] public string DefId;
        [Key("State")] public QuestStepState State;
        [Key("Deadline")] public long Deadline;
        [Key("Title")] public string Title;
        [Key("Desc")] public string Desc;
        [Key("Hidden")] public bool IsHidden;

        public static QuestStepData[] Serialize(QuestStep[] steps) {
            var res = new QuestStepData[steps.Length];
            for (int i = steps.Length - 1; i >= 0; i--) {
                QuestStep step = steps[i];
                res[i] = Serialize(step);
            }
            return res;
        }

        private static QuestStepData Serialize(QuestStep step) {
            var d = new QuestStepData();
            d.DefId = step.Def.Id;
            d.State = step.State;
            d.Deadline = step.Deadline;
            d.IsHidden = step.IsHidden;
            if (step.SaveLoadTexts) {
                d.Title = step.Title;
                d.Desc = step.Desc;
            }
            return d;
        }

        public static bool TryDeserialize(GameState s, QuestStepData[] steps, QuestData q) {
            q.Steps = new QuestStep[steps.Length];
            for (int i = steps.Length - 1; i >= 0; i--) {
                QuestStepData d = steps[i];
                var step = Deserialize(s, i, d, q);
                if (step == null) {
                    return false;
                }
                q.Steps[i] = step;
            }
            return true;
        }

        private static QuestStep Deserialize(GameState s, int idx, QuestStepData data,
                QuestData questData) {
            var def = Arrays.SafeGet(questData.Def.Steps, idx);
            if (def == null) {
                D.ErrSoft("QuestStep failed to deserialize, different number of steps in saved and actual quest def: {0}", data.DefId);
                return null;
            }
            if (def.Id != data.DefId) {
                D.ErrSoft("QuestStepDef ID mismatch! Loading {0} when actual id is {1} | {2}",
                    data.DefId, def.Id, questData);
                return null;
            }
            var step = new QuestStep(s, def, questData);
            step.State = data.State;
            step.LoadHidden(data.IsHidden);
            step.OnLoad();
            if (step.Check == null) {
                // Failed to deserialize
                return null;
            }
            // After OnLoad: creating the check sets the deadline from the quest variable,
            // which overwrote the saved one and gave completed steps (saved with 0) their
            // deadline back, so they could fail after the load
            step.Deadline = data.Deadline;
            // must be after OnLoad() or check will be null
            if (step.SaveLoadTexts) {
                step.Title = data.Title;
                step.Desc = data.Desc;
            }
            return step;
        }
    }
}
