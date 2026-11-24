using System;
using Game.Constants;
using Game.Components;
using KL.Clock;
using KL.Grid;
using KL.Utils;
using UnityEngine;
using System.Collections;
using KL.Randomness;
using System.Collections.Generic;
using KL.Signals;
using Game.Research;
using Game.Scenarios;
using KL.Collections;
using Game.Story;
using System.Text;
using Game.Data.Space;
using Game.Procgen.Space;
using Game.Rendering;
using Game.Utils;
using Game.CodeGen;
using System.Linq;
using Game.Data.Economy;
using Game.Systems;
using Game.Commands;
using Game.Tools;
using Game.Modifiers;
using Game.Migrations;
using Game.UI;
using Game.Achievements;
using UnityEngine.CrashReportHandler;
using Game.Systems.Creatures;
using UnityEngine.InputSystem;
using Game.Input;
using Game.Platform.Steam;
using Game.Systems.Story;
using Game.Systems.UIUX;
using Game.Platform;
using Game.Systems.Space;

namespace Game.Data {
    /// <summary>
    /// This is usually referred to as "S" variable across the codebase.
    /// It grew out of control over the years.
    /// FIXME: Refactor
    /// </summary>
    public class GameState : ITaggedSignalListener {
        [SelfInit]
        private static void ClearStatics() {
            gameId = 0;
            RenderUtils.CurrentGameId = 0;
        }
        // For marking save as non savable to prevent corrupting the save when
        // load failed + save on top
        public bool IsInconsistent;
        public int SaveVersion;
        public string GameName;
        public string UniqueId;
        public bool IsGenFailure;
        public bool IsSavingNow;
        public bool IsPermadeath;
        // Runtime only, set when game over starts. A save written after it (the permadeath
        // commit of the loss) is marked lost in its meta, and Bootstrap will not put a ship
        // without a computer behind the main menu.
        public bool IsGameOver;
        // Runtime only. Set by a successful save made to exit (Save and exit), so the
        // permadeath save on quit (Permadeath.CommitBeforeQuit) does not write it twice.
        public bool IsSavedForExit;
        public List<string> GameVersions;
        // List of def id h
        public List<int> RecentItems;
        public string SaveDir;
        public string SaveName;
        public string StoryGenId;
        // For diagnostics, not saved
        public int WinchSteps;
        private int winchStarts;
        private int winchEnds;
        public ISharedSave Shared;
        public void AddWinchStep() {
            WinchSteps++;
            CrashReportHandler.SetUserMetadata("winch_steps", Units.Num(WinchSteps));
        }
        public void SetWinchStarted() {
            winchStarts++;
            CrashReportHandler.SetUserMetadata("winch_starts", Units.Num(winchStarts));
        }
        public void SetWinchEnded() {
            winchEnds++;
            CrashReportHandler.SetUserMetadata("winch_ends", Units.Num(winchEnds));
        }
        public long LastWinchTick;
        public DateTime SaveTime;
        public DateTime LoadTime;
        public int SessionStartUnixTimestamp;
        public long LoadTick;
        public float TelescopeProgress;
        // 1f = biological mind control active, anything less - nope
        public float BetaWavesSync;
        public int TelescopeScanCount;
        public long TicksSinceLoad => Clock.Ticks - LoadTick;
        public Grid<int> Grid => Map.Grid;
        public Situation Situation => Map.Situation;
        public float GetTemp(int pos) {
            return Map.Temp.At(pos);
            //return Map.Heat.Data[pos];
        }

        public IEnumerator WaitForSecondsGameTime(float seconds) {
            var start = GameTimer.time;
            // waits while less time than asked has passed; the comparison was inverted, so
            // this returned at once (trails vanished instantly, camera tool delays did nothing)
            while (GameTimer.time - start < seconds) {
                if (G.ClockSpeed > ClockSpeed.Fast2) {
                    break;
                }
                yield return null;
                if (!IsCurrent(this)) {
                    break;
                }
            }
        }

        public static bool IsCurrent(GameState s) {
            if (s == null) { return false; }
            if (s.R == null) { return false; }
            return s.R.IsCurrent;
        }

        public void SetTemp(int pos, float val) {
            Map.Temp.Set(pos, val);
        }

        public float GetO2(int pos) {
            return Map.O2.At(pos);
        }
        public void SetO2(int pos, float val) {
            Map.O2.Set(pos, val);
        }

        /// <returns>The level after the reduction</returns>
        public float ReduceO2(int pos, float amt) {
            return Map.O2.Reduce(pos, amt);
        }

