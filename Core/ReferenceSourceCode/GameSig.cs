using System;
using System.Collections.Generic;
using Game.Components;
using Game.Data;
using Game.Data.Space;
using Game.Input;
using Game.Systems;
using Game.Tools;
using Game.UI;
using Game.Visuals;
using KL.Signals;
using UnityEngine;
using Game.Constants;
using Game.Systems.Atmo;
using Game.Systems.AI;
using Game.Systems.Conditions;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Systems.BodyParts;
using Game.Systems.Stats;
using Game.Systems.Mental;
using Game.Systems.Firex;
using Game.Systems.Sleep;
using Game.Systems.Trade;
using System.Reflection;
using Game.Systems.Space;
using Game.Systems.Copters;
using Game.Systems.Crafting;
using Game.Systems.Nanobots;
using Game.Systems.Ships;
using Game.Systems.Story;
using Game.Procgen;
using Game.Systems.Drama;
using Game.Systems.Creatures.Surgeries;
using Game.Systems.Quests;
using Game.Systems.AI.Combat;
using Game.Components.Upgrades;
using Game.Data.Factions;
using Game.Modifiers;
using Game.Systems.UIUX;

namespace Game {
    public class GameSig {
        public Signal1<Explosion> CreateExplosion = new(nameof(CreateExplosion));
        public Signal0 RebuildRegs = new(nameof(RebuildRegs));
        public Signal0 RebuildBeingGroups = new(nameof(RebuildBeingGroups), isOptional: true);
        public Signal1<BeingData> BeingInitialized = new(nameof(BeingInitialized), isOptional: true);
        public Signal1<BeingData> BeingUninitialized = new(nameof(BeingUninitialized), isOptional: true);
        // A being got job slots it did not have: a new ability-gated job, or a whole other mind's
        public Signal1<BeingData> BeingJobsChanged = new(nameof(BeingJobsChanged), isOptional: true);
        /// <summary>
        /// A guest (e.g. delivery quest passenger) stopped being a guest because their own
        /// faction started raiding, so they joined the raid. Used to fail owning quests.
        /// </summary>
        public Signal1<BeingData> BeingDefected = new(nameof(BeingDefected), isOptional: true);
        /// <summary>
        /// int = previous group
        /// </summary>
        public Signal2<BeingData, int> BeingGroupChanged = new(nameof(BeingGroupChanged), isOptional: false);
        public Signal2<BedComp, bool> RegisterBed = new(nameof(RegisterBed), isOptional: false);
        public Signal2<BotDockComp, bool> RegisterBotDock = new(nameof(RegisterBotDock), isOptional: false);
        public Signal1<uint> DockableStateChanged = new(nameof(DockableStateChanged), isOptional: true);
        public Signal1<uint> BedSlotStateChanged = new(nameof(BedSlotStateChanged), isOptional: true);
        public Signal2<NanobotHiveComp, bool> RegisterNanobotHive = new(nameof(RegisterNanobotHive), isOptional: false);
        /// <summary>
        /// bool = add or remove
        /// </summary>
        public Signal2<PlanterComp, bool> RegisterPlanter = new(nameof(RegisterBed), isOptional: false);
        public Signal2<IWildPlant, bool> RegisterWildPlant = new(nameof(RegisterWildPlant), isOptional: false);
        public Signal2<BeingData, BeingData> CrawlerAttached = new(nameof(CrawlerAttached));
        /// <summary>
        /// bool = started or extinguished
        /// </summary>
        public Signal2<Fire, bool> FireChanged = new(nameof(FireChanged));
        public Signal1<AtmoLayer> AtmoRecalculated = new(nameof(AtmoRecalculated));
        // string: why, shown in the abort dialog; null when unknown
        public Signal2<BeingData, string> FailedToEnterShuttle = new(nameof(FailedToEnterShuttle));

