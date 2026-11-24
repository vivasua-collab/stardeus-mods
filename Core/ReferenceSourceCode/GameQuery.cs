using System;
using System.Collections.Generic;
using Game.Components;
using Game.Data;
using Game.Data.Space;
using KL.Signals;
using UnityEngine;
using Game.Systems.AI;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Systems.BodyParts;
using Game.Systems.AI.Combat;
using KL.Randomness;
using Game.Systems.Motion;
using KL.Grid;
using Game.Systems.Reg;
using Game.Systems.Mental;
using Game.Systems.Sleep;
using Game.Systems.Trade;
using Game.Data.Factions;
using Game.Systems.Areas;
using Game.Systems.Naming;
using Game.Systems.Space;
using Game.Systems.Crafting;
using Game.Systems.Turrets;
using Game.Systems.Ships;
using Game.Constants;
using System.Text;
using Game.Systems.Story;
using Game.Systems.Drama;
using Game.Visuals;
using Game.Systems.Rendering;
using Game.Systems.TileAnim;
using Game.Systems.Proxy;
using Game.Systems.Creatures.Surgeries;
using Game.Systems.Quests;
using KL.Collections;
using Game.Systems.UIUX;
using Game.Systems.Notify;
using Game.Systems.Policies;

namespace Game {
    public class GameQuery {
        /// <summary>
        /// int = level, string = title
        /// </summary>
        public Query2<int, string, bool> ClickToolMenuBtn = new(nameof(ClickToolMenuBtn));
        public Query2<CombatUnit, CombatUnit, bool> StartCombatBetween = new(nameof(StartCombatBetween));
        public Query2<CombatUnit, CombatUnit, bool> StartFriendlyFightBetween = new(nameof(StartFriendlyFightBetween));
        /// <summary>
        /// Takes an army, returns an organized army or parent army that we joined
        /// </summary>
        public Query1<Army, Army> StartRaid = new(nameof(StartRaid));
        /// <summary>
        /// out region distance, -1 = no path
        /// </summary>
        public Query1<RegPathReq, int> RegionDist = new(nameof(RegionDist));
        /// <summary>
        /// The first door on the cheapest way to ToPos that Permissions may not pass, where
        /// breaking a door costs extra (RegPathReqFlags.Breach). Out: its position, 0 = the way
        /// is open, -1 = no way even through doors
        /// </summary>
        public Query1<RegPathReq, int> RegionBreachDoor = new(nameof(RegionBreachDoor));

