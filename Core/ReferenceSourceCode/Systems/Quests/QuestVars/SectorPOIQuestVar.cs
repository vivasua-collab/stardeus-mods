using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Data.Space;
using Game.Systems.Naming;
using Game.Systems.Space;
using Game.UI;
using Game.Utils;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class SectorPOIQuestVar : QuestVar, IQuestStepTargetVar, IQuestVar<SpaceObject> {
        [SelfInit]
        private static void Register() {
            Registries.QuestVars.Add(VarId,
                (s, def, data, seed, l) => new SectorPOIQuestVar(s, def, data, seed, l));
        }

        public const string VarId = "SectorPOI";
        private SpaceObject poiSO;
        public SpaceObject Value => poiSO;
        public SectorPOIQuestVar(GameState s, QuestVarDef def,
                QuestData data, int seed, bool isLoading)
                        : base(s, def, data, seed, isLoading) {

            var loadId = config.GetString(PropertyIdH.SharedVar_LoadPOIId, null);
            if (loadId != null) {
                var sharedData = s.Query.GetSharedQuestData.Ask();
                var poiId = sharedData.GetInt(loadId, 0);
                if (!s.Universe.TryFind(poiId, out poiSO)) {
                    if (!config.HasKey(PropertyIdH.POIConfigId)) {
                        // reference-only var (no generation config) - nothing to regenerate
                        // from, so a dead reference just resolves to null. This applies whether
                        // we're loading a save or creating the quest fresh (e.g. via a stranded
                        // chain recovery, or a fresh offer after the referenced POI was already
                        // destroyed by another quest) - neither case has anything to fall back to.
                        if (config.GetBool(PropertyIdH.MustExistOnLoad, true)) {
                            D.Warn("Shared POI no longer exists and {2} has no POIConfigId to regenerate from: {0}->{1}",
                                loadId, poiId, this);
                        }
                        return;
                    }
                    D.Err("Failed to find SpaceObject by id {0}->{1} | {2}",
                        loadId, poiId, this);
                } else {
                    return;
                }
            }

            if (!isLoading) {
                FindRandomTarget(def, data, seed);
                return;
            }

            var poiSOId = data.Data?.GetInt(def.DataKey(VarId), 0) ?? 0;
            if (poiSOId == 0) {
                throw new System.Exception(string.Format(
                    "Failed to load PoiSOId for var {0} | {1}", def, data));
            }
            if (!s.Universe.TryFind(poiSOId, out poiSO)) {
                #if !PRODUCTION_BUILD
                // It's ok for POIs since they get consumed and removed
                D.Warn("Failed to loading PoiSOId by id {0} for var {1} | {2}",
                        poiSOId, def, data);
                #endif
            }
        }

        private void FindRandomTarget(QuestVarDef def, QuestData data, int seed) {
            var poiCfgId = config.GetString(PropertyIdH.POIConfigId);
            var poiCfg = POIConfig.Get(poiCfgId);
            var sectorTemplateId = config.GetString(PropertyIdH.SectorTemplate, null);
            var factionId = config.GetString(PropertyIdH.FactionId, null);
            var wantedFac = factionId != null ? FactionDef.Get(factionId) : null;

            // Multiple SectorPOI vars in the same quest share one seed - mix in the var's own
            // id so they don't draw from identical RNG streams and pick the same sector.
            var varSeed = Hashes.Quick(seed, def.IdH);

            var (sector, reuseExisting) = FindTargetSector(
                varSeed, poiCfg, sectorTemplateId, wantedFac);
            if (wantedFac != null) {
                // sector ownership is only a weighted chance until generated - make it definite
                sector.SO.SetFaction(wantedFac);
            }

            var spawnRng = new Rng(Hashes.Quick(varSeed, Primes.Prime016));
            if (reuseExisting) {
                poiSO = FindExistingChildPOI(sector.SO, poiCfg, spawnRng);
            }
            poiSO ??= SOUtils.SpawnAdditionalPOI(sector.SO, poiCfg.Id, spawnRng);
            if (poiSO == null) {
                throw new System.Exception(
                    $"Failed spawning additional PoiSO: {this} | {data}");
            }
            // Claim this POI for the quest. POIs are consumable (removed on explore/deliver), so
            // no other quest may target the same one - see FindExistingChildPOI.
            poiSO.QuestId = data.Id;
            if (poiSO.Type.IdH != SpaceObjectTypeIdH.Port) {
                // generic POIs (signals, anomalies, events) have no inherent identity - Ports
                // already have a faction-flavored name that shouldn't be overwritten
                var nameReq = NameRequest.Generic(NameType.Basic, spawnRng);
                var name = S.Query.GenerateName.Ask(nameReq);
                name.Consume();
                poiSO.UpdateName(name.Name);
            }
            var qData = data.GetOrCreateData();
            // save it in QuestData for loading it later
            qData.SetInt(def.DataKey(VarId), poiSO.Id);

            var saveId = config.GetString(PropertyIdH.SharedVar_SavePOIId, null);
            if (saveId != null) {
                var d = S.Query.GetSharedQuestData.Ask();
                d.SetInt(saveId, poiSO.Id);
            }
        }

        /// <summary>
        /// Picks a target sector, preferring (in order): a sector matching SectorTemplate that
        /// already belongs to the wanted faction and already has the wanted POI planned (reuse);
        /// one that belongs to the wanted faction but doesn't have the POI yet (spawn); or one
        /// that belongs to no faction at all and has no POI yet (claim it for the faction and
        /// spawn). Sectors owned by a different faction are never considered. If nothing matches
        /// within a few attempts, falls back to the old template-agnostic behavior with a warning.
        /// </summary>
        private (SpaceSector sector, bool reuseExisting) FindTargetSector(int seed,
                POIConfig poiCfg, string sectorTemplateId, FactionDef wantedFac) {
            const int maxAttempts = 8;
            var tierReuse = new Dictionary<SpaceSector, float>();
            var tierOwnedNoPOI = new Dictionary<SpaceSector, float>();
            var tierUnownedNoPOI = new Dictionary<SpaceSector, float>();
            var seen = new HashSet<SpaceSector>();

            for (int attempt = 0; attempt < maxAttempts; attempt++) {
                var rng = new Rng(Hashes.Quick(seed, Primes.Prime013, attempt));
                var region = PickTargetRegion(rng);
                foreach (var sec in region.Sectors) {
                    if (sec == A.PlayerSector.Sector) { continue; }
                    if (HasPOIClaimedByThisQuest(sec)) { continue; }
                    if (!seen.Add(sec)) { continue; }
                    if (sec.Biome.Template is not SectorTemplate secTpl) { continue; }
                    if (!secTpl.MustHavePOIs) { continue; }
                    // storm sectors are peaceful - quests must never send the player there
                    if (SpaceEffectMagneticStorm.SectorHasEffect(sec)) { continue; }
                    if (sectorTemplateId != null && secTpl.Id != sectorTemplateId) { continue; }

                    var weight = sec.SO.IsVisited ? 0.5f : 1f;
                    var hasPoi = sec.Biome.HasAssignable(poiCfg, false);

                    if (wantedFac == null) {
                        (hasPoi ? tierReuse : tierOwnedNoPOI).Add(sec, weight);
                        continue;
                    }
                    var ownedBy = sec.Biome.FactionsByDominance;
                    var isUnowned = Arrays.IsEmpty(ownedBy);
                    if (!isUnowned && !ownedBy.ContainsKey(wantedFac)) {
                        // belongs to a different faction - never hijack it
                        continue;
                    }
                    if (!isUnowned) {
                        (hasPoi ? tierReuse : tierOwnedNoPOI).Add(sec, weight);
                    } else if (!hasPoi) {
                        tierUnownedNoPOI.Add(sec, weight);
                    }
                }
                if (tierReuse.Count > 0 || tierOwnedNoPOI.Count > 0 || tierUnownedNoPOI.Count > 0) {
                    break;
                }
            }

            var pickRng = new Rng(Hashes.Quick(seed, Primes.Prime017));
            if (tierReuse.Count > 0 && pickRng.TryWeightedFrom(tierReuse, out var secReuse)) {
                return (secReuse, true);
            }
            if (tierOwnedNoPOI.Count > 0 && pickRng.TryWeightedFrom(tierOwnedNoPOI, out var secOwned)) {
                return (secOwned, false);
            }
            if (tierUnownedNoPOI.Count > 0
                    && pickRng.TryWeightedFrom(tierUnownedNoPOI, out var secUnowned)) {
                return (secUnowned, false);
            }

            D.Warn("Failed to find a sector matching template {0}, faction {1}, POI {2} | {3} - falling back to any sector with POIs",
                sectorTemplateId, wantedFac, poiCfg, this);
            return (FindRandomSector(seed), false);
        }

        /// <summary>
        /// True if this sector already hosts a POI claimed by this same quest (by an earlier
        /// SectorPOI var). Prevents two vars in one quest from picking the same sector.
        /// </summary>
        private bool HasPOIClaimedByThisQuest(SpaceSector sec) {
            if (!sec.SO.HasChildren) { return false; }
            foreach (var child in sec.SO.Children) {
                if (child.QuestId == data.Id) { return true; }
            }
            return false;
        }

        private static SpaceObject FindExistingChildPOI(SpaceObject so, POIConfig poiCfg, Rng rng) {
            if (!so.HasChildren) {
                if (!so.S.Sys.Universe.GenerateSector(so, isEntering: true, false)) {
                    return null;
                }
            }
            var soType = SpaceObjectType.Find(poiCfg.Type);
            foreach (var child in so.Children) {
                if (child.Type.IdH != soType.IdH) { continue; }
                if (child.QuestId != 0) {
                    // already claimed by another quest - POIs get consumed, never share one
                    continue;
                }
                if (soType.IdH == SpaceObjectTypeIdH.Port) {
                    // not every sector generator (e.g. AsteroidZone) assigns Port faction itself
                    SOUtils.EnsurePortFaction(so, child, rng);
                }
                return child;
            }
            return null;
        }

        public QuestStepTarget GetTargetFor(QuestStep step) {
            return new QuestStepTarget() {
                Target = poiSO,
                Step = step,
            };
        }
    }
}
