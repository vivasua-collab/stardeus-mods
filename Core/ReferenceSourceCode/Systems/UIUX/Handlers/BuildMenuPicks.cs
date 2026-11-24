using System.Collections.Generic;
using Game.Data;

namespace Game.Systems.UIUX {
    // Which variant each build menu group shows, per game. Keeps def ids rather than Defs, so a
    // pick that is locked or out of season right now comes back once it is available again.
    public sealed class BuildMenuPicks {
        private readonly Dictionary<string, string> picks = new();

        public Def Resolve(string group, List<Def> members) {
            if (picks.TryGetValue(group, out var id)) {
                foreach (var def in members) {
                    if (def.Id == id) {
                        return def;
                    }
                }
            }
            return members[0];
        }

        public void Set(string group, Def def) {
            picks[group] = def.Id;
        }

        public void OnSave(UIUXSysData data) {
            if (picks.Count > 0) {
                data.BuildPicks = new Dictionary<string, string>(picks);
            }
        }

        public void OnLoad(UIUXSysData data) {
            picks.Clear();
            if (data.BuildPicks == null) { return; }
            foreach (var kv in data.BuildPicks) {
                picks[kv.Key] = kv.Value;
            }
        }
    }
}
