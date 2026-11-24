using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Modifiers;
using Game.Procgen.Factions;
using Game.Scenarios;
using Game.Systems.Space;
using Game.UI;
using KL.Collections;
using KL.Randomness;
using KL.Utils;

namespace Game.Procgen.Space {
    public static class GenUniverse {
        public const int Version = 3;
        public const float SectorSize = 1.25f;
        public const float RegionSize = 1.5f;
        private static readonly Dictionary<string, GenSpaceObject> generators = new();

        public static void RegisterGenerator(string id, GenSpaceObject generator) {
            if (generator.TypeId != id) {
                if (id != SpaceObjectTypeId.Childless) {
                    D.Err("Trying to register a GenSpace with typ mismatch! {0} != {1}. {2}",
                        generator.TypeId, id, generator);
                }
            }
            generators[id] = generator;
        }

        public static Universe Create(GameState s, SpaceRegion startRegion) {
            var spaceMap = startRegion.Map;
            // D.Warn("map: {0} -> {1}", spaceMap, spaceMap.Regions[0]);

            // D.Warn("Generating universe");
            var u = new Universe();
            u.S = s;
            u.Version = Version;
            u.MaxMapWidth = ModifierMapSize.Current;
            u.MaxMapHeight = ModifierMapSize.Current;

            var hsType = SpaceObjectType.Find(SpaceObjectTypeId.Hyperspace);
            var hsGen = FindGenerator(hsType.Id);
            var hsSO = hsGen.Generate(u, UniverseLayer.Hyperspace, hsType, null);

            u.Hyperspace = hsSO;

            var player = u.CreateObject();
            player.Type = SpaceObjectType.Find(SpaceObjectTypeId.Player);
            u.Hyperspace.AddChild(player);
            player.Name = s.GameName;
            u.Player = player;

            // space map root
            var rootType = SpaceObjectType.Find(SpaceObjectTypeId.Universe);
            var rootGen = FindGenerator(rootType.Id);
            var mapRootSO = rootGen.Generate(u, UniverseLayer.Universe, rootType, null);
            spaceMap.SO = mapRootSO;
            u.Root = mapRootSO;

            var regType = SpaceObjectType.Find(SpaceObjectTypeIdH.Region);
            var sectorType = SpaceObjectType.Find(SpaceObjectTypeIdH.Sector);
            var regGen = FindGenerator(regType.Id);
            var sectorGen = FindGenerator(sectorType.Id);
            foreach (var region in spaceMap.Regions) {
                var regSO = regGen.Generate(u, UniverseLayer.Region, regType, mapRootSO);
                mapRootSO.AddChild(regSO);
                regSO.Region = region;
                regSO.Location = region.Position;
                regSO.Size = RegionSize;
                region.SO = regSO;
                regSO.Name = region.Name;
                foreach (var sector in region.Sectors) {
                    var sectorSO = sectorGen.Generate(u, UniverseLayer.Sector,
                        sectorType, regSO);
                    sector.SO = sectorSO;
                    sectorSO.Size = SectorSize;
                    sectorSO.Name = sector.Name;
                    sectorSO.Sector = sector;
                    sectorSO.Location = sector.Position;
                    regSO.AddChild(sectorSO);
                }
            }

            var rng = new Rng(s.SeedSpace);
            GenMatAvailability.GenerateMatAvailability(s, spaceMap, rng);

            return u;
        }

        public static GenSpaceObject GetSectorGenerator(SpaceSector sector) {
            var tpl = sector.Biome.Template as SectorTemplate;
            if (tpl == null) {
                D.ErrSoft("Template is null");
                tpl = SectorTemplate.Get("EmptySpace");
            }
            if (!generators.TryGetValue(tpl.Type, out var gen)) {
                D.Err("SectorTemplate with id {0} does not have a GenSpaceObjectSector{1} implementation!",
                    tpl.Id, tpl.Type);
                gen = generators[SpaceObjectTypeId.Space];
            }
            // D.Warn("Sector {0} will be: {1}", sector.Name, gen.TypeId);
            return gen;
        }

        public static GenSpaceObject FindGenerator(string id) {
            return generators.Get(id, null)
                ?? generators.Get(SpaceObjectTypeId.Childless, null);
        }

        public static void GenerateCurrentMap(Universe u) {
            var ob = u.Player;
            ob.Map = new Map();
            u.S.Map = ob.Map;
            ob.Map.Initialize(u.MaxMapWidth, u.MaxMapHeight, u.S);
            ob.Map.CreateGrids();
        }
    }
}
