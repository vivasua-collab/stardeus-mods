using System;
using System.Collections.Generic;
using Game.Audio;
using Game.Components;
using Game.Components.Upgrades;
using Game.Constants;
using Game.Data;
using Game.Modifiers;
using Game.Platform.Presence;
using Game.Procgen;
using Game.Story.Events;
using Game.Systems.Creatures;
using Game.Systems.Ships;
using Game.Systems.Suggestions;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Scenarios {
    // The onboarding scenario, and the game's default. Same wrecked ship fantasy as
    // WreckedEmptyShipScenario, but the player only has to learn one system at a time:
    // no survivors and no corpses at the start, and the stasis array runs off its own
    // solar panel so the sleepers are not a countdown. The first human announces
    // themselves around day 6-8 and arrives half a day later, once there is somewhere to
    // put them.
    //
    // Deliberately a full copy of the wrecked scenario rather than a subclass or a
    // shared helper: the two are tuned independently and each should read on its own.
    public sealed class NewBeginningScenario : BaseRandomShipScenario {
        [SelfInit]
        private static void Load() {
            DefaultScenario = new NewBeginningScenario();
            Register(DefaultScenario);
        }
        public const string ScenarioId = "new_beginning";
        public override string Id => ScenarioId;
        public override string NameT => T.ScenarioNew_Beginning;
        public override string DescT => T.ScenarioNew_BeginningTooltip;
        public override bool UsesDestructionLevel => true;
        // A gentler wreck than Wrecked Empty Ship at the same difficulty: the first thing a
        // new player sees should be a ship to fix, not a debris field to make sense of.
        public override float DefaultDestructionLevel(Difficulty difficulty) {
            var level = CurveDef.FastGet(CurveIdH.Scenarios_NewBeginningDestructionLevel)
                .Evaluate(base.DefaultDestructionLevel(difficulty));
            // the slider's own step and range
            return Mathf.Clamp(Mathf.Round(level / 0.05f) * 0.05f, 0.15f, 0.75f);
        }
        public override bool StartsAlone => true;
        // Research is the build menu's valve here: the day-0 menu holds only what the opening
        // needs, and the stasis and flight quests teach unlocking the rest. The basics that
        // stay open (hygiene, food, kitchen storage, lights, ...) are NewBeginning_UnlockedResearch.
        public override ModifierBasicTech.Strategy DefaultBasicTech =>
            ModifierBasicTech.Strategy.Locked;
        // Hand-picked easy layouts - exactly what a beginner start wants, and Quick Start
        // always lands here.
        public override string[] QuickStartSeeds =>
            Tunables.NewBeginning_QuickStartSeeds;
        protected override string[] things => Tunables.NewBeginning_Objects;
        protected override string[] thingsInSpace => Tunables.NewBeginning_ObjectsInSpace;
        protected override string[] objs => Tunables.NewBeginning_Objs;
        public override string[] StartingWeapons => Tunables.NewBeginning_StartingWeapons;
        protected override int maxEngines => 2;
        protected override int minRandomLoot => 10;
        protected override int maxRandomLoot => 15;
        protected override int lootTechLevel => 2;
        // No dead workers or slaughter spots - the opening week has no bodies in it.
        protected override bool addDeadBeings => false;
        public override int FiresToIgnore => 1;
        // A pet is a biological mouth to feed, and there is no food production yet.
        public override int StartingPetCount => 0;
        private static readonly StasisManifest[] stasisManifests = {
            new StasisManifest(15, 15, 3, 5),
        };
        public override StasisManifest[] StasisManifests => stasisManifests;

        // No humans. Everything about colonist care - food, sleep, comfort, hygiene,
        // equipment, medicine - stays out of the game until the first stasis wake-up.
        public override (Species, int)[] GetStartingBeingCounts() {
            return new (Species, int)[] {
                (Species.Get(SpeciesIdH.Robot), 1),
                (Species.Get(SpeciesIdH.Drone), 1),
                (Species.Get(SpeciesIdH.Carrier), 1),
            };
        }

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
            new NewBeginningIntroCutscene(S, derelict, () => {
                S.Sig.AddSuggestion.Send(SuggestionStartWreckedScenario.SuggId);
                S.Research.UnlockForNewScenario(Tunables.NewBeginning_UnlockedResearch);
                UnlockAssemblerIfCrewCannotCraft();
            });
        }

        public override string PresenceId => PresenceActivity.Wrecked;

        protected override void AddPostStructureStages(ShipFactory factory, Rng rng) {
            factory.AddStage(new CaptureShipPlanGenStage());
            // No battery by the ship computer. A new player linked one to a floor light with
            // their first connector, saw it light up, and took the connector for the power
            // source. The first power they see should come from the reactor that
            // ProvideChargeStation has them build.
            factory.AddStage(new StartingAreaGenStage(false, true, numHeatSinks: 0));

            // An undamaged, near-full-integrity pod: the stasis array is a resource to grow
            // into here, not a wreck to race. The headcount is the same in every scenario.
            factory.AddStage(new ScenarioStasisAreaGenStage(stasisManifests[0], 10, 6,
                minIntegrity: 0.95f, doSomeDamageToTheArea: false));
            // The emergency pod used to be built here, before destruction. It now runs after
            // it (see AddPostDestructionStages), and this slot keeps every stage below on the
            // rng fork it had, so the curated Quick Start seeds keep their wrecks.
            factory.AddStage(new RngSlotGenStage());

            // Scrap, like Wrecked: it is what the ship is full of, and what the charge
            // station tutorial tells the player to burn
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
            GenShip.AddDestructionStages(factory, rng, maxIslandSize,
                ModifierDestructionLevel.Current);
        }

        protected override void AddPostDestructionStages(ShipFactory factory, Rng rng) {
            var inten = ModifierDestructionLevel.Intensity;
            var minSections = Mathf.RoundToInt(Mathf.Lerp(4, 2, inten));
            var maxSections = Mathf.RoundToInt(Mathf.Lerp(8, 4, inten));
            var minTilesRemaining = Mathf.RoundToInt(
                1500 * shipSize * baseSizeScale / Mathf.Max(0.2f, inten));
            factory.AddStage(new AbandonSectionsGenStage(minSections, maxSections,
                minTilesRemaining));

            if (ModifierDestructionLevel.Current >= 0.40f) {
                factory.AddStage(new SplitStasisAndCPUGenStage());
            }
            // Keeps stability up during the human-free week, and doubles as the second
            // power island the player has to reach with connectors. After the split, so its
            // invincible pieces cannot stop the slice, and before the breach.
            // Its rations come last (AddFinalStages), where nothing can cover them
            factory.AddStage(new StasisEmergencyPodGenStage());
            // The stasis area is invincible through destruction, so an array placed in an
            // intact room would stay sealed and skip the enclosing lesson entirely
            factory.AddStage(new BreachStasisRoomGenStage());
            factory.AddStage(new WallWindowFinalizeGenStage());
            factory.AddStage(new ReduceEnginesGenStage());
        }

        // Most of the starting materials are tucked into storage units regardless of the
        // destruction level. A wreck knee-deep in scattered rubble is the first thing a
        // new player has to mentally parse, and it is mostly noise. A few loose piles stay,
        // so hauling still comes up early.
        public const float StorageRatio = 0.8f;

        protected override void AddPostLootStages(ShipFactory factory, Rng rng) {
            factory.AddStage(new ObjectDamageGenStage(ModifierDestructionLevel.Intensity));
            factory.AddStage(new RoomStoragesGenStage("NewBeginning", StorageRatio));
            factory.AddStage(new StartingResourcesGenStage("NewBeginning"));
            factory.AddStage(new ShipCPUVentilationGenStage());
            // No InfectDeadBodiesGenStage - there are no bodies to infect, and the
            // DisposeOfInfectedBodies tutorial quest is filtered out of this scenario.
        }

        // The first colonist's rations, after every stage that could cover them: the tutorial
        // asks the player to find and allow them
        protected override void AddFinalStages(ShipFactory factory, Rng rng) {
            factory.AddStage(new StasisRationsGenStage(Tunables.NewBeginning_StasisRations));
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

        public override int Priority => int.MinValue;
    }
}