        public float GetTempResistance(int pos) {
            return Map.Temp.ResAt(pos);
        }
        public void SetTempResistance(int pos, float val) {
            Map.Temp.SetRes(pos, val);
        }
        public float GetO2Resistance(int pos) {
            return Map.O2.ResAt(pos);
        }
        public void SetO2Resistance(int pos, float val) {
            Map.O2.SetRes(pos, val);
        }
        public Grid<Tile>[] Grids => Map.Grids;
        public Grid<Tile>[] DataGrids => Map.DataGrids;
        public ComponentState Components => Map.Components;
        ///<summary>Currently active map. (As if there can be more than one, wishful thinking)</summary>
        public Map Map;
        // Global market
        public Market Market;
        public Difficulty Difficulty;
        public MarketPortfolio Portfolio;
        public Wallet Wallet;
        public Universe Universe;
        public Beings Beings => Map.Beings;
        public Objs Objs => Map.Objs;
        public string ShipComputerName;
        public void SetShipComputerName(string name) {
            ShipComputerName = string.IsNullOrWhiteSpace(name)
                ? Consts.DefaultShipComputerName : name;
        }
        public int SessionCount;
        public bool IsAchievementsBlocked;
        public float SessionTime;
        public float Playtime;
        public bool IsSaveVersionCurrent =>
            SaveVersion == Consts.CurrentSaveVersion;
        public Blackboard BB;
        /// <summary>
        /// Temporary state variables, not saved / loaded
        /// </summary>
        public StateTempVars Vars;
        public Toolbox Toolbox;
        public Scenario Scenario;
        public ResearchTree Research;
        public readonly Settings Prefs = new(false);
        public Clock Clock;
        public long Ticks => Clock.Ticks;
        public Rng Rng;
        public int NextId => Map.NextId;
        public object SignalTag => GameId;
        public int GridWidth => Map.Width;
        public int GridHeight => Map.Height;
        public int GridSize => Map.Size;
        public int Seed;
        public string SeedStr;
        public int SeedSpace;
        public string SeedSpaceStr;
        private GameRuntime R;
        #if !PRODUCTION_BUILD
        public void MakeNonCurrent() {
            R.IsCurrent = false;
        }
        #endif
        public void SetRuntime(GameRuntime r) {
            R = r;
            Toolbox = R.Toolbox;
        }
        public GameSystems Sys;
        public GameSig Sig;
        public UISig UISig;
        public UIQuery UIQuery;
        public GameQuery Query;
        private HashSet<string> newBlueprints;
        public void MarkAllBlueprintsSeen() {
            newBlueprints?.Clear();
        }
        public CommandQueue CmdQ;
        public ActionQueue ActQ;

        private static int gameId;
        public bool HasSeenBlueprint(string id) {
            if (Arrays.IsEmpty(newBlueprints)) { return true; }
            return !newBlueprints.Contains(id);
        }

        public void MarkBlueprintAsSeen(string id) {
            if (newBlueprints?.Remove(id) == true) {
                // D.Warn("Marking blueprint as seed: {0}", id);
            }
        }

        public void AddNewBlueprint(string id) {
            (newBlueprints ??= new()).Add(id);
        }

        public int GameId {
            get => gameId;
            private set {
                // D.Warn("GameId {0} -> {1}", gameId, value);
                gameId = value;
                CrashReportHandler.SetUserMetadata("gameId", Units.Num(gameId));
                The.SysSig.GameIdChanged.SendSafe(value);
                Coroutines.GameId = gameId;
                RenderUtils.CurrentGameId = gameId;
            }
        }
        ///<summary>Not current, used only for saving</summary>
        public Vector2 CameraPosition;
        ///<summary>Not current, used only for saving</summary>
        public float CameraZoom;
        public bool IsLoaded { get; private set; }
        public bool WasSaved;
        // Set when the player deletes every save of this game mid-session, so an
        // autosave does not resurrect it. A manual save into SaveDir clears it
        public bool AutoSaveSuspended;
        private readonly List<Entity> pendingRemoval = new();

        public void Cleanup() {
            Universe?.Unload();
            Universe = null;
            Clock?.Cleanup();
            Clock = null;
            BB = null;
            Vars = null;
            Map?.Unload();
            Map = null;
            Market = null;
            Difficulty = null;
            Portfolio = null;
            Toolbox = null;
            Sig?.ReleaseAll();
            Sig = null;
            UISig?.ReleaseAll();
            UISig = null;
            Query = null;
            UIQuery = null;
            Sys = null;
            R = null;
        }

        public List<Grid<Tile>> RenderableGrids => Map.RenderableGrids;

        public void IncrementGameId() {
            GameId++;
        }

        public IEnumerator LoadCurrentMap() {
            if (Universe.Player.Map == null) {
                // This will initialize
                GenUniverse.GenerateCurrentMap(Universe);
                Components.GameId = GameId;
            } else {
                Map = Universe.Player.Map;
                Map.Initialize(this);
                yield return Map.Deserialize(gameId);
            }
            Map.Subscribe();
        }

