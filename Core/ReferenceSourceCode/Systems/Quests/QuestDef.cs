using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Mods;
using Game.Utils;
using KL.Utils;
using Newtonsoft.Json;

namespace Game.Systems.Quests {
    public sealed class QuestDef {
        [SelfInit]
        public static void Init() {
            byId.Clear();
            All.Clear();
            allBoundTutorialIdHs.Clear();
        }
        private static readonly Dictionary<int, QuestDef> byId = new();
        public static readonly List<QuestDef> All = new();
        /// <summary>
        /// Union of every hashed tutorial id (BoundTutorialIds ∪ OnStartTutorialId) across all
        /// quest defs. Used for a fast O(1) check: is this tutorial popup bound to any quest?
        /// </summary>
        private static readonly HashSet<int> allBoundTutorialIdHs = new();
        /// <summary>Returns true when the given codex-page id hash is bound to at least one quest.</summary>
        public static bool IsBoundTutorial(int idH) => allBoundTutorialIdHs.Contains(idH);
        public static QuestDef FastGet(int id, bool logErr = true) {
            if (byId.TryGetValue(id, out var quest)) {
                return quest;
            }
            if (logErr) {
                D.Err("QuestDef not found by id: {0}", QuestIdH.ResolveName(id));
            }
            return null;
        }
        public static QuestDef Get(string id, bool required = false, object errorCtx = null) {
            if (string.IsNullOrWhiteSpace(id)) {
                if (required) {
                    D.Throw("Quest is required! {0}", errorCtx);
                }
                return null;
            }
            var q = FastGet(Hashes.S(id), false);
            if (q == null) {
                D.Err("QuestDef not found by id: {0}", id);
                D.Throw("QuestDef not found by id: {0} | {1}",
                    id, errorCtx);
            }
            return q;
        }
        public string Id;
        /// <summary>
        /// Used in JSON for linking quest providers with types.
        /// Could be a free form string, like ShipRace, etc.
        /// </summary>
        public string Type;
        [JsonEnum(typeof(Game.Systems.Quests.QuestProviderType))]
        public string[] QuestProviders;
        public QuestProviderType Providers;
        public int IdH;
        [JsonEnum(typeof(Game.Systems.Quests.QuestFlags))]
        public string[] QuestFlags;
        public float Weight = 1f;
        /// <summary>
        /// If it's null - any faction can give this, otherwise only listed
        /// factions can give this quest.
        /// </summary>
        public string[] CompatibleFactions;
        public HashSet<FactionDef> Factions;
        public string TitleKey;
        public string DescKey;
        public string[] TitleKeys;
        public string[] DescKeys;
        public string TitleT;
        public string DescT;
        public QuestFlags Flags;
        public Situation Require;
        public bool IsMain;
        public bool IsTutorial;
        /// <summary>
        /// Template quests will have dynamic variables that can be filled out when the quest gets created
        /// </summary>
        public bool IsTemplate;
        /// <summary>
        /// Unique quests can only be added to player's quests once per game
        /// </summary>
        public bool IsUnique;
        /// <summary>
        /// When these global quest variables are fulfilled, quest def becomes available
        /// </summary>
        [JsonConverter(typeof(QuestBoolsJsonConverter))]
        public QuestBools AvailableWhen;
        /// <summary>
        /// When these quest variables are fulfilled, quest is completed
        /// </summary>
        [JsonConverter(typeof(QuestBoolsJsonConverter))]
        public QuestBools CompleteWhen;
        /// <summary>
        /// If these global quest variables are fulfilled, quest fails
        /// </summary>
        [JsonConverter(typeof(QuestBoolsJsonConverter))]
        public QuestBools FailWhen;
        /// <summary>
        /// For performance reasons, if global blackboard contains any of these variables,
        /// the quest def will be permanently removed from the pool
        /// </summary>
        [JsonConverter(typeof(QuestBoolsJsonConverter))]
        public QuestBools RemoveIf;
        /// <summary>
        /// When quest completes, these global blackboard variables will be set
        /// </summary>
        [JsonConverter(typeof(QuestBoolsJsonConverter))]
        public QuestBools SetOnComplete;
        [JsonConverter(typeof(QuestBoolsJsonConverter))]
        public QuestBools SetOnActive;
        /// <summary>
        /// When quest fails, these global blackboard variables will be set
        /// </summary>
        [JsonConverter(typeof(QuestBoolsJsonConverter))]
        public QuestBools SetOnFail;
        /// <summary>
        /// Quest rewards triggered when quest completes.
        /// There are separate / additional step rewards too.
        /// </summary>
        public QuestRewardDef[] Rewards;
        /// <summary>
        /// Dynamic variables that can be used by steps to change their
        /// states, etc.
        /// WARNING: Must be defined in order if they depend on each other
        /// </summary>
        public QuestVarDef[] Variables;
        /// <summary>
        /// Definitions of the steps - everything that has to be done to complete this quest
        /// </summary>
        public QuestStepDef[] Steps;
        /// <summary>
        /// If not blank, only listed scenarios will have this quest. Other scenarios
        /// will set this quest vars as completed right away
        /// </summary>
        public string[] ForScenarios;

