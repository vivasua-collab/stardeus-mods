using System.Collections.Generic;
using Game.Data;
using KL.Collections;
using MessagePack;

namespace Game.Systems.Space {
    [MessagePackObject]
    public class SpaceMapData {
        [Key("Regions")] public SpaceRegionData[] Regions;
        [Key("RegionLinks")] public SpaceLinkData[] RegionLinks;
        [Key("Sectors")] public SpaceSectorData[] Sectors;
        [Key("SectorLinks")] public SpaceLinkData[] SectorLinks;
        [Key("GenParams")] public GenSpaceParams GenParams;
        [Key("SOId")] public int SOId;

        public static SpaceMapData Serialize(SpaceMap map) {
            var d = new SpaceMapData();
            d.SOId = map.SO.Id;
            d.GenParams = map.GenParams;

            var regs = new SpaceRegionData[map.Regions.Count];
            var regLinkSet = new HashSet<SpaceLink>();
            var sectors = new List<SpaceSector>();
            for (int i = map.Regions.Count - 1; i >= 0; i--) {
                var reg = map.Regions[i];
                foreach (var l in reg.Links) {
                    regLinkSet.Add(l);
                }
                sectors.AddRange(reg.Sectors);
                var regData = SpaceRegionData.Serialize(reg);
                regs[i] = regData;
            }
            d.Regions = regs;

            var regLinks = new SpaceLinkData[regLinkSet.Count];
            int j = 0;
            foreach (var l in regLinkSet) {
                regLinks[j++] = SpaceLinkData.Serialize(l);
            }
            d.RegionLinks = regLinks;

            var secs = new SpaceSectorData[sectors.Count];
            var secLinkSet = new HashSet<SpaceLink>();
            for (int i = sectors.Count - 1; i >= 0; i--) {
                var sec = sectors[i];
                foreach (var l in sec.Links) {
                    secLinkSet.Add(l);
                }
                secs[i] = SpaceSectorData.Serialize(sec);
            }
            d.Sectors = secs;

            var secLinks = new SpaceLinkData[secLinkSet.Count];
            j = 0;
            foreach (var l in secLinkSet) {
                secLinks[j++] = SpaceLinkData.Serialize(l);
            }
            d.SectorLinks = secLinks;

            return d;
        }

        public static SpaceMap Deserialize(GameState s, SpaceMapData data) {
            var map = new SpaceMap();
            map.SO = s.Universe.Find(data.SOId);
            map.Regions = new List<SpaceRegion>();
            map.GenParams = data.GenParams;
            var allSectors = new Dictionary<int, SpaceSector>();
            var allRegions = new Dictionary<int, SpaceRegion>();
            foreach (var rd in data.Regions) {
                var reg = SpaceRegionData.Deserialize(s, map, rd, data.Sectors);
                map.Regions.Add(reg);
                allRegions.Add(reg.Id, reg);
                foreach (var sec in reg.Sectors) {
                    allSectors.Add(sec.Id, sec);
                }
            }
            SpaceLinkData.DeserializeLinks(allRegions, data.RegionLinks);
            SpaceLinkData.DeserializeLinks(allSectors, data.SectorLinks);
            return map;
        }
    }
}