        /// <summary>
        /// reg id, posidx[], numfound
        /// </summary>
        public Query2<int, int[], int> RegionPositions = new(nameof(RegionPositions));
        /// <summary>
        /// pos idx, posidx[], numfound
        /// </summary>
        public Query2<int, int[], int> RegionPositionsAt = new(nameof(RegionPositionsAt));
        public Query1<int, int> RegionAt = new(nameof(RegionAt));
        public Query1<int, AreaType> RegionTypeAt = new(nameof(RegionTypeAt));
        /// <summary>
        /// Difference from FindNearby is that this will not return the first OK position, but will
        /// take all positions from all regions, then shuffle them and pick the first one that is ok
        /// pos, grounded, maxRegionDepth, check func(pos, bool isok), returns in pos
        /// </summary>
        public Query4<int, bool, int, Func<int, bool>, int> FindNearbyRandom = new(nameof(FindNearbyRandom));
        /// <summary>
        /// pos, grounded, maxRegionDepth, check func(pos, bool isok), returns in pos
        /// </summary>
        public Query4<int, bool, int, Func<int, bool>, int> FindNearby = new(nameof(FindNearby));
        /// <summary>
        /// pos, mover, max straight line distance, check func(pos, bool isok), returns pos.
        /// Region BFS that respects door permissions and searches the connected area around
        /// pos before crossing any door. Returns the straight line closest ok position within
        /// the nearest region levels that have one
        /// </summary>
        public Query4<int, MotionControl, int, Func<int, bool>, int> FindClosestNearby = new(nameof(FindClosestNearby));
        /// <summary>
        /// pos, grounded, depth, worldLayer, check func(tile, bool isok), returns Tile
        /// </summary>
        public Query5<int, bool, int, int, Func<Tile, bool>, Tile> FindTileNearby = new(nameof(FindTileNearby));
        /// <summary>
        /// agent, maxDepth, maxDifficulty, returns posIdx
        /// </summary>
        public Query3<AIAgentComp, int, float, int> RandomSafePosFrom = new(nameof(RandomSafePosFrom));
        /// <summary>
        /// pos from, agent, max depth, max difficulty, returns pos idx
        /// </summary>
        public Query4<int, AIAgentComp, int, float, int> RandomSafePosNearby = new(nameof(RandomSafePosNearby));
        /// <summary>
        /// request array, num requests, results array, bool success
        /// </summary>
        public Query3<RegPathReq[], int, int[], bool> RegPathLengths = new(nameof(RegPathLengths));
        /// <summary>
        /// hop counts from one position to many, results array (-1 = unreachable or not
        /// needed to pick the best target), bool success
        /// </summary>
        public Query2<RegDistsReq, int[], bool> RegionDists = new(nameof(RegionDists));
        public Query0<List<Army>> GetArmies = new(nameof(GetArmies));
        /// <summary>
        /// int is slot id h, ObjData[] is result, hashset<uint> is item id blacklist, HashSet<int> is storage id blacklist
        /// </summary>
        public Query5<AIAgentComp, int, ObjData[], HashSet<uint>, HashSet<int>, int> FindCompatibleEquipment = new(nameof(FindCompatibleEquipment));
        public Query3<AIAgentComp, InventoryItemDef, Color, ObjData> FindEquipmentOfColor = new(nameof(FindEquipmentOfColor));
        public Query2<GenBeingVars, Rng, BeingData> GenBeingFull = new(nameof(GenBeingFull));
        public Query2<BodyPartSlot, List<ObjData>, bool> InstallableBodyParts = new(nameof(InstallableBodyParts));
        public Query1<BodyPartSlot, ObjData> BestInstallableBodyPart = new(nameof(InstallableBodyParts));
        // int is need id h
        public Query2<BeingData, int, bool> CheckNeedActive = new(nameof(CheckNeedActive));
        // hashset<uint> is item id blacklist
        // hashset<int> is storage id blacklist
        public Query3<InventoryItemSlot, HashSet<uint>, HashSet<int>, ObjData> BestAvailableEquipment = new(nameof(BestAvailableEquipment));
        /// <summary>
        /// Resolves the Individual -> Species -> Global -> Default equipment policy chain
        /// for one being and one inventory slot type. Never returns null for a known
        /// slot type. See PoliciesSys.
        /// </summary>
        public Query2<BeingData, InventorySlotType, EquipmentSlotPolicy> EquipmentPolicyFor = new(nameof(EquipmentPolicyFor));
        /// <summary>
        /// Bumped whenever any equipment policy changes, so callers can cache resolved
        /// results and invalidate them with a single int compare.
        /// </summary>
        public Query0<int> EquipmentPolicyVersion = new(nameof(EquipmentPolicyVersion));
        public Query3<Rng, InventoryItemSlot, GenItemFlags, ObjData> GenInventoryItem = new(nameof(GenInventoryItem));
        public Query2<BeingData, Rng, bool> GenBeingPart = new(nameof(GenBeingPart));
        public Query2<GenBeingVars, Rng, Mind> GenBabyMind = new(nameof(GenBabyMind));
        public Query1<BeingData, BeingAvatar> GetBeingAvatar = new(nameof(GetBeingAvatar));
        public Query1<Mind, BeingAvatar> GetMindAvatar = new(nameof(GetMindAvatar));
        public Query1<BeingData, bool> ActivateBeingData = new(nameof(ActivateBeingData));
        public Query1<BeingData, bool> DeactivateBeingData = new(nameof(DeactivateBeingData));
        /// <summary>
        /// Registers a BeingData that has never been on the map (a stasis manifest sleeper),
        /// so it has an id, is saved, and can take part in relationships before it is woken.
        /// </summary>
        public Query1<BeingData, bool> RegisterDormantBeing = new(nameof(RegisterDormantBeing));
        public Query1<Def, int> ObjCountByDef = new(nameof(ObjCountByDef));
        public Query1<Def, SortedDictionary<long, int>> StockHistory = new(nameof(StockHistory));
        public Query1<Def, SortedDictionary<long, int>> DefProductionHistory = new(nameof(DefProductionHistory));
        public Query1<Def, SortedDictionary<long, int>> DefConsumptionHistory = new(nameof(DefConsumptionHistory));
        public Query2<Def, int, int> DefProductionRate = new(nameof(DefProductionRate));
        public Query2<Def, int, int> DefConsumptionRate = new(nameof(DefConsumptionRate));
        public Query2<List<ObjData>, Func<ObjData, bool>, int> FindObjs = new(nameof(FindObjs));
        public Query0<Dictionary<Def, int>> ObjCounts = new(nameof(ObjCounts));
        public Query2<Def, Rng, ObjData> GenObjFull = new(nameof(GenObjFull));
        public Query1<ObjData, bool> PersistObjData = new(nameof(PersistObjData));
        public Query1<ObjData, bool> ActivateObjData = new(nameof(ActivateObjData));
        public Query2<ObjData, ObjState, bool> DeactivateObjData = new(nameof(DeactivateObjData));
        public Query1<Vector2, bool> IsBlockedByTutorialMask = new(nameof(IsBlockedByTutorialMask));
        public Query0<bool> TryToolMenuSearch = new(nameof(TryToolMenuSearch));
        public Query0<bool> TryTradeSearch = new(nameof(TryTradeSearch));
        public Query0<bool> TryInventorySearch = new(nameof(TryInventorySearch));
        /// <summary>
        /// bool = show error tooltip
        /// </summary>
        public Query2<AIGoal, bool, bool> ForceGoal = new(nameof(ForceGoal));
        public Query1<uint, ObjData> FindObjData = new(nameof(FindObjData));
        public Query1<uint, ObjData> TryFindObjData = new(nameof(TryFindObjData));