        /// <summary>
        /// bool is true if condition is newly added rather than loaded
        /// </summary>
        public Signal2<ConditionState, bool> ConditionAdded = new(nameof(ConditionAdded), isOptional: false);
        /// <summary>
        /// This would add / remove the condition from the list, and set the appropriate flags
        /// </summary>
        public Signal2<ConditionState, bool> ActivateCondition = new(nameof(ActivateCondition), isOptional: false);
        public Signal2<BeingData, int> ConditionRemoved = new(nameof(ConditionRemoved), isOptional: false);
        public Signal2<ObjStorageComp, EntityChange> ObjStorageChanged = new(nameof(ObjStorageChanged), isOptional: false);
        public Signal2<BeingStorageComp, EntityChange> BeingStorageChanged = new(nameof(BeingStorageChanged), isOptional: false);
        /// <summary>
        /// started or ended
        /// </summary>
        public Signal2<bool, CombatFlags> CombatChanged = new(nameof(CombatChanged));
        /// <summary>
        /// started or ended
        /// </summary>
        public Signal1<bool> ShipCombatChanged = new(nameof(ShipCombatChanged));
        public Signal2<NPCShip, bool> ShipCombatEngagedWith = new(nameof(ShipCombatEngagedWith));
        /// <summary>
        /// An NPC ship joined (true) or left (false) the player's side in ship combat.
        /// Never sent for the player's own ship.
        /// </summary>
        public Signal2<NPCShip, bool> ShipCombatAllyChanged = new(nameof(ShipCombatAllyChanged));
        public Signal1<AIGoal> AIAddGoal = new(nameof(AIAddGoal));
        public Signal1<AIGoal> AIRemoveGoal = new(nameof(AIRemoveGoal));
        public Signal1<AIAgentComp> AIAddAgent = new(nameof(AIAddAgent));
        public Signal1<AIAgentComp> AIRemoveAgent = new(nameof(AIRemoveAgent));
        public Signal2<BeingData, MentalEffect> AddMentalEffect = new(nameof(AddMentalEffect), isOptional: false);
        /// <summary>
        /// float = autoLogThreshold
        /// bool = autoLog
        /// </summary>
        public Signal4<BeingData, MentalEffect, float, bool> AddMentalEffectWithLog = new(nameof(AddMentalEffectWithLog), isOptional: false);

