using System.Collections.Generic;
using System.Linq;
using KL.Collections;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.Systems.Ships;
using System;
using KL.Randomness;
using Game.Systems.Story;

namespace Game.Constants {
    public sealed class Difficulty {
        [SelfInit]
        private static void Init() {
            all.Clear();
            isLoaded = false;
        }

        public const string DefaultId = "balanced";
        public const string QuickStartId = "relaxing";
        public const string HardestId = "impossible";

        #region MAIN FIELDS
        public string Id;
        public float Level = 1;
        /// <summary>
        /// Level but no more than 1f
        /// </summary>
        public float Reduction;
        /// <summary>
        /// Level but no less than 1f
        /// </summary>
        public float Increment;
        /// <summary>
        /// Level with the high end compressed via sqrt: Reduction*(1+sqrt(Increment-1)).
        /// Identical to Level on easy/normal/challenging; tames Impossible's 5x to ~3x.
        /// Use for mechanics where raw Level produces an unreadable multiplier at high difficulties.
        /// </summary>
        public float SoftLevel;
        public StoryScheduleModifier[] StoryScheduleModifiers;
        public float StartingResources = 1;
        public float PlanetResources = 1;
        /// <summary>
        /// Default value for ModifierDestructionLevel on the Wrecked Empty Ship
        /// scenario, unless the player overrides the slider in custom setup.
        /// Range: 0.15 (timid) .. 0.75 (today's default destruction).
        /// </summary>
        public float DefaultDestructionLevel = 0.75f;
        public float DatoidCostScale = 1;
        public float SanityDropRate = 1;
        /// <summary>
        /// Resting sanity target for beings on this difficulty. Higher = calmer baseline.
        /// Replaces Tunables.SanityBaseline; used when generating beings and in MentalHealth.Initialize().
        /// </summary>
        public float SanityBaseline = 0.40f;
        public float BreachCapsuleSpeed = 1f;
        /// <summary>
        /// For how many in-game minutes a brand new fire (one that starts while
        /// nothing worth extinguishing is burning) stays "boosted": harder to
        /// extinguish, faster to grow and spread. 0 disables the mechanic.
        /// </summary>
        public int FireBoostMinutes = 120;
        /// <summary>
        /// Multiplier for fire strength growth rate while the fire boost is active.
        /// </summary>
        public float FireBoostGrowthMul = 2f;
        /// <summary>
        /// Multiplier for fire spread chance while the fire boost is active.
        /// </summary>
        public float FireBoostSpreadMul = 1.5f;
        /// <summary>
        /// Multiplier for firefighting effort while the fire boost is active.
        /// Lower = fire resists extinguishing more.
        /// </summary>
        public float FireBoostFirefightingMul = 0.25f;
        /// <summary>
        /// While the fire boost is active, fire builds up pressure to spread once
        /// its strength is at or above this (1 = only at full strength, which is
        /// the normal, unboosted behavior). Below Tunables.FireStrengthAfterSpread
        /// (0.7) means a fire that is being actively fought keeps spreading anyway,
        /// because it lands back above the threshold after every spread.
        /// </summary>
        public float FireBoostSpreadThreshold = 0.65f;
        /// <summary>
        /// Scales the chance that incidents (story events, device malfunctions)
        /// start extra fires or explosions. Kept separate from Level so harder
        /// difficulties can stay close to Balanced here: fire snowballs, so a
        /// raw Level multiplier makes it unplayable. Unset (negative) falls back
        /// to Level, so difficulty configs without the field keep the old scaling.
        /// </summary>
        public float FireChanceScale = -1f;
        public float WantedTensionMultiplier = 1f;
        public float NPCShipShootHesitationMin;
        public float NPCShipShootHesitationMax;
        public float[] NPCShipStrengthChance;
        public int MinDeepScansPerLocation;
        public int MaxDeepScansPerLocation;
        private Curve npcShipStrengthCurve;
        public Curve NPCShipStrengthCurve => npcShipStrengthCurve ??= NPCShipStrengthChance;
        /// <summary>
        /// Fraction of an NPC ship's free class points that offensiveness is allowed to spend
        /// on weapons (0.5 = at most 50% of what full offensiveness would normally buy).
        /// Leftover class points fall back to flux/shields via the existing reserve logic.
        /// </summary>
        public float ShipCombatDifficulty = 1f;
        /// <summary>
        /// Multiplier applied to quest-spawned NPC ship opponents' class points
        /// (NPCShipQuestVar.ApplyStrengthConfig). Story-event opponents use
        /// NPCShipStrengthCurve instead and are not affected by this field.
        /// </summary>
        public float QuestShipStrengthMul = 1f;
        /// <summary>
        /// False turns off major threats: scheduled raids and every Raid or HeavyDisaster
        /// story event. Only Peaceful sets it, the way the reference colony sim's Peaceful
        /// preset is the only one without raids. Every other difficulty keeps the same story
        /// cadence and changes how hard events hit instead.
        /// </summary>
        public bool AllowMajorThreats = true;
        /// <summary>
        /// Until this game day the first raid waits for a repair station on the ship
        /// (RaidStoryAspect), so a new player's first fight does not come before their bots
        /// can be repaired. 0 turns it off. Set on Relaxing and below.
        /// </summary>
        public float FirstRaidWaitsForRepairStationDays;
        /// <summary>
        /// How much of raid adaptation's swing reaches raid points (RaidAdaptation): 1 the whole
        /// 0.4x-1.47x, 0 none. Harder difficulties ease it, so losses buy less respite and a
        /// long run grows raids less, as the popular colony sim's difficulties do.
        /// </summary>
        public float AdaptationEffect = 1f;
        /// <summary>
        /// Scales how fast raid adaptation grows above 0, over a run without losses. 0 keeps
        /// raids at their starting strength; recovery from losses is never scaled.
        /// </summary>
        public float AdaptationGrowthOverZero = 1f;
        #endregion