        /// <summary>
        /// storage id, results array, search filter, returns count of found
        /// </summary>
        public Query4<int, ObjData[], string, ObjData.SortMode, int> FindStoredObjData = new(nameof(FindStoredObjData));
        public Query4<int, BeingData[], string, BeingData.SortMode, int> FindStoredBeingData = new(nameof(FindStoredBeingData));
        /// <summary>
        /// Any obj that is not equipped or destroyed
        /// </summary>
        public Query0<Dictionary<uint, ObjData>> GetAllObjs = new(nameof(GetAllObjs));
        /// <summary>
        /// results, accepts check func, returns count found
        /// </summary>
        public Query2<ObjData[], Func<ObjData, bool>, int> FindHaulableUnstoredObjs = new(nameof(FindHaulableUnstoredObjs));
        /// <summary>
        /// results, storage accepted group id hashes, returns count
        /// </summary>
        public Query2<List<Def>, int[], int> ListKnownStorableObjDefsForGroup = new(nameof(ListKnownStorableObjDefsForGroup));
        public Query1<int, CoverSlot> CoverSlotAt = new(nameof(CoverSlotAt));
        /// <summary>
        /// returns int with actually selected count. some beings may be inactive
        /// </summary>
        public Query1<List<Being>, int> SelectBeings = new(nameof(SelectBeings));
        public Query1<List<Being>, int> DeselectBeings = new(nameof(DeselectBeings));
        public Query0<bool> ClearSelectedBeings = new(nameof(ClearSelectedBeings));
        public Query0<int> SelectBeingCount = new(nameof(SelectBeingCount));
        public Query0<bool> IsAnythingSelected = new(nameof(IsAnythingSelected));
        /// <summary>
        /// true when the selection reached Consts.MaxSelectedEntities. Mass select
        /// loops must break on it. Warns the player when it first returns true.
        /// </summary>
        public Query0<bool> IsSelectionFull = new(nameof(IsSelectionFull));
        /// <summary>
        /// if bool is set to true, only one object must be selected, or it will return null
        /// </summary>
        public Query1<bool, Obj> GetSelectedObj = new(nameof(GetSelectedObj));
        /// <summary>
        /// if bool is set to true, only one tile must be selected, or it will return null
        /// </summary>
        public Query1<bool, Tile> GetSelectedParentTile = new(nameof(GetSelectedParentTile));
        /// <summary>
        /// if bool is set to true, only one being must be selected, or it will return null
        /// </summary>
        public Query1<bool, Being> GetSelectedBeing = new(nameof(GetSelectedBeing));
        public Query0<List<Tile>> GetSelectedTiles = new(nameof(GetSelectedTiles));
        public Query0<List<Obj>> GetSelectedObjs = new(nameof(GetSelectedObjs));
        public Query0<List<Being>> GetSelectedBeings = new(nameof(GetSelectedBeings));
        public Query1<Entity, bool> IsExclusivelySelected = new(nameof(IsExclusivelySelected));
        public Query1<Entity, bool> IsSelected = new(nameof(IsSelected));
        public Query1<Entity, bool> DeselectIfSelected = new(nameof(DeselectIfSelected));
        public Query0<bool> DeselectAny = new(nameof(DeselectAny));
        public Query0<bool> StartBuildACopy = new(nameof(StartBuildACopy));
        public Query0<bool> StartRelocate = new(nameof(StartRelocate));
        public Query0<bool> IsDisposableSelected = new(nameof(IsDisposableSelected));
        /// <summary>
        /// First bool = on next frame
        /// </summary>
        public Query1<bool, bool> TryDisposeSelected = new(nameof(TryDisposeSelected));
        public Query0<bool> TryCancelSelectedTasks = new(nameof(TryCancelSelectedTasks));
        public Query0<bool> HasSelectedTasks = new(nameof(HasSelectedTasks));
        public Query1<MotionComp, MotionControl> RegisterMovable = new(nameof(RegisterMovable));
        public Query1<MotionControl, bool> UnregisterMovable = new(nameof(UnregisterMovable));
        // bool = add / remove
        public Query2<RotatingTurret, bool, bool> RegisterTurret = new(nameof(RegisterTurret));
        // bool = add / remove
        public Query2<TileAnimation, bool, bool> RegisterTileAnim = new(nameof(RegisterTileAnim));
        public Query2<RemoteDrillComp, bool, bool> RegisterRemoteDrill = new(nameof(RegisterRemoteDrill));
        /// <summary>
        /// Returns true if the given resource deposit SpaceObject is currently
        /// being mined by the player's remote drill(s).
        /// </summary>
        public Query1<SpaceObject, bool> IsBeingDrilled = new(nameof(IsBeingDrilled));
        public Query0<byte[]> GetCoverMap = new(nameof(GetCoverMap));
        // changes whenever GetCoverMap does, for caches of what it lets through
        public Query0<int> GetCoverVersion = new(nameof(GetCoverVersion));
        public Query0<short[]> GetCoverTotalMap = new(nameof(GetCoverTotalMap));
        // bool = best possible, or closest
        public Query2<CombatUnit, bool, CoverSlot> FindCover = new(nameof(FindCover));
        // seeker, want position, max dist from want pos
        public Query3<CombatUnit, int, float, CoverSlot> FindCoverNear = new(nameof(FindCoverNear));
        public Query2<int, List<AIGoal>, int> AIGoalsAtPos = new(nameof(AIGoalsAtPos));
        public Query2<int, List<AIGoal>, bool> AIGoalsByType = new(nameof(AIGoalsByType));
        public Query2<PosRect, List<AIGoal>, int> AIGoalsInArea = new(nameof(AIGoalsInArea));
        public Query1<int, bool> HasAIGoalAtPos = new(nameof(HasAIGoalAtPos));
        public Query1<uint, BeingData> FindBeingData = new(nameof(FindBeingData));
        public Query0<Dictionary<uint, BeingData>> AllBeingData = new(nameof(AllBeingData));
        public Query1<uint, List<LogEntry>> GetBeingLogEntries = new(nameof(GetBeingLogEntries));
        public Query1<int, List<LogEntry>> GetTileLogEntries = new(nameof(GetTileLogEntries));
        public Query1<uint, List<LogEntry>> GetObjLogEntries = new(nameof(GetObjLogEntries));
        public Query1<List<LogEntry>, int> GetAllLogEntries = new(nameof(GetAllLogEntries));
        /// <summary>
        /// int = species id h, returns count, bool = alive only
        /// </summary>
        public Query2<int, bool, int> CountFriendlyBeingsBySpecies = new(nameof(CountFriendlyBeingsBySpecies));
        public Query0<List<BeingGroup>> GetBeingGroups = new(nameof(GetBeingGroups));
        public Query1<BeingGroup, bool> ToggleBeingGroupUI = new(nameof(ToggleBeingGroupUI));
        /// <summary>
        /// list is result, returns count loaded
        /// </summary>
        public Query2<BeingGroup, List<BeingData>, int> LoadBeingsForGroup = new(nameof(LoadBeingsForGroup));
        public Query2<BeingData, BedSlot, bool> AssignBedSlot = new(nameof(AssignBedSlot));
        /// <summary>
        /// bool = auto assign if not found
        /// </summary>
        public Query2<BeingData, bool, BedComp> FindBed = new(nameof(FindBed));
        public Query1<BeingData, bool> HasSafelyAssignableBedFor = new(nameof(HasSafelyAssignableBedFor));
        /// <summary>
        /// Won't auto assign, only temporary sleep spots like sofas will be returned.
        /// Bool = temp bed (otherwise any other bed, assigned prioritized)
        /// </summary>
        public Query2<BeingData, bool, BedComp> FindTemporaryBed = new(nameof(FindTemporaryBed));
        public Query2<BeingData, BotDockComp, bool> AssignBotDock = new(nameof(AssignBotDock));
        public Query1<BeingData, BotDockComp> FindBotDock = new(nameof(FindBotDock));
        /// <summary>
        /// bool = assignable beds only
        /// </summary>
        public Query3<BedComp[], BeingData, bool, int> FindBeds = new(nameof(FindBeds));
        /// <summary>
        /// bool = assignable bed users only
        /// </summary>
        public Query3<BeingData[], BedSlot, bool, int> FindBedUsers = new(nameof(FindBedUsers));
        /// <summary>
        /// bool = assignable docks only
        /// </summary>
        public Query2<BotDockComp[], bool, int> FindDocks = new(nameof(FindDocks));
        /// <summary>
        /// bool = assignable only
        /// </summary>
        public Query2<BeingData[], bool, int> FindDockables = new(nameof(FindDockables));
        public Query1<uint, BCallback> GetDockableStateB = new(nameof(GetDockableStateB));
        public Query1<uint, BCallback> GetBedUserStateB = new(nameof(GetBedUserStateB));
        public Query1<int, float> LightAtPos = new(nameof(LightAtPos));
        public Query1<TradeableItem, double> OnlinePriceFor = new(nameof(OnlinePriceFor));
        /// <summary>
        /// Treat this list as nullable and read only!
        /// </summary>
        public Query1<int, List<ObjStorageComp>> ObjStoragesForGroup = new(nameof(ObjStoragesForGroup));
        // returns number of storages found
        public Query2<BeingData, List<BeingStorageComp>, int> BeingStoragesFor = new(nameof(BeingStoragesFor));
        public Query0<HashSet<BeingStorageComp>> AllBeingStorages = new(nameof(AllBeingStorages));
        public Query1<int, List<Def>> ObjStorageDefsForGroup = new(nameof(ObjStorageDefsForGroup));
        public Query1<int, StorableTraits> StorableTraitsForGroup = new(nameof(StorableTraitsForGroup));
        public Query2<ObjData, ObjStorageComp, bool> StoreObj = new(nameof(StoreObj));
        public Query2<BeingData, BeingStorageComp, bool> StoreBeing = new(nameof(StoreBeing));
        public Query1<ObjData, bool> DestroyStoredObj = new(nameof(DestroyStoredObj));
        public Query2<ObjData, ObjStorageComp, bool> UnstoreObj = new(nameof(UnstoreObj));
        public Query2<BeingData, BeingStorageComp, bool> UnstoreBeing = new(nameof(UnstoreBeing));
        public Query0<IAIPlanner> GetAIPlanner = new(nameof(GetAIPlanner));
        /// <summary>
        /// goal id, search through agent personal goals
        /// </summary>
        public Query2<uint, bool, AIGoal> FindAIGoal = new(nameof(FindAIGoal));
        public Query2<Rng, FactionDef, TradeUI> CreateRandomTrade = new(nameof(CreateRandomTrade));
        public Query0<TradeUI> CreateOnlineTrade = new(nameof(CreateOnlineTrade));
        public Query1<Trader, TradeUI> StartTrade = new(nameof(StartTrade));
        /// <summary>
        /// Int = local pos idx
        /// </summary>
        public Query2<Trader, int, TradeUI> StartTradeLocal = new(nameof(StartTradeLocal));
        public Query1<uint, Trader> FindTrader = new(nameof(FindTrader));
        public Query0<Dictionary<Trader, TradeUI>> GetOngoingTrades = new(nameof(GetOngoingTrades));
        public Query1<TraderRequest, Trader> CreateTrader = new(nameof(CreateTrader));
        /// <summary>
        /// float is the new price change percent
        /// </summary>
        public Query2<Trader, TradeableItem, float> GetScarcityAdjustment = new(nameof(GetScarcityAdjustment));
        public Query1<SpaceObject, MatAvailability> GetMatAvailabilityFor = new(nameof(GetMatAvailabilityFor));
        public Query0<bool> CanAddSuggestionNow = new(nameof(CanAddSuggestionNow));
        public Query1<NameRequest, NameResult> GenerateName = new(nameof(GenerateName));
        public Query0<SpaceMap> GetSpaceMap = new(nameof(GetSpaceMap));
        /// <summary>
        /// HashSet contains Hashes.S of full names that are used
        /// </summary>
        public Query2<NameRequest, HashSet<int>, NameResult> GenerateNameExcept = new(nameof(GenerateNameExcept));

