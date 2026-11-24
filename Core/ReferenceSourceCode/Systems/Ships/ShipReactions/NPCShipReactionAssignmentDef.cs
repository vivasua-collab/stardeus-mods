#if !PRODUCTION_BUILD
// #define DEBUG_REACTIONS
#endif
using System;
using System.Collections.Generic;
using Game.Data;
using Game.Systems.Space;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Ships {
    [Serializable]
    public sealed class NPCShipReactionAssignmentDef {
        public static Dictionary<int, NPCShipReactionAssignmentDef> All = new();
        [ExecuteOnReload]
        private static void CleanupStatics() {
            All.Clear();
            Ignore = null;
        }
        [SelfInit(SelfInit.Stage.Finalize)]
        private static void Init() {
            LoadKnown();
        }
        public string Id;
        public int IdH;
        public string ReactionId;
        private NPCShipReaction reaction;
        public NPCShipReaction Reaction => reaction ??= Registries.NPCShipReactions.Get(ReactionId);
        public string[] FactionChanceKey;
        public float[] FactionChanceValue;
        public float DefaultFactionChance;

        public string[] ClassChanceKey;
        public float[] ClassChanceValue;
        public float DefaultClassChance;

        public float[] AttitudeMul;
        private Curve attitudeMulCurve;
        public Curve AttitudeMulCurve => attitudeMulCurve ??= AttitudeMul;

        public float[] RegionDifficultyMul;
        private Curve regionDifficultyCurve;
        public Curve RegionDifficultyCurve => regionDifficultyCurve ??= RegionDifficultyMul;

        public static NPCShipReactionAssignmentDef Ignore;

        public static NPCShipReactionAssignmentDef Get(string id) {
            if (All.TryGetValue(Hashes.S(id), out var tag)) {
                return tag;
            }
            D.Err("NPC Ship reaction def not found by id: {0}", id);
            return null;
        }
        private static void LoadKnown() {
            if (All.Count > 0) { return; }
            var types = The.ModLoader.LoadObjects<NPCShipReactionAssignmentDef>(
                "Config/Ships/Reactions");
            foreach (var type in types) {
                type.IdH = Hashes.S(type.Id);
                if (type.ReactionId == NPCShipReaction_Ignore.ReactionId) {
                    Ignore = type;
                }
                if (Arrays.Count(type.FactionChanceKey) != Arrays.Count(type.FactionChanceValue)) {
                    D.Err("Incorrectly defined NPCShipReactionAssignmentDef! Missmatched FactionChanceKey / FactionChanceValue, not same array size! {0}",
                        type.Id);
                }
                if (Arrays.Count(type.ClassChanceKey) != Arrays.Count(type.ClassChanceValue)) {
                    D.Err("Incorrectly defined NPCShipReactionAssignmentDef! Missmatched ClassChanceKey / ClassChanceValue, not same array size! {0}",
                        type.Id);
                }
                if (Arrays.IsEmpty(type.RegionDifficultyMul)) {
                    D.Err("Incorrectly defined NPCShipReactionAssignmentDef! RegionDifficultyMul is empty! {0}",
                        type.Id);
                    type.RegionDifficultyMul = new float[] { 0f, 1f, 1f, 1f };
                }
                if (!All.TryAdd(type.IdH, type)) {
                    D.Err("NPCShipReactionAssignmentDef with such ID already exists: {0}", type.Id);
                    continue;
                }
            }
        }

        public override string ToString() {
            return $"NPCShipReactionAssignmentDef[{Id}]";
        }

        public bool HasChanceFor(NPCShip observer, NPCShip ship, float attitude,
                out float weight) {
            weight = 1f;
            if (observer.Class.IsSpaceStation || observer.IsDerelict) {
                return false;
            }
            if (!Reaction.IsCompatibleWith(observer, ship, attitude, out float chanceAdj)) {
                weight = 0f;
                return false;
            }
            weight += chanceAdj;
            if (!Arrays.IsEmpty(AttitudeMul)) {
                weight *= AttitudeMulCurve.Evaluate(attitude);
                if (weight <= 0f) { return false; }
            }
            if (!Arrays.IsEmpty(FactionChanceKey)) {
                var factionIdx = Array.IndexOf(FactionChanceKey,
                    ship.Faction.Id);
                if (factionIdx == -1) {
                    weight *= DefaultFactionChance;
                } else {
                    weight *= FactionChanceValue[factionIdx];
                }
                if (weight <= 0f) { return false; }
            }
            if (!Arrays.IsEmpty(ClassChanceKey)) {
                var classIdx = Array.IndexOf(ClassChanceKey,
                    ship.Class.Id);
                if (classIdx == -1) {
                    weight *= DefaultClassChance;
                } else {
                    weight *= ClassChanceValue[classIdx];
                }
                if (weight <= 0f) { return false; }
            }
            var diffAdj = RegionDifficultyCurve.Evaluate(observer.Sector.Biome.Difficulty);
            #if DEBUG_REACTIONS
            D.Warn("Region difficulty adjustment for reaction {0} in region {1}: {2}",
                Id, observer.Sector.Biome.Difficulty, diffAdj);
            D.Warn("Chance for reaction {0}: {1} : {2}->{3} (Attitude: {4})",
                Id, weight, observer, ship, attitude);
            #endif
            weight *= diffAdj;
            return weight > 0f;
        }
    }
}
