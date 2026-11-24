using Game.CodeGen;

namespace Game.Constants {
    [GenerateHashes]
    public static class CounterId {
        public const string TicksWorkedTotal = nameof(TicksWorkedTotal);
        public const string TicksWorkedLastMonth = nameof(TicksWorkedLastMonth);
        public const string TicksWorkedMonth = nameof(TicksWorkedMonth);
        public const string TicksIdleTotal = nameof(TicksIdleTotal);
        public const string TicksIdleLastMonth = nameof(TicksIdleLastMonth);
        public const string TicksIdleMonth = nameof(TicksIdleMonth);
        public const string TicksMovingTotal = nameof(TicksMovingTotal);
        public const string TicksMovingLastMonth = nameof(TicksMovingLastMonth);
        public const string TicksMovingMonth = nameof(TicksMovingMonth);
        // Awake time in windowless areas since the being was last somewhere else, capped at
        // an hour (AINeedLight)
        public const string TicksInWindowlessArea = nameof(TicksInWindowlessArea);
    }
}