        private static bool isLoaded;
        public static void LoadKnown() {
            if (isLoaded) { return; }
            isLoaded = true;

            if (The.ModLoader == null) {
                D.Err("Trying to LoadKnown while mod loader not ready!");
                isLoaded = false;
                return;
            }

            var types = The.ModLoader.LoadObjects<Difficulty>("Config/Difficulty");
            foreach (var d in types) {
                Add(d);
            }
            if (all.Count == 0) {
                D.Err("Difficulty configurations failed to load!");
            }
        }
        public int AdjustedDatoidCost(int cost) {
            var adj = Mathf.RoundToInt(cost * DatoidCostScale);
            if (adj < 1) {
                adj = 1;
            }
            return adj;
        }
        private static void Add(Difficulty d) {
            if (d.Level < 0.1f) {
                D.Err("Level cannot be less than 0.1, fixing for Difficulty: {0}", d.Id);
                d.Level = 1f;
            }
            if (Arrays.IsEmpty(d.StoryScheduleModifiers)) {
                D.Err("Difficulty has no StoryScheduleModifiers! {0}", d.Id);
            } else {
                foreach (var mod in d.StoryScheduleModifiers) {
                    mod.Validate(d.Id);
                }
            }
            d.Reduction = Maths.Min(d.Level, 1f);
            d.Increment = Maths.Max(d.Level, 1f);
            d.SoftLevel = d.Reduction * (1f + Mathf.Sqrt(d.Increment - 1f));
            if (d.DatoidCostScale < 0.1f) {
                D.Err("DatoidCostScale cannot be less than 0.1, fixing for Difficulty: {0}", d.Id);
                d.DatoidCostScale = 1f;
            }
            if (d.StartingResources < 0.1f) {
                D.Err("StartingResources cannot be less than 0.1, fixing for Difficulty: {0}", d.Id);
                d.StartingResources = 1f;
            }
            if (d.PlanetResources < 0.1f) {
                D.Err("PlanetResources cannot be less than 0.1, fixing for Difficulty: {0}", d.Id);
                d.PlanetResources = 1f;
            }
            if (d.MinDeepScansPerLocation < 1) {
                D.Err("MinDeepScansPerLocation is not defined, or set to < 1. Setting to 1. Fixing for Difficulty: {0}", d.Id);
                d.MinDeepScansPerLocation = 1;
            }
            if (d.MaxDeepScansPerLocation < d.MinDeepScansPerLocation) {
                D.Err("MaxDeepScansPerLocation is < MinDeepScansPerLocation. Fixing for Difficulty: {0}", d.Id);
                d.MaxDeepScansPerLocation = Math.Max(d.MinDeepScansPerLocation + 1, 3);
            }
            if (d.BreachCapsuleSpeed < 0.1f) {
                D.Err("BreachCapsuleSpeed cannot be less than 0.1, fixing for Difficulty: {0}", d.Id);
                d.BreachCapsuleSpeed = 1f;
            }
            if (d.WantedTensionMultiplier < 0.1f) {
                D.Err("WantedTensionMultiplier cannot be less than 0.1, fixing for Difficulty: {0}", d.Id);
                d.WantedTensionMultiplier = 1f;
            }
            if (d.FireBoostMinutes < 0) {
                D.Err("FireBoostMinutes cannot be negative, fixing for Difficulty: {0}", d.Id);
                d.FireBoostMinutes = 0;
            }
            if (d.FireBoostGrowthMul < 1f) {
                D.Err("FireBoostGrowthMul cannot be less than 1, fixing for Difficulty: {0}", d.Id);
                d.FireBoostGrowthMul = 1f;
            }
            if (d.FireBoostSpreadMul < 1f) {
                D.Err("FireBoostSpreadMul cannot be less than 1, fixing for Difficulty: {0}", d.Id);
                d.FireBoostSpreadMul = 1f;
            }
            if (d.FireBoostFirefightingMul < 0.01f || d.FireBoostFirefightingMul > 1f) {
                D.Err("FireBoostFirefightingMul out of range [0.01, 1], fixing for Difficulty: {0}", d.Id);
                d.FireBoostFirefightingMul = Mathf.Clamp(d.FireBoostFirefightingMul, 0.01f, 1f);
            }
            if (d.FireBoostSpreadThreshold < 0.1f || d.FireBoostSpreadThreshold > 1f) {
                D.Err("FireBoostSpreadThreshold out of range [0.1, 1], fixing for Difficulty: {0}", d.Id);
                d.FireBoostSpreadThreshold = Mathf.Clamp(d.FireBoostSpreadThreshold, 0.1f, 1f);
            }
            if (d.FireChanceScale < 0f) {
                d.FireChanceScale = d.Level;
            }
            if (d.SanityDropRate < 0.1f) {
                D.Err("SanityDropRate cannot be less than 0.1, fixing for Difficulty: {0}", d.Id);
                d.SanityDropRate = 1f;
            }
            if (d.SanityBaseline < 0.1f || d.SanityBaseline > 0.9f) {
                D.Err("SanityBaseline out of range [0.1, 0.9], fixing for Difficulty: {0}", d.Id);
                d.SanityBaseline = 0.40f;
            }
            if (d.DefaultDestructionLevel < 0.15f || d.DefaultDestructionLevel > 0.75f) {
                D.Err("DefaultDestructionLevel out of range [0.15, 0.75], fixing for Difficulty: {0}", d.Id);
                d.DefaultDestructionLevel = Mathf.Clamp(d.DefaultDestructionLevel, 0.15f, 0.75f);
            }
            if (d.ShipCombatDifficulty < 0.1f) {
                D.Err("ShipCombatDifficulty cannot be less than 0.1, fixing for Difficulty: {0}", d.Id);
                d.ShipCombatDifficulty = 1f;
            }
            if (d.QuestShipStrengthMul < 0.1f) {
                D.Err("QuestShipStrengthMul cannot be less than 0.1, fixing for Difficulty: {0}", d.Id);
                d.QuestShipStrengthMul = 1f;
            }
            all[d.Id] = d;
        }
        private static readonly Dictionary<string, Difficulty> all = new();
        public static List<Difficulty> AllSorted() {
            LoadKnown();
            return all.Values.OrderBy(d => d.Level).ToList();
        }
        private string name;
        public string Name => name ??= string.Format(
            "{0} <b>[{1}]</b>", $"difficulty.{Id}".T(),
                Units.Percentage(Level, false));

