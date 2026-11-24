using System.Collections.Generic;
using Game.CodeGen;
using Game.Data.Factions;
using KL.Utils;

namespace Game.Systems.Diplomacy {
    /// <summary>
    /// Symmetric Neutral/Allied/Enemy state between factions. Every write updates both
    /// directions, so Get(a, b) always equals Get(b, a).
    /// </summary>
    public sealed class FactionRelations {
        private readonly Dictionary<FactionDef, Dictionary<FactionDef, FactionRelation>> relations = new();
        private List<(FactionDef, FactionDef)> diplomaticPairs;
        private List<FactionDef> diplomaticFactions;

        /// <summary>
        /// Factions that participate in diplomacy. Excludes internal helper factions
        /// (Blank, Trader, Crawler, AntiPlayerTemp) and the AntiPlayer faction, whose
        /// attitude is hardcoded to -1.
        /// </summary>
        public static bool IsDiplomatic(FactionDef f) {
            if (f == null) { return false; }
            return !f.IsInternal && !f.IsAntiPlayer && !f.IsBlank;
        }

        public List<FactionDef> DiplomaticFactions {
            get {
                if (diplomaticFactions == null) {
                    diplomaticFactions = new List<FactionDef>();
                    foreach (var f in FactionDef.Known.Values) {
                        if (IsDiplomatic(f)) {
                            diplomaticFactions.Add(f);
                        }
                    }
                    diplomaticFactions.Sort((a, b) => a.IdH.CompareTo(b.IdH));
                }
                return diplomaticFactions;
            }
        }

        /// <summary>
        /// Cached unordered pairs. Built once - this is iterated on a tick path.
        /// </summary>
        public List<(FactionDef, FactionDef)> DiplomaticPairs {
            get {
                if (diplomaticPairs == null) {
                    var all = DiplomaticFactions;
                    diplomaticPairs = new List<(FactionDef, FactionDef)>();
                    for (int i = 0; i < all.Count; i++) {
                        for (int j = i + 1; j < all.Count; j++) {
                            diplomaticPairs.Add((all[i], all[j]));
                        }
                    }
                }
                return diplomaticPairs;
            }
        }

        public int Count => relations.Count;

        public void Clear() {
            relations.Clear();
        }

        public FactionRelation Get(FactionDef a, FactionDef b) {
            if (a == null || b == null || a == b) { return FactionRelation.Neutral; }
            if (relations.TryGetValue(a, out var forA)
                    && forA.TryGetValue(b, out var rel)) {
                return rel;
            }
            return FactionRelation.Neutral;
        }

        /// <returns>true if the relation actually changed</returns>
        public bool Set(FactionDef a, FactionDef b, FactionRelation rel) {
            if (a == null || b == null || a == b) { return false; }
            if (Get(a, b) == rel) { return false; }
            SetOneWay(a, b, rel);
            SetOneWay(b, a, rel);
            return true;
        }

        private void SetOneWay(FactionDef from, FactionDef to, FactionRelation rel) {
            if (!relations.TryGetValue(from, out var forFrom)) {
                forFrom = new Dictionary<FactionDef, FactionRelation>();
                relations.Add(from, forFrom);
            }
            forFrom[to] = rel;
        }

        /// <summary>
        /// Permanently hostile pair declared in FactionDef.AlwaysEnemies. Symmetric:
        /// either side may declare it.
        /// </summary>
        public bool IsLockedEnemy(FactionDef a, FactionDef b) {
            if (a == null || b == null || a == b) { return false; }
            return a.IsAlwaysEnemyOf(b) || b.IsAlwaysEnemyOf(a);
        }

        public bool IsAsimovVirusPair(FactionDef a, FactionDef b) {
            if (a == null || b == null) { return false; }
            if (a.IdH == FactionIdH.Virus && b.IsPlayer) { return true; }
            return b.IdH == FactionIdH.Virus && a.IsPlayer;
        }

        /// <summary>
        /// The Asimov Override makes the player and the Virus faction allies, overriding
        /// the Virus faction's NeverAllies flag for as long as it stays installed.
        /// </summary>
        public bool IsAsimovAllianceActive(FactionDef a, FactionDef b) {
            return IsAsimovVirusPair(a, b) && A.HasAsimovOverride;
        }

        public bool CanAlly(FactionDef a, FactionDef b) {
            if (a == null || b == null || a == b) { return false; }
            if (!IsDiplomatic(a) || !IsDiplomatic(b)) { return false; }
            if (IsAsimovAllianceActive(a, b)) { return true; }
            if (IsLockedEnemy(a, b)) { return false; }
            return !a.NeverAllies && !b.NeverAllies;
        }

        public void CollectAllies(FactionDef f, List<FactionDef> into) {
            Collect(f, into, FactionRelation.Allied);
        }

        public void CollectEnemies(FactionDef f, List<FactionDef> into) {
            Collect(f, into, FactionRelation.Enemy);
        }

        private void Collect(FactionDef f, List<FactionDef> into, FactionRelation rel) {
            into.Clear();
            if (f == null) { return; }
            if (!relations.TryGetValue(f, out var forF)) { return; }
            foreach (var other in DiplomaticFactions) {
                if (other == f) { continue; }
                if (forF.TryGetValue(other, out var r) && r == rel) {
                    into.Add(other);
                }
            }
        }

        public Dictionary<string, Dictionary<string, int>> ToSaveData() {
            var res = new Dictionary<string, Dictionary<string, int>>();
            foreach (var byFaction in relations) {
                var towards = new Dictionary<string, int>();
                res.Add(byFaction.Key.Id, towards);
                foreach (var rel in byFaction.Value) {
                    towards.Add(rel.Key.Id, (int)rel.Value);
                }
            }
            return res;
        }

        public void FromSaveData(Dictionary<string, Dictionary<string, int>> data) {
            relations.Clear();
            if (data == null) { return; }
            foreach (var byFaction in data) {
                var fac = FactionDef.Get(byFaction.Key);
                if (fac == null) {
                    D.Err("Faction not found while loading relations: {0}", byFaction.Key);
                    continue;
                }
                foreach (var rel in byFaction.Value) {
                    var other = FactionDef.Get(rel.Key);
                    if (other == null) {
                        D.Err("Faction not found while loading relations: {0}", rel.Key);
                        continue;
                    }
                    SetOneWay(fac, other, (FactionRelation)rel.Value);
                }
            }
        }
    }
}