        public Signal2<RectTransform, bool> OnHoverUIBlockWithSubmenu = new(nameof(OnHoverUIBlockWithSubmenu));
        public Signal1<Being> DirectControlChanged = new(nameof(DirectControlChanged));
        public Signal1<Being> WorkerSwitchChanged = new(nameof(WorkerSwitchChanged));
        // This rotates the starfield to reflect the ship rotation
        public Signal1<float> OnShipRotate = new(nameof(OnShipRotate));
        public Signal1<bool> FlightReadyChanged = new(nameof(FlightReadyChanged));
        // from -> to
        public Signal3<Being, int, int> BeingPosChanged = new(nameof(BeingPosChanged));
        // When we fly, this will be called with new worldPos
        // public Signal1<Vector2> VisualWorldPosChanged = new(nameof(VisualWorldPosChanged));
        // This rotates the game camera so the ship looks slightly tilted. Don't rotate more than by +- 5 degrees
        public Signal1<float> RotateGameCam = new(nameof(RotateGameCam));
        public Signal1<TileLabelComp> LabelChanged = new(nameof(LabelChanged));
        public Signal1<float> OnFullScreenWidgetZoom = new(nameof(OnFullScreenWidgetZoom));
        public Signal2<StoryEventData, ShipFactory> DerelictShipApproached = new(nameof(DerelictShipApproached));
        public Signal1<bool> ToggleRendering = new(nameof(ToggleRendering));
        public Signal1<bool> TogglePauseFrame = new(nameof(TogglePauseFrame));
        public Signal1<TradeUI> TradeExecuted = new(nameof(TradeExecuted));
        public Signal0 Show = new(nameof(TradeExecuted));
        public Signal2<VentComp, bool> VentChanged = new(nameof(VentChanged));
        //public Signal2<FlammableComp, bool> FlammableChanged = new(nameof(FlammableChanged));
        //public Signal2<int, bool> SetOnFire = new(nameof(SetOnFire));
        public Signal3<TeleporterComp, int, int> AddTeleporter = new(nameof(AddTeleporter));
        public Signal2<TeleporterComp, int> RemoveTeleporter = new(nameof(RemoveTeleporter));
        // public Signal1<string> PauseTest = new(nameof(PauseTest));
        public Signal1<BeingData> BeingDied = new(nameof(BeingDied));
        // A being went down (incapacitated) while alive on the map, not when a save loads one
        // that already was
        public Signal1<BeingData> BeingIncapacitated = new(nameof(BeingIncapacitated));
        public Signal1<IWithTrajectory> AddObjWithTrajectory = new(nameof(AddObjWithTrajectory));
        public Signal1<bool> OnShowFullScreenWidget = new(nameof(OnShowFullScreenWidget));
        public Signal0 AreasInitialized = new(nameof(AreasInitialized));
        public Signal1<SpaceObject> SettleIn = new(nameof(SettleIn));
        public Signal0 TelescopeScanComplete = new(nameof(TelescopeScanComplete));
        public Signal1<SpaceObject> FoundTerraformable = new(nameof(FoundTerraformable));
        public Signal2<TerraformType, float> TerraformProgress = new(nameof(TerraformProgress));
        public Signal2<TerraformType, float> TerraformUpdated = new(nameof(TerraformUpdated));
        public Signal1<Projectile> FireProjectile = new(nameof(FireProjectile));
        public Signal2<Tile, bool> CoverChanged = new(nameof(CoverChanged));
        public Signal1<bool> ShowConnectorCounts = new(nameof(ShowConnectorCounts));
        public Signal1<bool> ShowConnectorCountsNew = new(nameof(ShowConnectorCountsNew));
        // dx, dy
        public Signal0 WinchStarted = new(nameof(WinchStarted));
        /// <summary>
        /// When ship is being winched.
        /// </summary>
        public Signal2<int, int> ShipMoved = new(nameof(ShipMoved));
        public Signal0 ShipRotated = new(nameof(ShipRotated));
        // moved?
        public Signal1<bool> WinchEnded = new(nameof(WinchEnded));
        // What, from, to
        public Signal3<Entity, TeleporterComp, TeleporterComp> EntityTeleportStarted = new(nameof(EntityTeleportStarted));
        // What, to
        public Signal2<Entity, TeleporterComp> EntityTeleportEnded = new(nameof(EntityTeleportEnded));
        public Signal0 SkipTutorial = new(nameof(SkipTutorial));
        public Signal1<ITool> ToolChanged = new(nameof(ToolChanged));
        // old tile id, new tile id
        public Signal2<int, int> TileRelocated = new(nameof(TileRelocated));
        public Signal1<LogEntry> LogEntryAdded = new(nameof(LogEntryAdded), isOptional: true);
        public Signal1<Def> AddToRecentBuildItems = new(nameof(AddToRecentBuildItems));
        public Signal1<Being> BeingChanged = new(nameof(BeingChanged));
        public Signal0 RefreshSearchIndex = new(nameof(RefreshSearchIndex));
        // entity id -> entity id -> on / off
        public Signal3<int, int, bool> EnergyConnectionChanged = new(nameof(EnergyConnectionChanged));
        public Signal0 MarketDayChanged = new(nameof(MarketDayChanged));
        // before, after
        public Signal2<int, int> IslandCountChanged = new(nameof(IslandCountChanged));
        /// <summary>
        /// Strictly for UI OR things that use Jobs with Allocator.TempJob, which can only live up to 4 frames.
        /// For game logic that does not have such limits as Allocator.TempJob, use S.Clock.AfterProgressUpdate
        /// difference from OnUpdate is that OnUpdate is not called if game is not ready
        /// </summary>
        public Signal0 EveryFrame = new(nameof(EveryFrame));
        public Signal0 OnLateUpdate = new(nameof(OnLateUpdate));
        public Signal0 PausePressed = new(nameof(PausePressed));
        public Signal0 ClickPressed = new(nameof(ClickPressed));
        public Signal0 ClickReleased = new(nameof(ClickReleased));
        public Signal0 AreasRecalculated = new(nameof(AreasRecalculated));
        public Signal0 UpdatePlan = new(nameof(UpdatePlan));
        public Signal0 TriggerSirens = new(nameof(TriggerSirens));
        public Signal1<int> CycleTool = new(nameof(CycleTool));
        public Signal2<EmergencySirenComp, bool> ToggleSiren = new(nameof(ToggleSiren));
        public Signal1<GameState> AfterLoadState = new(nameof(AfterLoadState));
        public Signal0 AfterLoad = new(nameof(AfterLoad));
        // public Signal0 BeforeInitState = new(nameof(BeforeInitState));
        public Signal0 RebuildBuildMenu = new(nameof(RebuildBuildMenu));
        public Signal1<TeleporterComp> TradeTeleporterSelected = new(nameof(TradeTeleporterSelected));
        /// <summary>
        /// Cleanable device, Cleaner
        /// </summary>
        public Signal2<CleanableComp, BeingData> CleanableCleaned = new(nameof(CleanableCleaned));
        /// <summary>
        /// float = initial value
        /// </summary>
        public Signal1<Being> NeedsChanged = new(nameof(NeedsChanged));
        public Signal2<Trader, List<TradeableItem>> LoadOurTradeableItemsFor = new(nameof(LoadOurTradeableItemsFor));
        public Signal2<SpaceObject, MatAvailability> SetMatAvailability = new(nameof(SetMatAvailability), isOptional: false);
        public Signal1<Trader> RemoveTrader = new(nameof(Trader));
        public Signal1<Entity> FollowEntity = new(nameof(FollowEntity));
        /// <summary>
        /// Will also focus the camera on the entity
        /// </summary>
        public Signal1<Entity> SelectEntity = new(nameof(SelectEntity), isOptional: false);
        public Signal1<Entity> SelectEntityUnfocused = new(nameof(SelectEntityUnfocused), isOptional: false);
        public Signal0 ElectricityGridChanged = new(nameof(ElectricityGridChanged));
        // delay, time
        public Signal2<float, float> ShakeCamera = new(nameof(ShakeCamera));
        public Signal1<IShipComputerModule> ShipComputerModuleChanged = new(nameof(ShipComputerModuleChanged));
        public Signal2<ISlots, bool> SlotsChanged = new(nameof(SlotsChanged));
        public Signal1<Particles> ParticlesCreated = new(nameof(ParticlesCreated));
        public Signal1<Particles> ParticlesDestroyed = new(nameof(ParticlesDestroyed));
        public Signal3<List<Being>, List<Obj>, List<Tile>> MultiSelect = new(nameof(MultiSelect));
        public Signal1<List<Being>> BeingsSelected = new(nameof(BeingsSelected));
        public Signal1<List<Obj>> ObjSelected = new(nameof(ObjSelected));
        public Signal1<List<Tile>> TilesSelected = new(nameof(TilesSelected));
        public Signal1<Tile> TileRepaired = new(nameof(TileRepaired));
        public Signal1<List<Tile>> ShowMultiTileContextMenu = new(nameof(ShowMultiTileContextMenu));
        public Signal1<List<Being>> ShowMultiBeingContextMenu = new(nameof(ShowMultiBeingContextMenu));
        public Signal1<List<Obj>> ShowMultiObjContextMenu = new(nameof(ShowMultiObjContextMenu));
        public Signal1<float> CamOrthoSizeChanged = new(nameof(CamOrthoSizeChanged));
        public Signal1<float> BeingNameFontSizeChanged = new(nameof(BeingNameFontSizeChanged));
        public Signal1<Tile> ConstructionCompleted = new(nameof(ConstructionCompleted));
        public Signal1<Entity> EntityRemoved = new(nameof(EntityRemoved));
        public Signal1<Entity> EntityAdded = new(nameof(EntityAdded));
        public Signal1<Being> BeingAdded = new(nameof(BeingAdded));
        public Signal2<StatsContainer, bool> ActivateStats = new(nameof(ActivateStats));
        public Signal0 ResetTileGraphics = new(nameof(ResetTileGraphics));
        public Signal2<Vector2, float> CameraGoto = new(nameof(CameraGoto));
        /// <summary>
        /// Position and padding
        /// </summary>
        public Signal2<Vector2, float> CameraEnsureVisible = new(nameof(CameraEnsureVisible));
        /// <summary>
        /// move position, rotation adjusted move pos
        /// </summary>
        public Signal2<Vector3, Vector3> OnMoveCamera = new(nameof(OnMoveCamera));
        /// <summary>
        /// position, size, dist (0 = auto)
        /// </summary>
        public Signal3<Vector2, Vector2, float> CameraFocus = new(nameof(CameraFocus));
        /// <summary>
        /// Like CameraFocus, but the flash outlines the entity's silhouette
        /// </summary>
        public Signal1<Entity> CameraFocusEntity = new(nameof(CameraFocusEntity));
        /// <summary>
        /// Use only when instant jump is necessary, like before making a screenshot
        /// </summary>
        public Signal3<Vector2, Vector2, float> CameraJump = new(nameof(CameraJump));
        public Signal3<Vector2, float, float> CameraDolly = new(nameof(CameraDolly));
        public Signal2<Entity, Entity> CameraFocusRooted = new(nameof(CameraFocusRooted));
        public Signal3<Entity, Entity, Color> CameraShowLink = new(nameof(CameraShowLink));
        public Signal4<Entity, Vector2, Vector2, Color> CameraShowPosLink = new(nameof(CameraShowPosLink));
        /// <summary>
        /// Like CameraShowPosLink, but outlines the tiles' shape (a room, a ship section)
        /// </summary>
        public Signal3<Entity, ICollection<int>, Color> CameraShowTilesLink = new(nameof(CameraShowTilesLink));
        /// <summary>
        /// Like CameraFocus on the tiles' bounds, but the flash outlines their shape
        /// </summary>
        public Signal1<ICollection<int>> CameraFocusTiles = new(nameof(CameraFocusTiles));
        public Signal1<int> RecalculateAtmosphere = new(nameof(RecalculateAtmosphere));
        public Signal1<int> RecalculatePassability = new(nameof(RecalculatePassability));
        public Signal2<OverlayInfo, bool> ToggleOverlay = new(nameof(ToggleOverlay));
        public Signal1<string> ToggleOverlayById = new(nameof(ToggleOverlayById));
        public Signal0 HideOverlay = new(nameof(HideOverlay));
        public Signal1<string> HideTargetOverlay = new(nameof(HideTargetOverlay));
        public Signal2<RectTransform, Color> SetUITarget = new(nameof(SetUITarget));
        public Signal2<RectTransform, float> SetUITargetWithTimeout = new(nameof(SetUITargetWithTimeout));
        public Signal2<RectTransform, Color> SetUITargetChildren = new(nameof(SetUITargetChildren));
        public Signal2<Rect, Color> SetUITargetRect = new(nameof(SetUITargetRect));
        /// <summary>
        /// Links the mouse to a UI element for this frame only, send it every frame the
        /// hover lasts
        /// </summary>
        public Signal1<RectTransform> HoverUITarget = new(nameof(HoverUITarget));
        public Signal1<string> ShowOverlay = new(nameof(ShowOverlay));
        public Signal1<Entity> FireStopped = new(nameof(FireStopped));
        public Signal0 ForceAreas = new(nameof(ForceAreas));
        //public Signal0 ForceIslands = new(nameof(ForceIslands));
        public Signal1<bool> GenerationRunning = new(nameof(GenerationRunning));
        // CPU, Memory, Disk, GPU
        public Signal4<int, int, int, int> CPUResourcesUpdated = new(nameof(CPUResourcesUpdated));
        public Signal0 RebuildMap = new(nameof(RebuildMap));
        public Signal1<Def> ResearchChanged = new(nameof(ResearchChanged));
        public Signal1<Dictionary<MatType, BInt>> DatoidCountsUpdated = new(nameof(DatoidCountsUpdated));
        /// <summary>
        /// int = quantity
        /// </summary>
        public Signal2<CraftingOrder, int> CrafterProducedItem = new(nameof(CrafterProducedItem));
        public Signal2<MatType, int> MatProduced = new(nameof(MatProduced), isOptional: false);
        public Signal2<MatType, int> MatConsumed = new(nameof(MatConsumed), isOptional: false);
        public Signal2<Def, int> ObjProduced = new(nameof(ObjProduced), isOptional: true);
        public Signal2<Def, int> ObjConsumed = new(nameof(ObjConsumed), isOptional: true);
        public Signal1<string> ShowFanfare = new(nameof(ShowFanfare));
        public Signal1<string> SayVoiceLine = new(nameof(SayVoiceLine));
        public Signal2<MatDeficitEventHolder, bool> MatDeficitChanged = new(nameof(MatDeficitChanged));
        public Signal2<ICanBeIdle, bool> IdleChanged = new(nameof(IdleChanged));
        public Signal2<ICanBeUnconfigured, bool> UnconfiguredChanged = new(nameof(UnconfiguredChanged));
        public Signal2<Vector2, float> OnUIScrollNav = new(nameof(OnUIScrollNav));
        public Signal1<Vector3> OnUIScrollDrag = new(nameof(OnUIScrollDrag));
        public Signal1<ActionType> ShortcutPressed = new(nameof(ShortcutPressed));
        public Signal2<SpaceObject, SpaceObject> SpaceObjectEnter = new(nameof(SpaceObjectEnter));
        /// <summary>
        /// bool is true if object became nearby, false if no longer nearby
        /// </summary>
        /// <returns></returns>
        public Signal2<SpaceObject, bool> SpaceObjectNearby = new(nameof(SpaceObjectNearby));
        public Signal1<SpaceObject> SpaceObjectChanged = new(nameof(SpaceObjectChanged));
        public Signal1<SpaceObject> SpaceObjectClicked = new(nameof(SpaceObjectClicked));
        public Signal1<SpaceObject> SpaceObjectAdded = new(nameof(SpaceObjectAdded));
        public Signal1<SpaceObject> SpaceObjectRemoved = new(nameof(SpaceObjectRemoved));
        /// <summary>
        /// string = debug reason why
        /// </summary>
        public Signal1<string> RefocusWidget = new(nameof(RefocusWidget));
        public Signal1<string> AddSuggestion = new(nameof(AddSuggestion));
        // id, quest data (could be null)
        public Signal1<string> AddQuest_Legacy = new(nameof(AddQuest_Legacy));
        // quest.State is now, second var is previous state
        public Signal1<Quest> AddQuestCustom_Legacy = new(nameof(AddQuestCustom_Legacy));
        public Signal2<SpaceObject, Action<UISpaceObject, SpaceObjectLooks>> UpdateSpaceObjectUI = new(nameof(UpdateSpaceObjectUI));
        /// <summary>
        /// Mind, From object, to being
        /// </summary>
        public Signal3<Mind, ObjData, BeingData> MindInstalled = new(nameof(MindInstalled));
        /// <summary>
        /// Mind, from being, to object
        /// </summary>
        public Signal3<Mind, BeingData, ObjData> MindRemoved = new(nameof(MindRemoved));
        /// <summary>
        /// Region = starting region, it has a reference to SpaceMap within it
        /// </summary>
        public Signal1<SpaceRegion> InitializeSpace = new(nameof(InitializeSpace), isOptional: false);
        public Signal1<bool> ShowJobMatrix = new(nameof(ShowJobMatrix), isOptional: false);
        /// <summary>
        /// int is position, bool is added / removed
        /// </summary>
        public Signal2<TileTransformComp, bool> PortsChanged = new(nameof(PortsChanged), isOptional: false);
        public Signal1<Expedition> ExpeditionStarted = new(nameof(ExpeditionStarted));
        public Signal1<Expedition> ExpeditionEnded = new(nameof(ExpeditionEnded));
        public Signal1<CopterMissionData> CopterMissionEnded = new(nameof(CopterMissionEnded));
        public Signal1<CopterMissionData> CopterMissionStarted = new(nameof(CopterMissionStarted));
        public Signal1<OnlineOrder> ShopOrderDelivered = new(nameof(ShopOrderDelivered));
        public Signal1<OnlineOrder> ShopOrderCreated = new(nameof(ShopOrderCreated));
        public Signal0 ToggleOnlineShopUI = new(nameof(ToggleOnlineShopUI));
        public Signal2<MultiCrafterComp, bool> RegisterCrafter = new(nameof(RegisterCrafter), isOptional: false);
        public Signal1<HyperdriveComp> HyperjumpExecuted = new(nameof(HyperjumpExecuted));
        public Signal1<bool> EnteredHyperspace = new(nameof(EnteredHyperspace));
        public Signal1<HyperspaceState> HyperspaceStateChanged = new(nameof(HyperspaceStateChanged));
        /// <summary>
        /// Max charge, can jump
        /// </summary>
        public Signal2<float, bool> HyperspaceCombatStateChanged = new(nameof(HyperspaceCombatStateChanged));
        public Signal2<SpaceObject, SpaceObject> HyperspaceLocationChanged = new(nameof(HyperspaceLocationChanged));
        public Signal0 UniverseRegenerated = new(nameof(UniverseRegenerated));
        /// <summary>
        /// Called after universe was created. NPC ships will be spawned after this.
        /// SpaceObject: universe root
        /// </summary>
        public Signal1<Universe> UniverseCreated = new(nameof(UniverseCreated));
        public Signal1<NPCShip> NPCShipSpawned = new(nameof(NPCShipSpawned));
        /// <summary>
        /// bool = true -> entered sector, false -> left sector (jumped into hyperspace)
        /// </summary>
        public Signal3<NPCShip, SpaceSector, bool> NPCShipEnteredSector = new(nameof(NPCShipEnteredSector));
        public Signal3<NPCShip, SpaceSector, SpaceSector> NPCShipMovedInHyperspace = new(nameof(NPCShipMovedInHyperspace));
        /// <summary>
        /// Called when NPCShip ShipView goes into screen or off screen
        /// </summary>
        public Signal2<ShipView, bool> ShipViewVisibilityChanged = new(nameof(ShipViewVisibilityChanged));
        /// <summary>
        /// Called after a new game was initialized for the first time.
        /// After ship and universe were generated.
        /// </summary>
        public Signal0 NewGameInitialized = new(nameof(NewGameInitialized));
        /// <summary>
        /// SpaceObject = Starmap.ParentSO after it gets resolved
        /// </summary>
        public Signal1<SpaceObject> BeforeLoadStarmap = new(nameof(BeforeLoadStarmap));
        /// <summary>
        /// Bool: will exit game
        /// </summary>
        public Signal2<GameState, bool> BeforeSave = new(nameof(BeforeSave));
        /// <summary>
        /// bool = added or removed
        /// </summary>
        public Signal2<NPCShip, bool> NPCShipSpawnedInGameView = new(nameof(NPCShipSpawnedInGameView));
        public Signal1<UIFocusablePanel> FocusUIPanel = new(nameof(FocusUIPanel));
        public Signal0 DefocusAllPanels = new(nameof(DefocusAllPanels));
        public Signal2<IThreat, bool> RegisterThreat = new(nameof(RegisterThreat));
        public Signal2<PIPCamera, bool> PIPCameraActivated = new(nameof(PIPCameraActivated));
        /// <summary>
        /// The ship the PIP inset shows has changed while the camera stays active
        /// </summary>
        public Signal1<NPCShip> PIPSubjectChanged = new(nameof(PIPSubjectChanged));
        /// <summary>
        /// Attacker, Victim
        /// </summary>
        public Signal2<NPCShip, NPCShip> ShipDefeatedInCombat = new(nameof(ShipDefeatedInCombat));
        /// <summary>
        /// Fired unconditionally whenever an NPCShip becomes a derelict (combat, console
        /// command, etc), regardless of whether an attacker is involved.
        /// </summary>
        public Signal1<NPCShip> ShipTurnedDerelict = new(nameof(ShipTurnedDerelict));
        public Signal1<FanfareMessage> ClearFanfare = new(nameof(ClearFanfare));
        /// <summary>
        /// entity = what we tried to store
        /// </summary>
        public Signal1<Entity> FailedFindingStorageFor = new(nameof(FailedFindingStorageFor));
        public Signal0 PlayerShipClassChanged = new(nameof(PlayerShipClassChanged));
        public Signal1<IShipDevice> ShipDevicePhaseChanged = new(nameof(ShipDevicePhaseChanged));
        public Signal1<StoryGenDef> SetStoryGen = new(nameof(SetStoryGen), isOptional: false);
        public Signal1<StoryEventData> StoryEventStateChanged = new(nameof(StoryEventStateChanged));
        public Signal1<EggComp> EggHatched = new(nameof(EggHatched));
        /// <summary>
        /// bool = player has communicator
        /// </summary>
        public Signal3<ShipHailActionUI, IShipHailAction_N2P, bool> OnNPCShipHailPlayer = new(nameof(OnNPCShipHailPlayer));
        /// <summary>
        /// For adding story events that just have a log record.
        /// bool = show notification
        /// </summary>
        public Signal2<StoryEventData, bool> AddCustomStoryEvent = new(nameof(AddCustomStoryEvent), isOptional: false);
        /// <summary>
        /// Called when story schedule IsAllowed changed
        /// </summary>
        public Signal1<StorySchedule> StoryScheduleChanged = new(nameof(StoryScheduleChanged));
        /// <summary>
        /// int, int = points before, points after
        /// </summary>
        public Signal3<StorySchedule, int, int> StoryScheduleAllowanceChanged = new(nameof(StoryScheduleAllowanceChanged));
        /// <summary>
        /// uint = total ticks in proximity between these two beings
        /// </summary>
        public Signal3<BeingData, BeingData, uint> BeingProximityDurationChanged = new(nameof(BeingProximityDurationChanged));
        /// <summary>
        /// bool = true if beings just got into proximity, false if they moved apart
        /// </summary>
        public Signal3<BeingData, BeingData, bool> BeingProximityChanged = new(nameof(BeingProximityDurationChanged));
        /// <summary>
        /// Bool = started or ended
        /// </summary>
        public Signal2<BeingChat, bool> BeingChatChanged = new(nameof(BeingChatChanged));
        public Signal0 BeforeDisposeRuntime = new(nameof(BeforeDisposeRuntime));
        /// <summary>
        /// int = posIdx
        /// </summary>
        public Signal2<int, TractorBeamTargetComp> TractorBeamTargetBuilt = new(nameof(TractorBeamTargetBuilt));
        public Signal2<ObjData, Mind> BeingAshesForgotten = new(nameof(BeingAshesForgotten));
        public Signal3<Mind, Mind, BeingOpinionFlags> BeingBondCreated = new(nameof(BeingBondCreated));
        /// <summary>
        /// queue, needs redraw
        /// </summary>
        public Signal2<HyperjumpQueue, bool> HyperjumpQueueChanged = new(nameof(HyperjumpQueueChanged));
        public Signal1<string> ShowErrorMessage = new(nameof(ShowErrorMessage));
        /// <summary>
        /// bool = added or removed
        /// </summary>
        public Signal2<SurgicalOperation, bool> SurgicalOperationRegistered = new(nameof(SurgicalOperationRegistered), isOptional: true);