        public string[] QuestValidators;
        public IQuestValidator[] Validators;

        // --- Tutorial binding ---
        /// <summary>
        /// Codex concept page ids (tutorial ids) whose popup countdowns are gated on this quest
        /// being the player's tracked quest. Any id listed here (or in OnStartTutorialId) will
        /// not be counted down unless this quest is tracked.
        /// </summary>
        public string[] BoundTutorialIds;
        /// <summary>
        /// Codex concept page id for a tutorial popup that fires automatically when this quest
        /// becomes the tracked quest. Implicitly treated as bound (same gating as BoundTutorialIds).
        /// </summary>
        public string OnStartTutorialId;
        /// <summary>Realtime seconds to delay the OnStartTutorialId popup after tracking begins.</summary>
        public float OnStartTutorialDelay;

        // Precomputed (not serialized) — populated by LoadOne:
        /// <summary>Hashes.S(OnStartTutorialId), or 0 if not set.</summary>
        public int OnStartTutorialIdH;
        /// <summary>
        /// O(1) set of hashed tutorial ids bound to this quest:
        /// BoundTutorialIds ∪ {OnStartTutorialIdH}. Null when the quest binds no tutorials.
        /// </summary>
        public HashSet<int> BoundTutorialIdHSet;

        public static void LoadKnown() {
            if (All.Count != 0) { return; }
            var quests = The.ModLoader.LoadObjects<QuestDef>("Config/Quests/Quests");
            StringBuilder err = Caches.StringBuilder4;
            err.Clear();
            bool hadErrors = false;
            foreach (var q in quests) {
                if (!LoadOne(q, err)) {
                    hadErrors = true;
                }
            }
            if (hadErrors) {
                D.Err(err.ToString());
                err.Insert(0, "<size=80%>");
                err.Append("</size>");
                ModRegistry.WarnAboutBrokenMod(typeof(QuestDef), err.ToString());
            }
            ValidateChain();
        }

        /// <summary>
        /// Structural validation, separate from the deserialize-error check above: logs every
        /// dead-end bool reference found by <see cref="FindDeadEnds"/>. Unlike a deserialize
        /// failure, a dead end doesn't break loading - the quest just becomes permanently
        /// unreachable - so this is reported as its own D.Err rather than folded into the
        /// broken-mod warning above.
        /// </summary>
        private static void ValidateChain() {
            var deadEnds = FindDeadEnds();
            if (deadEnds.Count == 0) { return; }
            var sb = new StringBuilder();
            foreach (var (def, b) in deadEnds) {
                sb.AppendFormat(
                    "{0}: AvailableWhen references '{1}', which no quest ever sets and is not a known code-set bool (QuestBoolIds)\n",
                    def.Id, b);
            }
            D.Err("Quest chain validation found {0} dead-end bool reference(s):\n{1}",
                deadEnds.Count, sb);
        }

