using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Data.Planets;
using Game.Data.Space;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using Unity.Collections;
using Unity.Collections.NotBurstCompatible;
using Unity.Jobs;
using Unity.Mathematics;
using Game.Modifiers;
using Game.Systems.Ships;
using Game.Systems.AI.Combat;

namespace Game.Systems.Planets {
    public sealed class PlanetsSys : GameSystem, ISaveableSpecial {
        private const int arrayIncr = 256;
        public const string SysId = "PlanetsSys";
        public override string Id => SysId;

        public Entity Entity => null;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new PlanetsSys());
        }

        private NativeArray<PlanetResource> planetResources;

        private NativeArray<float> depthRateMulCurvePoints;
        private bool depthRateMulCurveBuilt;

        private int planetResourcesIdx;

        private PlanetsSysData data;

        public override void Unload() {
            if (planetResources.IsCreated) {
                planetResources.Dispose();
            }
            if (depthRateMulCurvePoints.IsCreated) {
                depthRateMulCurvePoints.Dispose();
            }
            depthRateMulCurveBuilt = false;
            // baseSpeedMan = 0;
            // baseSpeedAuto = 0;
            planetResourcesIdx = 0;
        }

        public bool TryGetAutoMiningRate(SpaceObject target, out PlanetResource resource,
                 out float miningRateWithOutputMul, out MatType mt, out float depth) {
            if (TryGetResourceFor(target, out resource, out mt)) {
                depth = resource.MiningDepth;
                if (resource.UnretrievedMax - resource.Unretrieved > 0.1f) {
                    miningRateWithOutputMul = GetAutoMiningRateFor(resource, true);
                } else {
                    miningRateWithOutputMul = 0f;
                }
                return true;
            }
            depth = 0f;
            miningRateWithOutputMul = 0f;
            return false;
        }

        private static float GetAutoMiningRateFor(PlanetResource resource, bool withOutputMul) {
            if (resource.AutoMineMaxDepth < resource.MiningDepth) {
                return 0f;
            }
            float miningRate;
            var speed = Tunables.AutoMiningSpeed * resource.AutoMineSpeed;
            var diff = resource.MiningDifficulty;
            miningRate = (speed / diff) * Consts.TicksPerDay;
            if (withOutputMul) {
                miningRate *= ModifierMiningOutput.Current;
            }
            return miningRate;
        }

        public bool TryGetMiningRate(SpaceObject target,
                out PlanetResource resource,
                out float miningRateWithOutputMul, out MatType mt, out float depth) {
            if (TryGetResourceFor(target, out resource, out mt)) {
                depth = resource.MiningDepth;
                miningRateWithOutputMul = GetManualMiningRate(resource, resource.ManualMineFactor,
                    mt.Deposit.IsWithDepth);
                return true;
            }
            depth = 0f;
            miningRateWithOutputMul = 0f;
            return false;
        }

        /// <returns>mining rate WITH output multiplier included (for display only)</returns>
        private static float GetManualMiningRate(in PlanetResource resource,
                float factor, bool isDepositWithDepth) {
            if (isDepositWithDepth && resource.ManualMineMaxDepth < resource.MiningDepth) {
                return 0f;
            }
            float miningRate;
            var speed = Tunables.ExpeditionMiningSpeed * factor;
            var diff = resource.MiningDifficulty;
            miningRate = (speed / diff) * Consts.TicksPerDay;
            miningRate *= ModifierMiningOutput.Current;
            return miningRate;
        }

        public bool TryEstimateExpeditionMiningRate(SpaceObject target,
                float miningFactor,
                out float miningRateWithOutputMul, out MatType mt) {
            if (TryGetResourceFor(target, out var resource, out mt)) {
                var speed = Tunables.ExpeditionMiningSpeed * miningFactor;
                var diff = resource.MiningDifficulty;
                miningRateWithOutputMul = (speed / diff) * Consts.TicksPerDay;
                miningRateWithOutputMul *= ModifierMiningOutput.Current;
                return true;
            }
            miningRateWithOutputMul = 0f;
            return false;
        }

        // Max material a depth-limited deposit can deliver before MiningDepth reaches maxDepth
        // and mining stalls (mirrors the depth/output relation in PlanetAutoMineJob). Returns
        // false for deposits with no depth cap (surface / non-IsWithDepth) — no limit applies.
        public bool TryGetMaxExtractableOutput(SpaceObject target, float maxDepth, out float maxOutput) {
            maxOutput = 0f;
            if (!TryGetResourceFor(target, out var resource, out var mt)) { return false; }
            if (!mt.Deposit.IsWithDepth || resource.MiningDepth < 0f) { return false; }
            var headroom = maxDepth - resource.MiningDepth;
            if (headroom <= 0f) { return true; }
            var rate = depthIncRate > 0f ? depthIncRate : Tunables.MiningDepthIncreaseRate;
            // Depth burn per unit scales with the difficulty curve, and difficulty keeps
            // rising while mining (expedition mining). The first-guess units below assumes
            // f=1, so this estimate is only conservative once difficulty runs past the
            // curve's 1.0 anchor (where f keeps rising with difficulty); below that anchor
            // it can slightly overshoot achievable yield.
            var units = headroom / rate;
            var endDiff = resource.MiningDifficulty + units * Tunables.MiningDifficultyIncrease;
            var mul = MiningDifficultyCurve.Evaluate(endDiff);
            maxOutput = headroom / (rate * mul) * ModifierMiningOutput.Current;
            return true;
        }

        public bool IsMinableResource(SpaceObject so, out int idx, out MatType mt) {
            if (so.Type.IdH != SpaceObjectTypeIdH.Resource &&
                    so.Type.IdH != SpaceObjectTypeIdH.ResourceUnknown) {
                idx = -1;
                mt = null;
                return false;
            }
            idx = so.Data.GetInt(PlanetResource.Idx, -1);
            if (idx == -1) {
                D.Err("Resource doesn't have Idx: {0}", so);
                mt = null;
                return false;
            }
            var mtHash = so.Data.GetInt(PlanetResource.MatType, 0);
            mt = MatType.FastGet(mtHash);
            if (mt == null) {
                D.Err("Resource doesn't have a MatType: {0}. Idx: {1}. MATHash: {2}",
                    so, idx, mtHash);
                return false;
            }
            return true;
        }

        public bool TryManualMine(SpaceObject target, Expedition exp, int maxAmt,
                out int availableAmt, out MatType mt, out float rateWithOutputMul, out float difficulty,
                out float depth) {
            if (!IsMinableResource(target, out var idx, out mt)) {
                availableAmt = 0;
                rateWithOutputMul = 0f;
                difficulty = 0f;
                depth = 0f;
                return false;
            }
            int pickupRate = 20 + exp.Crew.Count;
            float miningFactor = exp.MiningFactor;
            // Be sure to set the struct back to the array before returning from this method
            var resource = planetResources[idx];
            // Something has to reset this to 0 before every expedition tick
            // as multiple expeditions can contribute to manual mine factor
            float unretrieved = resource.Unretrieved;
            resource.ManualMineFactor = miningFactor;
            resource.ManualMineMaxDepth = exp.MiningMaxDepth;
            rateWithOutputMul = GetManualMiningRate(resource, miningFactor, mt.Deposit.IsWithDepth);
            difficulty = resource.MiningDifficulty;
            if (mt.Deposit.IsWithDepth) {
                depth = resource.MiningDepth;
            } else {
                depth = 0f;
            }
            if (unretrieved > 1f) {
                if (unretrieved < maxAmt) {
                    var frac = math.modf(unretrieved, out float i);
                    availableAmt = (int) i;
                    if (availableAmt > pickupRate) {
                        resource.Unretrieved = availableAmt - pickupRate + frac;
                        availableAmt = pickupRate;
                    } else {
                        resource.Unretrieved = frac;
                    }
                } else {
                    availableAmt = maxAmt;
                    resource.Unretrieved = unretrieved - maxAmt;
                }
                UpdateResource(idx, resource);
                return true;
            } else {
                availableAmt = 0;
                UpdateResource(idx, resource);
                return false;
            }
        }
        public bool TryGetResource(int idx, out PlanetResource resource) {
            if (idx < 0 || idx >= planetResourcesIdx) {
                resource = default;
                return false;
            }
            resource = planetResources[idx];
            return true;
        }

        public bool TryGetResourceFor(SpaceObject so, out PlanetResource resource, out MatType mt) {
            if (IsMinableResource(so, out var idx, out mt)) {
                resource = planetResources[idx];
                return true;
            }
            resource = default;
            return false;
        }

        public bool TryGetResourceForUpdate(SpaceObject so, out int idx, out PlanetResource resource) {
            if (IsMinableResource(so, out idx, out _)) {
                resource = planetResources[idx];
                return true;
            }
            resource = default;
            idx = -1;
            return false;
        }
        public bool TryGetResourceForUpdate(SpaceObject so, out int idx, out PlanetResource resource, out MatType mt) {
            if (IsMinableResource(so, out idx, out mt)) {
                resource = planetResources[idx];
                return true;
            }
            resource = default;
            idx = -1;
            return false;
        }

        public void UpdateResource(int idx, in PlanetResource resource) {
            planetResources[idx] = resource;
        }

        public void AssignMaterialToPOI(SpaceObject poiSO, MatType matType) {
            // try find an index of an invalid resource and reuse it
            int idx = -1;
            for (int i = 0; i < planetResourcesIdx; i++) {
                var res = planetResources[i];
                if (!res.IsValid) {
                    idx = i;
                    break;
                }
            }

            if (idx == -1) {
                Arrays.EnsureIndexExists(ref planetResources, planetResourcesIdx, arrayIncr);
                idx = planetResourcesIdx++;
            }

            var rng = new Rng(Hashes.Quick(poiSO.Seed, Primes.Prime053));
            var resource = planetResources[idx];
            resource.Id = poiSO.Id;
            resource.FactionIdH = poiSO.Faction?.IdH ?? 0;
            resource.PlanetId = poiSO.Parent.Id;
            resource.ResourceId = matType.IdH;
            resource.MiningDifficulty = matType.Deposit.MiningDifficulty * rng.Range(0.8f, 1.2f);
            if (matType.Deposit.IsWithDepth) {
                resource.MiningDepth = matType.Deposit.InitialDepthCurve.Evaluate(
                    poiSO.Parent.Sector.Biome.Difficulty * rng.Range(0.9f, 1.1f));
                // resource.MiningDepth = rng.Curve(matType.Deposit.DepthCurve);
            } else {
                resource.MiningDepth = -1f;
            }
            UpdateResource(idx, resource);

            poiSO.SetType(SpaceObjectType.Find(SpaceObjectTypeId.ResourceUnknown));
            poiSO.Data.SetInt(PlanetResource.Idx, idx);
            poiSO.Data.SetInt(PlanetResource.MatType, matType.IdH);

            // D.Warn("Creating planet resource for so: {0} -> {1}", poiSO, idx);
            // This is now assigned in expedition stage identify resource
            //poiSO.UpdateName(T.OreDeposit_V1.F(matType.NameT));
            //poiSO.Icon = matType.IconId;
            //poiSO.Data.SetString(SpaceObjectLooks.VarSprite, IconId.CPickaxe);
        }

        // returns number of POIs that were assigned (or just number of children)
        public int AssignDepositsFor(SpaceObject parent) {
            // 1: Resources
            // keeping it simple : all resources have a difficulty level depending on rarity?
            // how many resources will the planet have?

            // We want to assign children randomly in a predictable way
            var children = parent.Children.Where(
                c => c.Type.IdH == SpaceObjectTypeIdH.PlanetPOI).ToArray();
            var rng = new Rng(Hashes.Quick(parent.Seed, Primes.Prime038));
            Arrays.Shuffle(rng, children);

            var mats = parent.Mats;

            // We ensured that children count will be larger than mats, so we can do this:
            int assigned = 0;
            for (int i = 0; i < mats.Count; i++) {
                var child = Arrays.SafeGet(children, i);
                if (child == null) {
                    D.Err("Something went wrong, trying to assign POI number {0} while there are only {1} children! Aborting",
                        i, D.Dump(children));
                    return parent.Children.Count;
                }
                assigned++;
                if (parent.Faction != null) {
                    // D.Warn("Setting faction on deposit: {0}", child);
                    child.SetFaction(parent.Faction);
                }
                AssignMaterialToPOI(child, mats[i]);
            }

            return parent.Children.Count;
        }

        public string GetName() {
            return Id;
        }

        private float depthIncRate;

        private void OnLoadSave(GameState s) {
            var mul = ModifierResourceMul.Current;
            if (mul <= 0f) {
                D.Err("ResourceMul modifier value is <= 0f! {0}", mul);
                mul = 1f;
            }
            depthIncRate = Tunables.MiningDepthIncreaseRate;
            // D.Warn("Setting depth increase rate: {0}/{1}={2}",
            //     depthIncRate, mul, depthIncRate / mul);
            depthIncRate /= mul;

            RestoreData();
            s.Clock.OnTick.AddListener(OnTick);
        }

        private void RestoreData() {
            PrevalidateSavedResources();
            planetResourcesIdx = data?.PlanetResources?.Length ?? 0;
            nextMiningConsequenceAttackAt = data?.NextMiningConsequenceRaidAt ?? 0L;
            if (planetResources.IsCreated) {
                D.Err("Planet resources created before restoring!");
                planetResources.Dispose();
            }
            planetResources = new NativeArray<PlanetResource>(
                planetResourcesIdx + arrayIncr, Allocator.Persistent);

            if (data?.PlanetResources != null) {
                for (int i = data.PlanetResources.Length - 1; i >= 0; i--) {
                    PlanetResource res = data.PlanetResources[i];
                    if (!res.IsValid) { continue; }
                    var mat = MatType.FastGet(res.ResourceId);
                    if (mat == null) {
                        D.Err("Unknown resource type for planet deposit! {0} -> {1}",
                            res, MatIdH.ResolveName(res.ResourceId));
                    } else {
                        if (mat.Deposit.IsWithDepth) {
                            if (res.MiningDepth < 0f) {
                                res.MiningDepth = 0f;
                            }
                        } else {
                            if (res.MiningDepth >= 0f) {
                                res.MiningDepth = -1f;
                            }
                        }
                    }
                }
                Arrays.CopyToNative(data.PlanetResources, planetResources);
            }
            data = null;
        }

        private void PrevalidateSavedResources() {
            if (data?.PlanetResources == null) {
                foreach (var so in S.Universe.ObjectsById.Values) {
                    if (!so.HasData) { continue; }
                    if (!so.Data.TryGetInt(PlanetResource.Idx, out var pIdx)) {
                        continue;
                    }
                    D.ErrSoft("Planet resources aren't created but SO has pIdx: {0} -> {1} | {2}",
                        so, pIdx, so.ParentPath(false));
                    so.SetType(SpaceObjectType.Find(SpaceObjectTypeIdH.PlanetPOI));
                    so.Data.RemoveInt(PlanetResource.Idx);
                    so.Data.RemoveInt(PlanetResource.MatType);
                }
                return;
            }

            Caches.SpaceObjectList.Clear();
            foreach (var so in S.Universe.ObjectsById.Values) {
                if (!so.HasData) { continue; }
                if (!so.Data.TryGetInt(PlanetResource.Idx, out var pIdx)) {
                    continue;
                }
                var isValid = true;
                var matTypeIdH = so.Data.GetInt(PlanetResource.MatType, 0);
                if (matTypeIdH == 0) {
                    isValid = false;
                    D.Err("SO has resource with undefined mat type: {0}", so);
                }
                var res = Arrays.SafeGet(data.PlanetResources, pIdx, default);
                if (!res.IsValid) {
                    isValid = false;
                    D.Err("SO links to invalid resource! #{0}: {1}: {2}", pIdx, so, res);
                }
                if (res.PlanetId != so.Parent?.Id) {
                    isValid = false;
                    D.Err("SO links to planet resource with different parent id! {0}: {1}",
                        so, res);
                }
                if (res.FactionIdH == 0 && so.Faction != null) {
                    D.ErrSoft("SO has a faction but resource doesn't: {0} | {1} | {2}",
                        pIdx, so, res);
                    res.FactionIdH = so.Faction.IdH;
                    data.PlanetResources[pIdx] = res;
                } else if (res.FactionIdH != 0 && so.Faction == null) {
                    D.ErrSoft("SO has no faction but resource has it: {0} | {1} | {2}",
                        pIdx, so, res);
                    res.FactionIdH = 0;
                    data.PlanetResources[pIdx] = res;
                }
                if (!isValid) {
                    Caches.SpaceObjectList.Add(so);
                }
            }

            if (Caches.SpaceObjectList.Count > 0) {
                D.Err("Removing {0} invalid space objects with resource issues",
                    Caches.SpaceObjectList.Count);
                foreach (var so in Caches.SpaceObjectList) {
                    D.Err("Removing invalid resource SO: {0}", so);
                    SOUtils.RemoveSO(so);
                }
            }

            int removed = 0;
            for (int i = data.PlanetResources.Length - 1; i >= 0; i--) {
                var res = data.PlanetResources[i];
                if (!res.IsValid) { continue; }
                if (!S.Universe.TryFind(res.Id, out _)) {
                    removed++;
                    D.Warn("Validation: removing planet resource that has no corresponding SO: {0}", res);
                    data.PlanetResources[i] = default;
                }
            }
            if (removed > 0) {
                D.Warn("Removed {0}/{1} broken planet resources",
                    removed, data.PlanetResources.Length);
            }
        }

        public void Reset() {
            Unload();
            planetResources = new NativeArray<PlanetResource>(
                planetResourcesIdx + arrayIncr, Allocator.Persistent);
        }

        private NativeList<int> minedOwnedResources;

        private void OnTick(long ticks) {
            var len = planetResourcesIdx;
            if (len == 0) { return; }
            bool checkConsequences = ticks % Consts.TicksPerHour == 17;
            var job = new PlanetAutoMineJob();
            if (!depthRateMulCurveBuilt) {
                BuildDepthRateMulCurvePoints();
            }
            job.DepthRateMulCurve = depthRateMulCurvePoints;
            minedOwnedResources = new NativeList<int>(Allocator.TempJob);
            job.MinedOwnedResources = minedOwnedResources;
            job.MarkMinedOwnedResources = checkConsequences;
            job.Count = len;
            job.BaseAutoMiningSpeed = Tunables.AutoMiningSpeed;
            job.BaseManualMiningSpeed = Tunables.ExpeditionMiningSpeed;
            job.DepthIncreaseRate = depthIncRate;
            job.ManualMiningDifficultyIncrease = Tunables.MiningDifficultyIncrease;
            job.AutoMiningDifficultyIncrease = Tunables.AutoMiningDifficultyIncrease;
            job.Resources = planetResources;
            job.MiningOutputMul = ModifierMiningOutput.Current;
            using (D.Timer("MineJob", 0.01f)) {
                job.Run();
            }
            if (checkConsequences) {
                ApplyMiningConsequences();
            }
            minedOwnedResources.Dispose();
        }

        private void BuildDepthRateMulCurvePoints() {
            var pts = MiningDifficultyCurve.Curve?.Points;
            if (pts == null || pts.Length == 0) {
                // No curve: 2-point flat fallback so the array is never empty and the
                // job's < 2 length check never has to run through an empty NativeArray.
                depthRateMulCurvePoints = new NativeArray<float>(
                    new[] { 0f, 1f, 1f, 1f }, Allocator.Persistent);
            } else {
                depthRateMulCurvePoints = new NativeArray<float>(
                    pts.Length * 2, Allocator.Persistent);
                for (int i = pts.Length - 1; i >= 0; i--) {
                    depthRateMulCurvePoints[i * 2] = pts[i].x;
                    depthRateMulCurvePoints[i * 2 + 1] = math.max(
                        pts[i].y, MiningDifficultyCurve.MinMul);
                }
            }
            depthRateMulCurveBuilt = true;
        }

        private long nextMiningConsequenceAttackAt;
        internal long NextMiningConsequenceAttackAt {
            get => nextMiningConsequenceAttackAt;
            set => nextMiningConsequenceAttackAt = value;
        }

        private void ApplyMiningConsequences() {
            if (minedOwnedResources.Length == 0) {
                // if we set this to zero here, this won't work with beam drills
                // nextMiningConsequenceAttackAt = 0;
                return;
            }
            for (int i = minedOwnedResources.Length - 1; i >= 0; i--) {
                var soId = minedOwnedResources[i];
                var so = S.Universe.Find(soId);
                if (so.Faction == null) {
                    D.Err("Expected to have a faction on {0} but there is none", so);
                    continue;
                }
                if (so.Faction.IsPlayer) {
                    D.Err("Expected to have a non-player faction on {0} but it is player", so);
                    continue;
                }
                if (TryGetResourceFor(so, out var res, out var mt)) {
                    ApplyMiningConsequence(so, mt);
                }
            }
        }

        public void ApplyMiningConsequence(SpaceObject so, MatType mt) {
            if (so.Faction == null) { return; }
            if (so.Faction.IsPlayer) { return; }
            // Allies let us mine their deposits. This has to gate the whole method, not
            // just the attitude hit below - otherwise they would still send raiders.
            if (S.Sys.Diplomacy.IsAlliedWithPlayer(so.Faction)) { return; }
            S.Sys.Diplomacy.OnMineResource(so.Faction, mt);
            if (so.Parent != S.Universe.Player.Parent) {
                return;
            }

            if (nextMiningConsequenceAttackAt == 0) {
                nextMiningConsequenceAttackAt = S.Ticks
                    + Rng.URange(Consts.TicksPer1Hours, Consts.TicksPer6Hours);
            }
            // D.Warn("Applying mining consequence for {0} | in {1}", so,
            //     Units.TicksToTime(nextMiningConsequenceAttackAt - G.Ticks));
            if (G.Ticks > nextMiningConsequenceAttackAt) {
                // we have't mined owned resources for more than a day, let's reset
                // back to 0
                if (G.Ticks - nextMiningConsequenceAttackAt > Consts.TicksPerDay) {
                    nextMiningConsequenceAttackAt = 0;
                    return;
                }
                // D.Warn("Applying mining consequence for {0} NOW", so);
                nextMiningConsequenceAttackAt = S.Ticks
                    + Rng.URange(Consts.TicksPer16Hours, Consts.TicksPer32Hours);
                AttackPlayer(so, mt);
            }
        }

        private void AttackPlayer(SpaceObject so, MatType mt) {
            // D.Warn("Will attack player for mining an owned deposit: {0}", so);
            if (A.Player.Ship.IsDerelict) {
                CombatSys.StartBreachCapsuleRaid(S, so.Faction);
                return;
            }
            // D.Warn("Will send a ship to attack the player: {0}", so);
            var playerSector = SOUtils.GetPlayerSector(S);
            var ships = S.Query.NPCShipsAt.Ask(playerSector);
            NPCShip sent = null;
            if (!so.Faction.IsOutlaw) {
                sent = SendAttacker(so, mt, ships, true);
            }
            if (sent == null) {
                sent = SendAttacker(so, mt, ships, false);
            }
            if (sent == null) {
                float breachChance;
                if (!A.IsCombatOn) {
                    breachChance = 0f;
                } else if (A.Player.Ship.IsInCombat) {
                    breachChance = 0.66f;
                } else {
                    breachChance = 0.33f;
                }
                if (Rng.UChance(breachChance)) {
                    CombatSys.StartBreachCapsuleRaid(S, so.Faction);
                    return;
                }

                var tpl = new NPCShip();
                tpl.Faction = so.Faction;
                if (!tpl.Faction.IsOutlaw) {
                    tpl.IsPolice = true;
                }
                var spawned = S.Query.SpawnOpponentShipFor.Ask(A.Player.Ship, tpl);
                if (spawned != null) {
                    if (playerSector.SO.HasChildren) {
                        foreach (var obj in playerSector.SO.Children) {
                            if (obj.Type.IdH == SpaceObjectTypeIdH.Port) {
                                spawned.TeleportToSectorLocation(obj.Location);
                                break;
                            }
                        }
                    }
                    NPCShipBehavior_Sector_Combat.AssignTo(spawned, A.Player.Ship);
                }
            }
        }

        private NPCShip SendAttacker(SpaceObject so, MatType mt,
                List<NPCShip> ships, bool tryPolice) {
            foreach (var ship in ships) {
                if (ship.IsDerelict) { continue; }
                // Police of any lawful faction answer for the owner, but never against
                // the player while allied with them - same veto as joining a fight
                if (S.Sys.Diplomacy.IsAlliedWithPlayer(ship.Faction)) { continue; }
                if (ship.IsInCombat) { continue; }
                if (tryPolice && !ship.IsPolice) { continue; }
                if (ship.Class.IsSpaceStation) { continue; }
                if (ship.Faction != so.Faction && (!ship.IsPolice || so.Faction.IsOutlaw)) {
                    continue;
                }
                if (ship.IsPlayer) {
                    D.Err("Ship is player, faction of enemy! skipping: {0}", ship);
                    continue;
                }
                if (ship.BehaviorSector?.IdH != NPCShipBehavior_Sector_Combat.BehaviorIdH
                        || ship.Memory.FollowTarget != A.Player) {
                    D.Warn("Player will get attacked by {0} due to mining {1}",
                        ship, mt);
                    NPCShipBehavior_Sector_Combat.AssignTo(ship, A.Player.Ship);
                    return ship;
                }
            }
            return null;
        }

        protected override void OnInitialize() {
            S.Sig.AfterLoadState.AddListener(OnLoadSave);
        }

        public void SaveSpecial(SystemsDataSpecial sd) {
            sd.Planets ??= new PlanetsSysData();
            sd.Planets.NextMiningConsequenceRaidAt = nextMiningConsequenceAttackAt;
            if (planetResourcesIdx > 0) {
                sd.Planets.PlanetResources =
                    planetResources.Slice(0, planetResourcesIdx).ToArray();
            }
        }

        public void LoadSpecial(SystemsDataSpecial sd) {
            data = sd.Planets;
        }

        public static string TooltipForResource(in PlanetResource res, MatType mt) {
            var diff = T.MiningDifficultyValue_V1.F(
                res.MiningDifficulty.ToString("0.#"));
            if (res.Unretrieved >= 3f && res.UnretrievedMax > 0) {
                var unrInt = (int) math.ceil(res.Unretrieved);
                var cap = Units.XOutOfY(unrInt, res.UnretrievedMax);
                if (mt.Deposit.IsWithDepth) {
                    return string.Format("<b>{0}</b> ({1}). {2} @ {3}", mt.Def.CodexLink,
                        Texts.Green(cap), diff, Units.Depth(res.MiningDepth));
                } else {
                    return string.Format("<b>{0}</b> ({1}). {2}", mt.Def.CodexLink,
                        Texts.Green(cap), diff);
                }
            } else {
                if (mt.Deposit.IsWithDepth) {
                    return string.Format("<b>{0}</b>. {1} @ {2}", mt.Def.CodexLink, diff,
                        Units.Depth(res.MiningDepth));
                } else {
                    return string.Format("<b>{0}</b>. {1}", mt.Def.CodexLink, diff);
                }
            }
        }
    }
}
