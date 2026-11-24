using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Modifiers;
using Game.Procgen;
using Game.Scenarios;
using Game.Systems.Creatures;
using Game.Systems.Space;
using Game.Systems.Story;
using Game.Utils;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.UIUX {
    public class GameSetupData {
        public enum Phase {
            NotInitialized,
            Space,
            Crew,
            Done,
        }

        public Phase SetupPhase;
        public bool IsConfigured { get; private set; }
        public bool IsCancelled { get; private set; }
        public Difficulty Difficulty;
        public StoryGenDef StoryGen;
        public bool IsPermadeath;
        public readonly Scenario Scenario;
        public string ShipName { get; private set; }
        public string ShipComputerName { get; private set; }
        public string SaveName { get; private set; }
        public string ShipType { get; private set; }
        public int ShipSeed { get; private set; }
        public string UniverseName { get; private set; }
        public int UniverseSeed { get; private set; }
        public readonly Dictionary<Species, List<StartingBeingData>> StartingBeings = new();
        public Dictionary<Species, StartingBeingData[]> InitialStartingBeings;
        // Stasis sleepers rolled on the Custom Start screen, per array of the scenario. Empty
        // for Quick Start, where generation rolls them from the ship seed. Never drained by
        // generation, unlike StartingBeings.
        public readonly Dictionary<StasisManifest, List<StartingBeingData>> StasisSleepers = new();
        public SpaceRegion StartingSpaceRegion;

        // Actual ship width / height, set during generation
        public int ShipWidth;
        public int ShipHeight;

        public bool IsQuickStart;

        public void SetShipName(string name) {
            ShipName = name;
            SaveName = Texts.CleanupFileName(name);
        }
        public void SetShipComputerName(string name) {
            ShipComputerName = name;
        }

        public void SetShipType(string type) {
            ShipType = type;
            ShipSeed = Strings.ToSeed(type);
        }

        public void SetUniverseName(string name) {
            UniverseName = name;
            UniverseSeed = Strings.ToSeed(name);
        }
        public void Save() {
            SetupPhase = Phase.Done;
            InitialStartingBeings = new Dictionary<Species, StartingBeingData[]>();
            foreach (var sb in StartingBeings) {
                InitialStartingBeings.Add(sb.Key, sb.Value.ToArray());
            }
            StoryGenDef.LastSelected = StoryGen;
            IsConfigured = true;
        }

        // Generation drains StartingBeings as it spawns the crew (GetBeingData), and a failed
        // generation is retried with this same data. Refilling from the snapshot taken in
        // Save() at the start of every attempt keeps the crew the player picked.
        public void RestoreStartingBeings() {
            if (InitialStartingBeings == null) { return; }
            StartingBeings.Clear();
            foreach (var sb in InitialStartingBeings) {
                StartingBeings.Add(sb.Key, new List<StartingBeingData>(sb.Value));
            }
        }

        public void Reset() {
            IsConfigured = false;
            IsCancelled = true;
            SetupPhase = Phase.NotInitialized;
            InitialStartingBeings?.Clear();
            StartingBeings.Clear();
            StasisSleepers.Clear();
            StartingSpaceRegion = null;
            StoryGen = null;
            Difficulty = null;
        }

        public GameSetupData(Scenario scenario) {
            Scenario = scenario;
            Difficulty = Difficulty.Find(Difficulty.DefaultId);
            var genId = scenario?.DefaultStoryGenId;
            StoryGen = genId == null
                ? StoryGenDef.Default
                : StoryGenDef.Get(genId);
            IsPermadeath = false;

            // // Used names will be loaded with the demo or another save
            // foreach (var ns in Registries.NameSources.All.Values) {
            //     ns.UsedNames.Clear();
            // }

            SetShipName(GenUtils.NewGameShipName(A.S));
            SetShipComputerName(GenUtils.NewShipComputerName(A.S));
            SetShipType(GenUtils.NewGameShipName(A.S));
            SetUniverseName(GenUtils.NewGenericName(A.S));
        }

        public static GameSetupData CreateNewFor(Scenario scenario, string difficultyId = null) {
            return CreateNewFor(scenario, new Rng((int) DateTime.UtcNow.ToFileTimeUtc()),
                difficultyId);
        }
        // difficultyId null keeps the scenario's own Quick Start difficulty
        public static GameSetupData CreateNewFor(Scenario scenario, Rng rng,
                string difficultyId = null) {
            var data = new GameSetupData(scenario);
            // Quick Start must always begin from default modifiers — reset any state
            // leaked from a previously-played game before reading modifier values below.
            Modifier.ResetAll(data);
            data.Difficulty = Difficulty.Find(difficultyId ?? scenario.QuickStartDifficultyId);
            var generator = new GenSpace(A.S, data.UniverseSeed);
            var map = generator.Generate(default);
            data.StartingSpaceRegion = StartingRegion.PickRandom(map, scenario, rng);

            var startBeingCounts = data.Scenario.GetStartingBeingCounts();
            var petCount = data.Scenario.StartingPetCount;
            var petSpecies = new List<Species>();
            for (int i = 0; i < petCount; i++) {
                var species = Species.RandomPet(rng);
                petSpecies.Add(species);
            }

            foreach (var cnt in startBeingCounts) {
                var species = cnt.Item1;
                // The scenario being set up, not A.S.Scenario: that is the game loaded behind
                // the menu, and its crew counts rolled No Robots with 2 people instead of 3
                var modCnt = ModifierBeingsCount.ValueFor(A.S, species.Id, out _,
                    scenario: data.Scenario);
                if (modCnt > 0) {
                    CreateBeings(data, species, modCnt);
                }
            }
            foreach (var sp in petSpecies) {
                CreateBeings(data, sp, 1);
            }
            // Quick Start has no crew screen to warn on
            StartingCrewJobs.RerollHoldouts(data);

            data.Save();
            return data;
        }

        private static void CreateBeings(GameSetupData data, Species species, int modCnt) {
            if (!data.StartingBeings.TryGetValue(species, out var beings)) {
                beings = new List<StartingBeingData>();
                data.StartingBeings.Add(species, beings);
            }
            for (int i = 0; i < modCnt; i++) {
                var b = new StartingBeingData();
                b.Species = species;
                b.Randomize(data, false);
                beings.Add(b);
            }
        }
    }
}
