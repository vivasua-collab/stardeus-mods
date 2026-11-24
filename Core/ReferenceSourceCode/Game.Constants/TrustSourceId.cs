using Game.CodeGen;

namespace Game.Constants {
    /// <summary>
    /// What a trust effect (Game.Systems.Mental.TrustEffect) came from. A standing effect is set
    /// and removed by this key, timed ones from the same source stack.
    /// </summary>
    [GenerateHashes]
    public static class TrustSourceId {
        public const string PersonalQuarters = nameof(PersonalQuarters);
        public const string FavorFulfilled = nameof(FavorFulfilled);
        public const string FavorFailed = nameof(FavorFailed);
        public const string NeglectDeath = nameof(NeglectDeath);
        public const string CrewSold = nameof(CrewSold);
        public const string FriendToStasis = nameof(FriendToStasis);
        public const string FriendWoken = nameof(FriendWoken);
        public const string NoCeremony = nameof(NoCeremony);
        public const string FriendChipped = nameof(FriendChipped);
        public const string TakenBack = nameof(TakenBack);
    }
}
