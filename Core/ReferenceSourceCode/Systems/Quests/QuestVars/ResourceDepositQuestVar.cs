using System;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Planets;
using Game.Data.Space;
using Game.Procgen.Space;
using Game.Systems.Space;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Quests {
    public sealed class ResourceDepositQuestVar : QuestVar, IQuestStepTargetVar,
            IQuestVar<SpaceObject> {
        [SelfInit]
        private static void Register() {
            Registries.QuestVars.Add(VarId,
                (s, def, data, seed, l) => new ResourceDepositQuestVar(s, def, data, seed, l));
        }

        public const string VarId = "ResourceDeposit";
        // 500m of reachable depth below best shuttle's max mining depth
        private const float MinDepthHeadroomKm = 0.5f;
        // very generous headroom for a freshly spawned fallback deposit
        private const float FallbackHeadroomKm = 1.5f;
        private SpaceObject deposit;
        private MatType depositType;
        public SpaceObject Value => deposit;

        public ResourceDepositQuestVar(GameState s, QuestVarDef def,
                QuestData data, int seed, bool isLoading)
                    : base(s, def, data, seed, isLoading) {

            if (isLoading) {
                if (data.Data.TryGetInt(def.DataKey(VarId), out var soId)) {
                    deposit = S.Universe.Find(soId);
                    if (deposit == null) {
                        D.Err("Failed restoring resource deposit by id {0} for quest {1}",
                            soId, data);
                    }
                    return;
                }
            }

            var tryCurrentSector = def.Config.GetBool(PropertyIdH.TryCurrentSector, false);
            SpaceSector sector;
            if (tryCurrentSector) {
                sector = A.PlayerSector.Sector;
            } else {
                throw new NotImplementedException(
                    $"only current sector is implemented for ResourceDeposit var for now! {data}");
            }
            var tryPlayerLowOn = def.Config.GetBool(PropertyIdH.TryResourcePlayerIsLowOn, true);
            var requireSellable = def.Config.GetBool(PropertyIdH.RequireSellable, false);
            var cnts = Caches.MatCounts;
            S.Sys.Inventory.LoadRemainingMaterials(cnts);
            Expedition.BestPossibleMiningFactor(s, out var maxDepth);

            int lowestCount = int.MaxValue;
            double highestValue = double.MinValue;
            foreach (var child in sector.SO.Children) {
                if (!child.Type.IsDeposit) { continue; }
                if (!S.Sys.Planets.TryGetResourceFor(child, out var resource, out var mt)) {
                    continue;
                }

                if (child.IsVisited || S.Sys.Expedition.HasExpeditionTo(child)) {
                    // already has, or already had, an expedition sent to it
                    continue;
                }
                if (resource.AutoMineSpeed > 0f) {
                    // already has an auto miner deployed, mining it automatically
                    continue;
                }
                if (S.Query.IsBeingDrilled.Ask(child)) {
                    // currently being mined by the player's remote drill
                    continue;
                }
                if (!IsReachable(resource, mt, maxDepth)) {
                    // too deep relative to best shuttle
                    continue;
                }
                var val = S.Sys.Economy.WorthOf(mt, 1, true);
                if (requireSellable && val <= 0f) {
                    continue;
                }
                if (tryPlayerLowOn) {
                    var cnt = cnts.TryGetValue(mt, out var c) ? c : 0;
                    if (cnt < lowestCount) {
                        lowestCount = cnt;
                        deposit = child;
                        depositType = mt;
                    }
                } else {
                    if (val > highestValue) {
                        highestValue = val;
                        deposit = child;
                        depositType = mt;
                    }
                }
            }

            if (deposit == null) {
                (deposit, depositType) = TrySpawnFallbackDeposit(
                    s, sector, requireSellable, maxDepth, seed);
            }
            if (deposit == null) {
                throw new Exception($"Deposit could not be selected for ResourceDeposit var! {data}");
            }

            var setMatTypeIdVar = def.Config.GetString(PropertyIdH.DVar_MatTypeId);
            var d = data.GetOrCreateData();
            d.SetString(setMatTypeIdVar, depositType.Id);
            d.SetInt(def.DataKey(VarId), deposit.Id);
        }

        public QuestStepTarget GetTargetFor(QuestStep step) {
            return new QuestStepTarget() {
                Target = deposit,
                Step = step,
            };
        }

        public static int GetLargestShuttleStorageCapacity(GameState s) {
            int maxCapacity = 100;
            var allShuttles = s.Components.AllByType<ShuttleComp>();
            if (allShuttles == null) {
                return maxCapacity;
            }
            foreach (ShuttleComp shuttle in allShuttles) {
                if (!shuttle.IsConstructed || shuttle.IsLost) { continue; }
                var storage = shuttle.Tile?.MaterialStorage;
                if (storage == null) { continue; }
                if (storage.MaxCapacity > maxCapacity) {
                    maxCapacity = storage.MaxCapacity;
                }
            }
            return maxCapacity;
        }

        // 500m of reachable depth below best shuttle; surface/no-depth deposits always reachable
        private static bool IsReachable(in PlanetResource resource, MatType mt, float maxDepth) {
            if (!mt.Deposit.IsWithDepth || resource.MiningDepth <= 0f) { return true; }
            return maxDepth - resource.MiningDepth >= MinDepthHeadroomKm;
        }

        // Validator gate: a reachable, sellable, un-claimed existing deposit, or a sellable
        // material in the sector pool the fallback spawn can use.
        public static bool CanProvideSellableDeposit(GameState s, SpaceSector sector) {
            Expedition.BestPossibleMiningFactor(s, out var maxDepth);
            foreach (var child in sector.SO.Children) {
                if (!child.Type.IsDeposit) { continue; }
                if (!s.Sys.Planets.TryGetResourceFor(child, out var resource, out var mt)) {
                    continue;
                }
                if (child.IsVisited || s.Sys.Expedition.HasExpeditionTo(child)) { continue; }
                if (resource.AutoMineSpeed > 0f) { continue; }
                if (s.Query.IsBeingDrilled.Ask(child)) { continue; }
                if (!IsReachable(resource, mt, maxDepth)) { continue; }
                if (s.Sys.Economy.WorthOf(mt, 1, true) > 0f) { return true; }
            }
            return HasSellableSectorMaterial(s, sector);
        }

        private static bool HasSellableSectorMaterial(GameState s, SpaceSector sector) {
            var mats = sector.SO.Mats;
            if (Arrays.IsEmpty(mats)) { return false; }
            for (int i = 0; i < mats.Count; i++) {
                if (s.Sys.Economy.WorthOf(mats[i], 1, true) > 0f) { return true; }
            }
            return false;
        }

        private static (SpaceObject, MatType) TrySpawnFallbackDeposit(
                GameState s, SpaceSector sector, bool requireSellable, float maxDepth, int seed) {
            var mats = sector.SO.Mats;
            if (Arrays.IsEmpty(mats)) { return default; }
            MatType chosen = null;
            for (int i = mats.Count - 1; i >= 0; i--) {
                var mat = mats[i];
                if (requireSellable && s.Sys.Economy.WorthOf(mat, 1, true) <= 0f) { continue; }
                chosen = mat;
                break;
            }
            if (chosen == null) { return default; }
            var rng = new Rng(Hashes.Quick(seed, Primes.Prime017));
            var soType = SpaceObjectType.Find(SpaceObjectTypeIdH.Resource);
            var gen = GenUniverse.FindGenerator(soType.Id);
            var child = gen.Generate(s.Universe, UniverseLayer.SectorContents, soType, sector.SO);
            sector.SO.AddChild(child);
            s.Sys.Scan.FindLocationForNewResource(child, sector.SO, rng, 10);
            s.Sys.Planets.AssignMaterialToPOI(child, chosen);
            // give the spawned deposit very generous headroom below the best shuttle's reach
            if (s.Sys.Planets.TryGetResourceForUpdate(child, out var idx, out var resource)
                    && resource.MiningDepth > 0f) {
                var target = maxDepth > 0f ? Mathf.Max(0.1f, maxDepth - FallbackHeadroomKm) : -1f;
                if (target < 0f || resource.MiningDepth > target) {
                    resource.MiningDepth = target;
                    s.Sys.Planets.UpdateResource(idx, in resource);
                }
            }
            SOActionScan.ScanResource(child, out _);
            s.Sig.SpaceObjectAdded.Send(child);
            D.Warn("Spawned fallback quest deposit {0} ({1}) in {2}", child, chosen, sector.SO);
            return (child, chosen);
        }
    }
}
