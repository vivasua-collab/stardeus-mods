using System;
using System.Collections;
using System.Collections.Generic;
using Game.Audio;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Modifiers;
using Game.Platform.Presence;
using Game.Procgen;
using Game.Story.Events;
using Game.Systems.Suggestions;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Rendering;
using Game.Components;
using Unity.Mathematics;
using Game.Systems.Ships;
using Game.Components.Upgrades;

namespace Game.Scenarios {
    public sealed class WreckedEmptyShipScenario : BaseRandomShipScenario {
        [SelfInit]
        private static void Load() {
            Register(new WreckedEmptyShipScenario());
        }
        public const string ScenarioId = "wrecked_empty_ship";
        public override string Id => ScenarioId;
        public override string NameT => T.ScenarioWrecked_Empty_Ship;
        public override string DescT => T.ScenarioWrecked_Empty_ShipTooltip;
        public override bool UsesDestructionLevel => true;
        public override bool StartsAlone => true;
        protected override string[] things => Tunables.WreckedEmptyShip_Objects;
        protected override string[] thingsInSpace => Tunables.WreckedEmptyShip_ObjectsInSpace;
        protected override string[] objs => Tunables.WreckedEmptyShip_Objs;
        public override string[] StartingWeapons => Tunables.WreckedEmptyShip_StartingWeapons;
        protected override int maxEngines => 2;
        protected override int minRandomLoot => 10;
        protected override int maxRandomLoot => 15;
        protected override int lootTechLevel => 2;
        public override int FiresToIgnore => 1;
        public override int StartingPetCount => 1;
        private static readonly StasisManifest[] stasisManifests = {
            new StasisManifest(15, 15, 3, 5),
        };
        public override StasisManifest[] StasisManifests => stasisManifests;

        protected override string[] engineOptions => new string[] {
            DefIds.ObjectsDevicesThruster
        };
        protected override void BeforeGenerate() {
            The.ScreenBlock.SetSideText(
                T.StoryWrecked_Empty_ShipLoading_Side);
            The.ScreenBlock.RevealCenterText(
                T.StoryWrecked_Empty_ShipLoading_V1.F(Texts.NoBr(S.GameName)));
        }
        protected override void AfterGenerate() {
            var rng = new Rng(S.Seed);
            // another part of the wreckage
            var derelict = GenShip.NewDerelictSO(S.Universe.Player.Parent,
                S.Universe.Player.Location,
                S, rng, true);
            var plClass = A.Player.Ship.Class;

            // Install snake
            var cpu = S.Components.FindFirstByType<ShipComputerComp>();
            cpu.Tile.Upgrades.Install(0, UpgradeRegistry.GetByMat(MatId.SnakeCartridgeUpgrade));

            if (derelict.Ship.Class == A.Player.Ship.Class) {
                ShipClassDef cl;
                if (plClass.IdH == ShipClassIdH.Fighter) {
                    cl = ShipClassDef.FastGet(ShipClassIdH.Corvette);
                } else {
                    cl = ShipClassDef.FastGet(ShipClassIdH.Fighter);
                }
                derelict.Ship.Class = cl;
                derelict.Ship.UpdateSize(rng.Range(cl.MinSize, cl.MaxSize));
            }
            The.ScreenBlock.SetSideText("");
            new WreckedEmptyShipScenarioIntroCutscene(S, derelict, () => {
                S.Sig.AddSuggestion.Send(SuggestionStartWreckedScenario.SuggId);
                S.Research.UnlockForNewScenario(
                    Tunables.WreckedEmptyShip_UnlockedResearch);
            });
        }

        public override string PresenceId => PresenceActivity.Wrecked;

        // The survivors' camp needs an undamaged room with a clear 4x4 spot, which a small
        // ship often lacks once the stasis room has its cracks
        protected override int spotRooms => base.spotRooms + 1;