        /// <summary>
        /// int = capsule entity id
        /// </summary>
        public Query1<int, OnlineOrder> GetOnlineOrderForCapsule  = new(nameof(GetOnlineOrderForCapsule));
        /// <summary>
        /// bool: true = delivered, false = pending
        /// </summary>
        public Query1<bool, List<OnlineOrder>> GetOnlineOrders = new(nameof(GetOnlineOrders));
        public Query1<MultiCrafterComp, List<CraftingOrder>> GetCraftingOrdersFor = new(nameof(GetCraftingOrdersFor));
        public Query2<MultiCrafterComp, CraftingDemand, CraftingOrder> CreateCraftingOrder = new(nameof(CreateCraftingOrder));
        public Query1<CraftingOrder, bool> RemoveCraftingOrder = new(nameof(RemoveCraftingOrder));
        /// <summary>
        /// Source, Target, copied
        /// </summary>
        public Query2<MultiCrafterComp, MultiCrafterComp, bool> CopyCrafterOrders = new(nameof(CopyCrafterOrders));
        public Query0<bool> CameraHideLink = new(nameof(CameraHideLink));
        /// <summary>
        /// If a link is showing, setting it to sticky will not close it when clicking esc / rmb.
        /// Useful for tutorials where we want to not block RMB but still show a link.
        /// first bool is sticky/not sticking, response is true if there was a link displayed
        /// </summary>
        public Query1<bool, bool> CameraSetLinkSticky = new(nameof(CameraSetLinkSticky));
        public Query0<bool> HasWorkingNanobots = new(nameof(HasWorkingNanobots));

