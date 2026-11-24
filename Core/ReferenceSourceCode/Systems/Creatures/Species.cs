using System;
using System.Collections.Generic;
using System.Linq;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Mods;
using Game.Procgen.Names;
using Game.Systems.Backstories;
using Game.Systems.BodyParts;
using Game.Systems.Mental;
using Game.Systems.Stats;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.AI.Combat;
using System.Threading;

namespace Game.Systems.Creatures {
    [Serializable]
    public class Species {
        public static readonly List<Species> Pets = new();
        public static readonly Dictionary<int, Species> Known = new();
        private static readonly Dictionary<int, Species> byDef = new();

        [ExecuteOnReload]
        private static void ClearStatics() {
            // We want them to be fresh in unity
            defaultFunRepetition = null;
            Known.Clear();
            byDef.Clear();
            Pets.Clear();
        }

        [SelfInit(SelfInit.Stage.Finalize)]
        private static void Register() {
            LoadKnown();
        }

        private string name;
        public string Name => name ??= $"species.{Id}".T();
        public Def Def;

        private static void LoadKnown() {
            if (Known.Count != 0) { return; }

            D.Warn("Loading species...");

            var species = The.ModLoader.LoadObjects<Species>(
                "Config/Species/Types");
            foreach (var s in species) {
                //// D.Log("Registering known species: {0}", s);
                s.IdH = Hashes.S(s.Id);
                Known[s.IdH] = s;
                s.Size = Enums.FromString<BodySize>(s.BodySize, s.Id);
                s.StatFlags = Enums.FlagsFromStringArray_UInt<StatFlags>(
                    s.Flags, s);
                s.StatSources = StatSourceData.Deserialize(s.ProvidesStats, s);
                s.StatModifiers = StatModifier.Deserialize(s.AddsStatModifiers, s.Name, s);
                if (!Arrays.IsEmpty(s.AgePhases)) {
                    for (int i = s.AgePhases.Length - 1; i >= 0; i--) {
                        AgePhaseRange a = s.AgePhases[i];
                        if (Enums.TryFromString<AgePhase>(a.Phase, out var ph)) {
                            a.AgePhase = ph;
                            s.AgePhases[i] = a;
                        } else {
                            D.Err("Species contains invalid AgePhase: {0} -> {1}",
                                s.Id, a.Phase);
                        }
                    }
                }
                if (s.StatFlags.HasFlag(StatFlags.Biological)) {
                    if (s.BloodCapacityLiters < 0.0001f) {
                        D.Err("Species has no BloodCapacityLiters! {0}", s.Id);
                        s.BloodCapacityLiters = 1f;
                    }
                }
                if (s.CombatBehavior == null) {
                    s.DefaultCombatConfig = CombatUnitConfig.Fight;
                } else {
                    s.DefaultCombatConfig = Enums.FromString<CombatUnitConfig>(s.CombatBehavior);
                }
                if (s.CustomMindWeight == 0f) {
                    s.CustomMindWeight = 1f;
                }
                if (s.RaidTierWeights != null
                        && s.RaidTierWeights.Length != RaiderSpecies.MaxTier + 1) {
                    D.Err("Species {0} RaidTierWeights needs one weight per raid gear tier ({1}), has {2}",
                        s.Id, RaiderSpecies.MaxTier + 1, s.RaidTierWeights.Length);
                    s.RaidTierWeights = null;
                }
                s.ValidateMentalNeeds();
                s.LoadDef();
                if (s.IsPet) {
                    Pets.Add(s);
                }
                if (s.Sexes != null) {
                    foreach (var g in s.Sexes) {
                        g.Initialize(s);
                    }
                }
                if (s.SexualAttractions != null) {
                    foreach (var a in s.SexualAttractions) {
                        a.Initialize(s);
                    }
                }
            }
            if (byDef.Count < species.Count) {
                D.ErrSoft("Species By Def count too low! {0} vs {1}",
                    byDef.Count, species.Count);
            }
            Ready.When(WhenH.CoreLoaded1, LinkLooks);
            BodyPartDef.FinalizeKnown();
            SpeciesJobs.LoadKnown();
        }

        private static void LinkLooks() {
            foreach (var s in Known) {
                var sp = s.Value;
                sp.LooksDef = SpeciesLooksDef.Get(sp.Looks);
                if (sp.LooksDef == null) {
                    var err = string.Format("BeingLooksDef not found for species {0} -> Looks: {1}. See Config/BeingLooks folder",
                        s.Key, sp.Looks);
                    D.Err(err);
                    if (sp.Def != null) {
                        ModRegistry.WarnAboutBrokenMod(sp.Def, err);
                    }
                }
            }
        }

