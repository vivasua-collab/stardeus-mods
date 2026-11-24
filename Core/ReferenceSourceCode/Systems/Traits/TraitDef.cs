using System;
using System.Collections.Generic;
using Game.Components;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.Mental;
using Game.Systems.Stats;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.Constants;
using Game.Systems.Creatures;
using System.Linq;

namespace Game.Systems.Traits {
    public sealed class TraitDef : IAbilitySourceItem, INeedSourceItem {
        public static readonly Dictionary<int, TraitDef> All = new();
        public static readonly Dictionary<TraitType, List<TraitDef>> ByType = new();

        [SelfInit]
        private static void Init() {
            All.Clear();
            ByType.Clear();
        }

        public static void LoadKnown() {
            if (All.Count != 0) { return; }
            D.Warn("Loading trait defs...");
            TraitType.LoadKnown();
            var types = The.ModLoader.LoadObjects<TraitDef>("Config/Traits/Defs");
            foreach (var def in types) {
                def.IdH = Hashes.S(def.Id);
                var tId = Strings.ToSnakeCase(def.Id);
                def.NameT = $"trait.{tId}".T();
                var descKey = $"trait.{tId}.about";
                if (descKey.HasT()) {
                    def.DescT = descKey.T();
                }
                if (def.TypeId == null) {
                    D.Err("TypeId is not defined for trait: {0}", def.Id);
                } else {
                    def.Type = TraitType.Get(def.TypeId);
                }
                if (def.Type == null) {
                    D.Err("TraitType is not found by TypeId: {0} for trait: {1}",
                        def.TypeId, def.Id);
                    continue;
                }
                if (!All.TryAdd(def.IdH, def)) {
                    D.Err("Cannot add trait: {0}. Already added!", def.Id);
                }
                def.StatSources = StatSourceData.Deserialize(def.ProvidesStats, def);
                def.StatModifiers = StatModifier.Deserialize(def.AddsStatModifiers, def.NameT, def);
                JobType.ParseJobs(def, def.IncapableJobTypes, true, ref def.IncapableJobs);
                JobType.ParseJobs(def, def.LovesJobTypes, false, ref def.LovesJobs);
                if (def.AdjustsSkills != null) {
                    for (int i = def.AdjustsSkills.Length - 1; i >= 0; i--) {
                        var adj = def.AdjustsSkills[i];
                        def.AdjustsSkills[i] = SkillAdjustment.Deserialize(adj, def);
                    }
                }
                def.Flags = Enums.FlagsFromStringArray_Byte<TraitFlags>(
                    def.TraitFlagIds, def);
                if (def.Rarity == 0f) {
                    D.Err("Rarity not defined for trait: {0}. Falling back to 0.5", def.Id);
                    def.Rarity = 0.5f;
                }
                if (!ByType.TryGetValue(def.Type, out var byType)) {
                    byType = new List<TraitDef>();
                    ByType[def.Type] = byType;
                }
                if (!Arrays.IsEmpty(def.Modifiers)) {
                    def.BeingModifiers = new BeingModifier[def.Modifiers.Length];
                    bool hasErrors = false;
                    for (int i = 0; i < def.Modifiers.Length; i++) {
                        var id = def.Modifiers[i];
                        var mod = Registries.BeingModifiers.Get(id);
                        if (mod == null) {
                            D.Err("Trait {0} - Modifier not found by id: {1}", def.Id, id);
                            hasErrors = true;
                        } else {
                            def.BeingModifiers[i] = mod;
                        }
                    }
                    if (hasErrors) {
                        // remove null entries
                        def.BeingModifiers = def.BeingModifiers.Where(m => m != null)
                            .ToArray();
                        if (def.BeingModifiers.Length == 0) {
                            def.BeingModifiers = null;
                        }
                    }
                }
                byType.Add(def);
            }
            // link incompatible
            foreach (var tr in All.Values) {
                if (tr.IncompatibleTraitIds != null) {
                    tr.IncompatibleTraits = new TraitDef[tr.IncompatibleTraitIds.Length];
                    for (int i = tr.IncompatibleTraitIds.Length - 1; i >= 0; i--) {
                        var incTr = tr.IncompatibleTraitIds[i];
                        var trait = Get(incTr);
                        if (trait == null) {
                            D.Err("Incorrect id {0} in incompatible traits of trait: {1}",
                                incTr, tr.Id);
                        }
                        tr.IncompatibleTraits[i] = trait;
                    }
                }
            }
            SpeciesTraitType.LoadKnown();
        }
        public string Tooltip(Jobs jobs) {
            var sb = Caches.StringBuilder;
            sb.Clear();
            sb.Append(Texts.SubTitle(T.NounTrait));
            sb.Append(Texts.Accented(NameT));
            if (DescT != null) {
                sb.AppendLine();
                sb.Append(DescT);
            }
            if (LovesJobs != null) {
                JobType.AppendJobList(sb, LovesJobs, jobs, true, T.VerbLoves);
            }
            if (IncapableJobs != null) {
                JobType.AppendJobList(sb, IncapableJobs, jobs, false, T.JobIncapableOf);
            }
            if (AdjustsSkills != null) {
                foreach (var adj in AdjustsSkills) {
                    adj.AddTooltip(sb, jobs);
                }
            }
            if (StatModifiers != null) {
                var stats = jobs.Owner.Owner.StatsBeing;
                foreach (var mod in StatModifiers) {
                    if (stats.Find(mod.Item1, out var stat)) {
                        mod.Item2.AppendInfo(sb, stat, true,
                            Stat.NameFor(stat));
                    }
                }
            }
            if (BeingModifiers != null) {
                foreach (var mod in BeingModifiers) {
                    sb.AppendLine();
                    sb.Append(mod.Tooltip);
                }
            }
            return sb.ToString();
        }

