using System;

namespace Game.Data {
    [Serializable]
    public struct SharedSaveInfo {
        public string SteamWorkshopId;
        public string Title;
        public string Description;
        public string GameVersion;
        public string Creator;
        public string ShareDate;
        public override string ToString() {
            return $"SharedSave: [{Title}]";
        }
    }
}