        /// <summary>
        /// Called by the scenario setup when creating a new game
        /// FIXME rethink / refactor
        /// </summary>
        public IEnumerator New(GameSetupData config) {
            // D.Warn("GameState.New with config: {0}", config.StartingSpaceRegion);
            IsInconsistent = true;
            //ClearSignalListeners();
            IncrementGameId();
            CmdQ.Clear();
            ActQ.Clear();
            Sig.InitializeSpace.Send(config.StartingSpaceRegion);
            The.Dialog?.Reset();
            var mapW = ModifierMapSize.Current;
            Pos.Prime(mapW, mapW);
            Consts.SetCornerMargin(mapW);
            Seed = config.ShipSeed;
            SeedStr = config.ShipType;
            SeedSpace = config.UniverseSeed;
            SeedSpaceStr = config.UniverseName;
            SetShipComputerName(config.ShipComputerName);
            Clock.Initialize();
            ApplySeed();
            ApplyModifiers(false);
            // Like a save loaded in this session (see SetData), a game started while
            // achievements are blocked for the session (cheat mods, tunables) stays blocked
            if (AchievementTracker.IsBlockedGlobally) {
                IsAchievementsBlocked = true;
            }
            var planTool = Toolbox.Get<ToolPlan>(ToolPlan.ToolId);
            planTool.SetMirrorPoint(mapW / 2, mapW / 2);
            planTool.MirrorEven = true;

            using (D.Timer("GenUniverse.Create", 0.5f)) {
                Universe = GenUniverse.Create(this, config.StartingSpaceRegion);
            }
            StoryGenId = config.StoryGen.Id;
            PlayStats.TrackNewGameStarted(
                config.Scenario.Id,
                Modifier.Modified.Count,
                config.Difficulty?.Id ?? "",
                config.Difficulty?.Level ?? 1f,
                config.StoryGen?.Id ?? "",
                mapW,
                config.IsQuickStart);
            Sig.UniverseCreated.Send(Universe);
            GameName = config.ShipName;
            UniqueId = Guid.NewGuid().ToString();
            SaveName = config.SaveName;
            SaveDir = SaveLoadUtils.SaveDir(SaveName);
            SaveVersion = Consts.CurrentSaveVersion;
            SessionTime = 0;
            SessionCount = 0;
            Playtime = 0;
            LoadTime = DateTime.UtcNow;
            SessionStartUnixTimestamp = Units.UnixTimestamp;

            Research.Clear();
            yield return LoadCurrentMap();

            CameraPosition = Grid.Center;
            CameraZoom = 10;
            Sig.AfterLoad.Send();
            Sig.AfterLoadState.Send(this);
            D.Warn("AfterLoadSaveState from New Game");
            The.SysSig.AfterLoadSaveState.Send(this);

            var startsAlone = config.Scenario.StartsAlone;
            SpaceSector startingSector = null;
            if (startsAlone) {
                startingSector = SecludedStart.Find(config.StartingSpaceRegion);
                if (startingSector == null) {
                    // The setup screen forbids such regions, only a caller bypassing it
                    // gets here
                    D.Err("No secluded starting sector in {0}", config.StartingSpaceRegion);
                }
            }
            startingSector ??= StartingSector.Find(config.StartingSpaceRegion);

            if (startingSector == null) {
                // The setup screen forbids such regions, only a caller bypassing it gets here
                D.Err("No planet or asteroid zone to start in {0}", config.StartingSpaceRegion);
                startingSector = config.StartingSpaceRegion.InboundSector;
            }
            if (startsAlone) {
                // Ships were already spawned by PopulateUniverse, the sector contents are
                // generated from the biome when we enter it below
                SecludedStart.Strip(startingSector);
                Sys.NPCShip.RemoveShipsIn(startingSector);
            }
            StartingSector.EnsureIron(startingSector);
            var startingSO = startingSector.SO;

            Sys.Universe.EnterObject(startingSO, Universe.Hyperspace, avoidStations: true);
            Sig.SpaceObjectEnter.Send(startingSO, Universe.Hyperspace);
            Sys.Ship.ForceStop();

            IsLoaded = true;
            IsInconsistent = false;
            #if PRODUCTION_BUILD
            D.Warn("Started Game with Scenario: {0}\n", Scenario.Id);
            #endif
            Sig.NewGameInitialized.Send();
            Ready.Set(WhenH.AllSystemsLoaded, true);
        }

        public GameStateMeta GetMeta() {
            var m = new GameStateMeta();
            m.GameName = GameName;
            m.UniqueId = UniqueId;
            m.Difficulty = Difficulty.Id;
            //D.Warn("Game versions: {0}", D.Dump(GameVersions));
            if (GameVersions == null) {
                m.GameVersion = "v0.5.0";
            } else {
                m.GameVersion = GameVersions[^1];
            }
            if (LoadTime.Year > 2000) {
                m.LoadTime = LoadTime.ToFileTimeUtc();
            }
            if (SaveTime.Year > 2000) {
                m.SaveTime = SaveTime.ToFileTimeUtc();
            }
            m.ClockTicks = Clock.Ticks;
            m.GridHeight = GridHeight;
            m.GridWidth = GridWidth;
            if (m.GridHeight == 0 || m.GridWidth == 0) {
                D.Err("GameStateMeta has grid width / height of 0!");
                m.GridWidth = Pos.GridW;
                m.GridHeight = Pos.GridH;
            }
            m.Playtime = Playtime;
            m.SaveVersion = SaveVersion;
            m.ScenarioId = Scenario.Id;
            m.StoryGenId = StoryGenId;
            m.Seed = Seed;
            m.IsPermadeath = IsPermadeath;
            m.IsGameOver = IsGameOver;
            m.SeedStr = SeedStr;
            m.IsAchievementsBlocked = IsAchievementsBlocked;
            SaveModifiers(null, m);
            SaveMods(m);
            return m;
        }

