#if !PRODUCTION_BUILD
#define DEBUG_BOOLS
#endif
using System;
using System.Collections.Generic;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Quests {
    [MessagePackObject]
    public sealed class QuestBools {
        [Key("Names")] public List<string> Names;
        [Key("NameHashes")] public HashSet<int> NameHashes;
        /// <summary>
        /// required for message pack deserializer, shouldn't be used.
        /// <see cref="Create"/>
        /// </summary>
        public QuestBools() { }
        public static QuestBools Create() {
            var b = new QuestBools();
            b.Names = new List<string>(16);
            b.NameHashes = new HashSet<int>(16);
            return b;
        }
        public QuestBools(string[] names) {
            Names = new List<string>(names);
            NameHashes = new HashSet<int>();
            for (int i = names.Length - 1; i >= 0; i--) {
                NameHashes.Add(Hashes.S(names[i]));
            }
        }

        /// <returns>false if other bools are null or if at least one bool is not contained</returns>
        public static bool ContainsAll(QuestBools superset, QuestBools subset) {
            if (superset?.NameHashes == null || subset?.NameHashes == null) {
                return false;
            }
            return subset.NameHashes.IsSubsetOf(superset.NameHashes);
        }

        /// <returns>false if either side is null, otherwise true when at least one bool
        /// from subset is contained in superset</returns>
        public static bool ContainsAny(QuestBools superset, QuestBools subset) {
            if (superset?.NameHashes == null || subset?.NameHashes == null) {
                return false;
            }
            return superset.NameHashes.Overlaps(subset.NameHashes);
        }

        public bool Set(string flag) {
            var h = Hashes.S(flag);
            if (NameHashes.Add(h)) {
                D.Warn("Set Quest Bool: {0}", flag);
                Names.Add(flag);
                return true;
            }
            return false;
        }
        public bool IsSet(string flag) {
            return NameHashes.Contains(Hashes.S(flag));
        }

        public bool Unset(string flag) {
            var h = Hashes.S(flag);
            if (NameHashes.Remove(h)) {
                D.Warn("Unset Quest Bool: {0}", flag);
                Names.Remove(flag);
                return true;
            }
            return false;
        }

        public bool SetAll(QuestBools bools) {
            if (bools == null) { return false; }
            bool changed = false;
            for (int i = bools.Names.Count - 1; i >= 0; i--) {
                string b = bools.Names[i];
                if (Set(b)) {
                    changed = true;
                }
            }
            return changed;
        }

        public override string ToString() {
            return $"QuestBools[{string.Join(",", Names)}]";
        }

    }
}
