using System;
using MessagePack;

namespace Game.Systems.Twitch {
    [MessagePackObject]
    public struct TwitchRoleData {
        [Key("RoleId")] public int RoleId;
        [Key("AddAtRep")] public int AddAtReputation;
        [Key("AddForFlags")] public TwitchViewerFlags AddForFlags;
        public bool UpdateFor(TwitchViewer viewer) {
            bool hasFlags = (AddForFlags & viewer.Flags) != TwitchViewerFlags.None;
            bool needsRole = hasFlags || (AddAtReputation > 0 && viewer.Reputation >= AddAtReputation);
            if (!needsRole && viewer.HasRole(RoleId)) {
                viewer.RemoveRole(RoleId);
                return true;
            } else if (needsRole && !viewer.HasRole(RoleId)) {
                viewer.AddRole(RoleId);
                return true;
            }
            return false;
        }


        public static TwitchRoleData DefaultFor(TwitchRole r) {
            return new TwitchRoleData {
                RoleId = r.IdH,
                AddAtReputation = r.AddAtReputation,
                AddForFlags = r.AddForFlags,
            };
        }
    }
}