        public GameStateData GetData() {
            var s = new GameStateData();
            s.GameName = GameName;
            if (Shared != null) {
                #if !DISABLESTEAMWORKS
                s.Shared = SteamSharedSave.Serialize(Shared);
                #endif
            }
            s.Difficulty = Difficulty.Id;
            s.UniqueId = UniqueId;
            var planTool = Toolbox.Get<ToolPlan>(ToolPlan.ToolId);
            s.MirrorPoint = planTool.MirrorPoint;
            s.MirrorEven = planTool.MirrorEven;
            s.IsAchievementsBlocked = IsAchievementsBlocked;
            s.BetaWavesSync = BetaWavesSync;
            s.NewBlueprints = newBlueprints;
            s.LastWinchTick = LastWinchTick;
            s.SaveVersion = SaveVersion;
            s.StoryGenId = StoryGenId;
            s.TelescopeProgress = TelescopeProgress;
            s.TelescopeScanCount = TelescopeScanCount;
            s.GameVersions = GameVersions;
            s.RecentItems = RecentItems;
            if (SaveTime.Year > 2000) {
                s.SaveTime = SaveTime.ToFileTimeUtc();
            }
            s.Tree = Research.SaveProgress();
            s.SettingsData = Prefs.Data;
            s.ClockTicks = Clock.Ticks;
            s.Universe = Universe.GetData();
            s.CameraPosition = CameraPosition;
            s.CameraZoom = CameraZoom;
            s.ShipComputerName = ShipComputerName;
            s.Seed = Seed;
            s.SeedSpace = SeedSpace;
            s.RngState = Rng.State;
            s.SeedStr = SeedStr;
            s.SeedSpaceStr = SeedSpaceStr;
            s.Market = Market;
            s.Portfolio = Portfolio;
            s.Playtime = Playtime;
            s.SessionCount = SessionCount;
            s.ScenarioData = Scenario.Data;
            s.Wallet = Wallet;
            Sys.SaveData(s);
            SaveModifiers(s, null);
            return s;
        }

        public void UpdateGameVersions() {
            if (GameVersions == null) {
                GameVersions = new() {
                    Consts.FullVersion
                };
                return;
            }
            var found = false;
            for (int i = GameVersions.Count - 1; i >= 0; i--) {
                string v = GameVersions[i];
                if (Consts.FullVersion == v) {
                    found = true;
                    break;
                }
            }
            if (!found) {
                GameVersions.Add(Consts.FullVersion);
            }
        }

        public void SetData(GameStateData s) {
            GameName = s.GameName;
            UniqueId = s.UniqueId ?? Guid.NewGuid().ToString();
            TelescopeProgress = s.TelescopeProgress;
            TelescopeScanCount = s.TelescopeScanCount;
            s.Difficulty ??= Difficulty.DefaultId;
            Difficulty = Difficulty.Find(s.Difficulty);
            SaveVersion = s.SaveVersion;
            if (s.Shared != null) {
                #if !DISABLESTEAMWORKS
                if (SteamSharedSave.TryDeserialize(s.Shared, out var shared)) {
                    Shared = shared;
                }
                #endif
            }
            StoryGenId = s.StoryGenId;
            GameVersions = s.GameVersions;
            RecentItems = s.RecentItems;
            newBlueprints = s.NewBlueprints;
            SaveTime = DateTime.FromFileTimeUtc(s.SaveTime);
            BetaWavesSync = s.BetaWavesSync;
            Research.LoadProgress(s.Tree);
            Prefs.SetData(s.SettingsData);
            // Has to happen after the prefs land, because that is where DevInstantBuild
            // comes from - LoadProgress itself runs too early to see it.
            if (A.InstantBuild) {
                Research.ApplyAllUnlockEffects();
            }
            LoadTick = s.ClockTicks;
            Clock.Load(s.ClockTicks);
            Universe.SetData(s.Universe);
            Map = Universe.Player.Map;
            Map.S = this;
            var planTool = Toolbox.Get<ToolPlan>(ToolPlan.ToolId);
            planTool.MirrorPoint = s.MirrorPoint;
            planTool.MirrorEven = s.MirrorEven;
            CameraPosition = s.CameraPosition;
            CameraZoom = s.CameraZoom;
            IsAchievementsBlocked = s.IsAchievementsBlocked
                || AchievementTracker.IsBlockedGlobally;
            Scenario = Scenario.Find(s.ScenarioData.Component);
            Scenario.Data = s.ScenarioData;
            Playtime = s.Playtime;
            Market = s.Market;
            Portfolio = s.Portfolio;
            SessionCount = s.SessionCount;
            Wallet = s.Wallet;
            Wallet.IsPlayer = true;
            // too early
            //R.Sys.Restore(s);
            SetShipComputerName(s.ShipComputerName);
            Seed = s.Seed;
            SeedSpace = s.SeedSpace;
            Rng = Rng.Load(s.RngState);
            SeedStr = s.SeedStr;
            SeedSpaceStr = s.SeedSpaceStr;
            IsInconsistent = false;
            WasSaved = true;
            RestoreModifiers(s);
        }

