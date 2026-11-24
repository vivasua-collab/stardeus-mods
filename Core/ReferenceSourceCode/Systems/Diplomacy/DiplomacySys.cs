using System.Collections.Generic;
using Game.Components.Upgrades;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.Systems.Trade;
using Unity.Mathematics;

namespace Game.Systems.Diplomacy {
    /*
    When player identifies a space object that belongs to a faction, the faction
    will be introduced in the diplomacy system. Initial relationship will be
    randomized and established.
    */
    public sealed class DiplomacySys : GameSystem, ISaveableSpecial {
        public const string SysId = "DiplomacySys";
        public override string Id => SysId;
        private readonly Dictionary<FactionDef, Dictionary<FactionDef, float>> attitudes = new();
        private readonly FactionRelations relations = new();
        private readonly Dictionary<FactionDef, long> allianceCooldowns = new();
        private FactionRelationSim sim;
        private Curve allianceAcceptanceCurve;
        private int universeGen;
        /// <summary>
        /// Whether the save we are loading actually contained relation data. This, and
        /// not relations.Count, decides whether first-time seeding is needed: Count
        /// cannot tell "no saved data" from "leftover runtime state".
        /// </summary>
        private bool hadSavedRelations;
        /// <summary>
        /// Set while seeding and while reconciling the Asimov alliance. Those transitions
        /// are not events the player caused, so they must not spam the ship log: seeding
        /// would report the whole starting diplomatic map at turn 0, and the Asimov
        /// alliance is already announced by AsimovOverrideUpgrade itself.
        /// </summary>
        private bool suppressRelationLog;

