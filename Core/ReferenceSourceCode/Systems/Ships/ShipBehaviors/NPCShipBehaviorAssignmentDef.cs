using System;
using System.Collections.Generic;
using Game.Data;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Ships {
    /// <summary>
    /// Each NPCShipBehavior can be assigned to specific ships according to their
    /// Faction, Class and other parameters, like difficulty of the region they
    /// have spawned in, or template of the region they spawned in
    /// <seealso cref="NPCShipBehaviorScope"/>
    /// <seealso cref="NPCShipBehavior"/>
    /// </summary>
    [Serializable]
    public sealed class NPCShipBehaviorAssignmentDef {
        public static Dictionary<int, NPCShipBehaviorAssignmentDef> All = new();
        [ExecuteOnReload]
        private static void CleanupStatics() {
            All.Clear();
        }
        [SelfInit(SelfInit.Stage.Finalize)]
        private static void Init() {
            LoadKnown();
        }
        public string Id;
        public int IdH;
        public string BehaviorId;
        private NPCShipBehavior behavior;
        public NPCShipBehavior Behavior => behavior ??= Registries.NPCShipBehaviors.Get(BehaviorId);
        public bool RequiresEngines;
        public string[] FactionChanceKey;
        public float[] FactionChanceValue;
        public float DefaultFactionChance;

        public string[] ClassChanceKey;
        public float[] ClassChanceValue;
        public float DefaultClassChance;

        public float[] OriginDifficultyChance;
        private Curve originDifficultyCurve;
        public Curve OriginDifficultyCurve => originDifficultyCurve ??= OriginDifficultyChance;
        public static NPCShipBehaviorAssignmentDef Get(string id) {
            if (All.TryGetValue(Hashes.S(id), out var tag)) {
                return tag;
            }
            D.Err("NPC Ship behavior def not found by id: {0}", id);
            return null;
        }
        private static void LoadKnown() {
            if (All.Count > 0) { return; }
            var types = The.ModLoader.LoadObjects<NPCShipBehaviorAssignmentDef>(
                "Config/Ships/Behaviors");
            foreach (var type in types) {
                type.IdH = Hashes.S(type.Id);
                if (Arrays.Count(type.FactionChanceKey) != Arrays.Count(type.FactionChanceValue)) {
                    D.Err("Incorrectly defined NPCShipBehaviorAssignmentDef! Missmatched FactionChanceKey / FactionChanceValue, not same array size! {0}",
                        type.Id);
                }
                if (Arrays.Count(type.ClassChanceKey) != Arrays.Count(type.ClassChanceValue)) {
                    D.Err("Incorrectly defined NPCShipBehaviorAssignmentDef! Missmatched ClassChanceKey / ClassChanceValue, not same array size! {0}",
                        type.Id);
                }
                if (!All.TryAdd(type.IdH, type)) {
                    D.Err("NPCShipBehaviorAssignmentDef with such ID already exists: {0}", type.Id);
                    continue;
                }
            }
        }

        public override string ToString() {
            return $"NPCShipBehaviorAssignmentDef[{Id}]";
        }

        public bool HasChanceFor(NPCShip ship, out float chance) {
            chance = 1f;
            if (RequiresEngines) {
                if (ship.IsDerelict) { return false; }
                if (Behavior.Scope == NPCShipBehaviorScope.Sector) {
                    if (ship.NumThrustersActual == 0) {
                        chance = 0f;
                        return false;
                    }
                } else if (Behavior.Scope == NPCShipBehaviorScope.Region) {
                    if (ship.NumHyperdrivesActual == 0) {
                        chance = 0f;
                        return false;
                    }
                } else {
                    D.Err("Unhandled scope for engines: {0}", Behavior.Scope);
                    return ship.NumHyperdrivesActual > 0 && ship.NumThrustersActual > 0;
                }
            }
            if (!Arrays.IsEmpty(OriginDifficultyChance)) {
                chance *= OriginDifficultyCurve.Evaluate(
                    ship.Memory.OriginSector.Biome.Difficulty);
                if (chance <= 0f) { return false; }
            }
            if (!Arrays.IsEmpty(FactionChanceKey)) {
                var factionIdx = Array.IndexOf(FactionChanceKey,
                    ship.Faction.Id);
                if (factionIdx == -1) {
                    chance *= DefaultFactionChance;
                } else {
                    chance *= FactionChanceValue[factionIdx];
                }
                if (chance <= 0f) { return false; }
            }
            if (!Arrays.IsEmpty(ClassChanceKey)) {
                var classIdx = Array.IndexOf(ClassChanceKey,
                    ship.Class.Id);
                if (classIdx == -1) {
                    chance *= DefaultClassChance;
                } else {
                    chance *= ClassChanceValue[classIdx];
                }
                if (chance <= 0f) { return false; }
            }
            // D.Warn("Chance for behavior {0}: {1} : {2}", Id, chance, ship);
            return chance > 0f;
        }
    }
}