        public static void  SaveModifiers(GameStateData data, GameStateMeta meta) {
            if (data != null) {
                data.Modifiers ??= new();
                data.Modifiers.Clear();
            }
            if (meta != null) {
                meta.ModifierTooltips ??= new();
                meta.ModifierTooltips.Clear();
            }
            foreach (var m in Modifier.All) {
                if (!m.IsActive) { continue; }
                data?.Modifiers.Add(m.Id);
                meta?.ModifierTooltips.Add(m.Id, m.NameT);
            }
        }

        private void SaveMods(GameStateMeta meta) {
            if (meta == null) { return; }
            meta.Mods ??= new Dictionary<string, string>();
            meta.Mods.Clear();
            foreach (var m in The.ModLoader.LoadedMods) {
                meta.Mods.Add(m.Id, m.ModVersion);
            }
        }

        private void RestoreModifiers(GameStateData s) {
            foreach (var m in Modifier.All) {
                m.IsActive = Arrays.ContainsSafe(s.Modifiers, m.Id);
            }
        }

        private void InitializeSettings() {
            Prefs.Reset();
            Prefs.RegisterBool(Pref.DevInstantBuild, false);
        }

        ///<summary>This is happening inside Empty scene. Any game objects
        /// instantiated here will be destroyed immediately</summary>
        public void InitializeForNewGame() {
            BB = new Blackboard("Local");
            Vars = new StateTempVars();
            Clock?.Cleanup();
            Clock = new Clock(this);
            CmdQ.SetClock(Clock);
            InitializeSettings();
            UpdateGameVersions();
            Universe = new Universe();
            Universe.S = this;
            Research = new(this);
            Wallet = new Wallet();
            Wallet.IsPlayer = true;
            Clock.OnTick.AddListener(OnTick);
            Sig.ConstructionCompleted.AddListener(OnConstructionCompleted);
            Sig.RebuildMap.AddListener(RebuildMap);
        }

        public void AddEntity(Entity entity) {
            if (!Ready.StateLock) {
                D.Err("Trying to add entity while saving! {0}", entity);
                CmdQ.Enqueue(new CmdLambda(() =>
                    AddEntity(entity),
                        "Add entity that was attempted to be removed during save", entity));
                return;
            }
            entity.Initialize(NextId, this);
            entity.S = this;
            Components.RegisterFor(entity);
            Map.RegisterEntity(entity);
            Sig.EntityAdded.Send(entity);
            UpdateSections(entity);
            // Beings and objs will be initialized after the Data is set on it
            if (entity is Tile t) {
                entity.InvokeAfterInitialize();
            }
        }

        private void OnConstructionCompleted(Entity entity) {
            UpdateSections(entity);
            Sys.Areas.NeedsRecalc(entity.Definition.LayerId, entity.PosIdx);
        }

        public T FindEntity<T>(int id) where T : Entity {
            if (Map.TryEntityById(id, out var ntt)) {
                if (ntt is T tntt) {
                    return tntt;
                }
                D.Err("Wrong type of entity by id {0}. Trying to get {1}, but it is {2}",
                    id, typeof(T), ntt.GetType());
            }
            return default;
        }

        public int CountByDef(Def def) {
            if (Map.TryAllByDef(def, out var byDef)) {
                return byDef.Count;
            }
            return 0;
        }
        public bool AllByDef(Def def, out Dictionary<int, Entity> res) {
            return Map.TryAllByDef(def, out res);
        }

        public void RebuildMap() {
            for (int i = 0; i < Grid.Data.Length; i++) {
                UpdateSection(i);
            }
        }

        public void UpdateSections(Entity entity) {
            if (entity is not Tile t) { return; }
            var pos = t.PosIdx;
            if (pos < 1) {
                D.Err("Updating section for invalid index! {0}", pos);
                return;
            }
            // Never do multi tile stuff here, it's handled before this!
            UpdateSection(pos);
        }

        public void UpdateSection(int posIdx) {
            var actualAreaType = Sys.Areas.ResolveAreaTypeAt(posIdx);
            if (actualAreaType != Sys.Areas.AreaTypeAt(posIdx)) {
                Sys.Areas.SetAreaType(posIdx, actualAreaType);
            }
            Sig.RecalculatePassability.Send(posIdx);
        }

