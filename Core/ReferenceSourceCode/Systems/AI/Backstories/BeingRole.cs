using System.Buffers;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Systems.Traits;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using System.Linq;
using KL.Randomness;

namespace Game.Systems.Backstories {
    public sealed class BeingRole {
        public static readonly Dictionary<int, BeingRole> All = new();
        public int IdH;
        public RoleFlags Flags;
        public JobType[] LovesJobs;
        public JobType[] IncapableJobs;
        /// <summary>
        /// We'll first try to find actual job by id, then if it is not found,
        /// try to find the category of jobs that are disliked
        /// </summary>
        public string[] IncapableJobTypes;
        /// <summary>
        /// We'll first try to find actual job by id, then if it is not found,
        /// try to find the category of jobs that are disliked.
        /// </summary>
        public string[] LovesJobTypes;
        /// <summary>
        /// If this is null, it is equal chance for any sex.
        /// If only one item is defined, then the missing item is not possible.
        /// If species doesn't have sex (i.e. robot), this field will be ignored
        /// </summary>
        public SexChance[] SexChances;
        /// <summary>
        /// If this is left blank in json, all body types will be compatible.
        /// One character per body type.
        /// [M] - masculine (sex: male)
        /// [F] - feminine (sex: female)
        /// [T] - thin (sex: any)
        /// [L] - large (obese) (sex: any)
        /// In this case string "ML" would limit to masculine and large only.
        /// </summary>
        public string BodyTypes;
        public TraitWithChance[] AddsTraits;
        public SkillAdjustment[] AdjustsSkills;
        public TraitDef[] IncompatibleTraits;
        public string[] IncompatibleTraitIds;
        /// <summary>
        /// Smart / Dumb, Rich / Poor, Good / Evil / Once
        /// </summary>
        [JsonEnum(typeof(Game.Systems.Backstories.RoleFlags))]
        public string[] RoleTypes;
        /// <summary>
        /// Compatible age phases.
        /// </summary>
        public AgePhase AgePhases;
        public string[] Species;
        public string[] IncompatibleSpecies;
        [JsonEnum(typeof(Game.Systems.Backstories.AgePhase))]
        public string[] AgePhaseIds;
        /// <summary>
        /// If this is not null, the existing roles must contain ANY of these
        /// </summary>
        public string[] RequiresRoles;
        /// <summary>
        /// May contain roles that cannot lead to this one logically. I.e.
        /// Homeless -> Millionaire is bad, but Millionaire -> Homeless is OK.
        /// Millionaire should have NotAfterRoles = ["Homeless"]
        /// </summary>
        public string[] NotAfterRoles;
        public string Id;
        public string NameT;
        public float Rarity;
        public string[] Modifiers;
        public BeingModifier[] BeingModifiers;
        [SelfInit]
        private static void Init() {
            // We want them to be fresh in unity
            All.Clear();
        }


        public static void LoadKnown() {
            if (All.Count > 0) { return; }

            var items = The.ModLoader.LoadObjects<BeingRole>("Config/Roles");
            foreach (var item in items) {
                item.IdH = Hashes.S(item.Id);
                JobType.ParseJobs(item, item.IncapableJobTypes, true, ref item.IncapableJobs);
                JobType.ParseJobs(item, item.LovesJobTypes, false, ref item.LovesJobs);
                item.Flags = Enums.FlagsFromStringArray_Byte<RoleFlags>(
                    item.RoleTypes, item);
                var id = item.Id;
                item.NameT = $"role.{id}".T();
                item.AgePhases = Enums.FlagsFromStringArray_Byte<AgePhase>(
                    item.AgePhaseIds, item);
                if (item.AdjustsSkills != null) {
                    for (int i = item.AdjustsSkills.Length - 1; i >= 0; i--) {
                        var adj = item.AdjustsSkills[i];
                        item.AdjustsSkills[i] = SkillAdjustment.Deserialize(adj, item);
                    }
                }
                if (!All.TryAdd(item.IdH, item)) {
                    D.Err("Adding duplicate BeingRole: {0}", item.Id);
                };

                if (item.Rarity == 0f) {
                    item.Rarity = 1f;
                }

                if (!Arrays.IsEmpty(item.Modifiers)) {
                    item.BeingModifiers = new BeingModifier[item.Modifiers.Length];
                    bool hasErrors = false;
                    for (int i = 0; i < item.Modifiers.Length; i++) {
                        var mId = item.Modifiers[i];
                        var mod = Registries.BeingModifiers.Get(mId);
                        if (mod == null) {
                            D.Err("BeingRole {0} - Modifier not found by id: {1}", item.Id, mId);
                            hasErrors = true;
                        } else {
                            item.BeingModifiers[i] = mod;
                        }
                    }
                    if (hasErrors) {
                        // remove null entries
                        item.BeingModifiers = item.BeingModifiers.Where(m => m != null)
                            .ToArray();
                        if (item.BeingModifiers.Length == 0) {
                            item.BeingModifiers = null;
                        }
                    }
                }
            }
            // validate prerequisites
            foreach (var item in All.Values) {
                if (!Arrays.IsEmpty(item.RequiresRoles)) {
                    foreach (var r in item.RequiresRoles) {
                        if (!All.ContainsKey(Hashes.S(r))) {
                            D.Err("Role {0} has non-existent id {1} in RequiredRoles list!",
                                item.Id, r);
                        }
                    }
                }
            }
            Ready.When(WhenH.CoreLoaded1, LinkTraits);
        }