        /// <summary>
        /// Loads the NPC ships that are in the given space sector
        /// Returns ship count.
        /// </summary>
        public Query1<SpaceSector, List<NPCShip>> NPCShipsAt = new(nameof(NPCShipsAt));
        public Query1<SpaceSector, List<ShipHyperTrail>> ShipHyperTrailsAt = new(nameof(ShipHyperTrailsAt));
        /// <summary>
        /// Spawns ship at given sector
        /// </summary>
        public Query1<SpaceObject, NPCShip> SpawnShipAt = new(nameof(SpawnShipAt));
        /// <summary>
        /// Parent, template, return spawned ship
        /// </summary>
        public Query2<SpaceObject, NPCShip, NPCShip> SpawnShipTplAt = new(nameof(SpawnShipTplAt));
        public Query1<SpaceObject, bool> HasNPCShipsAt = new(nameof(HasNPCShipsAt));
        public Query1<uint, NPCShip> NPCShipById = new(nameof(NPCShipById));
        public Query2<NPCShip, SpaceSector, bool> NPCShipJumpTo = new(nameof(NPCShipJumpTo));
        /// <summary>
        /// returns the newly set behavior (no need to set it)
        /// bool = new behavior can be the same as the previous one
        /// </summary>
        public Query3<NPCShip, NPCShipBehaviorScope, bool, NPCShipBehavior> ChangeNPCShipBehavior = new(nameof(ChangeNPCShipBehavior));
        public Query2<FactionDef, FactionDef, float> FactionAttitude = new(nameof(FactionAttitude));
        /// <summary>
        /// from => towards, change amount, returns current attitude (after change)
        /// </summary>
        public Query3<FactionDef, FactionDef, float, float> ChangeFactionAttitude = new(nameof(ChangeFactionAttitude));
        public Query1<SpaceObject, ShipHyperTrail> FindHyperTrail = new(nameof(FindHyperTrail));
        public Query1<NPCShip, ShipView> SpawnShipView = new(nameof(SpawnShipView));
        public Query3<NPCShip, Facing.Type, Vector2, ShipView> SpawnShipViewAtPos = new(nameof(SpawnShipViewAtPos));
        public Query1<NPCShip, bool> DespawnShipView = new(nameof(DespawnShipView));
        /// <summary>
        /// bool = deteriorate relationship with player
        /// </summary>
        public Query2<NPCShip, bool, bool> StartShipCombatWith = new(nameof(StartShipCombatWith));
        public Query2<NPCShip, NPCShip, bool> StartNPCShipCombatBetween = new(nameof(StartNPCShipCombatBetween));
        /// <summary>
        /// Makes an NPC ship fight on the player's side in an ongoing ship combat
        /// </summary>
        public Query1<NPCShip, bool> JoinShipCombatAsAlly = new(nameof(JoinShipCombatAsAlly));
        /// <summary>
        /// Makes an NPC ship fight against the player, subject to the enemy cap. Starts
        /// a combat when there is none yet.
        /// </summary>
        public Query1<NPCShip, bool> JoinShipCombatAgainstPlayer = new(nameof(JoinShipCombatAgainstPlayer));
        /// <summary>
        /// Live enemies plus every ship flying over to attack the player, each counted
        /// once. The returned list is reused - read it before asking again.
        /// </summary>
        public Query0<List<NPCShip>> GetShipsAgainstPlayer = new(nameof(GetShipsAgainstPlayer));
        /// <summary>
        /// Enemy cap minus GetShipsAgainstPlayer count. Can be negative. Re-ask after
        /// every dispatch rather than tracking spent slots.
        /// </summary>
        public Query0<int> GetShipCombatEnemySlots = new(nameof(GetShipCombatEnemySlots));
        /// <summary>
        /// Whether the player's current (or most recently ended) combat was started by
        /// a player action, as opposed to an NPC's own decision to attack. Gates whether
        /// the enemy side may reinforce at all - random NPC aggression must never
        /// escalate a fight the player didn't pick.
        /// </summary>
        public Query0<bool> IsShipCombatPlayerInitiated = new(nameof(IsShipCombatPlayerInitiated));
        /// <summary>
        /// The NPC vs NPC battle a ship is fighting in, null when it is in none
        /// </summary>
        public Query1<NPCShip, NPCShipBattle> GetNPCShipBattleFor = new(nameof(GetNPCShipBattleFor));
        /// <summary>
        /// Whether the player can join this battle helping that participant
        /// </summary>
        public Query2<NPCShipBattle, NPCShip, bool> CanJoinNPCShipBattle = new(nameof(CanJoinNPCShipBattle));
        /// <summary>
        /// Turns an NPC vs NPC battle into a real combat, with the player fighting
        /// alongside the given participant
        /// </summary>
        public Query2<NPCShipBattle, NPCShip, bool> JoinNPCShipBattle = new(nameof(JoinNPCShipBattle));
        /// <summary>
        /// Reconciles saved ship combat state and spawns everything it needs.
        /// Asked by the ship load sequencer once ships are restored and validated.
        /// </summary>
        public Query0<bool> RestoreShipCombatData = new(nameof(RestoreShipCombatData));
        public Query1<ShipWeaponShot, bool> ShootShipWeapon = new(nameof(ShootShipWeapon));
        /// <summary>
        /// bool = add or remove
        /// </summary>
        public Query2<ShipWeapon, bool, bool> RegisterShipWeapon = new(nameof(RegisterShipWeapon));
        public Query2<ShipShield, bool, bool> RegisterShipShield = new(nameof(RegisterShipShield));
        public Query2<ShipCapacitor, bool, bool> RegisterShipCapacitor = new(nameof(RegisterShipCapacitor));
        public Query2<ShipEngine, bool, bool> RegisterShipEngine = new(nameof(RegisterShipEngine));
        public Query1<float, bool> ShakePlayerShipShield = new(nameof(ShakePlayerShipShield));
        public Query0<List<NPCShip>> GetEnemyShips = new(nameof(GetEnemyShips));
        /// <summary>
        /// Ships fighting on the player's side, including the player's own ship
        /// </summary>
        public Query0<List<NPCShip>> GetAllyShips = new(nameof(GetAllyShips));
        public Query2<NPCShip, uint, IShipDevice> FindShipDevice = new(nameof(FindShipDevice));
        /// <summary>
        /// Flags = wanted ship flags after combat ends
        /// </summary>
        public Query2<NPCShip, ShipStateFlags, bool> EndShipCombatWith = new(nameof(StartShipCombatWith));
        /// <summary>
        /// bool = isUp, vector2 = center, float = radius
        /// </summary>
        public Query0<(bool, Vector2, float)> GetPlayerShield = new(nameof(GetPlayerShield));
        public Query0<bool> IsPlayerShieldAnimating = new(nameof(IsPlayerShieldAnimating));
        /// <summary>
        /// True while a magnetic storm rages and a shield with an EMP Shield upgrade is up
        /// </summary>
        public Query0<bool> IsPlayerShieldedFromStorm = new(nameof(IsPlayerShieldedFromStorm));
        public Query0<int> UpdatePlayerShipEnergy = new(nameof(UpdatePlayerShipEnergy));
        /// <summary>
        /// Player's ship (or NPC), Template, returns spawned ship
        /// </summary>
        public Query2<NPCShip, NPCShip, NPCShip> SpawnOpponentShipFor = new(nameof(SpawnOpponentShipFor));
        /// <summary>
        /// List = result cache (mat type id hashes), returns count of items
        /// </summary>
        public Query1<List<int>, int> GetMaterialDeficits = new(nameof(GetMaterialDeficits));
        /// <summary>
        /// suggestion id, bool: check limits
        /// </summary>
        public Query2<string, bool, bool> TryAddSuggestion = new(nameof(TryAddSuggestion));
        /// <summary>
        /// Goal, IncludeAgentName, tooltipSB
        /// </summary>
        public Query3<AIGoal, bool, StringBuilder, bool> LoadGoalTooltip = new(nameof(LoadGoalTooltip));
        /// <summary>
        /// Int = job type id hash
        /// </summary>
        public Query1<int, bool> IsNanobotsJobEnabled = new(nameof(IsNanobotsJobEnabled));
        /// <summary>
        /// Int = job type id hash, bool is enabled/disabled
        /// </summary>
        public Query2<int, bool, bool> ToggleNanobotsJob = new(nameof(ToggleNanobotsJob));
        public Query0<List<ShipClassDef>> GetUnlockedShipClasses = new(nameof(GetUnlockedShipClasses));
        /// <summary>
        /// If string is null, it will be a random event
        /// </summary>
        public Query1<string, StoryEventData> TriggerStoryEvent = new(nameof(TriggerStoryEvent));
        public Query1<StoryEventData, bool> StartStoryEvent = new(nameof(StartStoryEvent));
        public Query0<StoryGenDef> GetStoryGen = new(nameof(GetStoryGen));
        public Query0<StoryContext> GetStoryContext = new(nameof(GetStoryContext));
        public Query1<StoryEventHook, StoryEventData> AddStoryEventHook = new(nameof(AddStoryEventHook));
        public Query1<StoryEventOutcome, IStoryEventHandler> StoryEventHandlerFor = new(nameof(StoryEventHandlerFor));
        public Query0<float> ColonyCombatStrength = new(nameof(ColonyCombatStrength));
        /// <summary>
        /// Finds a registered mind by id.
        /// </summary>
        public Query1<uint, Mind> FindMind = new(nameof(FindMind));
        /// <summary>
        /// Registers the mind once it gets created for the first time and gets
        /// an ID assigned. Don't register blank / placeholder minds.
        /// </summary>
        public Query1<Mind, bool> RegisterMind = new(nameof(RegisterMind));
        public Query2<Mind, Mind, BeingOpinion> GetOpinionAbout = new(nameof(GetOpinionAbout));
        // Roll the innate attraction / affinity of an opinion (owner, target) the way
        // GetOpinionAbout does when it creates one, without creating it
        public Query3<BeingData, BeingData, Rng, float> RollSexualAttraction = new(nameof(RollSexualAttraction));
        public Query3<Mind, Mind, Rng, float> RollSocialAffinity = new(nameof(RollSocialAffinity));
        /// <summary>
        /// int = region id
        /// </summary>
        public Query1<int, BeingCluster> GetBeingCluster = new(nameof(GetBeingCluster));
        public Query0<List<BeingCluster>> GetSociableClusters = new(nameof(GetSociableClusters));
        /// <summary>
        /// All clusters except expedition
        /// </summary>
        public Query0<Dictionary<int, BeingCluster>> GetBeingClusters = new(nameof(GetBeingClusters));
        public Query0<Dictionary<Expedition, BeingCluster>> GetExpeditionClusters = new(nameof(GetBeingClusters));
        /// <summary>
        /// Tells DramaSys to start a given social event, regardless of the timers.
        /// Mostly for tests.
        /// </summary>
        public Query1<string, StoryEventData> TryForceSocialEvent = new(nameof(TryForceSocialEvent));
        public Query1<SocialInteraction, bool> RegisterSocialInteraction = new(nameof(RegisterSocialInteraction));
        /// <summary>
        /// bool = add or remove
        /// </summary>
        public Query2<SocialOpportunity, bool, bool> ChangeSocialOpportunity = new(nameof(ChangeSocialOpportunity));
        /// <summary>
        /// int = SocialOpportunityIdH
        /// </summary>
        public Query2<BeingData, int, SocialOpportunity> FindSocialOpportunity = new(nameof(FindSocialOpportunity));
        public Query0<List<SocialOpportunity>> GetSocialOpportunities = new(nameof(GetSocialOpportunities));
        public Query0<List<SocialInteraction>> GetSocialInteractions = new(nameof(GetSocialInteractions));
        public Query0<Dictionary<int, BeingChat>> GetBeingChats = new(nameof(GetBeingChats));
        /// <summary>
        /// A chat between two beings that the caller ends with EndForcedChat, even
        /// when they could not chat on their own. string = SocialContext.
        /// </summary>
        public Query3<BeingData, BeingData, string, bool> StartForcedChat = new(nameof(StartForcedChat));
        public Query2<BeingData, BeingData, bool> EndForcedChat = new(nameof(EndForcedChat));
        public Query0<Dictionary<int, (BeingData, BeingData)>> GetBeingProximity = new(nameof(GetBeingProximity));
        public Query2<RenderableMesh, bool, bool> RenderMesh = new(nameof(RenderMesh));
        public Query1<List<BeingData>, bool> GenerateRelationshipsFor = new(nameof(GenerateRelationshipsFor));
        public Query0<DramaMemory> GetDramaMemory = new(nameof(GetDramaMemory));
        public Query0<float> GetBeingNameFontSize = new(nameof(GetBeingNameFontSize));
        /// <summary>
        /// atlas hash, shader, initialCapacity, sortOrder, isSorted
        /// </summary>
        public Query5<int, string, int, int, bool, GPUInfoRenderer> GetInfoRenderer = new(nameof(GetInfoRenderer));
        public Query0<GPUInfoBarRenderer> GetInfoBarRenderer = new(nameof(GetInfoBarRenderer));
        /// <summary>
        /// areaId, result list, returns int count of tiles
        /// </summary>
        public Query2<int, List<Tile>, int> GetDevicesInArea = new(nameof(GetDevicesInArea));
        /// <summary>
        /// int = areaId
        /// </summary>
        public Query1<int, (bool hasIndustrial, bool hasNoisy)> HasIndustrialDevicesInArea = new(nameof(HasIndustrialDevicesInArea));
        /// <summary>
        /// int = areaId, sleeper, int2 = max (0 = no max), res = num beds
        /// </summary>
        public Query3<int, BeingData, int, int> NumBedsInArea = new(nameof(NumBedsInArea));
        /// <summary>
        /// int = areaId, max (0 = no max), res = num admirable
        /// </summary>
        public Query2<int, int, int> NumAdmirableInArea = new(nameof(NumAdmirableInArea));
        /// <summary>
        /// int = areaId, res = num windows (window walls or glass floors) touching the room
        /// </summary>
        public Query1<int, int> NumWindowsInArea = new(nameof(NumWindowsInArea));
        public Query1<EntityProxy, bool> AddEntityProxy = new(nameof(AddEntityProxy));
        public Query1<EntityProxy, bool> RemoveEntityProxy = new(nameof(RemoveEntityProxy));
        /// <summary>
        /// rect from, rect to, results, returns count found
        /// </summary>
        public Query3<Vector2, Vector2, List<EntityProxy>, int> GetEntityProxiesInRect = new(nameof(GetEntityProxiesInRect));
        public Query2<int, List<EntityProxy>, int> GetEntityProxiesAt = new(nameof(GetEntityProxiesAt));