        private string skinColorPalette;
        public string SkinColorPalette => skinColorPalette ??= $"{Id}.Skin";
        private string hairColorPalette;
        public string HairColorPalette => hairColorPalette ??= $"{Id}.Hair";
        private string eyeColorPalette;
        public string EyeColorPalette => eyeColorPalette ??= $"{Id}.Eye";
        public bool HasSkinColor => Palette.HasGroup(SkinColorPalette);
        public bool HasHairColor => Palette.HasGroup(HairColorPalette);
        public bool HasEyeColor => Palette.HasGroup(EyeColorPalette);
        public SpeciesLooksDef LooksDef;
        public BodySize Size;
        public CombatUnitConfig DefaultCombatConfig;
        [JsonEnum(typeof(Game.Systems.BodyParts.BodySize))]
        public string BodySize;
        /// <summary>
        /// Overrides BodyPartType.Size for the listed slot types on this species only, keyed by
        /// slot type id. Slot size drives hit weighting, bleed, burn and raid protection, so a
        /// species whose limbs are tiny compared to the shared slot type (a Crawler's eight
        /// legs vs a Human's two) needs its own value or it would soak every hit in its limbs.
        /// </summary>
        public Dictionary<string, float> BodyPartSlotSizes;
        public float BloodCapacityLiters;
        public float BeingValueBase;
        public SpeciesTradeConfig Trade;
        public bool IsTradeable => Trade != null && !string.IsNullOrEmpty(Trade.Group);
        /// <summary>
        /// Combat strength without regarding ability to fly
        /// </summary>
        public float BaseCombatStrength;
        /// <summary>
        /// Draw weight when a raid rolls a bot at gear tier 0 / 1 / 2 (see RaiderSpecies).
        /// Null for species that never raid; humans are drawn by FactionDef.AI instead.
        /// </summary>
        public float[] RaidTierWeights;
        /// <summary>
        /// when custom minds are generated, this weight will be used
        /// </summary>
        public float CustomMindWeight;

        private static Curve LoadFunRepetitionCurve(string curveId, string ctx) {
            if (string.IsNullOrWhiteSpace(curveId)) {
                return DefaultFunRepetition;
            }
            if (!CurveDef.TryGet(curveId, out var curve)) {
                D.Err("Species {0} has unknown FunRepetitionCurve: {1}", ctx, curveId);
                return curveId == CurveId.Beings_FunRepetition_Novelty ? null : DefaultFunRepetition;
            }
            var points = curve.Points;
            if (points != null && points.Length > 0
                    && points[points.Length - 1].x * 2f > FunMemory.MaxTrackedUsesPerSource) {
                D.Err("FunRepetitionCurve {0} ends at {1} uses, FunMemory.MaxTrackedUsesPerSource ({2}) must be at least twice that",
                    curveId, points[points.Length - 1].x, FunMemory.MaxTrackedUsesPerSource);
            }
            return curve;
        }

        public static Species Get(int idH) {
            if (Known.TryGetValue(idH, out var sp)) {
                return sp;
            }
            D.Err("Species {0} ({1}) is not defined! See Config/Species/Types folder",
                idH, SpeciesIdH.ResolveName(idH));
            return null;;
        }
        public static Species Get(string type) {
            if (type == null) {
                D.Err("Trying to get null species!");
                return null;
            }
            if (Known.TryGetValue(Hashes.S(type), out var sp)) {
                return sp;
            }
            D.Err("Species {0} is not defined! See Config/Species/Types folder", type);
            return null;
        }

        public string Id;
        public int IdH;
        public string Looks;
        public float LifeExpectancyYears;
        public float[] SpawnAgeCurve;

