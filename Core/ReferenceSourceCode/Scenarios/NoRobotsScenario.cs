using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Audio;
using Game.Constants;
using Game.Data;
using Game.Platform.Presence;
using Game.Procgen;
using Game.Story.Events;
using Game.Systems.Creatures;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Items;
using System.Data.SqlClient;
using Game.Research;
using System;
using Game.Components;
using Game.Systems.BodyParts;
using Game.Systems.Mental;
using Game.Systems;
using Game.Systems.Ships;
using Game.Components.Upgrades;

namespace Game.Scenarios {
    public sealed class NoRobotsScenario : BaseRandomShipScenario {
        public const string ScenarioId = "no_robots";
        public override string Id => ScenarioId;
        [SelfInit]
        private static void Load() {
            Register(new NoRobotsScenario());
        }
        public override int StartingPetCount => 0;
        public override bool GivesCleanerJetSuit => true;
        private static readonly StasisManifest[] stasisManifests = {
            new StasisManifest(10, 10, 3, 5),
        };
        public override StasisManifest[] StasisManifests => stasisManifests;
        protected override int numShuttles => 0;
        protected override bool addDeadBeings => false;
        public override string NameT => T.ScenarioNorobots;
        public override string DescT => T.ScenarioNorobotsTooltip;
        protected override string[] objs => Tunables.NoRobots_Objs;
        public override string[] StartingWeapons => Tunables.NoRobots_StartingWeapons;
        protected override string[] things => Tunables.NoRobots_Objects;
        protected override string[] engineOptions => new string[] {
            DefIds.ObjectsDevicesThruster,
            DefIds.ObjectsDevicesFusionDrive,
        };

        public override (Species, int)[] GetStartingBeingCounts() {
            return new (Species, int)[] {
                (Species.Get(SpeciesIdH.Human), 3)
            };
        }

        protected override void AddPostStructureStages(ShipFactory factory, Rng rng) {

            factory.AddStage(new CaptureShipPlanGenStage());
            factory.AddStage(new StartingAreaGenStage(false, true, numHeatSinks: 4));
            factory.AddStage(new BridgeGenStage(false));

            factory.AddStage(new ScenarioStasisAreaGenStage(stasisManifests[0], 10, 8, doSomeDamageToTheArea: false, addDeadBodies: 0));
            factory.AddStage(new ShuttleGenStage(1, true, false));
            factory.AddStage(new OpenShuttleAreaGenStage(false, true));

            factory.AddStage(new TemplateGenStage(DefIds.TemplatesNoRobotsLivingArea).Indoors());

            factory.AddStage(new OutdoorDeviceGenStage(DefIds.ObjectsDevicesRadar,
                DefIds.StructureFloorsFloorReinforced03, 1, mirror: false, chanceOnboard: 1f));
            factory.AddStage(new OutdoorDeviceGenStage(DefIds.ObjectsDevicesShredderCannon,
                DefIds.StructureFloorsFloorReinforced03, 2, mirror: true, chanceOnboard: 0f));
            factory.AddStage(new OutdoorDeviceGenStage(DefIds.ObjectsDevicesShieldEmitter,
                DefIds.StructureFloorsFloorReinforced03, 1, mirror: false, chanceOnboard: 1f));

            factory.AddStage(new PlaceTileGenStage(DefIds.ObjectsDevicesPlanterMini, 4, 2, null).Indoors());
            factory.AddStage(new PlaceTileGenStage(DefIds.ObjectsDevicesCPUModuleMini, 2, 2, null));
            factory.AddStage(new PlaceTileGenStage(DefIds.ObjectsDevicesMatterReactor, 2, 1, null));
            factory.AddStage(new PlaceTileGenStage(DefIds.ObjectsDevicesTradePortal, 1, 1, null));
            factory.AddStage(new PlaceTileGenStage(DefIds.ObjectsDevicesCommunicator, 1, 1, null));

            factory.AddStage(new RangedDeviceGenStage(DefIds.ObjectsDevicesOxygenPump, 20, true, minAreaSize: 30));
            factory.AddStage(new RangedDeviceGenStage(DefIds.ObjectsDevicesHeater, 20, true, minAreaSize: 30));

            factory.AddStage(new DatoidsGenStage(1, 0.7f, new Dictionary<int, float>() {
                { MatIdH.DatoidBasic, 0.15f },
                { MatIdH.DatoidTech, 0.3f },
            }));

            // factory.AddStage(new EnergyConnectorGenStage());

            factory.AddStage(new WallSocketGenStage());
            factory.AddStage(new PowerLayoutGenStage(true));

            factory.AddStage(new RefuelDevicesGenStage());

            factory.AddStage(new FullStoragesGenStage("NoRobots"));

            factory.AddStage(new ColonistGenStage(GenItemFlags.SpaceReady | GenItemFlags.JetSuit));
            factory.AddStage(new LandingAreaGenStage());

        }

        protected override void BeforeGenerate() {
            The.ScreenBlock.SetSideText(T.StoryWrecked_Empty_ShipLoading_Side);
            The.ScreenBlock.SetCenterText(T.StoryWrecked_Empty_ShipLoading_V1.F(Texts.NoBr(S.GameName)), 3f);
                // T.StoryLeaving_EarthLoading_V1.F(S.GameName), 3f);
        }
        public override string PresenceId => PresenceActivity.NoRobots;

        protected override void AfterGenerate() {
            // Install snake
            var cpu = S.Components.FindFirstByType<ShipComputerComp>();
            cpu.Tile.Upgrades.Install(0, UpgradeRegistry.GetByMat(MatId.SnakeCartridgeUpgrade));

            The.ScreenBlock.SetSideText("");
            new NoRobotsIntroCutscene(S, () => {
                S.Research.UnlockForNewScenario(
                    Tunables.NoRobots_UnlockedResearch);
            });
            var stasis = S.Components.FindFirstByType<StasisArrayComp>();
            // stasis.IsALSOn = true;
            stasis.Stability = 0.95f;

            // Atmosphere
            var temp = Units.CelsiusToKelvin(21);
            foreach (var a in S.Sys.Areas.Areas) {
                foreach (var p in a.Value) {
                    S.SetTemp(p, temp);
                    S.SetO2(p, 0.21f);
                }
            }
            A.Player.Ship.Class = ShipClassDef.FastGet(ShipClassIdH.Frigate);
            S.Clock.ExecuteAfterTicks(20, S.Sys.NPCShip.SetBestPlayerShipClass);

            ShipSys.FullScan(S.Universe.Player.Parent);
        }
        protected override void AddValidationStages(ShipFactory factory, Rng rng) {
            factory.AddStage(new ValidateShipGenStage(new Dictionary<Type, int>() {
                { typeof(ShuttleComp), 1 },
                { typeof(StasisArrayComp), 1 },
            }));
        }

        protected override void AddCosmeticStages(ShipFactory factory, Rng rng) {
            factory.AddStage(new DirtGenStage(0.1f));
        }

        protected override void AddDestructionStages(ShipFactory factory, Rng rng) {
        }

        protected override void AddPostLootStages(ShipFactory factory, Rng rng) {
            // damage was supposed to be done here
        }

        public override string[] InitialFavMats => new string[] {
            MatId.SteelPlate,
            MatId.TitaniumPlate,
            MatId.Gears,
            MatId.Transistor,
            MatId.Microchip,
            MatId.CopperWire,
        };

        public override int Priority => int.MinValue + 20;
    }
}
