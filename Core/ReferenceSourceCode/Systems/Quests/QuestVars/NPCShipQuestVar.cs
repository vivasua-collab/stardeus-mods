using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Data.Space;
using Game.Systems.Ships;
using Game.Systems.Space;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Quests {
    public sealed class NPCShipQuestVar : QuestVar, IQuestVar<NPCShip>, IQuestStepTargetVar {
        private const string FactionRandom = "Random";
        private const string FactionRandomFriendly = "RandomFriendly";
        private static HashSet<FactionDef> friendlyExcept;
        private static HashSet<FactionDef> FriendlyExcept => friendlyExcept ??= new() {
            FactionDef.Get(FactionId.Pirate),
            FactionDef.Get(FactionId.Virus),
        };
        [SelfInit]
        private static void Register() {
            Registries.QuestVars.Add(VarId,
                (s, def, data, seed, l) => new NPCShipQuestVar(s, def, data, seed, l));
        }

        private NPCShip ship;
        public const string VarId = "NPCShip";
        public NPCShip Value => ship;

        public NPCShipQuestVar(GameState s, QuestVarDef def,
                QuestData data, int seed, bool isLoading)
                    : base(s, def, data, seed, isLoading) {
            if (!isLoading) {
                SpawnShip(s, seed);
                return;
            }

            var soId = data.Data?.GetInt(def.DataKey(VarId), 0) ?? 0;
            if (soId == 0) {
                D.Throw("Failed to load PoiSOId for var {0} | {1}", def, data);
            }
            if (!s.Universe.TryFind(soId, out var so)) {
                if (config.GetBool(PropertyIdH.MustExistOnLoad, false)) {
                    D.Throw("Failed to loading existing SO by id {0} for var {1} | {2}",
                            soId, def, data);
                }
            }
            ship = so?.Ship;
        }

        private void SpawnShip(GameState s, int seed) {
            var template = new NPCShip();
            // A quest's own spawned ship (kill target, etc.) must never offer quests
            // of its own, even after this quest ends.
            template.NoQuests = true;
            ApplyStrengthConfig(template, seed);
            template.Faction = ResolveFaction();
            var target = FindRandomSector(seed);
            ship = s.Query.SpawnShipTplAt.Ask(target.SO, template);
            // -1 is natural chance from template / faction
            var chancePolice = config.GetFloat(PropertyIdH.ChancePolice, -1f);
            if (chancePolice >= 0f) {
                var rng = new Rng(Hashes.Quick(Primes.Prime002, seed));
                ship.IsPolice = rng.Chance(chancePolice);
            }
            if (ship == null) {
                D.Throw("Failed to spawn NPCShip for {0}", this);
            }
            var regBehId = config.GetString(PropertyIdH.ShipBehaviorRegionId, null);
            if (regBehId != null) {
                var beh = Registries.NPCShipBehaviors.Get(regBehId);
                ship.SetBehavior(beh, NPCShipBehaviorScope.Region);
            }
            var secBehId = config.GetString(PropertyIdH.ShipBehaviorSectorId, null);
            if (secBehId != null) {
                var beh = Registries.NPCShipBehaviors.Get(secBehId);
                ship.SetBehavior(beh, NPCShipBehaviorScope.Sector);
            }
            D.Warn("Spawned ship: {0} for {1}", ship, this);
            var qData = data.GetOrCreateData();
            // save it in QuestData for loading it later
            qData.SetInt(Def.DataKey(VarId), ship.SO.Id);
        }

        private void ApplyStrengthConfig(NPCShip template, int seed) {
            var strengthMul = ResolveStrengthMul(seed);
            if (strengthMul > 0f) {
                var cp = A.Player.Ship.CombatState.CurrentClassPoints;
                var wantedClassPoints = Mathf.RoundToInt(cp * strengthMul
                    * S.Difficulty.QuestShipStrengthMul
                    * ShipClassDef.OpponentStrengthCompression(cp));
                template.WantedClassPoints = wantedClassPoints;
                template.Class = ShipClassDef.StrongestClassForPoints(wantedClassPoints, false);
                return;
            }

            var legacyCP = A.Player.Ship.Class.MaxClassPoints;
            template.Class = ShipClassDef.StrongestClassForPoints(legacyCP * new Rng(seed).Range(1.5f, 2f), false);
        }

        private float ResolveStrengthMul(int seed) {
            var strengthMulCurve = config.GetFloatSet(PropertyIdH.StrengthMulCurve, null);
            if (!Arrays.IsEmpty(strengthMulCurve)) {
                var rng = new Rng(Hashes.Quick(Primes.Prime003, seed));
                Curve curve = strengthMulCurve;
                return rng.Curve(curve);
            }
            var str = config.GetFloat(PropertyIdH.StrengthMul, 0f);
            if (str > 0f) {
                return str * new Rng(seed).Range(0.9f, 1.1f);
            }
            return 0f;
        }

        private FactionDef ResolveFaction() {
            string facId = null;
            var factionIds = config.GetStringSet(PropertyIdH.FactionIds, null);
            if (factionIds != null && factionIds.Length > 0) {
                var idx = data.TextVariantIndex;
                if (idx < 0 || idx >= factionIds.Length) {
                    D.Err("Quest {0} NPCShip var {1}: FactionIds set but TextVariantIndex {2} is invalid",
                        data.Def?.Id, Def.Id, idx);
                } else {
                    facId = factionIds[idx];
                }
            }
            facId ??= config.GetString(PropertyIdH.FactionId, null);
            if (facId == null || facId == FactionRandom) {
                return S.Sys.Diplomacy.RandomFactionForRaid();
            }
            if (facId == FactionRandomFriendly) {
                return S.Sys.Diplomacy.RandomFactionForRaid(except: FriendlyExcept);
            }
            return FactionDef.Get(facId);
        }

        public QuestStepTarget GetTargetFor(QuestStep step) {
            if (ship == null) {
                return null;
            }
            return new QuestStepTarget() {
                Step = step,
                Target = ship,
            };
        }
    }
}