        private static void LinkTraits() {
            foreach (var item in All.Values) {
                if (item.AddsTraits != null) {
                    for (int i = item.AddsTraits.Length - 1; i >= 0; i--) {
                        var tr = item.AddsTraits[i];
                        tr.Trait = TraitDef.Get(tr.TraitId);
                        if (tr.Trait == null) {
                            D.Err("Missing trait by id {0} for role: {1}",
                                tr.TraitId, item.Id);
                        }
                        item.AddsTraits[i] = tr;
                    }
                }
            }
            // link incompatible traits
            foreach (var item in All.Values) {
                if (item.IncompatibleTraitIds != null) {
                    item.IncompatibleTraits = new TraitDef[item.IncompatibleTraitIds.Length];
                    for (int i = item.IncompatibleTraitIds.Length - 1; i >= 0; i--) {
                        var incTr = item.IncompatibleTraitIds[i];
                        var trait = TraitDef.Get(incTr);
                        if (trait == null) {
                            D.Err("Incorrect id {0} in incompatible traits of being role: {1}",
                                incTr, item.Id);
                        }
                        item.IncompatibleTraits[i] = trait;
                    }
                }
            }
        }

        public static BeingRole FastGet(int id) {
            if (All.TryGetValue(id, out var item)) {
                return item;
            }
            return null;
        }
        public static  BeingRole Get(string id) {
            return Get(id, Hashes.S(id));
        }

        public static BeingRole Get(string id, int idH) {
            if (All.TryGetValue(idH, out var item)) {
                return item;
            }
            D.Err("BeingRole {0} ({1}) not found! See Config/Backstories folder",
                id, idH);
            return null;
        }


        public string RoleTooltip(AgePhase phase, Species species, Jobs jobs) {
            var sb = Caches.StringBuilder3;
            sb.Clear();
            // Translation is "Backstory: {0}"
            var subTit = T.PersonaLogBackstory_V1.Replace(": {0}", string.Empty);
            var phText = Texts.AgePhase(phase, species);
            if (phText != null) {
                subTit = $"{subTit} ({phText})";
            }
            sb.Append(Texts.SubTitle(subTit));
            sb.AppendFormat("{0}", Texts.Accented(NameT));
            if (LovesJobs != null) {
                JobType.AppendJobList(sb, LovesJobs, jobs, true,
                    T.VerbLoves);
            }
            if (IncapableJobs != null) {
                JobType.AppendJobList(sb, IncapableJobs, jobs, false,
                    T.JobIncapableOf);
            }
            if (AdjustsSkills != null) {
                foreach (var adj in AdjustsSkills) {
                    adj.AddTooltip(sb, jobs);
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

        public static string PhaseIcon(AgePhase phase) {
            switch (phase) {
                case AgePhase.Childhood: return IconId.CAge1;
                case AgePhase.YoungAdult: return IconId.CAge2;
                case AgePhase.MidLife: return IconId.CAge3;
                case AgePhase.Retirement: return IconId.CAge4;
                default: return IconId.CTime;
            }
        }

        public void ApplyModifiersAfterGenBeing(GameState s, BeingData data,
                Rng rng, GenBeingVars vars) {
            if (BeingModifiers == null) { return; }
            foreach (var mod in BeingModifiers) {
                mod.AfterGenerateBeing(s, data, rng, vars);
            }
        }
        public void ApplyModifiersAfterLoadBeing(GameState s, BeingData data,
                bool isLoading) {
            if (BeingModifiers == null) { return; }
            foreach (var mod in BeingModifiers) {
                mod.AfterLoadBeing(s, data, isLoading);
            }
        }

        public override string ToString() {
            return $"BeingRole[{Id}]";
        }
    }
}
