using System;

namespace Game.Systems.Path {
    [Flags]
    public enum PathReqFlags : ushort {
        None = 0,
        AdjOnTop = 1 << 0,
        AdjNearby = 1 << 1,
        UnsafeOK = 1 << 2,
        CombatCover = 1 << 3,
        Estimate = 1 << 4,
        /// <summary>
        /// Circumvent the job system and do pathfinding immediately
        /// </summary>
        Instant = 1 << 5,
        TryWorkSpot = 1 << 6,
        /// <summary>
        /// If this flag is set, we will only go to the orthogonal neighbors.
        /// </summary>
        NonDiagonal = 1 << 7,
        /// <summary>
        /// We are not going to stay at the end point, therefore we will
        /// ignore any claimed positions because we are just passing through
        /// </summary>
        TempDestination = 1 << 8,
        /// <summary>
        /// Like UnsafeOK, but only widens the temperature tolerance (matching
        /// BeingSafety.IsActuallySurvivableAt's -100/+200 margin) - the oxygen
        /// requirement is not dropped.
        /// </summary>
        SurvivableOK = 1 << 9,
    }
}