        // Why do we want it not immediately? Answer - because it can happen inside tick listener update
        // and when removing we would modify the tick listeners collection.
        public void RemoveEntity(Entity entity, bool immediately = true,
                bool isSubTile = false, bool resolveSubTile = false) {
            if (entity.IsRemoved) {
                D.Err("Removing entity twice! {0}", entity);
                return;
            }
            if (!Ready.StateLock) {
                D.Err("Trying to remove an entity while saving! (Immediate: {0}, entity: {1})",
                    immediately, entity);
                CmdQ.Enqueue(new CmdLambda(() =>
                    RemoveEntity(entity, immediately, isSubTile, resolveSubTile),
                        "Remove entity that was attempted to be removed during save", entity));
                return;
            }
            entity.IsRemoved = true;
            #if UNITY_EDITOR
            if (!entity.IsActive) {
                //// D.Warn("Trying to remove inactive entity! {0}", entity);
            }
            #endif
            entity.HideAllInfo();
            if (Query.IsExclusivelySelected.Ask(entity)) {
                // deselect the selected entity
                Toolbox.FreeSelect.Deactivate();
            }
            entity.Deactivate(Clock.Ticks);
            // Adverts.RemoveForDestroyed(entity);
            if (!immediately) {
                if (!pendingRemoval.AddUniqueInefficient(entity)) {
                    D.Err("Duplicate in pending removal queue! {0}", entity);
                }
                Components.RemoveFor(entity, false);
                return;
            }
            if (resolveSubTile) {
                isSubTile = entity is Tile t && t.Transform.IsChild;
            }
            if (entity is Obj o) {
                if (Objs.All.ContainsRef(o)) {
                    D.Err("Removing Obj from state without removing it from Objs.All!. Use S.Objs.Destroy(obj) instead of S.RemoveEntity! {0}", o);
                    Objs.All.RemoveRef(o);
                }
            } else if (entity is Being b) {
                if (Beings.All.ContainsRef(b)) {
                    D.Err("Removing Being from state without removing it from Beings.All! Use S.Beings.Destroy(being) instead of S.RemoveEntity! {0}", b);
                    Beings.All.RemoveRef(b);
                }
            }
            FinalizeEntityRemoval(entity, isSubTile);
        }

        private void FinalizeEntityRemoval(Entity entity, bool isSubTile) {
            if (!isSubTile) {
                Map.UnregisterEntity(entity);
            }
            Components.RemoveFor(entity, true);
            Sig.EntityRemoved.Send(entity);
            if (!isSubTile) {
                entity.AfterRemove();
            }
            UpdateSections(entity);
        }

        private void ApplySeed() {
            if (Seed == 0) {
                Seed = Rng.UInt;
                SeedStr = Seed.ToString();
                D.Err("Game seed is 0, randomizing to {0}", Seed);
            }
            Rng ??= new Rng(Seed);
            var unseed = Hashes.Quick(Primes.Prime028, Seed);
            unseed = Hashes.Quick(unseed, (int) Clock.Ticks);
            Rng.Unseeded = new Rng(unseed);
        }

        public void BeforeSave(bool willExit) {
            Sig.BeforeSave.Send(this, willExit);
            SaveCameraPosition();
            Clock.BeforeSave();
        }

        private void SaveCameraPosition() {
            CameraPosition = BB.Get(BBKeyH.CameraPosition, Grid.Center);
            CameraZoom = BB.Get(BBKeyH.CameraZoom, 10f);
        }

        public void BeforeLoad() {
            IsInconsistent = true;
            IsLoaded = false;
            if (Research == null) {
                D.Err("Research is null in before load!");
            }
            Research?.Clear();
            GameId++;
            InitializeSettings();
        }

        public IEnumerator AfterLoad() {
            ApplySeed();
            Map?.PrimePos();
            ApplyModifiers(true);

            using (BootTimeline.Span("Load map")) {
                yield return LoadCurrentMap();
            }
            using (BootTimeline.Span("Load migrations")) {
                yield return Migration.RunOn(this, true);
            }
            The.SysSig.LoadingMsg.Send("Restoring research");
            RestoreResearch();

            SessionCount++;

            CmdQ.EnsureEmptyAfterLoad();
            ActQ.EnsureEmptyAfterLoad();
            // These cannod have yield return nulls in between, or it will
            // introduce bugs! :(
            The.SysSig.LoadingMsg.Send("Restoring state");
            yield return null;
            if (!IsCurrent(this)) {
                D.Err("Game state not current in AfterLoad");
                yield break;
            }
            bool signalsSent;
            using (BootTimeline.Span("Load signals")) {
                signalsSent = SendAfterLoadSignals();
            }
            if (!signalsSent) { yield break; }

            IsLoaded = true;

            Components.CleanupAfterLoad();
            SessionStartUnixTimestamp = Units.UnixTimestamp;
            #if PRODUCTION_BUILD
            D.Warn("Loaded Game with Scenario: {0}", Scenario.Id);
            #endif

            if (!IsCurrent(this)) { yield break; }
            try {
                Ready.Set(WhenH.AllSystemsLoaded, true);
            } catch (Exception ex) {
                D.LogEx(ex, "Error in Ready.Set(WhenH.AllSystemsLoaded)!");
                OnAfterLoadFailure(ex);
                yield break;
            }
        }

