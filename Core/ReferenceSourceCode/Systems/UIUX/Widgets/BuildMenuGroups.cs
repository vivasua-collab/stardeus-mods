using System;
using System.Collections.Generic;
using Game.Data;

namespace Game.Systems.UIUX {
    public static class BuildMenuGroups {
        public sealed class Entry {
            // Set for a lone def
            public Def Def;
            // Set for a group of two or more variants
            public string Group;
            public List<Def> Members;
        }

        private static readonly Comparison<Def> byId = (a, b) => string.CompareOrdinal(a.Id, b.Id);

        // Collapses defs that share a BuildGroup into one entry, placed where the first member
        // was. Members are ordered by id, never by name: the first one is the default pick, so
        // the preferred default (e.g. BedSteel before BedWood) gets the id that sorts first.
        public static List<Entry> Collapse(IReadOnlyList<Def> defs) {
            var result = new List<Entry>(defs.Count);
            Dictionary<string, Entry> groups = null;
            for (int i = 0; i < defs.Count; i++) {
                var def = defs[i];
                var group = def.BuildGroup;
                if (string.IsNullOrEmpty(group)) {
                    result.Add(new Entry { Def = def });
                    continue;
                }
                groups ??= new Dictionary<string, Entry>();
                if (groups.TryGetValue(group, out var existing)) {
                    existing.Members.Add(def);
                    continue;
                }
                var entry = new Entry { Group = group, Members = new List<Def> { def } };
                groups[group] = entry;
                result.Add(entry);
            }
            if (groups == null) {
                return result;
            }
            foreach (var entry in groups.Values) {
                if (entry.Members.Count == 1) {
                    entry.Def = entry.Members[0];
                    entry.Group = null;
                    entry.Members = null;
                } else {
                    entry.Members.Sort(byId);
                }
            }
            return result;
        }
    }
}
