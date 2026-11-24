using Game.CodeGen;
using Game.Constants;

namespace Game.Constants {
    [GenerateHashes]
    public static class UniverseLayer {
        public const string Undefined = null;
        public const string Hyperspace = "Hyperspace";
        public const string Universe = "Universe";
        public const string Region = "Region";
        public const string Sector = "Sector";
        public const string SectorContents = "SectorContents";

        public static string FlightType(string layer) {
            switch (layer) {
                case Hyperspace: return T.NounHyperjump;
                case Universe: return T.NounHyperjumpLong;
                case Region: return T.NounHyperjumpShort;
                case Sector: return T.ShipCapablePlanet;
                default: return $"{T.Unknown} (mod bug?)";
            }
        }
    }
}
