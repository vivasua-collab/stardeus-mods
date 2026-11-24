using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Systems.Diplomacy {
    /// <summary>
    /// Slow NPC-vs-NPC diplomatic simulation. Runs one Step() every
    /// DiplomacySimIntervalHours of game time.
    ///
    /// Attitudes drift towards a per-pair "era target" that is re-derived every
    /// DiplomacyEraHours. The target is a pure function of the seeds, the universe
    /// generation counter, the era index and the faction pair - it is never rolled from
    /// S.Rng and never stored, so it survives save/load unchanged and cannot perturb
    /// unrelated randomness.
    /// </summary>
    public sealed class FactionRelationSim {
        private readonly DiplomacySys sys;

        public FactionRelationSim(DiplomacySys sys) {
            this.sys = sys;
        }

        private GameState S => sys.S;

        public long EraIndex(long ticks) {
            var eraTicks = (long)Tunables.DiplomacyEraHours * Consts.TicksPerHour;
            if (eraTicks <= 0) { return 0; }
            return ticks / eraTicks;
        }

        /// <summary>
        /// One shared bias per unordered pair per era. It must be shared rather than
        /// rolled per direction: MutualAttitude takes the min of both directions, so
        /// independent rolls would let a single bad roll create enmity while an alliance
        /// would need two simultaneous good ones.
        /// </summary>
        public float EraBias(FactionDef a, FactionDef b, long eraIndex) {
            var lo = a;
            var hi = b;
            if (lo.IdH > hi.IdH) {
                lo = b;
                hi = a;
            }
            // S.Seed is the ship seed, S.SeedSpace the universe seed - both are needed,
            // and UniverseGen distinguishes regenerated universes (Regenerate() never
            // writes its seed back into S.SeedSpace).
            var seed = Hashes.Quick(S.Seed, S.SeedSpace, sys.UniverseGen, (int)eraIndex);
            seed = Hashes.Quick(seed, lo.IdH, hi.IdH);
            var spread = Tunables.DiplomacyEraSpread;
            return new Rng(seed).Range(-spread, spread);
        }

        public float EraTarget(FactionDef a, FactionDef b, long eraIndex) {
            var attDef = FactionAttitudeDef.Get(a.Id);
            if (attDef == null) { return 0f; }
            var baseline = attDef.AverageAttitudeTowards(b);
            return math.clamp(baseline + EraBias(a, b, eraIndex), -1f, 1f);
        }

        public void Step(long ticks) {
            var eraIndex = EraIndex(ticks);
            DriftAttitudes(eraIndex);
            EvaluateRelations();
        }

        /// <summary>
        /// NPC -> NPC only. NPC -> Player standing stays purely event driven (trade,
        /// gifts, combat, quests) so this cannot silently move raid targeting, relay
        /// tolls or trade pricing. Player -> NPC is never modelled at all.
        /// </summary>
        private void DriftAttitudes(long eraIndex) {
            var step = Tunables.NPCAttitudeStepPerSim;
            if (step <= 0f) { return; }
            var factions = sys.Relations.DiplomaticFactions;
            for (int i = 0; i < factions.Count; i++) {
                var a = factions[i];
                if (a.IsPlayer) { continue; }
                for (int j = 0; j < factions.Count; j++) {
                    var b = factions[j];
                    if (a == b || b.IsPlayer) { continue; }
                    var current = sys.AttitudeOf(a, b);
                    var target = EraTarget(a, b, eraIndex);
                    var delta = Mathf.MoveTowards(current, target, step) - current;
                    if (Maths.Approximately(delta, 0f)) { continue; }
                    sys.ChangeAttitude(a, b, delta);
                }
            }
        }

        /// <summary>
        /// Exactly one terminal transition per pair per step. The current relation is
        /// read once so a pair can never chain two transitions in a single pass.
        /// </summary>
        private void EvaluateRelations() {
            var enemyThreshold = Tunables.RelationEnemyThreshold;
            var enemyClearThreshold = Tunables.RelationEnemyClearThreshold;
            var breakThreshold = Tunables.AllianceBreakThreshold;
            var formThreshold = Tunables.NPCAllianceFormThreshold;
            var formChance = Tunables.NPCAllianceFormChance;
            var relations = sys.Relations;
            foreach (var (a, b) in relations.DiplomaticPairs) {
                // 0. Asimov-forced alliance: highest priority, terminal. Virus standing
                // towards the player can start at -0.8, below the Enemy threshold, so
                // this must precede both the locked-enemy and the Enemy rules.
                if (relations.IsAsimovAllianceActive(a, b)) {
                    sys.SetRelationAndReport(a, b, FactionRelation.Allied);
                    continue;
                }
                // 1. Permanently locked enemies: terminal.
                if (relations.IsLockedEnemy(a, b)) {
                    sys.SetRelationAndReport(a, b, FactionRelation.Enemy);
                    continue;
                }
                var current = relations.Get(a, b);
                var mutual = sys.MutualAttitude(a, b);
                switch (current) {
                    case FactionRelation.Neutral:
                        if (mutual <= enemyThreshold) {
                            sys.SetRelationAndReport(a, b, FactionRelation.Enemy);
                        } else if (CanFormNPCAlliance(a, b, mutual, formThreshold)
                                && S.Rng.Chance(formChance)) {
                            sys.SetRelationAndReport(a, b, FactionRelation.Allied);
                        }
                        break;
                    case FactionRelation.Allied:
                        if (mutual <= enemyThreshold) {
                            sys.SetRelationAndReport(a, b, FactionRelation.Enemy);
                        } else if (mutual <= breakThreshold) {
                            sys.SetRelationAndReport(a, b, FactionRelation.Neutral);
                        }
                        break;
                    case FactionRelation.Enemy:
                        if (mutual >= enemyClearThreshold) {
                            sys.SetRelationAndReport(a, b, FactionRelation.Neutral);
                        }
                        break;
                }
            }
        }

        private bool CanFormNPCAlliance(FactionDef a, FactionDef b, float mutual,
                float formThreshold) {
            if (a.IsPlayer || b.IsPlayer) { return false; }
            if (mutual < formThreshold) { return false; }
            return sys.Relations.CanAlly(a, b);
        }

        /// <summary>
        /// Applies the locked-enemy rule to every pair. Idempotent, so a newly added
        /// AlwaysEnemies entry takes effect on an existing save.
        /// </summary>
        public void ApplyLockedEnemies() {
            var relations = sys.Relations;
            foreach (var (a, b) in relations.DiplomaticPairs) {
                if (relations.IsLockedEnemy(a, b)) {
                    sys.SetRelationAndReport(a, b, FactionRelation.Enemy);
                }
            }
        }

        /// <summary>
        /// One threshold pass with no drift, so a fresh universe can start with pairs
        /// already hostile.
        /// </summary>
        public void EvaluateAllPairsOnce() {
            EvaluateRelations();
        }
    }
}
