using System;
using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Data.Planets;
using Game.Data.Space;
using Game.Utils;
using Game.CodeGen;
using Kl.Algos;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;
using Game.Systems.Mental;
using Game.Systems.Space;
using Game.Data.Factions;

namespace Game.Procgen.Space {
    public class GenSpaceObjectSectorPlanet : GenSpaceObject {
        public override string TypeId => SpaceObjectTypeId.Planet;
        [SelfInit]
        private static void Register() {
            GenUniverse.RegisterGenerator(SpaceObjectTypeId.Planet,
                new GenSpaceObjectSectorPlanet());
        }

        private static Curve numSatellitesCurve = new float[] {
            0f, 1f,
            0.2f, 2f,
            0.5f, 3f,
            0.9f, 4f,
            1.0f, 7f,
        };

        public override void GenerateSubObjects(Universe u, SpaceObject parent) {

            int numChildren = 0;
            if (parent.Mats == null) {
                parent.Mats = new List<MatType>();
                foreach (var cfg in parent.Sector.Biome.Deposits) {
                    for (int i = 0; i < cfg.Value; i++) {
                        parent.Mats.Add(cfg.Key.Mat);
                        numChildren++;
                    }
                }
                foreach (var cfg in parent.Sector.Biome.POIs) {
                    var type = SpaceObjectType.Find(cfg.Key.Type);
                    for (int i = 0; i < cfg.Value; i++) {
                        numChildren++;
                        parent.ChangePOIs(type.IdH, 1, syncKnowledge: true);
                    }
                }
            }

            // 1 - big copy of the planet itself
            var closeupType = SpaceObjectType.Find(SpaceObjectTypeId.PlanetCloseup);
            var closeup = Generate(u, UniverseLayer.Sector, closeupType, parent);
            closeup.Name = $"{parent.Name} (Closeup)";
            closeup.Location = Consts.V2Zero;
            parent.AddChild(closeup);

            var satellites = new List<SpaceObject>();
            var rng = new Rng(Hashes.Quick(parent.Seed, Primes.Prime027));

            // Outer stuff - moons, space stations
            GenerateSatellites(u, parent, rng.IntCurve(numSatellitesCurve), satellites);

            // Inner points of interest (on planet, or in satellites)
            // GeneratePointsOfInterest(u, parent, closeupType, satellites, numChildren);
            GeneratePOIs(u, parent, closeupType, satellites, null);

            int numPorts = parent.GetPOIs(SpaceObjectTypeIdH.Port);
            AssignFactionInfrastructure(u, parent, numPorts, rng);

            // only inner POIs at this time
            u.S.Sys.Planets.AssignDepositsFor(parent);

            // GenerateSpaceStations(u, parent, rng, numStations);

            // D.Warn("Done generating planet contents for {0}. ExitDist: {1}",
            //     parent, parent.ExitDist);
        }

        private void AssignFactionInfrastructure(Universe u, SpaceObject so, int numPorts, Rng rng) {
            if (numPorts == 0) { return; }
            FactionDef fac = so.Faction;
            if (fac == null) {
                // var temp = (RegionTemplate) so.Parent.Region.Biome.Template;
                if (!rng.TryWeightedFrom(so.Sector.Biome.FactionsByDominance, out fac)) {
                    return;
                }
                so.SetFaction(fac);
            }
            // D.Warn("Assigning infrastructure for {0}", so);
            int assignedSpacePorts = 0;
            foreach (var child in so.Children) {
                if (child.Type.IdH == SpaceObjectTypeIdH.Port) {
                    // D.Warn("Setting faction on port: {0}", child);
                    child.SetFaction(fac);
                    child.Data.SetString(SpaceObjectLooks.VarSprite, fac.FlagIcon);
                    assignedSpacePorts++;
                } else if (child.Ship?.Class.IsSpaceStation == true) {
                    // D.Warn("Setting faction on SS: {0}", child);
                    child.SetFaction(fac);
                }
            }
            if (assignedSpacePorts < numPorts) {
                D.Err("Assigned only {0}/{1} ports for {2}",
                    assignedSpacePorts, numPorts, so);
            }
        }

        public static void TurnIntoPort(SpaceObject so, SpaceObject child) {
            child.SetType(SpaceObjectType.Find(SpaceObjectTypeId.Port));
            child.Data.SetString(SpaceObjectLooks.VarSprite, so.Faction.FlagIcon);
            child.SetFaction(so.Faction);
            // child.UpdateName(T.SpaceObjectPort);
        }

        public static void SpawnPortNear(Universe u, SpaceObject so, SpaceObject child) {
            var loc = Vector2.MoveTowards(child.Location, Consts.V2Zero, 1f);
            var portType = SpaceObjectType.Find(SpaceObjectTypeIdH.Port);
            var gen = GenUniverse.FindGenerator(portType.Id);
            var port = gen.Generate(u, UniverseLayer.SectorContents, portType, so);
            port.Location = loc;
            port.Data.SetString(SpaceObjectLooks.VarSprite,
                so.Faction.FlagIcon);
            port.SetFaction(so.Faction);
            // port.UpdateName(T.SpaceObjectPort);
            so.AddChild(child);
        }

        private void GenerateSatellites(Universe u, SpaceObject parent, int numChildren,
                List<SpaceObject> generated) {
            var rng = new Rng(Hashes.Quick(parent.Seed, Primes.Prime070));
            // var numChildren = rng.IntCurve(parent.Biome.NumSubObjects);
            if (numChildren == 0) { return; }
            var satellitePoints = GenerateSpiralPoints(u, parent, numChildren, parent.Type.MinExitDist, rng);
            var possibilities = parent.Type.PossibleSubObjects(false);
            base.GenerateSubObjectsAt(u, parent, satellitePoints, possibilities,
                generated);
        }
    }
}
