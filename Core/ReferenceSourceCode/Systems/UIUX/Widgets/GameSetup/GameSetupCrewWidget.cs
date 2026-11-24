using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Modifiers;
using Game.Procgen;
using Game.Scenarios;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.Systems.Naming;
using Game.Systems.Story;
using Game.UI;
using Game.Utils;
using KL.Collections;
using KL.Pool;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class GameSetupCrewWidget : BaseWidget {
        private GameSetupData data;
        // Blocks
        [SerializeField] private GameObject blockCrewList;
        [SerializeField] private GameObject blockCrewMember;
        [SerializeField] private GameObject blockJobs;
        [SerializeField] private GameObject blockModifiers;

        // Block 1 - Game Rules
        [SerializeField] private WText wTextGameRules;
        [SerializeField] private WDropdownWithLabel wDropDifficulty;
        [SerializeField] private WDropdownWithLabel wDropStoryGen;
        [SerializeField] private WDropdownWithLabel wDropSaveLoad;
        [SerializeField] private WButtonText btnModifiers;
        [SerializeField] private ModifiersWidget modifiersWidget;

        // Block 1 - Ship Configuration
        [SerializeField] private WText wTextShipConfig;
        [SerializeField] private WInputWithLabelAndButtonIcon wInputShipName;
        [SerializeField] private WInputWithLabelAndButtonIcon wInputShipComputerName;
        [SerializeField] private WInputWithLabelAndButtonIcon wInputShipType;
        [SerializeField] private WRawImage wRawImgPreview;

        // Block 2 - Crew list
        [SerializeField] private WText wTextCrewList;
        [SerializeField] private WGroup wCrewListContainer;
        [SerializeField] private WButtonIcon btnRandomizeCrew;
        [SerializeField] private WButtonText btnToggleJobs;

        // Block 3 - Crew Member
        [SerializeField] private StartingBeingWidget startingBeingWidget;

        // Block 4 - Colony Jobs
        [SerializeField] private WButtonText wJobsHeader;
        [SerializeField] private WGroup wJobsContainer;
        private Lifetime jobsLife;
        private Lifetime crewLife;
        private Lifetime crewMemLife;
        private Lifetime lifetime;
        private Lifetime modifiersLife;

        private ShipPreview shipPreview;
        // For tests
        public ShipPreview ShipPreview => shipPreview;
        // For tests
        public StartingBeingData SelectedBeing => selectedBeing;
        // For tests: the dice buttons have no text to click
        public void RandomizeSelected() => RandomizeSelectedBeing();
        public void RandomizeAll() => RandomizeCrew();
        private StartingBeingData selectedBeing;
        private readonly List<MissingCrewJob> missingJobs = new();
        private WText txtFooterMessage;

        private string wantedShipType;
        private float shipTypeUpdateTimeout;

        public void Initialize(WText txtFooterMessage) {
            this.txtFooterMessage = txtFooterMessage;
            wCrewListContainer.DestroyChildren();
            startingBeingWidget.Initialize(blockCrewMember);
            startingBeingWidget.OnRandomize = RandomizeSelectedBeing;
            wJobsHeader.Load(T.NounColonyJobs, ToggleJobs);
            wJobsContainer.DestroyChildren();
            Toggle(false);
        }

        private void RandomizeSelectedBeing() {
            if (selectedBeing == null) { return; }
            if (selectedBeing.IsSleeper) {
                StasisManifestSetup.Reroll(data, selectedBeing);
            } else {
                selectedBeing.Randomize(data, true);
                StartingCrewJobs.RerollWhileHoldingBack(data, selectedBeing);
            }
            RebuildUI();
        }

        public void Show(Lifetime parentLife, GameSetupData data) {
            if (this.data != data) {
                if (lifetime != null) {
                    lifetime.Dispose();
                }
                lifetime = parentLife.CreateChild("GameSetupCrew");
                BeginSetup(data);
            }
            txtFooterMessage.SetActiveFast(false);
            data.SetupPhase = GameSetupData.Phase.Crew;
            gameObject.SetActiveFast(true);
        }
        public void Close() {
            Toggle(false);
        }

        private void BeginSetup(GameSetupData data) {
            this.data = data;
            modifiersWidget.Initialize(data, blockModifiers);
            modifiersWidget.OnClose = AfterCloseModifiers;
            blockJobs.SetActiveFast(false);
            blockCrewMember.SetActiveFast(false);
            blockCrewList.SetActiveFast(true);
            btnRandomizeCrew.Load(IconId.CDice, RandomizeCrew);
            UIUtils.AddTip(lifetime, btnRandomizeCrew, T.RandomizeAll);
            btnToggleJobs.Load(T.NounColonyJobs, ToggleJobs);
            LoadGameRules();
            LoadShipConfig();
            LoadCrewList(true);
            Toggle(true);
        }

        private void ToggleJobs() {
            if (jobsLife != null) {
                jobsLife.Dispose();
                jobsLife = null;
            }
            if (!blockJobs.activeSelf) {
                LoadJobs();
            }
            blockJobs.SetActiveFast(!blockJobs.activeSelf);
        }

        private void LoadJobs() {
            if (jobsLife != null) {
                jobsLife.Dispose();
                jobsLife = null;
            }
            jobsLife = lifetime.CreateChild("Jobs");

            var jobLevels = new Dictionary<JobType, int>();
            var jobLoves = new Dictionary<JobType, int>();
            var jobIncapables = new Dictionary<JobType, int>();
            var jobsIncapable = new HashSet<JobType>();
            foreach (var job in JobType.All.Values) {
                if (job.Internal) { continue; }
                jobsIncapable.Add(job);
            }
            foreach (var list in data.StartingBeings.Values) {
                foreach (var sb in list) {
                    foreach (var slot in sb.Data.Mind.Jobs.JobSlots.Values) {
                        if (slot.IsInternal) { continue; }
                        var job = slot.Job;
                        var level = slot.Level;
                        var loves = slot.Loves;
                        if (loves) {
                            jobLoves.GetIncrement(job);
                        }
                        if (!slot.Incapable) {
                            jobsIncapable.Remove(job);
                            jobLevels.GetIncrement(job, level);
                        } else {
                            jobIncapables.GetIncrement(job);
                        }
                    }
                }
            }

            int idx = 0;
            foreach (var job in JobType.All.Values
                    .Where(j => !j.Internal).OrderBy(j => j.NameT)) {
                var w = UISpawn.Widget<WIconText2>(jobsLife, UIPrefabId.WContent2Text2,
                    wJobsContainer.Container, idx++);
                string name = job.NameT;
                string level;
                var nobodyCapable = jobsIncapable.Contains(job);
                if (nobodyCapable) {
                    level = GlyphMap.SpriteCross;
                    name = Texts.Red(name);
                } else if (job.Unskilled) {
                    level = GlyphMap.SpriteCheck;
                } else {
                    level = Units.Num(jobLevels.Get(job, 0));
                }
                w.Load(job.IconId, name, level);
            }
        }

        private void RandomizeCrew() {
            SetSelectedBeing(null);
            LoadCrewList(true);
        }

        private void LoadCrewList(bool randomize) {
            ReconcileStartingBeings(randomize);
            BuildCrewListUI();
            if (randomize && blockJobs.activeSelf) {
                LoadJobs();
            }
        }

        // Keeps data.StartingBeings in sync with the current modifier counts.
        // Pure data mutation, no UI - safe to call whenever a modifier changes,
        // even while the crew list UI isn't visible (e.g. Custom Rules panel open).
        private void ReconcileStartingBeings(bool randomize) {
            if (randomize) {
                data.StartingBeings.Clear();
                StasisManifestSetup.RollAll(data, SetSelectedBeing);
            }
            var startBeingCounts = data.Scenario.GetStartingBeingCounts();

            // non-pets first
            foreach (var cnt in startBeingCounts) {
                var species = cnt.Item1;
                var modCnt = ModifierBeingsCount.ValueFor(A.S, species.Id,
                        out _, scenario: data.Scenario);
                ReconcileSpecies(species, modCnt, randomize);
            }

            var petCount = ModifierPetsCount.ValueFor(A.S, out _, scenario: data.Scenario);
            ReconcilePets(petCount);
            // Only on a roll: a count change is deliberate and must not replace crew members
            // the player has already seen, so the missing jobs block warns about it instead
            if (randomize) {
                StartingCrewJobs.RerollHoldouts(data);
            }
        }

        private void ReconcilePets(int petCount) {
            // pets are tracked only in data.StartingBeings - collect the ones
            // that survived (randomize clears StartingBeings above, so this
            // is empty right after a full crew randomize)
            var pets = new List<StartingBeingData>();
            foreach (var kvp in data.StartingBeings) {
                if (kvp.Key.IsPet) {
                    pets.AddRange(kvp.Value);
                }
            }
            while (pets.Count > petCount) {
                var last = pets[pets.Count - 1];
                pets.RemoveAt(pets.Count - 1);
                if (data.StartingBeings.TryGetValue(last.Species, out var l)) {
                    l.Remove(last);
                }
            }
            if (pets.Count < petCount) {
                var addRng = new Rng(Rng.Unseeded.Int);
                while (pets.Count < petCount) {
                    pets.Add(NewStartingBeing(Species.RandomPet(addRng)));
                }
            }
        }

        // Rebuilds the crew list widgets from the current data.StartingBeings.
        private void BuildCrewListUI() {
            DespawnCrewWidgets();
            crewLife = lifetime.CreateChild("Crew");
            int idx = 0;
            int total = 0;

            var grouped = new Dictionary<string, List<StartingBeingData>>() {
                { T.NounColonist, new List<StartingBeingData>() },
                { T.NounWorker, new List<StartingBeingData>() },
                { T.Pet, new List<StartingBeingData>() },
            };

            foreach (var kvp in data.StartingBeings) {
                var beings = kvp.Value;
                if (beings.Count == 0) { continue; }
                var species = kvp.Key;
                total += beings.Count;
                var gBeings = species.IsPet ? grouped[T.Pet]
                        : species.IsHumanoid ? grouped[T.NounColonist] : grouped[T.NounWorker];
                gBeings.AddRange(beings);
            }

            wTextCrewList.Load($"{T.InitialCrew}: {total}");

            SpawnMissingJobsItems(idx++);
            foreach (var g in grouped) {
                SpawnCrewGroupItems(idx++, g.Key, g.Value);
            }
            SpawnSleeperItems(idx++);
        }

        // Not part of the initial crew count or the colony jobs: they are asleep until the
        // game wakes them one by one. People first, then pets, in the order they were rolled.
        private void SpawnSleeperItems(int idx) {
            var count = StasisManifestSetup.Count(data);
            if (count == 0) { return; }
            var groupBlock = UISpawn.Widget<WContentBlock>(crewLife, UIPrefabId.WContentBlock,
                wCrewListContainer.Container, idx);
            var bIdx = 0;

            var header = UISpawn.Widget<WText>(crewLife, UIPrefabId.WContent2Header,
                groupBlock.Container, bIdx++);
            header.Load(T.BeingInStasis);

            for (int pass = 0; pass < 2; pass++) {
                bool pets = pass == 1;
                foreach (var list in data.StasisSleepers.Values) {
                    foreach (var sleeper in list) {
                        if (sleeper.Species.IsPet != pets) { continue; }
                        sleeper.AddListUI(crewLife, bIdx++, groupBlock);
                    }
                }
            }
        }

        // First in the list, so it is seen while the crew is being changed, not only on Confirm
        private void SpawnMissingJobsItems(int idx) {
            StartingCrewJobs.FindMissing(data, missingJobs);
            if (missingJobs.Count == 0) { return; }
            var block = UISpawn.Widget<WContentBlock>(crewLife, UIPrefabId.WContentBlock,
                wCrewListContainer.Container, idx);
            var bIdx = 0;

            var header = UISpawn.Widget<WText>(crewLife, UIPrefabId.WContent2Header,
                block.Container, bIdx++);
            header.Load($"{GlyphMap.SpriteWarning} {T.CrewMissingJobs}");

            foreach (var missing in missingJobs) {
                var w = UISpawn.Widget<WIconText2>(crewLife, UIPrefabId.WContent2Text2,
                    block.Container, bIdx++);
                w.Load(missing.Job.IconId, Texts.Red(missing.NameT), GlyphMap.SpriteCross);
                UIUtils.AddTip(crewLife, w, missing.TipT);
            }
        }


        private void DespawnCrewWidgets() {
            if (crewLife != null) {
                crewLife.Dispose();
                crewLife = null;
            }
        }

        private StartingBeingData NewStartingBeing(Species species) {
            if (!data.StartingBeings.TryGetValue(species, out var beings)) {
                beings = new List<StartingBeingData>();
                data.StartingBeings.Add(species, beings);
            }
            var b = new StartingBeingData();
            b.Species = species;
            b.OnClick = SetSelectedBeing;
            b.RebuildUI = RebuildUI;
            beings.Add(b);
            b.Randomize(data, false);
            return b;
        }

        private void ReconcileSpecies(Species species, int modCnt, bool randomize) {
            if (!data.StartingBeings.TryGetValue(species, out var beings)) {
                if (modCnt <= 0) { return; }
                beings = new List<StartingBeingData>();
                data.StartingBeings.Add(species, beings);
            }

            while (beings.Count > modCnt) {
                beings.RemoveAt(beings.Count - 1);
            }

            for (int i = 0; i < modCnt; i++) {
                var b = Arrays.SafeGetFast(beings, i);
                if (b == null) {
                    NewStartingBeing(species);
                } else if (randomize) {
                    b.Randomize(data, false);
                }
            }
        }

        private void SpawnCrewGroupItems(int idx, string name, List<StartingBeingData> beings) {
            if (beings.Count == 0) { return; }
            var groupBlock = UISpawn.Widget<WContentBlock>(crewLife, UIPrefabId.WContentBlock,
                wCrewListContainer.Container, idx++);
            var bIdx = 0;

            var header = UISpawn.Widget<WText>(crewLife, UIPrefabId.WContent2Header,
                groupBlock.Container, bIdx++);
            header.Load(name);

            foreach (var data in beings.OrderBy(b => b.Species.Name)) {
                data.AddListUI(crewLife, bIdx++, groupBlock);
            }
        }

        private void RebuildUI() {
            LoadCrewList(false);
            SetSelectedBeing(selectedBeing);
            if (blockJobs.activeSelf) {
                LoadJobs();
            }
        }

        private void SetSelectedBeing(StartingBeingData data) {
            if (crewMemLife != null) {
                crewMemLife.Dispose();
                crewMemLife = null;
            }
            selectedBeing = data;
            if (selectedBeing == null) {
                blockCrewMember.SetActiveFast(false);
            } else {
                crewMemLife = crewLife.CreateChild("CrewMember");
                startingBeingWidget.Show(crewMemLife, selectedBeing, () => {
                    RebuildUI();
                });
            }
            // show / rebuild ui
        }

        private void AfterCloseModifiers() {
            blockCrewList.SetActiveFast(true);
            LoadCrewList(false);
        }

        public void EveryFrame() {
            if (wantedShipType != data.ShipType) {
                shipTypeUpdateTimeout -= GameTimer.unscaledDeltaTime;
                if (shipTypeUpdateTimeout <= 0f) {
                    SetShipType(wantedShipType);
                }
            }
            shipPreview.OnUpdate();
        }

        private void SetShipType(string type) {
            data.SetShipType(type);
            shipPreview.Rebuild();
        }

        private void LoadShipConfig() {
            wTextShipConfig.Load(T.ShipConfiguration);

            // Ship name
            wInputShipName.Label.SetText(T.MenuGameName);
            wInputShipName.Input.Load(T.EnterName, data.ShipName, OnShipNameChange);
            wInputShipName.Button.Load(IconId.CDice, OnRandomizeName);


            // Ship computer name
            wInputShipComputerName.Label.SetText(T.ResearchShipcomputer);
            wInputShipComputerName.Input.Load(T.EnterName, data.ShipComputerName, OnShipComputerNameChange);
            wInputShipComputerName.Button.Load(IconId.CDice, OnRandomizeShipComputerName);

            // Ship Type
            wInputShipType.Label.SetText(T.NounShipType);
            wInputShipType.Input.Load(T.EnterName, data.ShipType, OnShipTypeChange);
            wInputShipType.Button.Load(IconId.CDice, OnRandomizeShipType);

            // Preview
            wRawImgPreview.Load((Texture2D) null);
            UIUtils.AddTip(lifetime, wRawImgPreview, T.ShipPreviewTip);

            shipPreview = new ShipPreview(data, wRawImgPreview);
            shipPreview.Rebuild();
            wantedShipType = data.ShipType;
        }

        private void OnRandomizeShipType() {
            var newType = GenUtils.NewGameShipName(A.S);
            wInputShipType.Input.SetValue(newType, false);
            wantedShipType = newType;
        }

        private void OnRandomizeName() {
            wInputShipName.Input.SetValue(GenUtils.NewGameShipName(A.S));
        }
        private void OnRandomizeShipComputerName() {
            wInputShipComputerName.Input.SetValue(GenUtils.NewShipComputerName(A.S));
        }

        private void OnShipNameChange(string name) {
            // Blank while the player retypes it, and TryNext refuses that anyway. Turning a
            // blank name into a save name would look up the save folder itself and log an
            // error, which CloneGameUI avoids the same way.
            if (string.IsNullOrWhiteSpace(name)) { return; }
            data.SetShipName(name);
        }
        private void OnShipComputerNameChange(string name) {
            data.SetShipComputerName(name);
        }
        private void OnShipTypeChange(string type) {
            wantedShipType = type;
            shipTypeUpdateTimeout = 0.5f;
            #if !PRODUCTION_BUILD
            if (The.TestsAreRunning) {
                shipTypeUpdateTimeout = 0f;
            }
            #endif
            wRawImgPreview.SetSprite((Sprite) null);
        }

        private void LoadGameRules() {
            wTextGameRules.Load(T.NounGameRules);

            // Difficulty
            var difficulties = ModifierDifficulty.Difficulties;
            var allDiffs = Difficulty.AllSorted();
            var selected = allDiffs.IndexOf(data.Difficulty);

            wDropDifficulty.Label.Load(T.GameDifficulty);
            wDropDifficulty.Dropdown.Load(difficulties, selected,
                (i) => OnChangeDifficulty(allDiffs[i]));
            #if STARDEUS_DEMO
            wDropDifficulty.Dropdown.SetInteractable(false);
            UIUtils.AddTip(lifetime, wDropDifficulty, $"{T.GameDifficultyTip}\n\n{T.NotInDemo}");
            #else
            UIUtils.AddTip(lifetime, wDropDifficulty, T.GameDifficultyTip);
            #endif


            // Storygen
            var storygens = StoryGenDef.Ordered;
            var names = storygens.ConvertAll(s => s.NameT);
            // Not StoryGenDef.Default - the scenario may preselect its own generator
            var selectedGen = storygens.IndexOf(data.StoryGen);

            wDropStoryGen.Label.Load(T.GameStorygen);
            wDropStoryGen.Dropdown.Load(names, selectedGen,
                (i) => OnChangeStoryGen(storygens[i]));
            #if STARDEUS_DEMO
            wDropStoryGen.Dropdown.SetInteractable(false);
            UIUtils.AddTip(lifetime, wDropStoryGen, $"{T.StorygenTip}\n\n{T.NotInDemo}");
            #else
            UIUtils.AddTip(lifetime, wDropStoryGen, T.StorygenTip);
            #endif

            // Save / Load
            var commitments = new List<string> {
                T.SaveLoadAnytime,
                T.ModifierNoreload
            };

            wDropSaveLoad.Label.SetText(T.PermadeathLabel);
            wDropSaveLoad.Dropdown.Load(commitments, data.IsPermadeath ? 1 : 0, OnChangeSaveLoad);
            // this translation uses "No Reload" and other adjacent tips use bold
            UIUtils.AddTip(lifetime, wDropSaveLoad, Texts.BoldQuotes(T.ModifierNoreloadDesc));

            btnModifiers.Load(T.NounCustomRules, ToggleModifiersPanel);
            #if STARDEUS_DEMO
            btnModifiers.SetInteractable(false);
            UIUtils.AddTip(lifetime, btnModifiers, T.NotInDemo);
            #endif
        }

        private void OnChangeSaveLoad(int choice) {
            data.IsPermadeath = choice == 1;
        }

        private void OnChangeStoryGen(StoryGenDef def) {
            data.StoryGen = def;
        }

        private void OnChangeDifficulty(Difficulty diff) {
            data.Difficulty = diff;
            D.Warn("Setting difficulty: {0}", data.Difficulty);
            ModifierDestructionLevel.RefreshForDifficultyChange(data);
        }

        private void ToggleModifiersPanel() {
            if (modifiersLife != null) {
                modifiersLife.Dispose();
                modifiersLife = null;
            }
            if (blockModifiers.activeSelf) {
                modifiersWidget.Close();
                blockCrewList.SetActiveFast(true);
            } else {
                modifiersLife = lifetime.CreateChild("ModifiersPanel");
                modifiersWidget.Show(modifiersLife);
                blockCrewList.SetActiveFast(false);
                LoadCrewList(false);
                blockCrewMember.SetActiveFast(false);
                blockJobs.SetActiveFast(false);
            }
        }

        public void Cleanup() {
            data = null;
            shipPreview?.Cleanup();
        }

        public void OnModifierChanged(Modifier mod) {
            if (data.SetupPhase == GameSetupData.Phase.Crew &&
                    (mod == null || mod.Id == ModifierShipSize.ModifierId || mod.Id == ModifierMapSize.ModifierId)) {
                data.SetShipType(null);
                wantedShipType = wInputShipType.Input.Text;
                shipTypeUpdateTimeout = 0.1f;
            }
            if (mod == null || mod is ModifierBeingsCount || mod is ModifierPetsCount) {
                SetSelectedBeing(null);
                ReconcileStartingBeings(false);
                if (blockCrewList.activeSelf) {
                    BuildCrewListUI();
                }
            }
        }

        public void RestoreAchievements() {
            foreach (var m in Modifier.All) {
                if (m.BlocksAchievements) {
                    m.Reset(data);
                }
            }
            if (blockModifiers.activeSelf) {
                if (modifiersLife != null) {
                    modifiersLife.Dispose();
                    modifiersLife = null;
                }
                modifiersLife = lifetime.CreateChild("ModifiersPanel");
                modifiersWidget.Show(modifiersLife);
            }
            A.S.Sig.ModifierChanged.Send(null);
        }

        // proceedAnyway runs when the player confirms a crew that is missing essential jobs
        public bool TryNext(Action proceedAnyway) {
            var sb = Caches.StringBuilder2;
            sb.Clear();
            bool hasMissing = false;
            if (string.IsNullOrWhiteSpace(wInputShipName.Input.Text)) {
                sb.AppendFormat("{0} {1}: {2}\n", GlyphMap.SpriteWarning, T.Missing, T.MenuGameName);
                hasMissing = true;
            }
            if (string.IsNullOrWhiteSpace(wInputShipType.Input.Text)) {
                sb.AppendFormat("{0} {1}: {2}\n", GlyphMap.SpriteWarning, T.Missing, T.NounShipType);
                hasMissing = true;
            }
            if (hasMissing) {
                UISounds.PlayActionDenied();
                WPopup.Create(true, IconId.CStop, T.OperationForbidden, null, sb.ToString())
                    .Spawn(UIPoolRoot.SystemPopupContainer);
                return false;
            }
            StartingCrewJobs.FindMissing(data, missingJobs);
            if (missingJobs.Count > 0) {
                sb.Clear();
                // The general text says progress stalls, which panel cleaning alone does not
                // do: dirt only takes a panel's power down over weeks
                var desc = T.CrewMissingPanelCleaningDesc;
                foreach (var missing in missingJobs) {
                    sb.AppendFormat("{0} {1}\n", GlyphMap.SpriteCross, Texts.Red(missing.NameT));
                    if (!missing.NeedsFlyer) {
                        desc = T.CrewMissingJobsDesc;
                    }
                }
                UISounds.PlayActionDenied();
                // Continue, not Confirm: the footer button the player just pressed is Confirm
                WPopup.Create(true, IconId.CWarning, T.CrewMissingJobs, desc, sb.ToString())
                    .WithAction(T.Continue, proceedAnyway)
                    .WithAction2(T.Cancel, null)
                    .Unique("CrewMissingJobs")
                    .Spawn(UIPoolRoot.SystemPopupContainer);
                return false;
            }
            return true;
        }
    }
}