        // Non biological species will most likely be AI or robotic based
        public bool IsBiological => (StatFlags & StatFlags.Biological) == StatFlags.Biological;
        public bool IsPet => (StatFlags & StatFlags.Pet) == StatFlags.Pet;
        public bool IsAnimal => (StatFlags & StatFlags.Animal) == StatFlags.Animal;
        public bool IsHumanoid => (StatFlags & StatFlags.Humanoid) == StatFlags.Humanoid;
        public bool IsWild => (StatFlags & StatFlags.Wild) == StatFlags.Wild;
        public StatFlags StatFlags;
        [JsonEnum(typeof(Game.Systems.Stats.StatFlags))]
        public string[] Flags;
        public string[] SelectSounds;
        public bool HasFullName;
        public float[] NameChanceWeights;
        public NamePartDef[] NameParts;
        public BeingSex[] Sexes;
        // If there's no full name all name parts will get combined into a nickname
        // Weights will define how likely each NameChance will happen (by index)
        public SexualAttraction[] SexualAttractions;
        public SexualOrientation[] SexualOrientations;
        public string BloodSpriteRoot;
        // Extra traits shared by the species
        public string[] Traits;
        public string[] InventorySlotTypes;
        /// <summary>
        /// Whether generated equipment can ever include a weapon for this species. Species
        /// with no Weapon slot (Carrier, pets) fight at BaseCombatStrength no matter what a
        /// raid's gear tier tries to hand them.
        /// </summary>
        public bool CanHoldWeapon => Arrays.ContainsSafe(InventorySlotTypes, InventorySlotId.Weapon);
        public string[] BodyPartSlotTypes;
        public string[] AttackSounds;
        /// <summary>
        /// Played 10% of the time when being gets damaged
        /// </summary>
        public string[] PainSounds;
        /// <summary>
        /// Played every time a hit is made
        /// </summary>
        public string[] TakeDamageSounds;
        public string DefId;
        public string ForcedFaction;
        public float StasisWakeUpChance;
        public AgePhaseRange[] AgePhases;
        public Situation SpawnRequirements;
        [JsonEnum(typeof(Game.Systems.AI.Combat.CombatUnitConfig))]
        public string CombatBehavior;
        /// <summary>
        /// The mental needs of a MIND of this species, never of a body. They are carried by the
        /// mind holding slot, so a mind moved into another frame takes them along and a frame
        /// left without a mind has none of them. Physical needs live on the body part defs.
        /// </summary>
        public string[] AddsNeeds;
        /// <summary>
        /// True when AddsNeeds lists MentalHealth. Whether a mind can have a mood at all is a
        /// property of the mind, not of the brain or core it happens to sit in.
        /// </summary>
        public bool GrantsMentalHealth;
        // CurveDef id mapping recency-weighted uses of a fun source to how well it still works.
        // Species with few reachable fun sources (animals) use a gentler curve.
        public string FunRepetitionCurve;
        private Curve funRepetition;
        public Curve FunRepetition => funRepetition ??= LoadFunRepetitionCurve(FunRepetitionCurve, Id);
        private static Curve defaultFunRepetition;
        public static Curve DefaultFunRepetition => defaultFunRepetition ??=
            LoadFunRepetitionCurve(CurveId.Beings_FunRepetition_Novelty, "default");
        public string[] AddsAbilities;
        public StatSourceData[] StatSources;
        public ComponentConfig[] ProvidesStats;
        /// <summary>
        /// item1 is stat id hash
        /// </summary>
        public (int, StatModifier)[] StatModifiers;
        public ComponentConfig[] AddsStatModifiers;


        /// <summary>
        /// A mood driven need is meaningless without MentalHealth, and since both come from the
        /// same list a config can only get this wrong by omission. Catching it here beats
        /// letting AINeedFun.Tick discover it once per being per tick.
        /// </summary>
        private void ValidateMentalNeeds() {
            if (Arrays.IsEmpty(AddsNeeds)) { return; }
            foreach (var needId in AddsNeeds) {
                if (needId == NeedId.MentalHealth) {
                    GrantsMentalHealth = true;
                    return;
                }
            }
            foreach (var needId in AddsNeeds) {
                var need = Registries.AINeeds.Get(needId);
                if (need != null && need.RequiresMentalHealth) {
                    D.Err("Species {0} adds the {1} need, which requires MentalHealth, but does not add MentalHealth",
                        Id, needId);
                }
            }
        }

        public AgePhase AgePhaseFor(uint age) {
            if (Arrays.IsEmpty(AgePhases)) {
                D.Err("Age phases not defined for species: {0}", Id);
                return AgePhase.None;
            }
            for (int i = AgePhases.Length - 1; i >= 0; i--) {
                AgePhaseRange p = AgePhases[i];
                if (age >= p.AgeFrom) {
                    return p.AgePhase;
                }
            }
            D.Err("Age phases are defined incorrectly. did not find any age phase with AgeFrom > age {0} in species {1}: {2}",
                age, Id, D.Dump(AgePhases));
            return AgePhase.Childhood;
        }

        public float LifeCycleStageFor(float age) {
            if (age == 0f) { return 0f; }
            var numPhases = AgePhases.Length;
            AgePhaseRange p = AgePhases[numPhases - 1];
            var retirementAge = p.AgeFrom;
            float totalAge = retirementAge + retirementAge / (float) numPhases;
            return age / totalAge;
        }

        public string AgeIconFor(float age) {
            switch (AgePhaseFor((uint) age)) {
                case AgePhase.YoungAdult: return IconId.CAge2;
                case AgePhase.MidLife: return IconId.CAge3;
                case AgePhase.Retirement: return IconId.CAge4;
                default: return IconId.CAge1;
            }
        }

        public override string ToString() {
            return $"Species: {Id}";
        }

