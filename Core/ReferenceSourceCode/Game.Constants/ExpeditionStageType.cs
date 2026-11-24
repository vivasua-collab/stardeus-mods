using Game.CodeGen;

namespace Game.Constants {
    [GenerateHashes]
    public static class ExpeditionStageType {
        // We have ordered a new expedition, at this point we need to wait
        // until all beings are inside the shuttle and ready to go
        public const string Prep = nameof(Prep);

        // When all beings are in the shuttle, show some animation of
        // shuttle leaving the base.
        // Early on it will be black screen and just hiding the shuttle
        public const string Takeoff = nameof(Takeoff);

        // Shuttle has left the base and is traveling towards the target
        public const string TravelToDest = nameof(TravelToDest);

        // Mining POIs are unidentified initially
        public const string IdentifyResource = nameof(IdentifyResource);
        // New type for mining POIs
        public const string MineResource = nameof(MineResource);
        // A special rare event type
        public const string Anomaly = nameof(Anomaly);

        // A quite common point of interest, can produce various events and turn into another type of thing
        public const string PlanetPOI = nameof(PlanetPOI);
        // Workers are still on the object, but they are getting ready to
        // leave it and go back to the base
        public const string PrepForReturn = nameof(PrepForReturn);

        // Waiting for base to be close to the shuttle to pick it back up
        public const string WaitingForBase = nameof(WaitingForBase);

        // Shuttle is returning to base
        public const string TravelToBase = nameof(TravelToBase);

        // Shuttle landing on base animation
        public const string LandingOnBase = nameof(LandingOnBase);

        // Shuttle will drop the loot and take off
        public const string Unload = nameof(Unload);
        // Shuttle is fully back in base, crew are released, etc
        // We will rig the shuttle with actions that will release the loot
        public const string Done = nameof(Done);

        public const string Quest = nameof(Quest);
    }
}
