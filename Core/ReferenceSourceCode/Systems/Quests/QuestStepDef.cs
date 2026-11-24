using System;
using System.Collections.Generic;
using System.Security.Policy;
using System.Text;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Story;
using Game.Utils;
using KL.Utils;
using Newtonsoft.Json;

namespace Game.Systems.Quests {
    [Serializable]
    public sealed class QuestStepDef {
        public string Id;
        /// <summary>
        /// C# implementation
        /// </summary>
        public string Type;
        private BaseQuestStep.Factory factory;
        public string TitleKey;
        public string[] TitleKeys;
        /// <summary>
        /// Some quest steps will set it on their own, but it can be overridden.
        /// </summary>
        public string DescKey;
        public string[] DescKeys;
        public string Deadline;
        /// <summary>
        /// Goes in addition to Desc, which has to be set by the step.
        /// </summary>
        public string TooltipKey;
        [JsonEnum(typeof(Game.Systems.Quests.QuestStepFlags))]
        public string[] StepFlags;
        public QuestStepFlags Flags;
        /// <summary>
        /// In other scenarios the step is created completed and hidden. Unlike the quest-level
        /// field it never touches global bools, only this quest's SetOnComplete.
        /// </summary>
        public string[] ForScenarios;
        [JsonConverter(typeof(QuestStepStateStringDictJsonConverter))]
        public Dictionary<QuestStepState, string> StateStoryEvents;
        /// <summary>
        /// When these quest variables are fulfilled, step becomes active
        /// </summary>
        [JsonConverter(typeof(QuestBoolsJsonConverter))]
        public QuestBools ActivateWhen;
        /// <summary>
        /// When these quest variables are fulfilled, step completed
        /// </summary>
        [JsonConverter(typeof(QuestBoolsJsonConverter))]
        public QuestBools CompleteWhen;
        /// <summary>
        /// If these quest variables are fulfilled, steps fails
        /// </summary>
        [JsonConverter(typeof(QuestBoolsJsonConverter))]
        public QuestBools FailWhen;
        /// <summary>
        /// For cascading pre-check completions
        /// </summary>
        [JsonConverter(typeof(QuestBoolsJsonConverter))]
        public QuestBools CompleteAndHideWhen;
        [JsonConverter(typeof(QuestBoolsJsonConverter))]
        public QuestBools RemoveDeadlineWhen;
        [JsonConverter(typeof(QuestBoolsJsonConverter))]
        public QuestBools SetOnComplete;
        [JsonConverter(typeof(QuestBoolsJsonConverter))]
        public QuestBools SetOnHide;
        /// <summary>
        /// When quest fails, these global blackboard variables will be set
        /// </summary>
        [JsonConverter(typeof(QuestBoolsJsonConverter))]
        public QuestBools SetOnFail;
        /// <summary>
        /// Configuration that can be used by the step implementation
        /// </summary>
        [JsonConverter(typeof(ComponentConfigJsonConverter))]
        public ComponentConfig Config;
        public bool Deserialize(StringBuilder err, object context) {
            if (string.IsNullOrWhiteSpace(Id)) {
                err.AppendFormat("{0}: {1}: field missing: Id\n", context, this);
                return false;
            }
            if (string.IsNullOrWhiteSpace(Type)) {
                err.AppendFormat("{0}: {1}: field missing: Type\n", context, this);
                return false;
            }
            if (!ValidateTextVariants(err, context)) {
                return false;
            }
            Flags = Enums.FlagsFromStringArray_Int<QuestStepFlags>(StepFlags, this);
            factory = Registries.QuestSteps.Get(Type);
            if (factory == null) {
                err.AppendFormat("Couldn't find QuestStep implementation by id {0} for step: {1}",
                    Type, context);
                return false;
            }
            return true;
        }

        public override string ToString() {
            return $"QuestStepDef[{Id}|{Type}|{Flags}]";
        }

        public string GetTitleKey(QuestData data) {
            return GetTextKey(TitleKeys, TitleKey, data, "TitleKeys");
        }

        public string GetDescKey(QuestData data) {
            return GetTextKey(DescKeys, DescKey, data, "DescKeys");
        }

        private string GetTextKey(string[] keys, string fallback, QuestData data,
                string fieldName) {
            var idx = data.TextVariantIndex;
            if (idx >= 0 && !Arrays.IsEmpty(keys)) {
                if (idx < keys.Length) {
                    return keys[idx];
                }
                D.Err("{0} index out of range: {1} / {2} | {3}",
                    fieldName, idx, keys.Length, this);
            }
            return fallback;
        }

        private bool ValidateTextVariants(StringBuilder err, object context) {
            var hasTitleKeys = !Arrays.IsEmpty(TitleKeys);
            var hasDescKeys = !Arrays.IsEmpty(DescKeys);
            if (hasTitleKeys && hasDescKeys && TitleKeys.Length != DescKeys.Length) {
                err.AppendFormat("{0}: {1}: TitleKeys and DescKeys length mismatch: {2} != {3}\n",
                    context, this, TitleKeys.Length, DescKeys.Length);
                return false;
            }
            if (context is QuestDef questDef && !Arrays.IsEmpty(questDef.TitleKeys)) {
                var variantCount = questDef.TitleKeys.Length;
                if (hasTitleKeys && TitleKeys.Length < variantCount) {
                    err.AppendFormat("{0}: {1}: TitleKeys has {2} entries, but quest has {3} variants\n",
                        context, this, TitleKeys.Length, variantCount);
                    return false;
                }
                if (hasDescKeys && DescKeys.Length < variantCount) {
                    err.AppendFormat("{0}: {1}: DescKeys has {2} entries, but quest has {3} variants\n",
                        context, this, DescKeys.Length, variantCount);
                    return false;
                }
            }
            return true;
        }

        public BaseQuestStep CreateCheck(GameState s, QuestStep item, bool isLoading) {
            try {
                return factory.Invoke(s, item, isLoading);
            } catch (Exception ex) {
                D.LogEx(ex, "Failed creating BaseQuestStep: {0}. Loading: {1}",
                    this, isLoading);
                return null;
            }
        }
        public string DataKey(string k) {
            return $"v.{Id}.{k}";
        }

        public void OnStateChange(GameState s, QuestStepState after, QuestStep step) {
            if (StateStoryEvents == null) { return; }
            var rng = step.SeededRng;
            if (StateStoryEvents.TryGetValue(after, out var evId)) {
                if (evId.Contains(',')) {
                    var evIds = evId.Split(',');
                    evId = rng.From(evIds);
                }
                TryStartEvent(s, step, evId);
            }
        }

        private void TryStartEvent(GameState s, QuestStep step, string evId) {
            var def = StoryEventDef.Get(evId);
            if (def == null) {
                D.Err("Missing story event by id: {0} for quest step def: {1} in quest step: {2}",
                    evId, this, step);
                return;
            }
            var evData = StoryEventData.CreateFor(def);
            evData.Seed = Hashes.Quick(step.SeededRng.Int, Primes.Prime037);
            if (!s.Query.StartStoryEvent.Ask(evData)) {
                // combat may be disabled, and that will fail to spawn raids
                if (A.IsCombatOn || !evData.Def.Flags.HasFlag(StoryEventFlags.Raid)) {
                    D.Err("Failed starting quest step state story event: {0} | {1}",
                        evData, step);
                } else {
                    D.Warn("Failed starting quest step state story event (combat disabled): {0} | {1}",
                        evData, step);
                }
            }
        }
    }
}