        private void LoadDef() {
            Def = The.Defs.Get(DefId);
            if (Def == null) {
                D.Err("Species {0} has missing or incorrect DefId: {1}", Id, DefId);
                return;
            }
            Def.IsBeing = true;
            byDef[Def.IdH] = this;
        }


        public static Species ForDef(int defId) {
            if (!byDef.TryGetValue(defId, out var def)) {
                return null;
            }
            return def;
        }
        private static readonly Dictionary<BeingSex, float> weightedSexes = new();

        public BeingSex RandomSex(Rng rng) {
            if (Sexes == null) {
                return null;
            }
            weightedSexes.Clear();
            for (int i = 0; i < Sexes.Length; i++) {
                var g = Sexes[i];
                if (g.Type == null) {
                    D.Err("Sex type not defined for species: {0}. {1}", this, D.DumpInstance(g));
                    continue;
                }
                weightedSexes.TryAdd(g, g.Chance);
            }
            if (weightedSexes.Count == 0) {
                D.Err("No sexes available for species! {0}", Id);
                return null;
            }
            return rng.WeightedFrom(weightedSexes);
        }

        private static readonly Dictionary<SexualOrientation, float> orientationsTemp = new();
        public SexualOrientation RandomSexualOrientation(Rng rng) {
            orientationsTemp.Clear();
            for (int i = 0; i < SexualOrientations.Length; i++) {
                var o = SexualOrientations[i];
                orientationsTemp.Add(o, o.Chance);
            }
            if (rng.TryWeightedFrom(orientationsTemp, out var orient)) {
                return orient;
            }
            D.Err("Could not pick random sexual orientation for species: {0}", this);
            return null;
        }

        public BeingSex GetSex(char type) {
            if (!char.IsLetter(type)) {
                return null;
            }
            if (Sexes == null) {
                return null;
            }
            // This likely means that type is null or blank string
            if (Sexes.Length == 1) {
                return Sexes[0];
            }
            for (int i = Sexes.Length - 1; i >= 0; i--) {
                var g = Sexes[i];
                if (g.TypeChar == type) { return g; }
            }
            // Lame, but if someone would type Male instead of male it will work
            for (int i = Sexes.Length - 1; i >= 0; i--) {
                var g = Sexes[i];
                if (g.TypeChar == type) {
                    return g;
                }
            }
            // We didn't find it! second pass with lowercase
            D.Err("Unknown sex type: {0} for species {1}. Options: {2}",
                type, Id, D.Dump(Sexes));
            if (Sexes.Length > 0) {
                return Sexes[0];
            }
            return null;
        }

        public BeingSex[] GetAttraction(string sexOrientation, BeingSex sex) {
            if (string.IsNullOrWhiteSpace(sexOrientation)) {
                return null;
            }
            if (sex == null) {
                D.Err("Trying to get sexual attraction for null sex for species: {0}", this);
                return null;
            }
            if (Arrays.IsEmpty(SexualAttractions)) {
                D.Err("Trying to get sexual attractions for orientation {0} in species {1} when there are no SexualAttractions defined!",
                    sexOrientation, this);
                return null;
            }
            for (int i = 0; i < SexualAttractions.Length; i++) {
                var a = SexualAttractions[i];
                if (a.SexChar == sex.TypeChar && a.Orientation == sexOrientation) {
                    return a.Attractions;
                }
            }
            D.Err("Unknown attraction {0} with orientation{1} for species {2}",
                sex, sexOrientation, Id);
            return null;
        }

        public static Species RandomPet(Rng rng) {
            return rng.From(Pets);
        }

        public static Species FindByName(string args) {
            if (args == null) {
                return null;
            }
            var str = Strings.ToSearchString(args);
            foreach (var sp in Known) {
                if (Strings.ToSearchString(sp.Value.Name) == str) {
                    return sp.Value;
                }
            }
            return null;
        }

        public static Species RandomExcept(Rng rng, Species except) {
            var ws = Caches.WeightedInts;
            ws.Clear();
            foreach (var sp in Known.Values) {
                if (sp == except) { continue; }
                if (sp.CustomMindWeight < 0f) { continue; }
                if (sp.CustomMindWeight > 0f) {
                    ws.Add(sp.IdH, sp.CustomMindWeight);
                }
            }
            if (!rng.TryWeightedFrom(ws, out var spIdH)) {
                D.Err("Couldn't get random species except, returning: {0}", except);
                return except;
            }
            if (Known.TryGetValue(spIdH, out var result)) {
                return result;
            }
            D.Err("Should never happen, species not fond in Known by idH: {0}",
                SpeciesIdH.ResolveName(spIdH));
            return except;
        }
    }
}
