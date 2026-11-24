using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    public abstract class BaseQuestStep {
        public delegate BaseQuestStep Factory(GameState s, QuestStep check, bool isLoading);
        public readonly string Id;
        public readonly int IdH;
        protected readonly GameState S;
        protected readonly QuestStep step;
        /// <summary>
        /// Does not affect InactiveTitle/Desc - those shouldn't have variables
        /// </summary>
        public virtual bool SaveLoadTexts => false;
        protected ComponentConfig config => step.Def.Config;
        protected BaseQuestStep(string id, GameState s, QuestStep step, bool isLoading) {
            Id = id;
            IdH = Hashes.S(id);
            S = s;
            this.step = step;
            if (!isLoading || !SaveLoadTexts) {
                ResetDesc();
            }
            SetStepDeadline();
        }

        protected void ResetDesc() {
            if (!string.IsNullOrWhiteSpace(step.Def.TooltipKey)) {
                step.Desc = step.Def.TooltipKey.T();
            } else {
                step.Desc = null;
            }
        }

        public virtual void OnStateChange(QuestStepState before, QuestStepState after) { }
        public virtual void OnQuestStateChange(QuestState before, QuestState after) { }

        /// <summary>
        /// Executed after quest is removed from the active quests or when the step completes or fails.
        /// This should be used to cleanup any listeners, etc.
        /// </summary>
        public virtual void Cleanup() { }

        protected static void SetInactiveTexts(QuestStep step) {
            var cfg = step.Def.Config;
            if (cfg == null) { return; }
            var inactiveTitleKey = cfg.GetString(PropertyIdH.InactiveTitleKey, null);
            if (inactiveTitleKey != null) {
                step.TitleInactive = inactiveTitleKey.T();
            }
            var inactiveDescKey = cfg.GetString(PropertyIdH.InactiveDescKey, null);
            if (inactiveDescKey != null) {
                step.DescInactive = inactiveDescKey.T();
            }
        }

        protected QuestVar GetQuestVarByKey(int propIdH, bool isRequired = true) {
            var keyStr = step.Def.Config.GetString(propIdH, null);
            if (isRequired && string.IsNullOrWhiteSpace(keyStr)) {
                D.Throw("No QuestVar by key: {0} | {1} | {2}",
                    PropertyIdH.ResolveName(propIdH), step, step.Data);
            }
            return step.Data.GetQuestVar(keyStr);
        }

        protected T GetQuestVarValByKey<T>(int propIdH, bool isRequired = true) {
            var var = GetQuestVarByKey(propIdH, isRequired);
            if (var is IQuestVar<T> varT) {
                return varT.Value;
            }
            if (isRequired) {
                D.Throw("Required QuestVar was not found or is of wrong type! {0} (want type: {1}",
                    var, typeof(T));
            }
            return default;
        }
        private void SetStepDeadline() {
            if (step.Def.Deadline == null) { return; }
            step.Deadline = step.Data.GetQuestVar(step.Def.Deadline).GetValue<long>();
        }

        protected void SetDesc(string desc) {
            if (step.Desc != null) {
                desc = $"{step.Desc}{GlyphMap.BRHR}{desc}";
            }
            step.Desc = desc;
        }
        protected void CompleteStep() {
            step.SetState(QuestStepState.Completed);
        }

        protected void FailStep() {
            step.SetState(QuestStepState.Failed);
        }

        protected string DataKey(string key) {
            return step.Def.DataKey(key);
        }
        protected string GetTextFromVar(int keyVarH, bool required) {
            var msgKey = config.GetString(keyVarH, null);
            string msg = null;
            if (msgKey != null) {
                msg = msgKey.TOrNull();
            }
            if (msg == null && required) {
                D.Throw("Message is required for step, but translation unavailable: Key: {0} | {1}",
                    PropertyIdH.ResolveName(keyVarH), this);
            }
            return msg;
        }
        protected void PopulatePackageRewards(QuestPackage pack, QuestDef packageQuest) {
            bool populated = false;
            var packRewIds = config.GetStringSet(PropertyIdH.PackageRewardIds, null);
            if (packRewIds != null) {
                foreach (var id in packRewIds) {
                    var rew = step.Data.FindReward(id);
                    if (rew is IRewardPackage rpack) {
                        populated |= rpack.PopulatePackage(pack);
                    } else {
                        D.Err("Package reward not IRewardPackage! {0}", rew);
                    }
                }
            }
            if (packageQuest != null) {
                pack.AddQuestId = packageQuest.Id;
                populated = true;
            }
            if (!populated) {
                D.Err("No rewards were marked as IRewardPackage or population failed for quest step: {0} | {1}",
                    this, step);
                // fallback reward
                pack.Add(MatType.FastGet(MatIdH.PlatinumIngot), 3);
            }
        }
        public override string ToString() {
            return $"QuestStepCheck[{GetType().Name}|{step}]";
        }
    }
}
