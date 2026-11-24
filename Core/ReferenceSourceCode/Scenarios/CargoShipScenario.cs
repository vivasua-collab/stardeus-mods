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
    public sealed class CargoShipScenario : BaseRandomShipScenario {
        public const string ScenarioId = "cargo_ship";
        public override string Id => ScenarioId;
        [SelfInit]
        private static void Load() {
            Register(new CargoShipScenario());
        }
        public override int StartingPetCount => 0;
        private static readonly StasisManifest[] stasisManifests = {
            new StasisManifest(8, 8, 2, 2),
            new StasisManifest(7, 7, 1, 3),
        };
        public override StasisManifest[] StasisManifests => stasisManifests;
        protected override bool addDeadBeings => false;
        public override string NameT => T.ScenarioCargo_Ship;
        public override string DescT => T.ScenarioCargo_ShipTooltip;
        protected override string[] objs => Tunables.CargoShip_Objs;
        public override string[] StartingWeapons => Tunables.CargoShip_StartingWeapons;
        protected override string[] engineOptions => new string[] {
            DefIds.ObjectsDevicesThruster,
            DefIds.ObjectsDevicesFusionDrive,
        };

        public override (Species, int)[] GetStartingBeingCounts() {
            return new (Species, int)[] {
                (Species.Get(SpeciesIdH.Robot), 1),
                (Species.Get(SpeciesIdH.Drone), 2),
                (Species.Get(SpeciesIdH.Carrier), 2)
            };
        }

        protected override void BeforeGenerate() {
            The.ScreenBlock.SetSideText(T.StoryWrecked_Empty_ShipLoading_Side);
            The.ScreenBlock.SetCenterText(T.StoryWrecked_Empty_ShipLoading_V1.F(Texts.NoBr(S.GameName)), 3f);
                // T.StoryLeaving_EarthLoading_V1.F(S.GameName), 3f);
        }
        public override string PresenceId => PresenceActivity.Cargo;

        protected override void AfterGenerate() {
            // Install snake
            var cpu = S.Components.FindFirstByType<ShipComputerComp>();
            cpu.Tile.Upgrades.Install(0, UpgradeRegistry.GetByMat(MatId.SnakeCartridgeUpgrade));

            The.ScreenBlock.SetSideText("");

            InstallConversationModule();

            new CargoShipIntroCutscene(S, () => {
                S.Research.UnlockForNewScenario(
                    Tunables.CargoShip_UnlockedResearch);
                UnlockAssemblerIfCrewCannotCraft();
            });

            var stasis = S.Components.FindFirstByType<StasisArrayComp>();
            stasis.IsALSOn = true;
            stasis.Stability = 0.95f;

            A.Player.Ship.Class = ShipClassDef.FastGet(ShipClassIdH.Frigate);
            S.Clock.ExecuteAfterTicks(20, S.Sys.NPCShip.SetBestPlayerShipClass);

            ShipSys.FullScan(S.Universe.Player.Parent);
        }

        // The ship generates with a Communicator, but no stock bot can hear or
        // speak, and ConversationModule is the only part that grants those
        // abilities. Without this the device is dead weight for the whole early
        // game. Candidates are picked by capability rather than by species,
        // because Manipulate and Intelligence are needed on top of the implant,
        // and only some bot bodies provide them.
        private void InstallConversationModule() {
            if (!BodyPartDef.TryForDef(DefIdsH.ObjPartsImplantConversationModule,
                    out var module)) {
                D.Err("Cargo ship: ConversationModule body part is missing");
                return;
            }
            var bot = S.Beings.FindFirstMatching(b => b.IsAlive
                && b.Data.Mind.Jobs.CanDo(JobTypeIdH.Social)
                && b.Data.HasAbility(AbilityIdH.Manipulate)
                && b.Data.HasAbility(AbilityIdH.Intelligence)
                && b.Data.Body.ImplantSlot != null
                && b.Data.Body.ImplantSlot.InstalledDef == null
                && !b.Data.Body.ImplantSlot.IsDestroyed);
            if (bot == null) {
                D.Warn("Cargo ship: nobody in the crew can take a ConversationModule");
                return;
            }
            var slot = bot.Data.Body.ImplantSlot;
            if (slot.InstallPart(S, module, BodyPartState.BrandNew())) {
                slot.UpdateCondition(false, false, forceUpdateHierarchy: true);
            }
        }

        protected override void AddPostLootStages(ShipFactory factory, Rng rng) {
        }

        protected override void AddStructureStages(ShipFactory factory, Rng rng) {
            var layout = ShipLayout.Roll(rng, false);
            factory.Layout = layout;
            factory.AddStage(layout.CreateHullStage(factory.ShipShape, null));
            GenShip.AddHullExtras(factory, rng, layout, false);

            factory.AddStage(new IslandDetectionAndAttachmentGenStage());

            factory.AddStage(new HoleFillerGenStage());

            if (maxEngines > 0 && !Arrays.IsEmpty(engineOptions)) {
                factory.AddStage(new EngineGenStage(2 * engineOptions.Length,
                    maxEngines, 0f, engineOptions));
            }

            factory.AddStage(new AngleRoundingGenStage(rng.Range(0f, 1f)));
            factory.AddStage(new ThickenShipGenStage());
            if (rng.Chance(0.5f)) {
                factory.AddStage(new RoundSharpCornersGenStage());
            }

            Func<ShipSpec, bool> makeDoubleWalls = (spec) =>
                spec.Width > 90 && spec.Height > 70;
            factory.AddStage(new OuterWallGenStage(makeDoubleWalls));

            // size 8 will make wall sockets not reach the ship computer!
            GenShip.AddRoomStages(factory, layout, 6, shipSize, stasisManifests.Length);
            factory.AddStage(new AirlockGenStage());
            factory.AddStage(new RemoveUnreachableAreasGenStage());
            factory.AddStage(new VentGenStage());
            factory.AddStage(new FloorRandomizeGenStage());
            factory.AddStage(new FloorWindowGenStage());
            factory.AddStage(new WallWindowGenStage());
            factory.AddStage(new FloorLightsGenStage());
            factory.AddStage(new EngineDoorGenStage());
            //factory.AddStage(new RoundSharpCornersGenStage());
        }

        protected override void AddPostStructureStages(ShipFactory factory, Rng rng) {
            // Seed: Batens - not enough cpu
            // seed: Erida - too much hollow space

            factory.AddStage(new CaptureShipPlanGenStage());
            factory.AddStage(new StartingAreaGenStage(false, true, numHeatSinks: 4));
            factory.AddStage(new BridgeGenStage(true));

            // factory.AddStage(new EnergyConnectorGenStage());

            // 15 people and 3-5 pets like every other scenario, split over the two arrays
            factory.AddStage(new ScenarioStasisAreaGenStage(stasisManifests[0], 5, 5, 0, 0.95f));
            factory.AddStage(new ScenarioStasisAreaGenStage(stasisManifests[1], 5, 5, 0, 0.95f));

            factory.AddStage(new ShuttleGenStage(1, true, false));
            factory.AddStage(new OpenShuttleAreaGenStage(false, true));

            factory.AddStage(new OutdoorDeviceGenStage(DefIds.ObjectsDevicesRadar,
                DefIds.StructureFloorsFloorReinforced03, 1, mirror: false, chanceOnboard: 1f));
            factory.AddStage(new OutdoorDeviceGenStage(DefIds.ObjectsDevicesShredderCannon,
                DefIds.StructureFloorsFloorReinforced03, 2, mirror: true, chanceOnboard: 0f));
            factory.AddStage(new OutdoorDeviceGenStage(DefIds.ObjectsDevicesShieldEmitter,
                DefIds.StructureFloorsFloorReinforced03, 1, mirror: false, chanceOnboard: 1f));

            factory.AddStage(new PlaceTileGenStage(DefIds.ObjectsDevicesDiskModule, 4, 2, null));
            factory.AddStage(new PlaceTileGenStage(DefIds.ObjectsDevicesFridge, 1, 1, null).Indoors());
            factory.AddStage(new PlaceTileGenStage(DefIds.ObjectsDevicesChargeStation, 2, 2, null));
            factory.AddStage(new PlaceTileGenStage(DefIds.ObjectsDevicesCPUModule, 6, 2, null));
            factory.AddStage(new PlaceTileGenStage(DefIds.ObjectsDevicesMatterReactor, 3, 3, null));
            factory.AddStage(new PlaceTileGenStage(DefIds.ObjectsDevicesTradePortal, 1, 1, null));
            factory.AddStage(new PlaceTileGenStage(DefIds.ObjectsDevicesCommunicator, 1, 1, null));

            factory.AddStage(new DatoidsGenStage(2, 0.2f, new Dictionary<int, float>() {
                { MatIdH.DatoidBasic, 0.1f },
                { MatIdH.DatoidTech, 0.1f },
            }));

            // factory.AddStage(new PlaceTileNearCPUGenStage(5, 7, DefIds.TemplatesLeavingEarthSetup));

            factory.AddStage(new FullStoragesGenStage("CargoShip"));
            // After every powered device is placed, so connectors go where they are needed,
            // and before the colonists, who could otherwise stand where one should go
            factory.AddStage(new WallSocketGenStage());
            factory.AddStage(new PowerLayoutGenStage(true));
            factory.AddStage(new ColonistGenStage(GenItemFlags.SpaceReady));
            factory.AddStage(new RefuelDevicesGenStage());
            factory.AddStage(new LandingAreaGenStage());
        }

        protected override void AddCosmeticStages(ShipFactory factory, Rng rng) {
            factory.AddStage(new DirtGenStage(0.1f));
        }

        protected override void AddDestructionStages(ShipFactory factory, Rng rng) {
        }
        protected override void AddValidationStages(ShipFactory factory, Rng rng) {
            factory.AddStage(new ValidateShipGenStage(new Dictionary<Type, int>() {
                { typeof(ShuttleComp), 1 },
                { typeof(StasisArrayComp), 1 },
            }));
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
