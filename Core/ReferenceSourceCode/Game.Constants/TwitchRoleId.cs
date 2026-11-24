using Game.CodeGen;

namespace Game.Constants {
    [GenerateHashes]
    public static class TwitchRoleId {
        public const string Build = nameof(Build);
        public const string Camera = nameof(Camera);
        public const string Destroy = nameof(Destroy);
        public const string Play = nameof(Play);
        public const string Talk = nameof(Talk);
        public const string Story = nameof(Story);
    }
}
