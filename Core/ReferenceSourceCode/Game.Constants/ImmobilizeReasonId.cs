using Game.CodeGen;

namespace Game.Constants {
    [GenerateHashes]
    public static class ImmobilizeReasonId {
        public const string Dead = "Dead";
        public const string Operation = "Operation";
        /// <summary>
        /// Lacks body part that gives the Move ability
        /// </summary>
        public const string NoAbility = "NoAbility";
        public const string Disabled = "Disabled";
        /// <summary>
        /// Someone is carrying the being now
        /// </summary>
        public const string Carried = "Carried";
        /// <summary>
        /// Waiting for interaction with another being, usually through goal
        /// </summary>
        public const string Waiting = "Waiting";
        public const string Discharged = "Discharged";
        public const string GroundedInSpace = "GroundedInSpace";
        public const string OnFire = "OnFire";

    }
}