        private bool SendAfterLoadSignals() {
            try {
                Sig.AfterLoad.Send();
            } catch (System.Exception ex) {
                D.LogEx(ex, "Error in Sig.AfterLoad.Send()!");
                OnAfterLoadFailure(ex);
                return false;
            }
            try {
                Sig.AfterLoadState.Send(this);
            } catch (System.Exception ex) {
                D.LogEx(ex, "Error in Sig.AfterLoadState.Send()!");
                OnAfterLoadFailure(ex);
                return false;
            }
            // D.Warn("AfterLoadSaveState from Load Game");
            // This is the main hook point for most systems
            try {
                The.SysSig.AfterLoadSaveState.Send(this);
            } catch (System.Exception ex) {
                D.LogEx(ex, "Error in SysSig.AfterLoadSaveState.Send()!");
                OnAfterLoadFailure(ex);
                return false;
            }
            return true;
        }

        private void OnAfterLoadFailure(Exception ex) {
            IsInconsistent = true;
            // MiscUtils.LoadSceneTimed("Empty");
            // MiscUtils.LoadSceneTimed("Boot");
            Coroutines.StartDirect(ShowAfterLoadErrorPopup(
                $"GAME BREAKING BUG! There was an error in AfterLoad! Check installed mods! {ex}"));
        }

        private static IEnumerator ShowAfterLoadErrorPopup(string msg) {
            yield return null;
            WPopup.Create(true, IconId.CWarning, T.Error, null, msg)
                .Spawn(UIPoolRoot.SystemPopupContainer);
            yield return null;
            PlatformUtils.SuggestRestart(true);
        }

        private void ApplyModifiers(bool isLoading) {
            Modifier.Modified.Clear();
            foreach (var m in Modifier.All) {
                if (m.IsModified) {
                    Modifier.Modified.Add(m.Id, m);
                }
                //D.Warn("Applying modifier: {0} Actr: {1} Load {2}. IsModified: {3}",
                    //m.Id, m.IsActive, isLoading, m.IsModified);
                m.Apply(this, m.IsActive, isLoading);
                if (!isLoading) {
                    // starting a new game
                    if (m.BlocksAchievements) {
                        IsAchievementsBlocked = true;
                        D.Warn("Blocking achievements due to modifier: {0}",
                            m.DescT);
                    }
                }
            }
        }

        private void RestoreResearch() {
            Research.Reload();
        }

        private void OnTick(long ticks) {
            Flush();
        }

        public void Flush() {
            if (pendingRemoval.Count == 0) { return; }
            if (!Ready.StateLock) {
                D.Err("Trying to finalize pendingRemoval during save!");
                return;
            }
            // inverse for because we can append to this list while removing
            for (int i = pendingRemoval.Count - 1; i >= 0; i--) {
                Entity entity = pendingRemoval[i];
                pendingRemoval.RemoveAt(i);
                if (entity is Tile t) {
                    FinalizeEntityRemoval(entity, t.Transform?.IsChild == true);
                } else {
                    FinalizeEntityRemoval(entity, false);
                }
            }
        }