        /// <summary>
        /// Quest, StateBefore, StateAfter
        /// </summary>
        public Signal3<QuestData, QuestState, QuestState> QuestStateChanged = new(nameof(QuestStateChanged), isOptional: false);
        /// <summary>
        /// package, before, after
        /// </summary>
        public Signal3<QuestPackage, QuestPackageState, QuestPackageState> QuestPackageStateChanged = new(nameof(QuestPackageStateChanged));

        /// <summary>
        /// When a GLOBAL quests bool (flag) gets added or removed
        /// </summary>
        public Signal2<string, bool> QuestBoolsChanged = new(nameof(QuestBoolsChanged));
        /// <summary>
        /// step, from, to
        /// </summary>
        public Signal3<QuestStep, QuestStepState, QuestStepState> QuestStepStateChanged = new(nameof(QuestStepStateChanged));
        /// <summary>
        /// from, to
        /// </summary>
        public Signal2<Facing.Type, Facing.Type> ShipForwardDirChanged = new(nameof(ShipForwardDirChanged));
        public Signal1<SpaceObject> SpaceObjectNuked = new(nameof(SpaceObjectNuked));

        public Signal3<Upgrade, Tile, bool> TileUpgradeChanged = new(nameof(TileUpgradeChanged));
        /// <summary>
        /// A faction pair's Neutral/Allied/Enemy relation changed. Both factions are
        /// passed; null, null means "rebuild everything" (sent after a universe regen).
        /// Only DiplomacySys.SetRelationAndReport sends this.
        /// </summary>
        public Signal2<FactionDef, FactionDef> FactionRelationChanged = new(nameof(FactionRelationChanged));
        public Signal0 ElysiumEnabled = new(nameof(ElysiumEnabled));
        public Signal1<Dictionary<MatType, int>> MatCountChanged = new(nameof(MatCountChanged));
        public Signal2<Dictionary<Def, int>, Dictionary<Def, int>> ObjCountChanged = new(nameof(ObjCountChanged));
        public Signal1<Situation> SituationUpdated = new(nameof(SituationUpdated));
        public Signal1<BeingData> BeingNameChanged = new(nameof(BeingNameChanged));
        // Any equipment policy (global, species or individual) was edited.
        public Signal0 EquipmentPolicyChanged = new(nameof(EquipmentPolicyChanged), isOptional: true);
        public Signal1<Modifier> ModifierChanged = new(nameof(ModifierChanged));
        public Signal1<BattleStationComp> BattleStationChanged = new(nameof(BattleStationChanged));
        /// <summary>
        /// int = count threshold. If it is above 0, we won't display the mat in favorites unless we have less than that amount
        /// </summary>
        public Signal3<MatType, bool, int> SetFavMat = new(nameof(SetFavMat));

        public void ReleaseAll() {
            foreach (var field in GetType().GetFields(
                    BindingFlags.Public | BindingFlags.Instance)) {
                var sig = field.GetValue(this);
                if (sig is ISignal s) {
                    // D.Warn("Removing all listeners from game sig: {0}", s.Handle);
                    s.RemoveAllListeners();
                }
            }
        }
    }
}