        public Query1<ISpaceEntity, HyperjumpQueue.OpResult> AddToHyperjumpQueue = new(nameof(AddToHyperjumpQueue));
        public Query1<ISpaceEntity, HyperjumpQueue.OpResult> SetWarpTarget = new(nameof(SetWarpTarget));
        public Query1<SpaceObject, HyperjumpQueue.OpResult> JumpToReturnBeacon = new(nameof(JumpToReturnBeacon));
        public Query0<ISpaceEntity> GetWarpTarget = new(nameof(GetWarpTarget));
        public Query0<HyperjumpProgress> GetHyperjumpProgress = new(nameof(GetHyperjumpProgress));
        public Query0<ISpaceEntity> GetNextHyperjumpTarget = new(nameof(GetNextHyperjumpTarget));
        public Query1<ISpaceEntity, HyperjumpQueue.OpResult> RemoveFromHyperjumpQueue = new(nameof(RemoveFromHyperjumpQueue));
        public Query0<HyperjumpQueue> GetHyperjumpQueue = new(nameof(GetHyperjumpQueue));
        public Query1<ISpaceEntity, LinkedList<ISpaceEntity>> GetHyperjumpPathPreviewFor = new(nameof(GetHyperjumpPathPreviewFor));
        /// <summary>
        /// bool = add/remove, result = added or removed
        /// </summary>
        public Query2<SurgicalOperation, bool, bool> RegisterSurgicalOperation = new(nameof(RegisterSurgicalOperation));
        /// <summary>
        /// bool = include mass operations that affect this body part
        /// </summary>
        public Query2<BodyPartSlot, bool, SurgicalOperation> FindSurgicalOperation = new(nameof(FindSurgicalOperation));
        /// <summary>
        /// SurgicalOperation[] is cache array,
        /// returns count found
        /// </summary>
        public Query2<Body, SurgicalOperation[], int> GetSurgicalOperationsFor = new(nameof(GetSurgicalOperationsFor));
        public Query1<BeingData, bool> ClearSurgicalOpsFor = new(nameof(ClearSurgicalOpsFor));
        /// <summary>
        /// Mass surgeries will be included even if slot is not null!
        /// </summary>
        public Query2<BeingData, BodyPartSlot, bool> HasPendingSurgeries = new(nameof(HasPendingSurgeries));
        public Query2<BeingData, SurgicalOpType, bool> CanEnqueueMassSurgicalOp = new(nameof(CanEnqueueMassSurgicalOp));
        public Query0<List<SurgicalOperation>> GetAllSurgeries = new(nameof(GetAllSurgeries));
        public Query1<int, SurgicalOperation> GetSurgicalOp = new(nameof(GetSurgicalOp));
        /// <summary>
        /// Gets the surgical operation that is "InProgress", or null
        /// </summary>
        public Query1<BeingData, SurgicalOperation> GetOngoingSurgicalOp = new(nameof(GetSurgicalOp));
        public Query1<BeingData, SurgicalOperation> TryNextSurgicalProcedure = new(nameof(TryNextSurgicalProcedure));
        /// <summary>
        /// Bool = activate
        /// </summary>
        public Query2<QuestData, bool, Quest> AddQuest = new(nameof(AddQuest));
        /// <summary>
        /// For accepting NPC quests that haven't been added before
        /// </summary>
        public Query1<Quest, bool> AcceptQuest = new(nameof(AcceptQuest));
        /// <summary>
        /// bool = add or remove, returns added or removed quest instance
        /// </summary>
        public Query2<QuestData, bool, Quest> RegisterAvailableQuest = new(nameof(RegisterAvailableQuest));
        /// <summary>
        /// bool = track, returns true if track state changed
        /// </summary>
        public Query2<Quest, bool, bool> TrackQuest = new(nameof(TrackQuest));
        public Query1<Quest, bool> IsTrackedQuest = new(nameof(IsTrackedQuest));
        public Query1<IQuestProvider, FrugalArray<QuestStepTarget>> GetQuestStepTargets = new(nameof(GetQuestStepTargets));
        public Query2<QuestStepTarget, bool, bool> RegisterQuestStepTarget = new(nameof(RegisterQuestStepTarget));
        public Query1<QuestPackage, bool> AddQuestPackage = new(nameof(AddQuestPackage));
        public Query1<uint, QuestPackage> FindQuestPackage = new(nameof(FindQuestPackage));
        /// <summary>
        /// list = result, int = count
        /// </summary>
        public Query2<QuestData, List<QuestPackage>, int> GetQuestPackages = new(nameof(GetQuestPackages));
        public Query1<uint, bool> DestroyQuestPackage = new(nameof(DestroyQuestPackage));
        /// <summary>
        /// int = packId, int = posidx
        /// </summary>
        public Query2<uint, int, Entity> SpawnQuestPackage = new(nameof(SpawnQuestPackage));
        public Query1<QuestPackageComp, bool> OpenQuestPackage = new(nameof(OpenQuestPackage));
        public Query0<List<Quest>> GetActiveQuests = new(nameof(GetActiveQuests));
        public Query0<List<Quest>> GetAllPlayerQuests = new(nameof(GetAllPlayerQuests));
        public Query0<Quest> GetTrackedQuest = new(nameof(GetTrackedQuest));
        public Query1<IQuestProvider, bool> IsActiveQuestProvider = new(nameof(IsActiveQuestProvider));
        /// <summary>
        /// False while the colonist (mind id) has an offered or open quest, or already
        /// accepted this OncePerGiver quest
        /// </summary>
        public Query2<uint, QuestDef, bool> CanOfferColonistQuest = new(nameof(CanOfferColonistQuest));
        /// <summary>
        /// True while the ship is the NPCShip variable of an ACTIVE quest (kill / interact
        /// target). Such ships hyperjump much less so the player can actually catch them.
        /// </summary>
        public Query1<NPCShip, bool> IsActiveQuestTargetShip = new(nameof(IsActiveQuestTargetShip));
        /// <summary>
        /// True while any ACTIVE quest requires the given mind id - either as its stored
        /// delivery passenger or as a registered step target's required crew member. Used
        /// to keep quest-locked guests out of "send beings" offers.
        /// </summary>
        public Query1<uint, bool> IsMindRequiredByQuest = new(nameof(IsMindRequiredByQuest));
        /// <summary>
        /// bool = create new quest if one did not exist
        /// bool2 = make quest giver if it's an NPC ship
        /// </summary>
        public Query3<IQuestProvider, bool, bool, Quest> GetQuestFor = new(nameof(GetQuestFor));
        public Query3<IQuestProvider, QuestDef, Rng, Quest> ForceQuestFor = new(nameof(ForceQuestFor));
        /// <summary>
        /// if NPCShip is null, it will be a global delay
        /// </summary>
        public Query1<NPCShip, bool> DelayNextNPCShipQuest = new(nameof(DelayNextNPCShipQuest));
        /// <summary>
        /// Used for ship reaction only
        /// </summary>
        public Query1<NPCShip, bool> WantsToOfferQuestToPlayer = new(nameof(WantsToOfferQuestToPlayer));

