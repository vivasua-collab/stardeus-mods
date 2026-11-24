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
using Game.Components;
using System;
using Game.Components.Upgrades;

namespace Game.Scenarios {
    public sealed class LeavingEarthScenario : BaseRandomShipScenario {
        public const string ScenarioId = "leaving_earth";
        public override string Id => ScenarioId;
        [SelfInit]
        private static void Load() {
            Register(new LeavingEarthScenario());
        }
        public override int StartingPetCount => 0;
        private static readonly StasisManifest[] stasisManifests = {
            new StasisManifest(15, 15, 3, 5),
        };
        public override StasisManifest[] StasisManifests => stasisManifests;
        public override string NameT => T.ScenarioLeaving_Earth;
        public override string DescT => T.ScenarioLeaving_EarthTooltip;
        protected override string[] objs => Tunables.LeavingEarth_Objs;
        public override string[] StartingWeapons => Tunables.LeavingEarth_StartingWeapons;

        public override (Species, int)[] GetStartingBeingCounts() {
            return new (Species, int)[] {
                (Species.Get(SpeciesIdH.Robot), 1),
                (Species.Get(SpeciesIdH.Drone), 1),
                (Species.Get(SpeciesIdH.Carrier), 1),
                (Species.Get(SpeciesIdH.Human), 2),
            };
        }

        protected override void BeforeGenerate() {
            The.ScreenBlock.SetSideText(T.StoryWrecked_Empty_ShipLoading_Side);
            The.ScreenBlock.SetCenterText(T.StoryLeaving_EarthLoading_V1.F(Texts.NoBr(S.GameName)), 3f);
        }
        public override string PresenceId => PresenceActivity.LeavingEarth;

        protected override void AfterGenerate() {
            // Install snake
            var cpu = S.Components.FindFirstByType<ShipComputerComp>();
            cpu.Tile.Upgrades.Install(0, UpgradeRegistry.GetByMat(MatId.SnakeCartridgeUpgrade));

            The.ScreenBlock.SetSideText("");
            new LeavingEarthIntroCutscene(S, () => {
                S.Research.UnlockForNewScenario(
                    Tunables.LeavingEarth_UnlockedResearch);
            });
        }
        protected override void AddValidationStages(ShipFactory factory, Rng rng) {
            factory.AddStage(new ValidateShipGenStage(new Dictionary<Type, int>() {
                { typeof(ShuttleComp), 1 },
                { typeof(StasisArrayComp), 1 },
            }));
        }

        protected override void ConfigureShipFactory(ShipFactory factory, Rng rng,
                bool isPreview) {
            // the player builds every room, so only the hull is rolled
            var layout = ShipLayout.Roll(rng, false, hasRooms: false);
            factory.Layout = layout;
            factory.AddStage(layout.CreateHullStage(factory.ShipShape, null));
            factory.AddStage(new IslandDetectionAndAttachmentGenStage());
            factory.AddStage(new AngleRoundingGenStage(rng.Range(0f, 1f)));
            if (isPreview) { return; }
            factory.AddStage(new RoundSharpCornersGenStage());
            factory.AddStage(new StartingAreaGenStage(false, true));
            factory.AddStage(new PlaceTileNearCPUGenStage(5, 7, DefIds.TemplatesLeavingEarthSetup));
            factory.AddStage(new ScenarioStasisAreaGenStage(stasisManifests[0], 6, 5, 0, 0.95f));
            factory.AddStage(new PlaceTileGenStage(DefIds.ObjectsDevicesChargeStation, 1, 1, null));
            // The template brings the wiring: connectors only where a device would be dark
            factory.AddStage(new PowerLayoutGenStage(false));
            factory.AddStage(new ShuttleGenStage(1, true, false));
            factory.AddStage(new OpenShuttleAreaGenStage(true, false));
            factory.AddStage(new FullStoragesGenStage("LeavingEarth"));
            factory.AddStage(new ColonistGenStage(GenItemFlags.SpaceReady));
            factory.AddStage(new DirtGenStage(0.1f));
            factory.AddStage(new RefuelDevicesGenStage());
        }
        public override string[] InitialFavMats => new string[] {
            MatId.SteelPlate,
            MatId.TitaniumPlate,
            MatId.Gears,
            MatId.Transistor,
            MatId.Microchip,
            MatId.CopperWire,
        };

        public override int Priority => int.MinValue + 30;
    }
}