        public static List<TraitDef> ForType(TraitType type) {
            if (ByType.TryGetValue(type, out var list)) {
                return list;
            }
            D.Err("There are no traits available for type: {0}", type);
            return null;
        }

        public static TraitDef Get(string type) {
            if (All.Count == 0) {
                LoadKnown();
            }
            return Get(type, Hashes.S(type));
        }

        public static TraitDef Get(string type, int typeH) {
            if (All.TryGetValue(typeH, out var part)) {
                return part;
            }
            D.Err("TraitDef with id {0} (hash {1}) is not defined! See Config/Traits/Defs folder",
                    type, typeH);
            return null;
        }
        public static TraitDef Get(int type) {
            if (All.TryGetValue(type, out var part)) {
                return part;
            }
            D.Err("TraitDef with idHash {0} is not defined! See Config/Traits/Defs folder",
                    type);
            return null;
        }
        // A mood whose value a trait decides names the trait: "No Shower +5" on an Ablutophobic
        // colonist read like a bug, and the trait behind it stayed invisible. Not for sleep
        // moods: SleepMoods and MentalHealth.AddEffect match timed moods by exact reason text.
        public static string MoodReason(string reason, int traitIdH) {
            if (!All.TryGetValue(traitIdH, out var trait)) {
                return reason;
            }
            return $"{reason} ({trait.NameT})";
        }

        public string NameT;
        public string DescT;
        public string Id;
        public int IdH;
        public string IconId;
        public TraitFlags Flags;
        public TraitType Type;
        public string TypeId;
        public string BodyTypes;
        public string[] CompatibleSpecies;
        public string CompatibleSex;
        public string[] AddsAbilities;
        public string[] AddsNeeds;
        [JsonEnum(typeof(Game.Systems.Traits.TraitFlags))]
        public string[] TraitFlagIds;
        public TraitDef[] IncompatibleTraits;
        public JobType[] LovesJobs;
        public JobType[] IncapableJobs;
        /// <summary>
        /// We'll first try to find actual job by id, then if it is not found,
        /// try to find the category of jobs that are disliked
        /// </summary>
        public string[] IncapableJobTypes;
        /// <summary>
        /// We'll first try to find actual job by id, then if it is not found,
        /// try to find the category of jobs that are disliked
        /// </summary>
        public string[] LovesJobTypes;
        /// <summary>
        /// 0.0001 to 1.0. Closer to 1 is more common.
        /// </summary>
        public float Rarity;
        public string[] IncompatibleTraitIds;
        /// <summary>
        /// item1 is stat id hash
        /// </summary>
        public (int, StatModifier)[] StatModifiers;
        public ComponentConfig[] ProvidesStats;
        public StatSourceData[] StatSources;
        public ComponentConfig[] AddsStatModifiers;
        public ComponentConfig[] AddsAttackMethods;
        public SkillAdjustment[] AdjustsSkills;
        public string[] Modifiers;
        public BeingModifier[] BeingModifiers;
        public string ItemName => NameT;
        public bool IsRoleOnly => (Flags & TraitFlags.RoleOnly) == TraitFlags.RoleOnly;

        public bool GetStatBaseFor(Stat stat, out float toAdd, out float limit) {
            for (int i = StatSources.Length - 1; i >= 0; i--) {
                StatSourceData src = StatSources[i];
                if (src.StatId == stat.Id) {
                    if (src.StateMul > 0f)  {
                        D.Err("Trait stat modifiers cannot have StateMultiplier! Trait: {0}. Stat: {1}",
                            Id, stat);
                    }
                    toAdd = src.Amount;
                    limit = src.CompoundLimit;
                    return true;
                }
            }
            D.Err("Could not get stat base from body part {0} for stat {1}", Id, stat);
            toAdd = 0f;
            limit = 0f;
            return false;
        }
        public override string ToString() {
            return $"TraitDef[{Id}]";
        }

        public bool IsAvailableForBodyType(char bodyType) {
            return BodyTypes == null || bodyType == 'N'
                || BodyTypes.Contains(bodyType);
        }

        public bool IsAvailableForSpecies(Species species) {
            if (CompatibleSpecies == null) { return true; }
            return Arrays.Contains(CompatibleSpecies, species.Id);
        }

        public bool IsAvailableForSex(BeingSex sex) {
            if (CompatibleSex == null) { return true; }
            if (sex == null) { return false; }
            return sex.Type == CompatibleSex;
        }
    }
}
