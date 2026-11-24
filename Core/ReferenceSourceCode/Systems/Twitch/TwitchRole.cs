using Game.Utils;
using System;
using System.Collections.Generic;
using KL.Utils;

namespace Game.Systems.Twitch {
    [Serializable]
    public class TwitchRole {
        public static readonly Dictionary<int, TwitchRole> All = new();
        public string Id;
        public int IdH;
        public TwitchViewerFlags AddForFlags;
        public int AddAtReputation;
        [JsonEnum(typeof(Game.Systems.Twitch.TwitchViewerFlags))]
        public string[] AlwaysAddFor;

        public static Dictionary<int, TwitchRoleData> Defaults {
            get {
                var defs = new Dictionary<int, TwitchRoleData>();
                if (All.Count == 0) {
                    LoadKnown();
                }
                foreach (var r in All.Values) {
                    defs.Add(r.IdH, TwitchRoleData.DefaultFor(r));
                }
                return defs;
            }
        }

        public static void LoadKnown() {
            var roles = The.ModLoader.LoadObjects<TwitchRole>("Config/Twitch/Roles");
            foreach (var role in roles) {
                if (Arrays.IsEmpty(role.AlwaysAddFor)) {
                    D.Err("Twitch Role does not declare AddFor string[]! {0}", role.Id);
                    continue;
                }
                role.IdH = Hashes.S(role.Id);
                role.AddForFlags = Enums.FlagsFromStringArray_Byte<TwitchViewerFlags>(
                    role.AlwaysAddFor, role.Id);

                All[role.IdH] = role;
            }
        }
    }
}