        public static Difficulty Find(string id) {
            LoadKnown();
            if (id == null) {
                D.Err("Trying to get difficulty with id null! Fallback to balanced");
                id = DefaultId;
            }
            if (all.TryGetValue(id, out var diff)) {
                return diff;
            }
            D.Err("Could not find difficulty by id: {0}", id);
            if (all.TryGetValue(DefaultId, out diff)) {
                return diff;
            }
            D.Err("Could not find difficulty by id 'balanced'. Fallback failed");
            // Defaults should be 1 anyway
            var d = new Difficulty();
            d.Id = DefaultId;
            return d;
        }
        public static Difficulty FindByName(string name) {
            LoadKnown();
            foreach (var s in all) {
                if (s.Value.Name == name) {
                    return s.Value;
                }
            }
            return null;
        }

        public int ShootHesitationFor(NPCShip ship) {
            if (ship.IsPlayer) {
                return Rng.URange(0, 3);
            }
            var secDiff = ship.Sector.Biome.Difficulty;
            var minutes = ship.SO.S.Rng.Range(NPCShipShootHesitationMin,
                NPCShipShootHesitationMax) * (2f - secDiff);
            return Mathf.RoundToInt(minutes);
        }

        public override string ToString() {
            return $"Difficulty[{Id}]";
        }
    }
}
