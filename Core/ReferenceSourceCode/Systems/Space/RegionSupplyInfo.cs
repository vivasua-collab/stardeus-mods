using System.Collections.Generic;
using System.Text;
using Game.Constants;
using Game.Data;
using Game.Systems.Trade;
using Game.Utils;

namespace Game.Systems.Space {
    // What a region's traders have plenty of or are short on - the abundant and scarce
    // materials GenMatAvailability rolled per region. It is only revealed for regions the ship
    // is in or has been to, or that border one, so it stays knowledge earned by travelling.
    // See Docs/Architecture/Plans/TargetedResourceAcquisition.md
    public static class RegionSupplyInfo {
        private static readonly List<SpaceRegion> ring = new();
        private static readonly List<SpaceRegion> nextRing = new();
        private static readonly HashSet<int> seen = new();
        private static readonly List<string> names = new();

        public static bool IsKnown(GameState s, SpaceRegion region) {
            var home = HomeRegion(s);
            if (IsVisited(region, home)) { return true; }
            foreach (var link in region.Links) {
                if (IsVisited((SpaceRegion) link.OtherSideOf(region), home)) { return true; }
            }
            return false;
        }

        // Null in hyperspace. Not SOUtils.GetPlayerSector, which logs errors there.
        private static SpaceRegion HomeRegion(GameState s) {
            return s.Universe.Player?.Parent?.Sector?.Region;
        }

        // The current sector is only flagged visited once the ship leaves it or the starmap
        // opens on it, so the ship's own region counts as visited by itself
        private static bool IsVisited(SpaceRegion region, SpaceRegion home) {
            if (region == home) { return true; }
            if (region.Sectors == null) { return false; }
            foreach (var sec in region.Sectors) {
                if (sec.SO != null && sec.SO.IsVisited) { return true; }
            }
            return false;
        }

        // Unknown regions show nothing rather than a hint about how to learn more
        public static bool Append(GameState s, StringBuilder sb, SpaceRegion region) {
            if (!IsKnown(s, region)) { return false; }
            var avail = s.Query.GetMatAvailabilityFor.Ask(region.SO);
            if (avail.Availabilities == null) { return false; }
            // Only what traders have plenty of: scarce covers every material the region has no
            // deposits of, a long list of defaults that would bury the useful line
            if (!CollectAbundantNames(avail)) { return false; }
            sb.Append('\n');
            sb.Append(T.RegionSupplyAbundant_V1.F(Texts.Green(string.Join(", ", names))));
            return true;
        }

        private static bool CollectAbundantNames(MatAvailability avail) {
            names.Clear();
            foreach (var (matIdH, level) in avail.Availabilities) {
                if (level <= 1f) { continue; }
                var mat = MatType.FastGet(matIdH);
                if (mat == null) { continue; }
                names.Add(mat.NameT);
            }
            names.Sort();
            return names.Count > 0;
        }

        // Nearest known region, by hyperjump hops from the ship, whose traders have plenty of
        // the material. The ship's own region counts.
        public static bool TryNearestKnownAbundant(GameState s, MatType mat,
                out SpaceRegion found) {
            found = null;
            var home = HomeRegion(s);
            if (home == null) { return false; }
            ring.Clear();
            nextRing.Clear();
            seen.Clear();
            ring.Add(home);
            seen.Add(home.Id);
            while (ring.Count > 0) {
                foreach (var region in ring) {
                    if (!IsKnown(s, region)) { continue; }
                    var avail = s.Query.GetMatAvailabilityFor.Ask(region.SO);
                    if (avail.For(mat) > 1f) {
                        found = region;
                        return true;
                    }
                }
                nextRing.Clear();
                foreach (var region in ring) {
                    foreach (var link in region.Links) {
                        var n = (SpaceRegion) link.OtherSideOf(region);
                        if (seen.Add(n.Id)) {
                            nextRing.Add(n);
                        }
                    }
                }
                ring.Clear();
                ring.AddRange(nextRing);
            }
            return false;
        }

        public static string AbundantHintFor(GameState s, MatType mat) {
            if (!TryNearestKnownAbundant(s, mat, out var region)) { return null; }
            var regionLink = Texts.LinkToStarmap(region.SO,
                T.RegionName_V1.F(region.SO.DisplayName));
            return T.ScanAbundantHint_V2.F(regionLink, mat.NameT);
        }
    }
}