        protected override void AddPostStructureStages(ShipFactory factory, Rng rng) {
            factory.AddStage(new CaptureShipPlanGenStage());
            // No battery by the ship computer, for the reason given in NewBeginningScenario
            factory.AddStage(new StartingAreaGenStage(false, true, numHeatSinks: 0));

            factory.AddStage(new ScenarioStasisAreaGenStage(stasisManifests[0], 10, 6, doSomeDamageToTheArea: true));

            var foodPerSurvivor = Tunables.WreckedEmptyShip_FoodPerSurvivor;
            var fuel = Tunables.WreckedEmptyShip_FuelInSurvivorArea;
            factory.AddStage(new SurvivorAreaGenStage(foodPerSurvivor, fuel, false));
            // Scrap: it is what a wreck is full of, and what the charge station tutorial
            // tells the player to burn
            factory.AddStage(new RefuelDevicesGenStage(MatId.ScrapMetal));
        }

        protected override void AddValidationStages(ShipFactory factory, Rng rng) {
            factory.AddStage(new SpaceSuitWhenNoRobotsGenStage());
            factory.AddStage(new ValidateShipGenStage(new Dictionary<Type, int>() {
                { typeof(ShuttleComp), 1 },
                { typeof(RawMaterialGeneratorComp), 1 },
                { typeof(StasisArrayComp), 1 },
            }));
        }

        protected override void AddDestructionStages(ShipFactory factory, Rng rng) {
            // The only call site that ever passes a non-default destruction
            // level; every other GenShip.AddDestructionStages caller (Sandbox,
            // CloneGameUI, mid-game derelict ship story events, ...) keeps the
            // historic 0.75 default and is unaffected by this scenario's slider.
            GenShip.AddDestructionStages(factory, rng, maxIslandSize,
                ModifierDestructionLevel.Current);
        }

        protected override void AddPostDestructionStages(ShipFactory factory, Rng rng) {
            // Scale how many sections we're willing to abandon by the
            // destruction-level slider; inten == 1 (today's default)
            // reproduces the historic (2, 4, 1500 * shipSize) call, scaled to the base size.
            var inten = ModifierDestructionLevel.Intensity;
            var minSections = Mathf.RoundToInt(Mathf.Lerp(4, 2, inten));
            var maxSections = Mathf.RoundToInt(Mathf.Lerp(8, 4, inten));
            var minTilesRemaining = Mathf.RoundToInt(
                1500 * shipSize * baseSizeScale / Mathf.Max(0.2f, inten));
            factory.AddStage(new AbandonSectionsGenStage(minSections, maxSections,
                minTilesRemaining));

            // Below 40% destruction the ship is mostly intact already; skip the
            // extra CPU/stasis-separating cut so it doesn't get fragmented further.
            if (ModifierDestructionLevel.Current >= 0.40f) {
                factory.AddStage(new SplitStasisAndCPUGenStage());
            }
            factory.AddStage(new WallWindowFinalizeGenStage());
            factory.AddStage(new ReduceEnginesGenStage());
        }

        protected override void AddPostLootStages(ShipFactory factory, Rng rng) {
            factory.AddStage(new ObjectDamageGenStage(ModifierDestructionLevel.Intensity));
            // The less destroyed the ship, the more of its starting
            // materials get tucked into storage units instead of scattered
            // as rubble; at the default destruction level storageRatio == 0,
            // so this is a no-op and behavior is unchanged.
            var storageRatio = ModifierDestructionLevel.StorageRatio;
            if (storageRatio > 0f) {
                factory.AddStage(new RoomStoragesGenStage("WreckedEmptyShip", storageRatio));
            }
            factory.AddStage(new StartingResourcesGenStage("WreckedEmptyShip"));
            factory.AddStage(new ShipCPUVentilationGenStage());
            factory.AddStage(new InfectDeadBodiesGenStage());
        }

        public override string[] InitialFavMats => new string[] {
            MatId.Protein,
            MatId.SteelPlate,
            MatId.ScrapMetal,
            MatId.Gears,
            MatId.Transistor,
            MatId.Microchip,
            MatId.CopperWire,
        };

        public override int Priority => int.MinValue + 1;
    }
}