        public string DebugInfo(bool forPlayer) {
            #if UNITY_EDITOR
            forPlayer = false;
            #endif
            var sb = new StringBuilder();
            try {
                int genAttempts = The.BB.Get(BBKeyH.GlobalGenAttempt, 0);
                if (IsAchievementsBlocked) {
                    if (forPlayer) {
                        sb.AppendLine(Texts.Red(T.AchievementsDisabled));
                    } else {
                        sb.AppendLine("Achievements Blocked");
                    }
                }
                var storyCtx = Query.GetStoryContext.Ask();
                if (!forPlayer) {
                    sb.AppendFormat("Versions: {0}\n", string.Join(", ", GameVersions));
                }
                sb.AppendFormat("Seed: {0} ({1})\n", SeedStr, Seed);
                sb.AppendFormat("Seed (Space): {0} ({1})\n", SeedSpaceStr, SeedSpace);
                if (genAttempts > 0) {
                    sb.AppendFormat("Gen Attempts: {0}\n", genAttempts);
                }
                sb.AppendFormat("Size: {0}x{1}\n", GridWidth, GridHeight);
                sb.AppendFormat("Ticks: {0} ({1})\n", Clock.Ticks,
                    Units.TicksToTime(Clock.Ticks));
                if (Clock.IsForceStopped) {
                    sb.AppendFormat("ForceStopped: {0}\n", Clock.StopReasons);
                }
                //Clock.PrintStats(sb);
                sb.AppendFormat("Session: {0} [x{1}]\n",
                    Units.SecondsToTime(SessionTime), SessionCount);
                sb.AppendFormat("Playtime: {0}\n", Units.SecondsToTime(Playtime));
                sb.AppendFormat("Player: {0}/{1}:{2}\n", The.Platform.PlayerName,
                    The.Platform.PlayerHash, The.Platform.SysHash);
                sb.AppendFormat("Scenario: {0}\n", Scenario.Id);
                sb.AppendFormat("Storygen: {0}\n", storyCtx.Generator.Id);
                sb.AppendFormat("Difficulty: {0}. Sector: {1}. Region: {2}\n",
                    Difficulty.Id,
                    Units.Percentage(A.PlayerSector?.Sector?.Biome.Difficulty ?? 0f, false),
                    Units.Percentage(A.PlayerSector?.Sector?.Region?.Biome.Difficulty ?? 0f, false));
                sb.AppendFormat("GameID: {0}\n", GameId);
                sb.AppendFormat("Ship Class: {0}\n", A.Player?.Ship?.Class.Id);
                sb.AppendFormat("Class Points: {0}\n", A.Player?.Ship?.CombatState?.CurrentClassPoints);
                sb.AppendFormat("Is Outlaw: {0}\n", A.Player?.Ship?.IsOutlaw);
                sb.AppendFormat("Is In Hyperspace: {0}\n", A.IsInHyperspace);
                sb.AppendFormat("Starcreds: {0}\n", Wallet.ValueString);
                sb.AppendFormat("Beings: {0}\n", Beings.All.Count);
                sb.AppendFormat("Objects: {0}\n", Objs.All.Count);
                sb.AppendFormat("Allocated Textures: {0} (VRAM: {1})\n",
                    The.Alloc.AllocatedTextures,
                    The.Alloc.AllocatedGPUMB);
                sb.AppendFormat("Telescope: {0}/{1}\n", Units.PercentageWithFraction4(TelescopeProgress), TelescopeScanCount);
                sb.AppendFormat("Floors: {0}\n", Map.Grids[WorldLayer.Floor]
                    .Data.Count(f => f != null && f.IsActive));
                sb.AppendFormat("Walls: {0}\n", Map.Grids[WorldLayer.Walls]
                    .Data.Count(f => f != null && f.IsActive));
                sb.AppendFormat("Grid Objects: {0}\n", Map.Grids[WorldLayer.Objects]
                    .Data.Count(f => f != null && f.IsActive && !f.Transform.IsChild));
                //sb.AppendFormat("Research: {0}\n", Research.DebugInfo());
                sb.AppendFormat("Mods: {0}\n", string.Join(", ", The.ModLoader.LoadedMods));
                sb.AppendFormat("Connected input devices:\n");
                foreach (var dev in InputSystem.devices) {
                    if (dev.enabled && dev != VirtualMouseInput.VMouse) {
                        sb.AppendFormat(" - {0}", dev);
                        if (dev.noisy) {
                            sb.Append(" [noisy!]");
                        }
                        sb.AppendLine();
                    }
                }
                if (Modifier.Modified.Count > 0) {
                    sb.AppendFormat("Modifiers: {0}\n", Modifier.ActiveNames(","));
                }
                if (forPlayer) {
                    sb.AppendFormat("Ongoing Events: {0}\n", D.DumpCollection(
                        storyCtx.OngoingEvents));
                } else {
                    sb.AppendFormat("Ongoing Events: {0}\n",
                        storyCtx.OngoingEvents.Count);
                }
            } catch (System.Exception ex) {
                D.LogEx(ex, "Failed processing debug info for game state");
            }
            return sb.ToString();

        }

        public override string ToString() {
            if (Map == null) {
                return "GameState[Uninitialized]";
            }
            return string.Format("GameState[{0}, {1}x{2}]",
                SeedStr, GridWidth, GridHeight);
        }
        public void SetO2AtLeast(int pos, float amount) {
            Map.O2.SetAtLeast(pos, amount);
        }
        public void SetTempAtLeast(int pos, float amount) {
            Map.Temp.SetAtLeast(pos, amount);
        }

        public float IncrTemp(int pos, float amount, float max) {
            return Map.Temp.Increase(pos, amount, max);
        }

        public float IncrO2(int pos, float amount, float max, float minRes) {
            return Map.O2.Increase(pos, amount, max, minRes);
        }

        public void PinO2AtLeast(int pos, float value) {
            Map.O2.PinAtLeast(pos, value);
        }

        public void PinTempAtLeast(int pos, float value) {
            Map.Temp.PinAtLeast(pos, value);
        }

        public void PinTempAtMost(int pos, float value) {
            Map.Temp.PinAtMost(pos, value);
        }

        public void DecrO2(int pos, float amount) {
            Map.O2.Decrease(pos, amount);
        }
    }
}
