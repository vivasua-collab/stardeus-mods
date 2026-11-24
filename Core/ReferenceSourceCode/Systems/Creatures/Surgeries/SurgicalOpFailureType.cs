namespace Game.Systems.Creatures.Surgeries {
    public enum SurgicalOpFailureType : byte {
        None = 0,
        /// <summary>
        /// A wound + scar gets created on the body part. Wound severity is random, from mild to fatal
        /// </summary>
        WoundPart = 1,
        /// <summary>
        /// Installed part can never be removed
        /// </summary>
        WeldPart = 2,
        /// <summary>
        /// The part gets crippled, limiting max condition to 50%
        /// </summary>
        CripplePart = 3,
        /// <summary>
        /// Part gets destroyed without trace
        /// </summary>
        DestroyPart = 4,
        /// <summary>
        /// Slot gets destroyed - it will have to remain empty forever
        /// </summary>
        DestroySlot = 5,
        /// <summary>
        /// Body gets infected with one of the systemic diseases, like gangrene or sepsis
        /// </summary>
        InfectBody = 6,
        /// <summary>
        /// The surgery results in the death of the patient
        /// </summary>
        Fatality = 7,
        /// <summary>
        /// The body part was rejected
        /// </summary>
        Rejection = 8,
    }
}