        /// <summary>
        /// Finds every AutoAdd quest whose AvailableWhen names a bool that no quest's
        /// SetOnComplete/SetOnFail ever sets and that isn't one of the known code-set bools
        /// (<see cref="QuestBoolIds"/>, set directly via SetQuestBool from scenario-intro
        /// cutscenes / space-object actions). Such a bool can never become true in any save - a
        /// true dead end, almost always a content/authoring bug (typo, or a producer quest was
        /// renamed/removed).
        /// </summary>
        public static List<(QuestDef Def, string Bool)> FindDeadEnds() {
            var producible = new HashSet<string>(
                typeof(QuestBoolIds).GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Where(f => f.IsLiteral)
                    .Select(f => (string) f.GetRawConstantValue()));
            foreach (var d in All) {
                if (d.SetOnComplete?.Names != null) { producible.UnionWith(d.SetOnComplete.Names); }
                if (d.SetOnFail?.Names != null) { producible.UnionWith(d.SetOnFail.Names); }
            }
            var result = new List<(QuestDef, string)>();
            foreach (var d in All) {
                if (!d.Flags.HasFlag(Quests.QuestFlags.AutoAdd)) { continue; }
                if (d.AvailableWhen?.Names == null) { continue; }
                foreach (var need in d.AvailableWhen.Names) {
                    if (!producible.Contains(need)) { result.Add((d, need)); }
                }
            }
            return result;
        }

        private static bool LoadOne(QuestDef def, StringBuilder err) {
            if (Arrays.IsEmpty(def.QuestFlags)) {
                LogErr(err, def, "Missing field: QuestFlags");
                return false;
            } else {
                def.Flags = Enums.FlagsFromStringArray_Int<QuestFlags>(def.QuestFlags, def.Id);
            }
            if (!TryParseQuestProviders(def, err)) {
                return false;
            }
            if (!TryValidateTextVariants(def, err)) {
                return false;
            }
            if (!string.IsNullOrWhiteSpace(def.TitleKey)) {
                def.TitleT = def.TitleKey.T();
            }
            if (!string.IsNullOrWhiteSpace(def.DescKey)) {
                def.DescT = def.DescKey.T();
            }
            if (!Arrays.IsEmpty(def.CompatibleFactions)) {
                def.Factions = new HashSet<FactionDef>(def.CompatibleFactions.Length);
                foreach (var id in def.CompatibleFactions) {
                    var fac = FactionDef.Get(id);
                    if (fac == null) {
                        LogErr(err, def, $"Faction not found by id: {id}");
                    } else {
                        def.Factions.Add(fac);
                    }
                }
            }
            def.IsMain = def.Flags.HasFlag(Quests.QuestFlags.Main);
            def.IsTutorial = def.Flags.HasFlag(Quests.QuestFlags.Tutorial);
            def.IsTemplate = def.Flags.HasFlag(Quests.QuestFlags.Template);
            def.IsUnique = def.Flags.HasFlag(Quests.QuestFlags.Unique);
            bool hadDeserializeErrors = false;
            if (!Arrays.IsEmpty(def.QuestValidators)) {
                def.Validators = new IQuestValidator[def.QuestValidators.Length];
                for (int i = def.QuestValidators.Length - 1; i >= 0; i--) {
                    string id = def.QuestValidators[i];
                    var val = Registries.QuestValidators.Get(id);
                    if (val == null) {
                        err.AppendFormat("QuestValidator not found by id {0}", id);
                        hadDeserializeErrors = true;
                    }
                    def.Validators[i] = val;
                }
            }
            if (!Arrays.IsEmpty(def.Rewards)) {
                foreach (var r in def.Rewards) {
                    if (!r.Deserialize(err, def.Id)) {
                        hadDeserializeErrors = true;
                    }
                }
            }
            if (!Arrays.IsEmpty(def.Variables)) {
                foreach (var v in def.Variables) {
                    if (!v.Deserialize(err, def.Id)) {
                        hadDeserializeErrors = true;
                    }
                }
            }
            if (Arrays.IsEmpty(def.Steps)) {
                LogErr(err, def, "Missing Steps");
                return false;
            }
            foreach (var c in def.Steps) {
                if (!c.Deserialize(err, def)) {
                    hadDeserializeErrors = true;
                }
            }
            if (hadDeserializeErrors) { return false; }

            def.IdH = Hashes.S(def.Id);
            if (byId.TryAdd(def.IdH, def)) {
                All.Add(def);
            } else {
                LogErr(err, def,
                    string.Format("QuestDef with ID {0} already exists", def.Id));
                return false;
            }

            // Build tutorial-binding accelerators.
            if (!string.IsNullOrEmpty(def.OnStartTutorialId)) {
                def.OnStartTutorialIdH = Hashes.S(def.OnStartTutorialId);
            }
            if (!Arrays.IsEmpty(def.BoundTutorialIds) || def.OnStartTutorialIdH != 0) {
                def.BoundTutorialIdHSet = new HashSet<int>();
                if (!Arrays.IsEmpty(def.BoundTutorialIds)) {
                    foreach (var tid in def.BoundTutorialIds) {
                        if (!string.IsNullOrEmpty(tid)) {
                            def.BoundTutorialIdHSet.Add(Hashes.S(tid));
                        }
                    }
                }
                if (def.OnStartTutorialIdH != 0) {
                    def.BoundTutorialIdHSet.Add(def.OnStartTutorialIdH);
                }
                allBoundTutorialIdHs.UnionWith(def.BoundTutorialIdHSet);
            }

            // D.Warn("Loaded quest def: {0}", def);
            return true;
        }

