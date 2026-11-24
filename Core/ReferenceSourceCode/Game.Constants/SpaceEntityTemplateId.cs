using Game.CodeGen;

namespace Game.Constants {
    [GenerateHashes]
    public static class SpaceEntityTemplateId {
        public const string HyperjumpRelay = nameof(HyperjumpRelay);
        public const string BlackHole = nameof(BlackHole);
        public const string DarkPortal = nameof(DarkPortal);
        public const string SpaceEvent = nameof(SpaceEvent);
    }
}