        /// <summary>
        /// int = min MatRarity, float = min item price, double = value
        /// </summary>
        public Query4<int, float, double, Rng, Mat> RandomMatWithValue = new(nameof(RandomMatWithValue));
        public Query1<QuestData, bool> UpdateQuestSeen = new(nameof(UpdateQuestSeen));
        /// <summary>
        /// Read-only weighted pick of a text-variant index for the given QuestDef.
        /// Less-recently-seen variants are slightly more likely (gentle decay weighting).
        /// Returns -1 if the def has no TitleKeys.
        /// </summary>
        public Query2<QuestDef, Rng, int> GetQuestVariantIndex = new(nameof(GetQuestVariantIndex));
        /// <summary>
        /// Records that variant index was seen for the given QuestDef, incrementing its
        /// decayed seen-count and stamping the current tick.
        /// </summary>
        public Query2<QuestDef, int, bool> MarkQuestVariantSeen = new(nameof(MarkQuestVariantSeen));
        /// <summary>
        /// Sets global quest bool (flag), bool value = add or remove, returns true if changed
        /// </summary>
        public Query2<string, bool, bool> SetQuestBool = new(nameof(SetQuestBool));
        /// <summary>
        /// Returns true if the global quest bool is set
        /// </summary>
        public Query1<string, bool> GetQuestBool = new(nameof(GetQuestBool));
        /// <summary>
        /// target, timer (ticks), rigged
        /// </summary>
        public Query2<SpaceObject, int, bool> RigSpaceObjectForDestruction = new(nameof(RigSpaceObjectForDestruction));
        public Query0<ComponentData> GetSharedQuestData = new(nameof(GetSharedQuestData));
        public Query2<IInteractiveQuestStep, bool, bool> RegisterGlobalInteractiveQuestStep = new(nameof(RegisterGlobalInteractiveQuestStep));
        public Query0<List<IInteractiveQuestStep>> GetGlobalInteractiveQuestSteps = new(nameof(RegisterGlobalInteractiveQuestStep));
        public Query0<bool> ResetAllQuests = new(nameof(ResetAllQuests));
        public Query0<ShipFlightCondition> GetShipFlightCondition = new(nameof(GetShipFlightCondition));
        public Query1<NotificationData, NotificationHandle> AddNotification = new(nameof(AddNotification));
        /// <summary>
        /// bool = immediately
        /// </summary>
        public Query1<NotificationHandle, bool> RemoveNotification = new(nameof(RemoveNotification));
        public Query1<NotificationHandle, NotificationData> GetNotification = new(nameof(GetNotification));

        /// <summary>
        /// Checks if this posidx has an adjacent region that is reachable
        /// </summary>
        public Query1<int, bool> IsRegionReachable = new(nameof(IsRegionReachable));
        public Query0<Dictionary<Craftable, List<CraftingOrder>>> GetAllCraftingOrders = new(nameof(GetAllCraftingOrders));

        public Query0<bool> OrderRemovingSelectedTiles = new(nameof(OrderRemovingSelectedTiles));
        /// <summary>
        /// if count threshold is above 0, we won't display the mat in favorites unless we have less than that amount
        /// </summary>
        public Query1<MatType, (bool isFav, int countThreshold)> IsFavMat = new(nameof(IsFavMat));
    }
}
