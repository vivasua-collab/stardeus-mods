using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Research;
using Game.Systems.Space;
using KL.Utils;

namespace Game.Systems {
    /// <summary>
    /// Shared Rogue AI route helpers: how deep the player is committed to the branch
    /// (which the galaxy answers with heavier raids), and the one-shot elimination of the
    /// Sentinel faction. See Docs/Architecture/Systems/RogueAIRoute.md.
    /// </summary>
    public static class RogueAIUtils {
        public const string ResearchPrefix = "Research/RogueAI/";
        /// <summary>
        /// Global quest bool, set by Main/EliminateSentinelHQ's SetOnComplete. Read through
        /// A.SentinelEliminated.
        /// </summary>
        public const string BoolSentinelEliminated = "SentinelEliminated";

        /// <summary>
        /// True for a faction that no longer exists in this playthrough and must never be
        /// picked to spawn ships, raids or regions again. Shared by the faction selection
        /// paths: DiplomacySys.RandomFactionForRaid, NPCShipGen.GenerateFaction and
        /// RegionTemplate.IsAllowedFor.
        /// </summary>
        public static bool IsFactionEliminated(FactionDef f) {
            return f != null && f.IdH == FactionIdH.Sentinel && A.SentinelEliminated;
        }

        /// <summary>
        /// Id based overload for the world generation path, which runs per template per
        /// region and must not pay for a FactionDef lookup just to answer "not Sentinel".
        /// </summary>
        public static bool IsFactionEliminated(string factionId) {
            return factionId == FactionId.Sentinel && A.SentinelEliminated;
        }

        /// <summary>
        /// Hands every Sentinel holding in the galaxy to the Virus. Runs once, from the
        /// EliminateSentinelHQ quest step, and is idempotent.
        /// </summary>
        /// <returns>How many space objects changed hands, for the ending epilogue.</returns>
        public static int EliminateSentinel(GameState s) {
            var sentinel = FactionDef.Get(FactionId.Sentinel);
            var virus = FactionDef.Get(FactionId.Virus);
            if (sentinel == null || virus == null) {
                D.Err("Cannot eliminate the Sentinel without both factions: {0} / {1}",
                    sentinel, virus);
                return 0;
            }
            int objects = 0;
            int biomes = 0;
            foreach (var so in s.Universe.ObjectsById.Values) {
                // SetFaction propagates into SpaceObject.Ship, so existing NPC ships and
                // space stations are converted by this same pass.
                if (so.Faction == sentinel) {
                    so.SetFaction(virus);
                    objects++;
                }
                if (ConvertDominance(so.SpaceEntity, sentinel, virus)) {
                    biomes++;
                }
            }
            D.Warn("Sentinel eliminated: {0} space objects and {1} biomes handed to the Virus",
                objects, biomes);
            return objects;
        }

        /// <summary>
        /// Folds a sector's/region's Sentinel dominance weight into the Virus, so anything
        /// spawned there later (ports via SOUtils.EnsurePortFaction, ships via
        /// NPCShipGen.GenerateFaction) picks the Virus instead. Persisted as part of
        /// SpaceBiomeData.Factions.
        /// </summary>
        private static bool ConvertDominance(ISpaceEntity ntt, FactionDef sentinel,
                FactionDef virus) {
            var dominance = ntt?.Biome?.FactionsByDominance;
            if (dominance == null) { return false; }
            if (!dominance.TryGetValue(sentinel, out var weight)) { return false; }
            dominance.Remove(sentinel);
            dominance.TryGetValue(virus, out var existing);
            dominance[virus] = existing + weight;
            return true;
        }

        /// <summary>
        /// 0..1 - how much of the Rogue AI tree the player has actually paid for. Tier 0
        /// (Root) is excluded: it is costless, so it unlocks itself the moment the branch is
        /// discovered and would otherwise report commitment the player never made.
        /// </summary>
        public static float RogueCommitmentFraction(GameState s) {
            CountRogueNodes(s, out var unlocked, out var total);
            if (total == 0) { return 0f; }
            return unlocked / (float) total;
        }

        /// <summary>
        /// Paid-for vs total Rogue AI tree nodes. Tier 0 excluded, see above.
        /// </summary>
        public static void CountRogueNodes(GameState s, out int unlocked, out int total) {
            total = 0;
            unlocked = 0;
            var all = ResearchTree.All;
            for (int i = all.Count - 1; i >= 0; i--) {
                Def def = all[i];
                if (def.Tier == 0) { continue; }
                if (!def.Id.StartsWith(ResearchPrefix, StringComparison.Ordinal)) { continue; }
                total++;
                // InternalUnlocked rather than IsUnlocked - the latter is true for every
                // node under dev instant build, which would silently max out raid pressure.
                if (s.Research.Get(def.IdH)?.InternalUnlocked == true) {
                    unlocked++;
                }
            }
        }
    }
}