        public FactionRelations Relations => relations;
        public FactionRelationSim Sim => sim;
        public int UniverseGen => universeGen;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new DiplomacySys());
        }

        protected override void OnInitialize() {
            sim = new FactionRelationSim(this);
            S.Sig.AfterLoadState.AddListener(OnLoadSave);
        }

        private void OnLoadSave(GameState state) {
            state.Clock.OnTick.AddListener(OnTick);
            state.Query.FactionAttitude.SetResponder(GetFactionAttitude);
            state.Query.ChangeFactionAttitude.SetResponder(ChangeFactionAttitude);
            state.Sig.UniverseRegenerated.AddListener(OnUniverseRegen);
            state.Sig.TileUpgradeChanged.AddListener(OnTileUpgradeChanged);
            FactionDef.Player.OverrideName(state.GameName);
            // AfterLoadState runs after AfterLoad, where UpgradesComp restores installed
            // upgrades, so HasAsimovOverride is settled by the time we reconcile below.
            suppressRelationLog = true;
            SeedAttitudes();
            sim.ApplyLockedEnemies();
            if (!hadSavedRelations) {
                sim.EvaluateAllPairsOnce();
            }
            ReconcileAsimovAlliance();
            suppressRelationLog = false;
        }

        private void OnUniverseRegen() {
            // Nothing else in GameState reliably changes on regeneration - Regenerate()
            // never writes its seed back into S.SeedSpace - so we bump our own counter
            // to give the derived era bias schedule a fresh trajectory.
            universeGen++;
            ClearRuntimeState();
            suppressRelationLog = true;
            SeedAttitudes();
            sim.ApplyLockedEnemies();
            sim.EvaluateAllPairsOnce();
            ReconcileAsimovAlliance();
            suppressRelationLog = false;
            S.Sig.FactionRelationChanged.Send(null, null);
        }

        /// <summary>
        /// Deliberately does not touch universeGen - its whole job is to differ from the
        /// previous universe, so it must survive a regeneration.
        /// </summary>
        private void ClearRuntimeState() {
            attitudes.Clear();
            relations.Clear();
            allianceCooldowns.Clear();
        }

        /// <summary>
        /// Materialises every NPC-sourced diplomatic pair upfront instead of relying on
        /// lazy creation. Idempotent: GetFactionAttitude only creates missing entries, so
        /// existing saved values are preserved and pairs added by a mod or content update
        /// are filled in. Never uses the player as a source - that direction is not
        /// modelled and has no FactionAttitudeDef.
        /// </summary>
        private void SeedAttitudes() {
            var factions = relations.DiplomaticFactions;
            for (int i = 0; i < factions.Count; i++) {
                var source = factions[i];
                if (source.IsPlayer) { continue; }
                for (int j = 0; j < factions.Count; j++) {
                    var target = factions[j];
                    if (source == target) { continue; }
                    GetFactionAttitude(source, target);
                }
            }
        }

        private void OnTileUpgradeChanged(Upgrade upgrade, Tile tile, bool installed) {
            if (upgrade == null || upgrade.UpgradeId != AsimovOverrideUpgrade.Id) { return; }
            ReconcileAsimovAlliance();
        }

        /// <summary>
        /// The Asimov Override forces an alliance with the Virus faction. When it is gone
        /// we only step back to Neutral if we are still Allied, so we never stomp an Enemy
        /// state we did not create.
        /// </summary>
        private void ReconcileAsimovAlliance() {
            var virus = FactionDef.FastGet(FactionIdH.Virus);
            var player = FactionDef.Player;
            if (virus == null || player == null) { return; }
            var wasSuppressed = suppressRelationLog;
            // The upgrade announces itself with its own dialog and log line, and this
            // alliance was never "proposed", so the generic relation log would mislead.
            suppressRelationLog = true;
            if (A.HasAsimovOverride) {
                SetRelationAndReport(virus, player, FactionRelation.Allied);
            } else if (relations.Get(virus, player) == FactionRelation.Allied) {
                SetRelationAndReport(virus, player, FactionRelation.Neutral);
            }
            suppressRelationLog = wasSuppressed;
        }

        public void AddTrade(TradeUI trade) {
            if (!trade.IsExecuted) { return; }
            if (trade.Trader.Faction.IdH == FactionIdH.Trader) { return; }
            var impact = trade.SaleImpact;
            var change = math.lerp(0.0f, 0.05f, math.saturate(impact));
            if (!Maths.Approximately(change, 0f)) {
                ChangeFactionAttitude(trade.Trader.Faction, FactionDef.Player, change);
            }
        }
        public void OnMineResource(FactionDef faction, MatType mat) {
            var rarity = mat.MatRarity + 1;
            var change = -0.0002f * rarity;
            ChangeFactionAttitude(faction, FactionDef.Player, change);
        }

        /// <param name="a">of</param>
        /// <param name="b">towards</param>
        private float GetFactionAttitude(FactionDef a, FactionDef b) {
            if (a == b) { return 1f; }
            if (!attitudes.TryGetValue(a, out var facAtts)) {
                facAtts = new();
                attitudes.Add(a, facAtts);
            }
            if (!facAtts.TryGetValue(b, out var att)) {
                if (a.IdH == FactionIdH.AntiPlayer) { return -1f; }
                var attDef = FactionAttitudeDef.Get(a.Id);
                if (attDef == null) { return 0f; }
                att = attDef.RandomAttitudeTowards(b, S.Rng);
                facAtts.Add(b, att);
                // D.Warn("Creating Attitude: {0} -> {1}: {2}",
                //     a.Id, b.Id, Units.PercentageSigned(facAtts[b]));
            }
            return att;
        }
        /// <param name="a">of</param>
        /// <param name="b">towards</param>
        private float ChangeFactionAttitude(FactionDef a, FactionDef b, float change) {
            if (a == b) {
                D.Err("Trying to change faction attitude for yourself {0}", a);
                return 1f;
            }
            if (a.IdH == FactionIdH.AntiPlayer) { return -1f; }
            var current = GetFactionAttitude(a, b);
            var newAtt = Mathf.Clamp(current + change, -1f, 1f);
            attitudes[a][b] = newAtt;
            if (b.IsPlayer) {
                LogStandingChange(a, change, newAtt);
            }
            return newAtt;
        }

        private void LogStandingChange(FactionDef fac, float change, float standing) {
            var standingChangeStr = Units.PercentageSigned(change, fraction: 3);
            var standingStr = Units.PercentageSigned(standing);
            if (change >= 0f) {
                standingChangeStr = Texts.Green(standingChangeStr);
            } else {
                standingChangeStr = Texts.Red(standingChangeStr);
            }
            S.Sys.Log.AddLine(T.FactionStandingTip_V2.F(
                Texts.Accented(fac.NameT),
                    $"<nobr>{standingChangeStr} ({standingStr})</nobr>"));
        }

        private void OnTick(long ticks) {
            var interval = Tunables.DiplomacySimIntervalHours * Consts.TicksPerHour;
            if (interval <= 0) { return; }
            if (ticks % interval != 0) { return; }
            sim.Step(ticks);
        }

        /// <param name="a">of</param>
        /// <param name="b">towards</param>
        public float AttitudeOf(FactionDef a, FactionDef b) {
            return GetFactionAttitude(a, b);
        }

        public float ChangeAttitude(FactionDef a, FactionDef b, float change) {
            return ChangeFactionAttitude(a, b, change);
        }

        /// <summary>
        /// Symmetric relationship value used by all relation thresholds. Attitudes are
        /// directional, but a relation is not, so we take the min of both directions:
        /// a pair turns Enemy if either side drops far enough, an alliance breaks if
        /// either side sours, and an alliance can only form if both sides are warm.
        /// The player's own side is never modelled, so player pairs use the NPC's
        /// attitude towards the player.
        /// </summary>
        public float MutualAttitude(FactionDef a, FactionDef b) {
            if (a == null || b == null || a == b) { return 1f; }
            if (a.IsPlayer) { return GetFactionAttitude(b, a); }
            if (b.IsPlayer) { return GetFactionAttitude(a, b); }
            return math.min(GetFactionAttitude(a, b), GetFactionAttitude(b, a));
        }

        /// <summary>
        /// The single authoritative relation mutation path. Everything - the simulation,
        /// alliance proposals, breaks and the Asimov reconcile - goes through here, and
        /// nothing else sends FactionRelationChanged.
        /// </summary>
        public bool SetRelationAndReport(FactionDef a, FactionDef b, FactionRelation rel) {
            var previous = relations.Get(a, b);
            if (!relations.Set(a, b, rel)) { return false; }
            S.Sig.FactionRelationChanged.Send(a, b);
            // suppressRelationLog is also set while ReconcileAsimovAlliance forces the Virus
            // alliance - that one was never negotiated, so it should not count either.
            if (rel == FactionRelation.Allied && (a.IsPlayer || b.IsPlayer)
                    && !suppressRelationLog) {
                The.AchievementTracker.Unlock(
                    Achievements.AchievementFactionAlliance.AchievementId);
            }
            if (!suppressRelationLog && (a.IsPlayer || b.IsPlayer)) {
                LogRelationChange(a.IsPlayer ? b : a, previous, rel);
            }
            return true;
        }

        private void LogRelationChange(FactionDef fac, FactionRelation from,
                FactionRelation to) {
            var name = Texts.Accented(fac.NameT);
            switch (to) {
                case FactionRelation.Allied:
                    S.Sys.Log.AddLine(Texts.Green(T.DiplomacyAllianceAccepted_V1.F(name)));
                    break;
                case FactionRelation.Enemy:
                    S.Sys.Log.AddLine(Texts.Red(T.DiplomacyRelationBecameEnemy_V1.F(name)));
                    break;
                case FactionRelation.Neutral:
                    if (from == FactionRelation.Allied) {
                        S.Sys.Log.AddLine(Texts.Red(T.DiplomacyAllianceBroken_V1.F(name)));
                    } else {
                        S.Sys.Log.AddLine(T.DiplomacyRelationNoLongerEnemy_V1.F(name));
                    }
                    break;
            }
        }

        public long AllianceCooldownUntil(FactionDef fac) {
            if (fac == null) { return 0L; }
            return allianceCooldowns.TryGetValue(fac, out var until) ? until : 0L;
        }

        public float AllianceAcceptanceChance(FactionDef fac) {
            allianceAcceptanceCurve ??= CurveDef.Get(
                "Diplomacy/AllianceAcceptanceChance", false);
            if (allianceAcceptanceCurve == null) { return 0f; }
            var standing = MutualAttitude(FactionDef.Player, fac);
            return Mathf.Clamp01(allianceAcceptanceCurve.Evaluate(standing));
        }

        public bool CanProposeAlliance(FactionDef fac, out string reason) {
            reason = null;
            var player = FactionDef.Player;
            if (fac == null || player == null || fac == player
                    || !FactionRelations.IsDiplomatic(fac)) {
                reason = T.OperationForbidden;
                return false;
            }
            if (relations.Get(fac, player) == FactionRelation.Allied) {
                reason = T.OperationForbidden;
                return false;
            }
            if (!relations.CanAlly(player, fac)) {
                reason = T.DiplomacyAllianceImpossible;
                return false;
            }
            var until = AllianceCooldownUntil(fac);
            if (G.Ticks < until) {
                reason = T.DiplomacyProposeCooldown_V1.F(
                    Units.TicksToTime(until - G.Ticks, true));
                return false;
            }
            return true;
        }

        /// <summary>
        /// Guarded here rather than only in the UI, so quests, story events and mods
        /// cannot bypass the cooldown or the structural restrictions.
        /// </summary>
        public bool TryProposeAlliance(FactionDef fac) {
            if (!CanProposeAlliance(fac, out _)) { return false; }
            var player = FactionDef.Player;
            allianceCooldowns[fac] = G.Ticks
                + (long)Tunables.AllianceProposalCooldownHours * Consts.TicksPerHour;
            if (!S.Rng.Chance(AllianceAcceptanceChance(fac))) {
                S.Sys.Log.AddLine(Texts.Red(
                    T.DiplomacyAllianceRejected_V1.F(Texts.Accented(fac.NameT))));
                // A turned-down approach costs a little standing, so spamming proposals
                // at a faction that is unlikely to accept is not free. Because the
                // acceptance curve is read from standing, each refusal also makes the
                // next attempt slightly less likely.
                ChangeFactionAttitude(fac, player,
                    Tunables.AllianceRejectStandingPenalty);
                return false;
            }
            SetRelationAndReport(fac, player, FactionRelation.Allied);
            return true;
        }

        /// <summary>
        /// Returns false when there is no alliance to break, which makes a confirmation
        /// popup left open across an auto-break a harmless no-op instead of a second
        /// standing penalty.
        /// </summary>
        public bool BreakAlliance(FactionDef fac) {
            var player = FactionDef.Player;
            if (fac == null || player == null) { return false; }
            if (relations.Get(fac, player) != FactionRelation.Allied) { return false; }
            if (relations.IsAsimovAllianceActive(fac, player)) { return false; }
            SetRelationAndReport(fac, player, FactionRelation.Neutral);
            ChangeFactionAttitude(fac, player, Tunables.AllianceBreakStandingPenalty);
            allianceCooldowns[fac] = G.Ticks
                + (long)Tunables.AllianceBreakCooldownHours * Consts.TicksPerHour;
            return true;
        }

        public override void Unload() {
            // Otherwise the state will be dangling within the dataBlock
            FactionDef.Player?.OverrideName(null);
        }

        public float RelationshipWith(FactionDef fac) {
            if (fac == null) {
                D.Err("Trying to get null faction!");
                return 0f;
            }
            return GetFactionAttitude(fac, FactionDef.Player);
        }

        public bool IsAlliedWithPlayer(FactionDef fac) {
            if (fac == null) { return false; }
            return relations.Get(fac, FactionDef.Player) == FactionRelation.Allied;
        }

        /// <summary>
        /// Whether the given faction is hostile towards the player by its own rules:
        /// born-hostile (Pirate/Virus/Scavenger), explicitly anti-player, or currently
        /// at war. Shared by derelict survivor handling so "is this wreck's crew
        /// hostile" is decided in one place.
        /// </summary>
        public bool IsHostileTowardsPlayer(FactionDef fac) =>
            fac != null && (fac.Friendliness < 0f || fac.IsAntiPlayer
                || relations.Get(fac, FactionDef.Player) == FactionRelation.Enemy);

        public FactionDef RandomFactionForTrade(HashSet<FactionDef> except = null,
                bool fallback = false) {
            if (!fallback) {
                Caches.WeightedFactions.Clear();
                S.Sys.Universe.FactionsIn(S.Universe.Player.Parent,
                    Caches.WeightedFactions, FactionIdH.Adventurer);
            }
            // 1. if we have negative relationships with some faction already,
            // let's have a chance to pick one of those
            Dictionary<FactionDef, float> traders = new();
            // 2. pick random faction by their aggressiveness
            foreach (var f in Caches.WeightedFactions) {
                if (f.Key.IsInternal) { continue; }
                if (f.Key.Friendliness > 0) {
                    if (except?.Contains(f.Key) == true) { continue; }
                    var chance = 1f + f.Key.Friendliness * 0.5f
                        + RelationshipWith(f.Key);
                    if (chance > 0f) {
                        traders.Add(f.Key, f.Value * chance);
                    }
                }
            }
            if (!fallback && traders.Count == 0)  {
                return RandomFactionForTrade(except, true);
            }
            if (except != null && traders.Count == 0) {
                D.Err("Could not find a second faction, skipping exceptions list");
                return RandomFactionForTrade(null);
            }
            if (traders.Count == 0) {
                return FactionDef.FastGet(FactionIdH.Capitalist);
            }
            return S.Rng.WeightedFrom(traders);
        }

        public FactionDef RandomFactionForRaid(HashSet<FactionDef> except = null,
                bool forceHumans = false, bool fallback = false) {
            if (!fallback) {
                Caches.WeightedFactions.Clear();
                S.Sys.Universe.FactionsIn(S.Universe.Player.Parent,
                    Caches.WeightedFactions, FactionIdH.Pirate);
            }

            var sectorDiff = S.Situation.SectorDifficulty;

            // 1. if we have negative relationships with some faction already,
            // let's have a chance to pick one of those
            Dictionary<FactionDef, float> enemies = new();
            if (S.Rng.Chance(0.33f)) {
                foreach (var f in FactionDef.Known.Values) {
                    if (!Caches.WeightedFactions.TryGetValue(f, out var fWeight)) {
                        if (!fallback) {
                            continue;
                        }
                    }
                    if (!fallback && fWeight <= 0f) { continue; }
                    if (except?.Contains(f) == true) { continue; }
                    if (f.IsInternal || f.IsPlayer) { continue; }
                    if (RogueAIUtils.IsFactionEliminated(f)) { continue; }
                    if (IsAlliedWithPlayer(f)) { continue; }
                    if (!f.CanRaid) { continue; }
                    if (forceHumans && f.AI > 0.95f) { continue; }
                    var regionWeight = f.RegionDifficultyCurve?.Evaluate(sectorDiff) ?? 1f;
                    if (regionWeight <= 0f) { continue; }
                    var standing = RelationshipWith(f);
                    if (standing < 0f) {
                        var chance = 0.1f - standing;
                        if (chance > 0f) {
                            enemies.Add(f, chance * (fWeight + 0.1f) * regionWeight);
                        }
                    }
                }
                if (enemies.Count > 0) {
                    return S.Rng.WeightedFrom(enemies);
                }
            }
            // 2. pick random faction by their aggressiveness
            foreach (var f in FactionDef.Known.Values) {
                if (except?.Contains(f) == true) { continue; }
                if (!Caches.WeightedFactions.TryGetValue(f, out var fWeight)) {
                    if (!fallback) { continue; }
                }
                if (!fallback && fWeight <= 0f) { continue;}
                if (f.IsInternal || f.IsPlayer) { continue; }
                if (IsAlliedWithPlayer(f)) { continue; }
                if (!f.CanRaid) { continue; }
                if (forceHumans && f.AI > 0.95f) { continue; }
                var regionWeight = f.RegionDifficultyCurve?.Evaluate(sectorDiff) ?? 1f;
                if (regionWeight <= 0f) { continue; }
                var fr = f.Friendliness + RelationshipWith(f);
                if (fr <= 0) {
                    var chance = 0.1f - fr;
                    if (chance > 0f) {
                        enemies.Add(f, chance * (fWeight + 0.1f) * regionWeight);
                    }
                }
            }
            if (!fallback && enemies.Count == 0) {
                // fallback!
                return RandomFactionForRaid(except, forceHumans, true);
            }
            if (except != null && enemies.Count == 0) {
                if (fallback) {
                    #if !PRODUCTION_BUILD
                    D.ErrSoft("Could not find a additional faction, skipping exceptions list. Force humans: {0}. Except: {1}. Factions: {2}",
                        forceHumans, D.Dump(except), D.Dump(FactionDef.Known));
                    #endif
                }
                return RandomFactionForRaid(null, forceHumans: forceHumans);
            }
            if (!S.Rng.TryWeightedFrom(enemies, out var res)) {
                res = FactionDef.FastGet(FactionIdH.Pirate);
            }
            return res;
        }

        public string GetName() {
            return "DiplomacySys";
        }

        public void SaveSpecial(SystemsDataSpecial sd) {
            sd.Diplomacy ??= new();
            var atts = sd.Diplomacy.Attitudes = new();
            foreach (var attitude in attitudes) {
                var attsTowards = new Dictionary<string, float>();
                atts.Add(attitude.Key.Id, attsTowards);
                foreach (var attTowards in attitude.Value) {
                    attsTowards.Add(attTowards.Key.Id, attTowards.Value);
                }
            }
            sd.Diplomacy.Relations = relations.ToSaveData();
            var cooldowns = sd.Diplomacy.AllianceCooldowns = new();
            foreach (var cooldown in allianceCooldowns) {
                cooldowns.Add(cooldown.Key.Id, cooldown.Value);
            }
            sd.Diplomacy.UniverseGen = universeGen;
        }

        public void LoadSpecial(SystemsDataSpecial sd) {
            // Never assume this system is a fresh instance - clear before restoring so a
            // load-into-load cannot merge the previous game's state into this one.
            ClearRuntimeState();
            var data = sd.Diplomacy;
            hadSavedRelations = data?.Relations != null;
            if (data == null) { return; }
            universeGen = data.UniverseGen;
            var atts = data.Attitudes;
            if (atts != null) {
                foreach (var attitude in atts) {
                    var fac = FactionDef.Get(attitude.Key);
                    if (fac == null) {
                        D.Err("Faction not found: {0}", attitude.Key);
                        continue;
                    }
                    var attsTowards = new Dictionary<FactionDef, float>();
                    attitudes[fac] = attsTowards;
                    foreach (var attTowards in attitude.Value) {
                        var fac2 = FactionDef.Get(attTowards.Key);
                        if (fac2 == null) {
                            D.Err("Faction not found: {0}", attTowards.Key);
                            continue;
                        }
                        attsTowards[fac2] = attTowards.Value;
                    }
                }
            }
            relations.FromSaveData(data.Relations);
            if (data.AllianceCooldowns != null) {
                foreach (var cooldown in data.AllianceCooldowns) {
                    var fac = FactionDef.Get(cooldown.Key);
                    if (fac == null) {
                        D.Err("Faction not found: {0}", cooldown.Key);
                        continue;
                    }
                    allianceCooldowns[fac] = cooldown.Value;
                }
            }
            // D.Warn("Loaded diplomacy attitudes: {0}", D.DumpCollection(attitudes));
        }
    }
}
