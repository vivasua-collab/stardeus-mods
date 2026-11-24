using Game.CodeGen;

namespace Game.Constants {
    // To tell the generator what is supposed to be there
    [GenerateHashes]
    public static class SpaceObjectTypeId {
        public const string Hyperspace = "Hyperspace";
        public const string ReturnBeacon = "ReturnBeacon";
        public const string HyperjumpRelay = "HyperjumpRelay";
        public const string Region = "Region";
        public const string Sector = "Sector";
        public const string SectorUnknown = "SectorUnknown";
        public const string DerelictZone = "DerelictZone";
        public const string AsteroidZone = "AsteroidZone";
        public const string Space = "Space";
        public const string Player = "Player";
        public const string SpaceEvent = "SpaceEvent";
        public const string SpaceEventConsumed = "SpaceEventConsumed";
        public const string CargoCopter = "CargoCopter";
        public const string Childless = "Childless";

        // Minable asteroid
        public const string Asteroid = "Asteroid";
        public const string Star = "Star";

        // Interactable
        public const string Shuttle = "Shuttle";
        public const string ShipDerelict = "ShipDerelict";
        public const string Ship = "Ship";
        public const string ShipHyperTrail = "ShipHyperTrail";
        public const string TradeZone = "TradeZone";
        public const string Planet = "Planet";
        public const string Moon = "Moon";
        public const string PlanetCloseup = "PlanetCloseup";
        public const string StarCloseup = "StarCloseup";
        public const string HyperspaceCloseup = "HyperspaceCloseup";
        public const string PlanetPOI = "PlanetPOI";
        public const string Anomaly = "Anomaly";
        public const string Signal = "Signal";
        public const string Port = "Port";
        public const string SpaceStation = "SpaceStation";
        public const string Resource = "Resource";
        public const string ResourceUnknown = "ResourceUnknown";
        public const string BlackHole = "BlackHole";
        public const string DarkPortal = "DarkPortal";
        public const string Universe = "Universe";
    }
}