        public string GetTitleT(int variantIndex) {
            if (variantIndex >= 0 && !Arrays.IsEmpty(TitleKeys)) {
                if (variantIndex < TitleKeys.Length) {
                    return TitleKeys[variantIndex].T();
                }
                D.Err("Text variant index out of range for title: {0} | {1}", variantIndex, this);
            }
            return TitleT;
        }

        public string GetDescT(int variantIndex) {
            if (variantIndex >= 0 && !Arrays.IsEmpty(DescKeys)) {
                if (variantIndex < DescKeys.Length) {
                    return DescKeys[variantIndex].T();
                }
                D.Err("Text variant index out of range for desc: {0} | {1}", variantIndex, this);
            }
            return DescT;
        }

        public string GetTitleKey(int variantIndex) {
            if (variantIndex >= 0 && TitleKeys != null && variantIndex < TitleKeys.Length) {
                return TitleKeys[variantIndex];
            }
            return TitleKey;
        }

        public string GetDescKey(int variantIndex) {
            if (variantIndex >= 0 && DescKeys != null && variantIndex < DescKeys.Length) {
                return DescKeys[variantIndex];
            }
            return DescKey;
        }

        private static bool TryValidateTextVariants(QuestDef def, StringBuilder err) {
            var hasTitleKeys = !Arrays.IsEmpty(def.TitleKeys);
            var hasDescKeys = !Arrays.IsEmpty(def.DescKeys);
            if (hasTitleKeys != hasDescKeys) {
                LogErr(err, def, "TitleKeys and DescKeys must be configured together");
                return false;
            }
            if (hasTitleKeys && def.TitleKeys.Length != def.DescKeys.Length) {
                LogErr(err, def,
                    $"TitleKeys and DescKeys length mismatch: {def.TitleKeys.Length} != {def.DescKeys.Length}");
                return false;
            }
            return true;
        }

        private static bool TryParseQuestProviders(QuestDef def, StringBuilder err) {
            if (Arrays.IsEmpty(def.QuestProviders)) {
                LogErr(err, def, "Missing field: QuestProviders");
                return false;
            }

            bool hadErrors = false;
            def.Providers = QuestProviderType.None;
            foreach (var id in def.QuestProviders) {
                if (Enums.TryFromString<QuestProviderType>(id, out var provider)) {
                    def.Providers |= provider;
                } else {
                    LogErr(err, def, $"QuestProviderType not found by id: {id}");
                    hadErrors = true;
                }
            }
            return !hadErrors;
        }

        private static void LogErr(StringBuilder sb, QuestDef def, string err) {
            sb.AppendFormat("{0}: {1}\n", def.Id, err);
        }

        public override string ToString() {
            return $"QuestDef[{Id}|{Flags}]";
        }
    }
}
