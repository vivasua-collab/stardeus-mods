
using Game.CodeGen;

namespace Game.Constants {
    [GenerateHashes]
    public static class BeingGroupId {
        public const string Bot = "Bot";
        public const string Colonist = "Colonist";
        public const string Animal = "Animal";
        // non-hostile visitor of a different faction (see BeingData.IsGuest)
        public const string Guest = "Guest";
        public const string Enemy = "Enemy";
        // different faction and non-hostile or dead
        public const string Other = "Other";
    }
}
