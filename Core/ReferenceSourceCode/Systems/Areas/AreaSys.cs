#if UNITY_EDITOR
// #define IMMEDIATE_FF
// #define VERBOSE_AREAS
#define DEBUG_OUTSIDE
#endif
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Systems.UIUX;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Game.Systems.Areas;
using Game.Components;
using Game.Systems.Path;
using Game.Data.Space;
using Game.Data.Economy;
using KL.Clock;
using Game.Input;
using Game.Achievements;
using Game.Systems.Ships;
using KL.Pool;
using Game.Systems.Atmo;
using Game.Systems.Notify;
using Game.Systems.Sleep;
using Game.Systems.Creatures;

namespace Game.Systems {
    public sealed class AreaSys : GameSystem, IOverlayProvider {
        public const string SysId = "AreasSys";
        public override string Id => SysId;
        public DoorConnectionCheck DoorCheck;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new AreaSys());
        }
        private ComputeShader areaVisualization;
        // Can't reuse areaIdBuffer, it needs to be always up to date because lights
        // system is using it
        private ComputeBuffer areaIdBuffer;
        private ComputeBuffer areaTypeBuffer;
        private ComputeBuffer islandIdBuffer;
        private ComputeBuffer cornerGapBuffer;
        private FloorKindBuffer floorKinds;
        // How much light each wall layer tile lets through (windows, open doors)
        public LightPassBuffer LightPass { get; private set; }
        // Which rooms are plain rectangles, for the light's wall walk (AreaShapeBuffer.cs)
        public AreaShapeBuffer Shape { get; private set; }
        // How worn each floor, wall and object tile looks
        public TileWearBuffer Wear { get; private set; }
        private int uploadedGapVersion = -1;
        private ComputeBuffer colorsR;
        private ComputeBuffer colorsG;
        private ComputeBuffer colorsB;
        private RenderTexture areaTexture;
        private NativeArray<int> areaIds;
        private NativeArray<int> islandIds;
        #if LEGACY_PATH
        private PathfinderSys path;
        #endif
        private PathSys path;
        private readonly List<int> solidArea = new();
        public int Count => Areas.Count;
        public int TotalAreaSize;
        private readonly HashSet<int> EmptySet = new();
        private int groupsX;
        private int groupsY;
        private bool isGen;
        // Island count the player last saw in play (NoteLiveIslandCount)
        private int liveIslandCount;
        public bool HasUnreachableAreas;
        public readonly Dictionary<int, HashSet<int>> Areas = new();
        public readonly Dictionary<int, HashSet<int>> OuterAreas = new();
        public int LandableAreaSize;
        public bool HasLandingZone;
        private readonly List<HashSet<int>> islandHashSets = new();
        private readonly List<HashSet<int>> areaHashSets = new();
        private readonly List<HashSet<int>> outerAreaHashSets = new();
        public Dictionary<int, HashSet<int>> Islands = new();
        private IslandReach reach;
        // Most incremental changes (a wall or a door on existing floor) leave every tile's
        // island as it was, and a reach mask costs about a millisecond to rebuild
        private bool reachIslandsChanged;
        // Set for a full recalculation that cannot have changed the islands' tiles, which
        // runs after every wall or door built: the masks are then kept under the new ids
        private bool reachKeepOnFlood;
        // What lies within reach of each island; see IslandReach
        public IslandReach Reach => reach ??= new IslandReach(this);
        public int IslandCount => Islands.Count;
        // Sparse set of constructed window tile positions (window walls + glass floors).
        // Seeded once in WarmupSystem, maintained incrementally via entity events.
        private readonly HashSet<int> windowPositions = new();
        // Per-room window count derived from windowPositions after each area recalc.
        // Key = areaId (> 0, enclosed rooms only), value = number of touching windows.
        private readonly Dictionary<int, int> windowsByArea = new();
        private bool needsWindowRecalc;
        private readonly List<OverlayInfo> overlays = new();
        public List<OverlayInfo> Overlays => overlays;
        private string overlayMode;
        public bool IsSwitchingOverlay { get; private set; }
        private bool isComputePrepared;
        private List<(ComputeShader, int)> pendingAreaComputes;
        private ShipOffScreenMarker offScreenMarker;
        private CornerDerelictMarkers cornerMarkers;
        private AreasContextPopup areasPopup;
        private IslandsContextPopup islandsPopup;
        public AreaCornerGaps CornerGaps { get; private set; }
        public bool IsRoofMode => isRoofMode;
        public void AttachAreaIdBufferTo(ComputeShader shader, int kernel) {
            if (isComputePrepared) {
                //D.Warn("Setting area buffer on compute directly: {0}", shader);
                shader.SetBuffer(kernel, ShaderVars._AreaIds, areaIdBuffer);
            } else {
                //D.Warn("Setting area buffer on compute indirectly: {0}", shader);
                // harmless
                //D.Err("Trying to attach compute buffer too early, this may not work!");
                pendingAreaComputes ??= new();
                pendingAreaComputes.Add((shader, kernel));
            }
        }
        // If task is in open space, maybe we have access to it if it's
        // adjacent to an island. That way robots or other grounded workers
        // can reach it
        public int MaybeAdjacentIslandId(int pos) {
            if (!EntityUtils.IsWithinMargin(pos)) { return 0; }
            var nId = IslandAt(Pos.N(pos));
            if (nId != 0) { return nId; }
            nId = IslandAt(Pos.S(pos));
            if (nId != 0) { return nId; }
            nId = IslandAt(Pos.E(pos));
            if (nId != 0) { return nId; }
            return IslandAt(Pos.W(pos));
        }

        private void OnToggleOverlay(OverlayInfo info, bool on) {
            if (!on || (info.Id != OverlayId.Areas && info.Id != OverlayId.Islands)) {
                areasPopup.Hide();
                islandsPopup.Hide();
                S.Query.CameraHideLink.Ask();
                overlayMode = null;
            } else {
                overlayMode = info.Id;
                if (!isRecalcRunning) {
                    RedrawCurrentTexture();
                }
                // A popup that throws must not leave this on: closing the popup would then
                // never hide the overlay again
                IsSwitchingOverlay = true;
                try {
                    if (info.Id == OverlayId.Areas) {
                        UIUtils.CloseFullScreenUI();
                        islandsPopup.Hide();
                        areasPopup.Show();
                    } else {
                        UIUtils.CloseFullScreenUI();
                        areasPopup.Hide();
                        islandsPopup.Show();
                    }
                } finally {
                    IsSwitchingOverlay = false;
                }
            }
        }

        private void RedrawCurrentTexture() {
            if (overlayMode == OverlayId.Islands) {
                RedrawIslandTexture();
            } else {
                RedrawAreaTexture();
            }
        }

        public void OutlineOfArea(int areaId, HashSet<int> outline) {
            //TODO cache
            outline.Clear();
            if (areaId == 0 || areaId == -1) { return; }

            HashSet<int> area;
            if (areaId > 0) {
                if (!Areas.TryGetValue(areaId, out area)) {
                    return;
                }
            } else {
                if (!OuterAreas.TryGetValue(areaId, out area)) {
                    return;
                }
            }
            foreach (var pos in area) {
                Pos.ForEachAround(pos, 1, false, true, (neighbor) => {
                    if (!area.Contains(neighbor)) {
                        outline.Add(neighbor);
                    }
                    return true;
                });
            }
        }
        public void OutlineOfPos(int posIdx, HashSet<int> outline) {
            OutlineOfArea(At(posIdx), outline);
        }

        public HashSet<int> AreaById(int areaId) {
            if (areaId < 1) { return EmptySet; }
            if (Areas.TryGetValue(areaId, out var area)) {
                return area;
            }
            return EmptySet;
        }

        // Returns the enclosed area id of the being's personal quarters, or 0 if none: an owned
        // non-temporary bed, room size within range, no other bed the being could be given in
        // the room, and no industrial equipment. The one rule - the room list label, the
        // personal quarters quest and request event, and breakdowns all ask this. Their own
        // copies had drifted apart (the quest ignored size, the label ignored equipment).
        public int PersonalQuartersAreaOf(BeingData being) {
            if (!TryGetOwnBedRoom(being, out var roomId) || roomId <= 0) { return 0; }
            var size = AreaById(roomId).Count;
            PersonalQuartersSizeRange(being, out var min, out var max);
            if (size < min || size > max) { return 0; }
            if (S.Query.NumBedsInArea.Ask(roomId, being, 2) > 1) { return 0; }
            if (HasIndustrialDevicesInArea(roomId).hasIndustrial) { return 0; }
            return roomId;
        }

        public bool HasPersonalQuarters(BeingData being) {
            return PersonalQuartersAreaOf(being) > 0;
        }

        private PersonalQuartersOwners quartersOwners;

        // BeingData id of whoever has this room as personal quarters, 0 for nobody. Cached
        // (see PersonalQuartersOwners) for searches that ask it per candidate
        public uint PersonalQuartersOwnerOf(int areaId) {
            quartersOwners ??= new PersonalQuartersOwners(this);
            return quartersOwners.OwnerOf(areaId);
        }

        public void InvalidatePersonalQuartersOwners() {
            quartersOwners?.Invalidate();
        }

        // The area of the being's own, non-temporary bed (not enclosed when 0 or below)
        public bool TryGetOwnBedRoom(BeingData being, out int roomId) {
            roomId = 0;
            var bed = S.Query.FindBed.Ask(being, false);
            if (bed == null) { return false; }
            var slot = bed.SlotFor(being);
            if (slot == null || slot.IsTemporary) { return false; }
            roomId = At(bed.Tile.PosIdx);
            return true;
        }

        // Minimalists sleep well in rooms others find cramped and find big rooms too big
        // sooner. The sleep mood effects (AIActionSleepInBed) use the same sizes.
        internal const int MinimalistRoomMinSize = 3;
        internal const int MinimalistCozyRoomMaxSize = 9;
        internal const int MinimalistRoomTooBigSize = 25;

        // The room sizes that count as personal quarters for this being: neither cramped
        // nor too big to sleep in
        public static void PersonalQuartersSizeRange(BeingData being, out int min, out int max) {
            if (being.HasTrait(TraitIdH.Minimalist)) {
                min = MinimalistRoomMinSize;
                max = MinimalistRoomTooBigSize;
            } else {
                min = Tunables.Room_PersonalQuartersMinSize;
                max = Tunables.Room_PersonalQuartersMaxSize;
            }
        }

        public HashSet<int> OuterAreaById(int areaId) {
            if (areaId > -1) { return EmptySet; }
            if (OuterAreas.TryGetValue(areaId, out var area)) {
                return area;
            }
            return EmptySet;
        }

        private bool isShowingNoOuterAreasWarning;
        private NotificationHandle noOuterAreasWarning;
        public NativeArray<AreaType> AreaTypes;
        // for jobs reading area ids (At), e.g. LightMapJob
        public NativeArray<int> AreaIds => areaIds;
        public AreaType AreaTypeAt(int pos) {
            return AreaTypes[pos];
        }

        private int areaKernel = -1;
        //private int islandKernel = -1;
        private int gridSize;
        private int gridH;
        private bool needsRecalc;
        private bool needsFullRecalc;
        private JobHandle ffJob;
        private bool isRecalcRunning;

        private readonly struct PendingAreaChange {
            public readonly int Pos;
            public readonly AreaType OldType;
            public readonly AreaType NewType;
            public PendingAreaChange(int pos, AreaType oldType, AreaType newType) {
                Pos = pos;
                OldType = oldType;
                NewType = newType;
            }
        }
        private readonly List<PendingAreaChange> pendingChanges = new();
        private int deferredFullRecalcFrames;
        private int framesSinceLastFullRecalc;
        private const int DeferredRecalcDelay = 10;
        private const int MaxFramesWithoutFullRecalc = 300;
        private const int MaxBFSVisits = 256;
        // Reusable collections for incremental BFS
        private readonly Queue<int> bfsQueue = new();
        private readonly HashSet<int> bfsVisited = new();

        public override void Unload() {
            // Closing them redraws the overlay texture, so before the buffers go: leaving the
            // game with the Rooms overlay open threw in the UI unload after this one
            areasPopup?.Hide();
            islandsPopup?.Hide();
            if (isRecalcRunning) {
                ffJob.Complete();
                AfterFlood();
            }
            ReleaseResources();
            Areas.Clear();
            OuterAreas.Clear();
            Islands.Clear();
            windowPositions.Clear();
            windowsByArea.Clear();
            CornerGaps?.Clear();
            colR = null;
            colG = null;
            colB = null;
            DoorCheck = null;
        }

        protected override void OnInitialize() {
            CleanupCache();
            DoorCheck = new DoorConnectionCheck(this);
            areasPopup = new AreasContextPopup(S, this);
            islandsPopup = new IslandsContextPopup(S, this);
            CornerGaps = new AreaCornerGaps(S, this);
            S.Sig.AfterLoadState.AddListener(OnLoadSave);
            S.Sig.ToggleOverlay.AddListener(OnToggleOverlay);
            S.Sig.ConstructionCompleted.AddListener(OnConstructionCompleted);
            S.Sig.GenerationRunning.AddListener(OnGenRunning);
            S.Sig.LabelChanged.AddListener(OnLabelChanged);
            S.Query.GetDevicesInArea.SetResponder(GetDevicesInArea);
            S.Query.NumAdmirableInArea.SetResponder(NumAdmirableInArea);
            S.Query.NumWindowsInArea.SetResponder(NumWindowsInArea);
            S.Query.HasIndustrialDevicesInArea.SetResponder(HasIndustrialDevicesInArea);
            Ready.SetAreasInitial(false, "Initializing");
            Ready.SetAreas(false, "Initializing");
        }

        private void OnConstructionCompleted(Tile tile) {
            OnEntityChanged(tile, true);
        }

        private int NumAdmirableInArea(int areaId, int max) {
            if (!Areas.TryGetValue(areaId, out var area)) {
                return 0;
            }
            var admirables = S.Components.AllByType<AdmirableComp>();
            if (Arrays.IsEmpty(admirables)) {
                return 0;
            }
            int num = 0;

            if (area.Count < admirables.Count) {
                foreach (var areaPos in area) {
                    var tile = EntityUtils.ObjAt(areaPos);
                    if (tile == null) { continue; }
                    if (tile.Transform.IsChild) { continue; }
                    if (!tile.IsConstructed) { continue; }
                    if (tile.TryGetComponent<AdmirableComp>(out var adm)) {
                        num++;
                        if (num == max) {
                            return num;
                        }
                    }
                }
            } else {
                foreach (AdmirableComp adm in admirables) {
                    if (adm.Tile?.IsConstructed == false) { continue; }
                    if (!area.Contains(adm.Entity.PosIdx)) { continue; }
                    num++;
                    if (num == max) {
                        return num;
                    }
                }
            }
            return num;
        }

        // Enclosed rooms have positive ids and outer areas negative ones (below -1), each in its
        // own dictionary. Checking one id against both dictionaries always failed, which left
        // the industrial room check dead from 2026-01 until 2026-09.
        private HashSet<int> AreaOrOuterAreaById(int areaId) {
            return areaId > 0 ? AreaById(areaId) : OuterAreaById(areaId);
        }

        private (bool hasIndustrial, bool hasNoisy) HasIndustrialDevicesInArea(int areaId) {
            var area = AreaOrOuterAreaById(areaId);
            if (area.Count == 0) {
                return (false, false);
            }
            var industrial = S.Components.AllByType<IndustrialEquipmentComp>();
            if (industrial == null) {
                return (false, false);
            }
            bool hasIndustrial = false;
            foreach (IndustrialEquipmentComp ind in industrial) {
                var tile = ind.Tile;
                if (tile == null || !tile.IsConstructed) { continue; }
                if (!area.Contains(tile.PosIdx)) { continue; }
                if (ind.IsNoisy) {
                    return (true, true);
                }
                hasIndustrial = true;
            }
            return (hasIndustrial, false);
        }

        private int GetDevicesInArea(int areaId, List<Tile> tiles) {
            var area = AreaOrOuterAreaById(areaId);
            var withLabel = S.Components.AllByType<TileLabelComp>();
            if (withLabel == null || area.Count < withLabel.Count) {
                foreach (var areaPos in area) {
                    var tile = EntityUtils.ObjAt(areaPos);
                    if (tile == null) { continue; }
                    if (tile.Transform.IsChild) { continue; }
                    tiles.Add(tile);
                }
            } else {
                foreach (TileLabelComp label in withLabel) {
                    var tile = label.Tile;
                    if (area.Contains(tile.PosIdx)) {
                        tiles.Add(tile);
                    }
                }
            }
            return tiles.Count;
        }

        private void OnLabelChanged(TileLabelComp label) {
            areasPopup.Refresh();
        }

        private void CleanupCache() {
            foreach (var hs in areaHashSets) {
                hs.Clear();
            }
            foreach (var hs in outerAreaHashSets) {
                hs.Clear();
            }
            foreach (var hs in islandHashSets) {
                hs.Clear();
            }
        }

        private void OnGenRunning(bool r) {
            isGen = r;
        }

        private void OnLoadSave(GameState state) {
            Ready.SetAreasInitial(false, "Initializing");
            Ready.SetAreas(false, "Initializing");
            #if LEGACY_PATH
            path = state.Sys.Path;
            #endif
            path = state.Sys.Path;
            gridSize = state.GridSize;
            gridH = state.GridHeight;
            S.Sig.ForceAreas.AddListener(ForceAreas);
            S.Sig.EntityAdded.AddListener(OnEntityAdded);
            S.Sig.EntityRemoved.AddListener(OnEntityRemoved);
            S.Sig.AtmoRecalculated.AddListener(OnAtmoRecalc);
            S.Sig.ShipMoved.AddListener(OnShipMoved);
            S.Sig.ShipRotated.AddListener(OnShipRotated);
            state.Clock.EveryTickOrFrame.AddListener(OnTickOrFrame);
            state.Sig.EveryFrame.AddListener(EveryFrame);
            PrepareCompute();
            WarmupSystem();
            // A loaded ship in pieces was seen in pieces. A new game lands here before
            // generation, on an empty map.
            liveIslandCount = IslandCount;
            // Areas (and therefore IsSectionFloorAt) are valid after warmup, so this is the
            // earliest safe point to re-validate loaded construction waves. Old/buggy saves
            // may already contain a seedless or misanchored wave that would otherwise never
            // self-heal, since only completing/removing a floor re-validates its wave.
            // Harmless no-op when there are no unfinished wave floors (e.g. a fresh game).
            FloorConstructionWavePlanner.RevalidateAllWaves();
        }

        private void OnAtmoRecalc(AtmoLayer layer) {
            DoorCheck.UpdateAtmo(layer);
        }

        // Winching (CmdMoveSectionBase) and rotation (CmdRotateSection) relocate tiles by
        // writing them directly into the grid (g.Set) and never go through
        // S.AddEntity/RemoveEntity, so EntityAdded/EntityRemoved (and therefore
        // windowPositions, which is keyed by absolute grid position) never fire for the move.
        // AreaTypes gets fixed up separately via RebuildMap -> UpdateSection; windowPositions
        // needs the same kind of full refresh here, or every window in a moved/rotated section
        // silently stops counting toward its room.
        private void OnShipMoved(int dx, int dy) {
            RefreshWindowPositionsAfterShipChange();
        }

        private void OnShipRotated() {
            RefreshWindowPositionsAfterShipChange();
        }

        private void RefreshWindowPositionsAfterShipChange() {
            SeedWindowPositions();
            RebuildWindowsByArea();
        }

        private void WarmupSystem() {
            AreaTypes = new NativeArray<AreaType>(gridSize,
                Allocator.Persistent,
                NativeArrayOptions.ClearMemory);
            areaIds = new NativeArray<int>(gridSize, Allocator.Persistent);
            islandIds = new NativeArray<int>(gridSize, Allocator.Persistent);
            for (int i = 0; i < gridSize; i++) {
                var areaType = ResolveAreaTypeAt(i);
                SetAreaType(i, areaType);
            }
            ForceAreas();
            // Seed windowPositions after areas are calculated (areaIds are valid),
            // then rebuild the per-room cache since AfterFlood ran before the seed.
            SeedWindowPositions();
            RebuildWindowsByArea();
            Ready.SetAreasInitial(true, "Warmup done");
            S.Sig.AreasInitialized.Send();
            S.Sys.Areas.DoorCheck.ValidateDoors();

            var prefab = ResourceLibrary.FindPrefab<ShipOffScreenMarker>();
            offScreenMarker = PrefabPool.Spawn(prefab, Consts.V3Zero, Consts.QId,
                ResourceLibrary.Root);
            offScreenMarker.Mode = ShipMarkerMode.OurShip;
            cornerMarkers = new CornerDerelictMarkers(S);
            // ForceAreas() above already flooded, and its Refresh happened before
            // this existed - without this the arrows stay hidden on a loaded save
            // until something else dirties the areas
            cornerMarkers.Refresh();

            #if VERBOSE_AREAS
            D.Warn("Areas warmed up. Islands: {0}. Rooms: {1}", IslandCount, Count);
            #endif
        }

        public AreaType ResolveAreaTypeAt(int i) {
            var floor = EntityUtils.FloorAt(i);
            if (floor?.IsConstructed == true
                    && floor.Passability?.IsUnbuiltOverrideActive != true) {
                var wall = EntityUtils.WallAt(i);
                if (wall?.IsConstructed == true
                        && wall.Passability?.IsUnbuiltOverrideActive != true) {
                    if (wall.Door != null) {
                        return AreaType.Door;
                    } else {
                        return AreaType.Wall;
                    }
                } else {
                    if (floor.Integrity.CanHaveFullIntegrity || floor.Passability.IsWalkable) {
                        var obj = EntityUtils.ParentObjAt(i);
                        if (obj != null && obj.IsConstructed
                                && obj.Passability?.IsUnbuiltOverrideActive != true
                                && !obj.Passability.IsPassable) {
                            return AreaType.BlockedByObject;
                        }
                        return AreaType.Floor;
                    } else {
                        return AreaType.Space;
                    }
                }
            }
            return AreaType.Space;
        }

        private void EveryFrame() {
            floorKinds?.Flush();
            LightPass?.Flush();
            Wear?.Flush();
            if (!isRecalcRunning) { return; }
            ffJob.Complete();
            AfterFlood();
        }

        private void ForceAreas() {
            pendingChanges.Clear();
            deferredFullRecalcFrames = 0;
            needsFullRecalc = false;
            needsRecalc = false;
            framesSinceLastFullRecalc = 0;
            if (!isRecalcRunning) {
                FullRecalculate();
            }
            if (isRecalcRunning) {
                ffJob.Complete();
                AfterFlood();
            }
        }

        private HashSet<int> CachedSetFrom(List<HashSet<int>> source, int id) {
            for (int i = source.Count; i <= id; i++) {
                source.Add(new HashSet<int>());
            }
            return source[id];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int At(int idx) {
            #if !PRODUCTION_BUILD
            if (idx < 0 || idx >= gridSize) {
                D.Err("Change this to AtSafe! Areas ready: {0}. Pos: {1}/{2}. AreaIds.len {3}",
                    Ready.AreasInitial, idx, Pos.String(idx), areaIds.Length);
                return 0;
            }
            #endif
            return areaIds[idx];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int IslandAt(int idx) {
            #if !PRODUCTION_BUILD
            if (idx < 0 || idx >= gridSize) {
                D.Err("Change this to IslandAtSafe! {0} | {1}", idx, Ready.ShortStatus);
                return 0;
            }
            #endif
            return islandIds[idx];
        }
        public int IslandSizeAt(int idx) {
            #if !PRODUCTION_BUILD
            if (idx < 0 || idx >= gridSize) {
                D.Err("Change this to IslandAtSafe! {0} | {1}", idx, Ready.ShortStatus);
                return 0;
            }
            #endif
            var islandId = islandIds[idx];
            if (islandId == 0) { return 0; }
            if (Islands.TryGetValue(islandId, out var isle)) {
                return isle.Count;
            }
            return 0;
        }

        private bool needsUpdateNoOuterAreasWarning;

        private int shipSize = -1;
        public int ShipSize {
            get {
                if (shipSize != -1) {
                    return shipSize;
                }
                if (IslandCount == 1) {
                    shipSize = TotalAreaSize;
                    return shipSize;
                }

                if (TryGetShipIsland(out var shipIsland)) {
                    shipSize = IslandSize(shipIsland);
                } else {
                    shipSize = TotalAreaSize;
                }
                return shipSize;
            }
        }

        private void UpdateNoOuterAreasWarning() {
            if (!Ready.Gen || !Ready.AreasInitial) {
                needsUpdateNoOuterAreasWarning = true;
                return;
            }
            needsUpdateNoOuterAreasWarning = false;
            if (!isShowingNoOuterAreasWarning && !HasLandingZone) {
                isShowingNoOuterAreasWarning = true;
                if (!noOuterAreasWarning.IsValid) {
                    noOuterAreasWarning = Notification.Build(IconId.CRoom,
                            T.NoLandingZone, 0, NotificationType.Warning)
                        .WithOnClick(() => S.Sig.ShowOverlay.Send(OverlayId.Areas))
                        .WithTooltipFunc(ShowLandingZoneTip)
                        .Sticky()
                        .Show();
                }
            } else if (isShowingNoOuterAreasWarning && HasLandingZone) {
                isShowingNoOuterAreasWarning = false;
                if (noOuterAreasWarning.IsValid) {
                    Notification.Remove(ref noOuterAreasWarning);
                }
            }
        }
        private string ShowLandingZoneTip() {
            var sb = Caches.StringBuilder2;
            sb.Clear();
            sb.Append(T.NounLandingZone);
            sb.Append(":\n");
            sb.Append(T.AmountCurrent);
            sb.Append(": ");
            if (HasLandingZone) {
                sb.AppendLine(Texts.Green(Units.Num(LandableAreaSize)));
            } else {
                sb.AppendLine(Texts.Yellow(Units.Num(LandableAreaSize)));
            }
            sb.Append(T.AmountRecommendedMinimum);
            sb.Append(": ");
            sb.Append(Consts.MinLandingZoneSize);
            sb.Append(GlyphMap.BRHR);
            if (HasLandingZone) {
                sb.AppendLine(T.AreaExposedToSpaceTip);
            } else {
                sb.AppendLine(T.NoLandingZoneTip);
            }
            return sb.ToString();
        }

        // One Piece is for joining sections the player saw apart. Ship generation floats and
        // rejoins sections before the game is ready, and bulk moves turn Gen off while they
        // run, so only counts seen in play are kept.
        private void NoteLiveIslandCount(int now) {
            if (!Ready.Game || !Ready.Gen) { return; }
            if (now == 1 && liveIslandCount > 1) {
                The.AchievementTracker.Unlock(AchievementOnePiece.AchievementId);
            }
            liveIslandCount = now;
        }

        private void AfterFlood() {
            isRecalcRunning = false;
            var islandsBefore = Islands.Count;

            Islands.Clear();
            Areas.Clear();
            OuterAreas.Clear();
            tempCurrentItems.Dispose();
            tempProcessedAreas.Dispose();
            tempVisited.Dispose();
            tempQueue.Dispose();
            tempProcessedIslands.Dispose();
            solidArea.Clear();
            TotalAreaSize = 0;
            shipSize = -1;
            LandableAreaSize = 0;

            //var startTime = Time.realtimeSinceStartupAsDouble;
            // Cache frequently accessed arrays
            var islandIdsArray = islandIds;
            var areaIdsArray = areaIds;

            for (int i = 0; i < gridSize; i++) {
                // Cache array access - only read once per iteration
                var islandId = islandIdsArray[i];
                if (islandId == 0) { continue; }

                TotalAreaSize++;

                // Get or create island set
                if (!Islands.TryGetValue(islandId, out var islandSet)) {
                    islandSet = CachedSetFrom(islandHashSets, islandId);
                    islandSet.Clear();
                    Islands.Add(islandId, islandSet);
                }
                islandSet.Add(i);
                solidArea.Add(i);

                // Cache array access - only read once per iteration
                var areaId = areaIdsArray[i];
                if (areaId > 0) {
                    // Indoor area
                    if (!Areas.TryGetValue(areaId, out var areaSet)) {
                        areaSet = CachedSetFrom(areaHashSets, areaId);
                        areaSet.Clear();
                        Areas.Add(areaId, areaSet);
                    }
                    areaSet.Add(i);
                // -1 is a wall, anything below will be areas outside
                } else if (areaId < -1) {
                    // Outdoor area - check if within margin first (cheaper check)
                    if (EntityUtils.IsWithinMargin(i)) {
                        var floor = EntityUtils.FloorAt(i);
                        if (floor == null) {
                            // #if !PRODUCTION_BUILD
                            // this can happen when tile gets removed in mid FF
                            // D.ErrSoft("Floor is null after flood fill at pos: {0}",
                            // if (Ready.Gen) {
                            //     //     Pos.String(i));
                            //     The.ScreenBlock.HideImmediate();
                            //     EntityUtils.CameraFocusOn(i);
                            // }
                            // #endif
                            continue;
                        }
                        // Check integrity threshold (const inlined by compiler)
                        if (floor.Integrity.Integrity > Consts.MinLandingZoneIntegrity) {
                            if (!OuterAreas.TryGetValue(areaId, out var areaSet)) {
                                areaSet = CachedSetFrom(outerAreaHashSets, -areaId);
                                areaSet.Clear();
                                OuterAreas.Add(areaId, areaSet);
                            }
                            areaSet.Add(i);
                            LandableAreaSize++;
                        } else {
                            // -1 is
                            //areaIds[i] = 0;
                        }
                    } else {
                        //areaIds[i] = 0;
                    }
                }
            }
            var islCntNow = Islands.Count;
            if (!Ready.Gen) {
                // to get randomness in predictable way when generating stuff
                solidArea.Sort();
            }
            NoteLiveIslandCount(islCntNow);
            HasLandingZone = LandableAreaSize >= Consts.MinLandingZoneSize;

            S.Situation.Islands = islCntNow;
            S.Situation.Areas = Areas.Count;
            S.Situation.ShipSize = ShipSize;

            HasUnreachableAreas = DoorCheck.FindUnreachableAreas();
            S.Sys.Path.HasUnreachableAreas = HasUnreachableAreas;

            UpdateNoOuterAreasWarning();

            areaIdBuffer.SetData(areaIds);
            Shape?.Rebuild();
            CornerGaps.MarkDirty();

            if (UIShowing.Overlay) {
                if (UIShowing.OverlayType == OverlayId.Islands) {
                    RedrawIslandTexture();
                    islandsPopup.Refresh();
                } else if (UIShowing.OverlayType == OverlayId.Areas) {
                    RedrawAreaTexture();
                    areasPopup.Refresh();
                }
            }
            S.Vars.LastAreaChangeTick = G.Ticks;
            hasCachedShipBounds = false;
            if (reachKeepOnFlood) {
                reach?.Remap();
            } else {
                reach?.Invalidate();
            }
            reachKeepOnFlood = false;
            reachIslandsChanged = false;
            cornerMarkers?.Refresh();

            RebuildWindowsByArea();
            // room ids are renumbered by a recalculation
            quartersOwners?.Invalidate();
            Ready.SetAreas(true, "Done recalculating");

            S.Sig.AreasRecalculated.Send();
            if (islCntNow != islandsBefore) {
                S.Sig.IslandCountChanged.Send(islandsBefore, islCntNow);
            }
            // D.Warn("AfterFlood took: {0}", Time.realtimeSinceStartupAsDouble - startTime);
            #if VERBOSE_AREAS
            D.Warn("Done Recalc. Islands: {0}. Areas: {1}", IslandCount, Count);
            #endif
        }

        private void OnTickOrFrame() {
            if (needsUpdateNoOuterAreasWarning) {
                UpdateNoOuterAreasWarning();
            }
            if (!needsRecalc && pendingChanges.Count == 0
                    && !needsFullRecalc && !isRecalcRunning) {
                if (DoorCheck.TeleportersChanged) {
                    DoorCheck.FindUnreachableAreas();
                }
                if (needsWindowRecalc) {
                    needsWindowRecalc = false;
                    RebuildWindowsByArea();
                }
            }
            if (!isRecalcRunning && !isGen) {
                if (needsFullRecalc || needsRecalc) {
                    needsRecalc = false;
                    needsFullRecalc = false;
                    pendingChanges.Clear();
                    deferredFullRecalcFrames = 0;
                    framesSinceLastFullRecalc = 0;
                    FullRecalculate();
                } else if (pendingChanges.Count > 0) {
                    ProcessPendingChanges();
                    framesSinceLastFullRecalc++;
                } else if (deferredFullRecalcFrames > 0) {
                    deferredFullRecalcFrames--;
                    if (deferredFullRecalcFrames == 0) {
                        framesSinceLastFullRecalc = 0;
                        FullRecalculate(keepReach: true);
                    } else {
                        framesSinceLastFullRecalc++;
                    }
                } else {
                    framesSinceLastFullRecalc++;
                }
                #if !PRODUCTION_BUILD
                if (EmptySet.Count > 0) {
                    D.Err("Something wrote something into Areas.EmptySet!");
                }
                #endif
            }
        }
        private NativeArray<bool> tempProcessedIslands;
        private NativeArray<bool> tempProcessedAreas;
        private NativeList<int> tempCurrentItems;
        private NativeQueue<int> tempQueue;
        private NativeArray<bool> tempVisited;

        private void ProcessPendingChanges() {
            bool doFullRecalc = false;
            // Islands are made of solid tiles: only a tile turning into space or out of it
            // can change one (a wall or a door on floor cannot)
            bool solidChanged = false;
            for (int i = 0; i < pendingChanges.Count; i++) {
                var change = pendingChanges[i];
                solidChanged |= (change.OldType == AreaType.Space)
                    != (change.NewType == AreaType.Space);
                if (!doFullRecalc && CanUpdateIncrementally(change)) {
                    ApplyIncrementalChange(change);
                } else {
                    doFullRecalc = true;
                }
            }
            pendingChanges.Clear();
            if (doFullRecalc
                    || framesSinceLastFullRecalc > MaxFramesWithoutFullRecalc) {
                deferredFullRecalcFrames = 0;
                framesSinceLastFullRecalc = 0;
                FullRecalculate(keepReach: !solidChanged && !reachIslandsChanged);
            } else {
                FinishIncrementalUpdate();
                deferredFullRecalcFrames = DeferredRecalcDelay;
            }
        }

        private bool CanUpdateIncrementally(PendingAreaChange change) {
            bool wasSpace = change.OldType == AreaType.Space;
            bool isSpace = change.NewType == AreaType.Space;
            // Only handle transitions to/from Space incrementally
            if (wasSpace == isSpace) {
                return false;
            }
            if (isSpace) {
                // Tile was removed (non-Space → Space)
                return !WouldSplitConnectivity(change.Pos);
            } else {
                // Tile was added (Space → non-Space)
                if (WouldMergeConnectivity(change.Pos, change.NewType)) {
                    return false;
                }
                // Verify we have a neighbor to join (otherwise need full recalc)
                return HasNeighborIsland(change.Pos);
            }
        }

        private bool HasNeighborIsland(int pos) {
            int n;
            n = Pos.N(pos);
            if (!Pos.IsOOB(n) && islandIds[n] > 0) { return true; }
            n = Pos.S(pos);
            if (!Pos.IsOOB(n) && islandIds[n] > 0) { return true; }
            n = Pos.E(pos);
            if (!Pos.IsOOB(n) && islandIds[n] > 0) { return true; }
            n = Pos.W(pos);
            if (!Pos.IsOOB(n) && islandIds[n] > 0) { return true; }
            return false;
        }

        private bool WouldSplitConnectivity(int pos) {
            var islandId = islandIds[pos];
            if (islandId == 0) { return false; }

            // Collect same-island cardinal neighbors
            int neighborCount = 0;
            int firstNeighbor = -1;
            int n;
            n = Pos.N(pos);
            if (!Pos.IsOOB(n) && islandIds[n] == islandId) {
                neighborCount++;
                firstNeighbor = n;
            }
            n = Pos.S(pos);
            if (!Pos.IsOOB(n) && islandIds[n] == islandId) {
                neighborCount++;
                if (firstNeighbor < 0) firstNeighbor = n;
            }
            n = Pos.E(pos);
            if (!Pos.IsOOB(n) && islandIds[n] == islandId) {
                neighborCount++;
                if (firstNeighbor < 0) firstNeighbor = n;
            }
            n = Pos.W(pos);
            if (!Pos.IsOOB(n) && islandIds[n] == islandId) {
                neighborCount++;
                if (firstNeighbor < 0) firstNeighbor = n;
            }
            // 0 or 1 same-island neighbor: can't split
            if (neighborCount <= 1) { return false; }

            // BFS from first neighbor to verify all other same-island neighbors
            // are still connected without going through pos
            bfsQueue.Clear();
            bfsVisited.Clear();
            bfsQueue.Enqueue(firstNeighbor);
            bfsVisited.Add(firstNeighbor);
            bfsVisited.Add(pos); // exclude the removed tile

            int remaining = neighborCount - 1;
            int visits = 0;

            while (bfsQueue.Count > 0 && remaining > 0
                    && visits < MaxBFSVisits) {
                var cur = bfsQueue.Dequeue();
                visits++;
                // Check each cardinal neighbor
                n = Pos.N(cur);
                if (!Pos.IsOOB(n) && bfsVisited.Add(n)
                        && islandIds[n] == islandId) {
                    if (IsOriginalNeighbor(n, pos)) { remaining--; }
                    bfsQueue.Enqueue(n);
                }
                n = Pos.S(cur);
                if (!Pos.IsOOB(n) && bfsVisited.Add(n)
                        && islandIds[n] == islandId) {
                    if (IsOriginalNeighbor(n, pos)) { remaining--; }
                    bfsQueue.Enqueue(n);
                }
                n = Pos.E(cur);
                if (!Pos.IsOOB(n) && bfsVisited.Add(n)
                        && islandIds[n] == islandId) {
                    if (IsOriginalNeighbor(n, pos)) { remaining--; }
                    bfsQueue.Enqueue(n);
                }
                n = Pos.W(cur);
                if (!Pos.IsOOB(n) && bfsVisited.Add(n)
                        && islandIds[n] == islandId) {
                    if (IsOriginalNeighbor(n, pos)) { remaining--; }
                    bfsQueue.Enqueue(n);
                }
            }
            // If we couldn't reach all neighbors, it might split
            return remaining > 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsOriginalNeighbor(int candidate, int center) {
            return candidate == Pos.N(center)
                || candidate == Pos.S(center)
                || candidate == Pos.E(center)
                || candidate == Pos.W(center);
        }

        private bool WouldMergeConnectivity(int pos, AreaType newType) {
            int islandA = 0;
            int n;
            n = Pos.N(pos);
            if (!Pos.IsOOB(n)) {
                var nIsland = islandIds[n];
                if (nIsland > 0) {
                    islandA = nIsland;
                }
            }
            n = Pos.S(pos);
            if (!Pos.IsOOB(n)) {
                var nIsland = islandIds[n];
                if (nIsland > 0 && islandA != 0 && nIsland != islandA) {
                    return true;
                }
                if (islandA == 0) islandA = nIsland;
            }
            n = Pos.E(pos);
            if (!Pos.IsOOB(n)) {
                var nIsland = islandIds[n];
                if (nIsland > 0 && islandA != 0 && nIsland != islandA) {
                    return true;
                }
                if (islandA == 0) islandA = nIsland;
            }
            n = Pos.W(pos);
            if (!Pos.IsOOB(n)) {
                var nIsland = islandIds[n];
                if (nIsland > 0 && islandA != 0 && nIsland != islandA) {
                    return true;
                }
            }
            // For Floor additions, also check if it would merge areas
            if (newType == AreaType.Floor) {
                int areaA = 0;
                n = Pos.N(pos);
                if (!Pos.IsOOB(n)) {
                    var nArea = areaIds[n];
                    if (nArea > 0) {
                        areaA = nArea;
                    }
                }
                n = Pos.S(pos);
                if (!Pos.IsOOB(n)) {
                    var nArea = areaIds[n];
                    if (nArea > 0 && areaA != 0 && nArea != areaA) {
                        return true;
                    }
                    if (areaA == 0) areaA = nArea;
                }
                n = Pos.E(pos);
                if (!Pos.IsOOB(n)) {
                    var nArea = areaIds[n];
                    if (nArea > 0 && areaA != 0 && nArea != areaA) {
                        return true;
                    }
                    if (areaA == 0) areaA = nArea;
                }
                n = Pos.W(pos);
                if (!Pos.IsOOB(n)) {
                    var nArea = areaIds[n];
                    if (nArea > 0 && areaA != 0 && nArea != areaA) {
                        return true;
                    }
                }
            }
            return false;
        }

        private void ApplyIncrementalChange(PendingAreaChange change) {
            int pos = change.Pos;
            var islandBefore = islandIds[pos];
            if (change.NewType == AreaType.Space) {
                // Tile removed: remove from island and area
                var oldIsland = islandIds[pos];
                var oldArea = areaIds[pos];
                islandIds[pos] = 0;
                areaIds[pos] = 0;
                reachIslandsChanged |= islandBefore != 0;
                if (oldIsland > 0 && Islands.TryGetValue(oldIsland, out var isle)) {
                    isle.Remove(pos);
                    if (isle.Count == 0) {
                        Islands.Remove(oldIsland);
                    }
                }
                if (oldArea > 0) {
                    if (Areas.TryGetValue(oldArea, out var area)) {
                        area.Remove(pos);
                        if (area.Count == 0) {
                            Areas.Remove(oldArea);
                        }
                    }
                } else if (oldArea < -1) {
                    if (OuterAreas.TryGetValue(oldArea, out var area)) {
                        area.Remove(pos);
                        if (area.Count == 0) {
                            OuterAreas.Remove(oldArea);
                        }
                        if (EntityUtils.IsWithinMargin(pos)) {
                            LandableAreaSize--;
                        }
                    }
                }
                TotalAreaSize--;
                solidArea.Remove(pos);
            } else {
                // Tile added (Space → non-Space)
                // Find the island to join
                int neighborIsland = 0;
                int n;
                n = Pos.N(pos);
                if (!Pos.IsOOB(n) && islandIds[n] > 0) {
                    neighborIsland = islandIds[n];
                }
                if (neighborIsland == 0) {
                    n = Pos.S(pos);
                    if (!Pos.IsOOB(n) && islandIds[n] > 0) {
                        neighborIsland = islandIds[n];
                    }
                }
                if (neighborIsland == 0) {
                    n = Pos.E(pos);
                    if (!Pos.IsOOB(n) && islandIds[n] > 0) {
                        neighborIsland = islandIds[n];
                    }
                }
                if (neighborIsland == 0) {
                    n = Pos.W(pos);
                    if (!Pos.IsOOB(n) && islandIds[n] > 0) {
                        neighborIsland = islandIds[n];
                    }
                }
                islandIds[pos] = neighborIsland;
                reachIslandsChanged |= islandBefore != neighborIsland;
                if (neighborIsland > 0
                        && Islands.TryGetValue(neighborIsland, out var isle)) {
                    isle.Add(pos);
                }

                // Handle area assignment
                if (change.NewType == AreaType.Wall
                        || change.NewType == AreaType.Door) {
                    areaIds[pos] = -1;
                } else {
                    // Floor: join nearest neighbor area
                    int neighborArea = 0;
                    n = Pos.N(pos);
                    if (!Pos.IsOOB(n) && areaIds[n] != 0 && areaIds[n] != -1) {
                        neighborArea = areaIds[n];
                    }
                    if (neighborArea == 0) {
                        n = Pos.S(pos);
                        if (!Pos.IsOOB(n) && areaIds[n] != 0 && areaIds[n] != -1) {
                            neighborArea = areaIds[n];
                        }
                    }
                    if (neighborArea == 0) {
                        n = Pos.E(pos);
                        if (!Pos.IsOOB(n) && areaIds[n] != 0 && areaIds[n] != -1) {
                            neighborArea = areaIds[n];
                        }
                    }
                    if (neighborArea == 0) {
                        n = Pos.W(pos);
                        if (!Pos.IsOOB(n) && areaIds[n] != 0 && areaIds[n] != -1) {
                            neighborArea = areaIds[n];
                        }
                    }
                    areaIds[pos] = neighborArea;
                    if (neighborArea > 0) {
                        if (Areas.TryGetValue(neighborArea, out var area)) {
                            area.Add(pos);
                        }
                    } else if (neighborArea < -1) {
                        if (OuterAreas.TryGetValue(neighborArea, out var area)) {
                            area.Add(pos);
                            if (EntityUtils.IsWithinMargin(pos)) {
                                var floor = EntityUtils.FloorAt(pos);
                                if (floor != null && floor.Integrity.Integrity
                                        > Consts.MinLandingZoneIntegrity) {
                                    LandableAreaSize++;
                                }
                            }
                        }
                    }
                }
                TotalAreaSize++;
                solidArea.Add(pos);
            }
            shipSize = -1;
        }

        private void FinishIncrementalUpdate() {
            HasLandingZone = LandableAreaSize >= Consts.MinLandingZoneSize;
            S.Situation.Islands = Islands.Count;
            NoteLiveIslandCount(Islands.Count);
            S.Situation.Areas = Areas.Count;
            S.Situation.ShipSize = ShipSize;
            areaIdBuffer.SetData(areaIds);
            Shape?.Rebuild();
            CornerGaps.MarkDirty();
            S.Vars.LastAreaChangeTick = G.Ticks;
            hasCachedShipBounds = false;
            if (reachIslandsChanged) {
                reachIslandsChanged = false;
                reach?.Invalidate();
            }
            cornerMarkers?.Refresh();
            if (UIShowing.Overlay) {
                if (UIShowing.OverlayType == OverlayId.Islands) {
                    RedrawIslandTexture();
                } else if (UIShowing.OverlayType == OverlayId.Areas) {
                    RedrawAreaTexture();
                }
            }
            RebuildWindowsByArea();
        }

        // keepReach: no tile changed between space and solid since the reach masks were
        // built, so the islands keep their tiles and only their ids may change
        private void FullRecalculate(bool keepReach = false) {
            if (isRecalcRunning) {
                D.Err("Trying to run FullRecalculate when it's already running! Force completing previous job");
                ffJob.Complete();
                AfterFlood();
            }
            reachKeepOnFlood = keepReach;
            isRecalcRunning = true;
            // D.Warn("Full Recalculate");
            tempProcessedIslands = new NativeArray<bool>(gridSize, Allocator.TempJob);
            tempProcessedAreas = new NativeArray<bool>(gridSize, Allocator.TempJob);
            tempCurrentItems = new NativeList<int>(Allocator.TempJob);
            tempQueue = new NativeQueue<int>(Allocator.TempJob);
            tempVisited = new NativeArray<bool>(gridSize, Allocator.TempJob);
            var job = new AreaFloodFillJob {
                AreaTypes = AreaTypes,
                OutAreas = areaIds,
                OutIslands = islandIds,
                GridSize = gridSize,
                GridH = gridH,
                ProcessedAreas = tempProcessedAreas,
                ProcessedIslands = tempProcessedIslands,
                Visited = tempVisited,
                Queue = tempQueue,
                CurrentItems = tempCurrentItems
            };
            Ready.SetAreas(false, "Areas Recalc");

            #if IMMEDIATE_FF
            var isImmediate = true;
            #else
            var isImmediate = G.ClockSpeed > ClockSpeed.Fast2 || !Ready.Gen;
            #endif
            if (isImmediate) {
                using (D.Timer("FloodJob", 0.01f)) {
                    job.Run();
                }
                AfterFlood();
            } else {
                ffJob = job.Schedule();
            }
        }

        private void ReleaseResources() {
            if (offScreenMarker != null) {
                PrefabPool.Despawn(offScreenMarker.gameObject);
                offScreenMarker = null;
            }
            if (cornerMarkers != null) {
                cornerMarkers.Dispose();
                cornerMarkers = null;
            }
            if (isRecalcRunning) {
                ffJob.Complete();
                isRecalcRunning = false;
            }
            if (AreaTypes.IsCreated) {
                AreaTypes.Dispose();
            }
            if (areaIds.IsCreated) {
                areaIds.Dispose();
            }
            if (islandIds.IsCreated) {
                islandIds.Dispose();
            }
            Shader.SetGlobalBuffer(ShaderVars._AreaIds, (ComputeBuffer) null);
            floorKinds?.Dispose();
            floorKinds = null;
            LightPass?.Dispose();
            LightPass = null;
            Shape?.Dispose();
            Shape = null;
            Wear?.Dispose();
            Wear = null;
            if (areaIdBuffer != null) {
                areaIdBuffer.Release();
                areaTypeBuffer.Release();
                islandIdBuffer.Release();
                cornerGapBuffer.Release();
                The.Alloc.Unregister(areaIdBuffer);
                The.Alloc.Unregister(areaTypeBuffer);
                The.Alloc.Unregister(islandIdBuffer);
                The.Alloc.Unregister(cornerGapBuffer);
                uploadedGapVersion = -1;
                colorsR.Release();
                colorsG.Release();
                colorsB.Release();
                The.Alloc.Unregister(colorsR);
                The.Alloc.Unregister(colorsG);
                The.Alloc.Unregister(colorsB);
                areaTexture.Release();
                The.Alloc.Unregister(areaTexture);
            }

            overlays.Clear();
            CleanupCache();
        }

        private float[] colR;
        private float[] colG;
        private float[] colB;
        // if you change it here, change AreaVizualize shader too!
        private const int maxAreas = 128;

        public Color ColorForArea(int areaId) {
            if (areaId < 1) {
                return Consts.ColorWhite;
            } else if (areaId >= maxAreas) {
                areaId %= maxAreas;
            }
            return new Color(colR[areaId], colG[areaId], colB[areaId]);
        }

        private void PrepareCompute() {
            //ReleaseResources();

            if (!Consts.HasComputeShaders) { return; }
            if (areaVisualization == null) {
                areaVisualization = Resources.Load<ComputeShader>(
                        "Shaders/Compute/AreaVisualize");
            }
            areaVisualization.GetKernelThreadGroupSizes(0,
                out var x, out var y, out var _);
            groupsX = S.GridWidth / (int) x;
            groupsY = S.GridHeight / (int) y;

            colorsR = new ComputeBuffer(maxAreas, sizeof(float));
            colorsR.name = "AreaColorsR";
            colorsG = new ComputeBuffer(maxAreas, sizeof(float));
            colorsG.name = "AreaColorsG";
            colorsB = new ComputeBuffer(maxAreas, sizeof(float));
            colorsB.name = "AreaColorsB";

            The.Alloc.Register(colorsR);
            The.Alloc.Register(colorsG);
            The.Alloc.Register(colorsB);

            colR = new float[maxAreas];
            colG = new float[maxAreas];
            colB = new float[maxAreas];

            var rng = new Rng(Primes.Prime040);
            for (int i = 1; i < maxAreas; i++) {
                var c = rng.Color(50, 50);
                colR[i] = c.r;
                colG[i] = c.g;
                colB[i] = c.b;
            }

            colorsR.SetData(colR);
            colorsG.SetData(colG);
            colorsB.SetData(colB);

            areaIdBuffer = new ComputeBuffer(gridSize, sizeof(int));
            areaIdBuffer.name = "AreaIDBuffer";
            The.Alloc.Register(areaIdBuffer);
            areaTypeBuffer = new ComputeBuffer(gridSize, sizeof(AreaType));
            areaTypeBuffer.name = "AreaTypeBuffer";
            The.Alloc.Register(areaTypeBuffer);
            islandIdBuffer = new ComputeBuffer(gridSize, sizeof(int));
            islandIdBuffer.name = "IslandIdBuffer";
            The.Alloc.Register(islandIdBuffer);
            cornerGapBuffer = new ComputeBuffer(gridSize, sizeof(int));
            cornerGapBuffer.name = "CornerGapBuffer";
            The.Alloc.Register(cornerGapBuffer);
            // New buffer contents are not guaranteed to be zero
            cornerGapBuffer.SetData(new int[gridSize]);
            uploadedGapVersion = -1;

            areaTexture = new RenderTexture(S.GridWidth, S.GridHeight, 0,
                RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            areaTexture.enableRandomWrite = true;
            areaTexture.name = "AreaSysAreaTex";
            areaTexture.filterMode = FilterMode.Point;
            areaTexture.Create();
            The.Alloc.Register(areaTexture);
            areasOverlay = new OverlayInfo(OverlayType.Visual,
                OverlayId.Areas, IconId.WOverlayArea, ref areaTexture);
            overlays.Add(areasOverlay);
            UpdateAreasOverlayLights();

            overlays.Add(new OverlayInfo(OverlayType.Visual, OverlayId.Islands,
                IconId.WOverlayIsland, ref areaTexture));

            try {
                areaKernel = areaVisualization.FindKernel("CSAreaVisualize");
            } catch (System.Exception ex) {
                D.LogEx(ex, "Could not load AreaVisualize compute shader");
                return;
            }

            areaVisualization.SetInt(ShaderVars.Size,  gridSize);
            areaVisualization.SetInt(ShaderVars._GridHeight, S.GridHeight);

            areaVisualization.SetTexture(areaKernel, "tex", areaTexture);

            areaVisualization.SetBuffer(areaKernel, "colorsR", colorsR);
            areaVisualization.SetBuffer(areaKernel, "colorsG", colorsG);
            areaVisualization.SetBuffer(areaKernel, "colorsB", colorsB);

            areaVisualization.SetBuffer(areaKernel, "areaIds", areaIdBuffer);
            areaVisualization.SetBuffer(areaKernel, "areaTypes", areaTypeBuffer);
            areaVisualization.SetBuffer(areaKernel, "islandIds", islandIdBuffer);
            areaVisualization.SetBuffer(areaKernel, "cornerGaps", cornerGapBuffer);

            areaVisualization.SetVector("colLandZone", Consts.ColorLandingZone);
            areaVisualization.SetVector("colCornerGap", Consts.ColorCornerGap);
            areaVisualization.SetVector("colRoof", Consts.ColorRoof);

            areaVisualization.SetVector("colNoBuild", Consts.ColorNoBuildZone);
            areaVisualization.SetVector("colNoFly", Consts.ColorNoFlyZone);

            areaVisualization.SetInt("cornerMargin", Consts.CornerMargin);
            areaVisualization.SetInt("mapMargin", Consts.MapMargin);

            Shader.SetGlobalBuffer(ShaderVars._AreaIds, areaIdBuffer);
            floorKinds = new FloorKindBuffer(S);
            LightPass = new LightPassBuffer(S);
            Shape = new AreaShapeBuffer(S, this);
            Shape.Rebuild();
            Wear = new TileWearBuffer(S);
            Shader.SetGlobalVector(ShaderVars._TileInfoNoFXColor, Consts.ColorRoof);
            isComputePrepared = true;
            if (pendingAreaComputes != null) {
                foreach (var pair in pendingAreaComputes) {
                    //D.Warn("Setting area buffer on compute: {0}", pair.Item1);
                    pair.Item1.SetBuffer(pair.Item2, ShaderVars._AreaIds, areaIdBuffer);
                }
                pendingAreaComputes = null;
            }
        }
        // "Display Outer Hull" starts on: the hull view shows the rooms and what is open to
        // space better than the overlay's colours
        private bool isRoofMode = true;
        private OverlayInfo areasOverlay;

        public void SetRoofMode(bool yes) {
            isRoofMode = yes;
            UpdateAreasOverlayLights();
            RedrawAreaTexture();
        }

        // "Area exposed to space" hovered in the Areas overlay list: in roof mode its stripes
        // show only then
        public bool IsSpaceHighlighted { get; private set; }

        public void SetSpaceHighlighted(bool yes) {
            if (IsSpaceHighlighted == yes) { return; }
            IsSpaceHighlighted = yes;
            UpdateAreasOverlayLights();
            RedrawAreaTexture();
        }

        // In roof mode the overlay draws only corner gaps over the hull view, so the lighting
        // stays; the exposed area stripes need the flat overlay look again
        private void UpdateAreasOverlayLights() {
            if (areasOverlay == null) { return; }
            areasOverlay.KeepsLights = isRoofMode && !IsSpaceHighlighted;
        }

        public string GetLandingZoneTip() {
            return ShowLandingZoneTip();
        }

        // The room hovered in the Areas overlay list (0 for none); the hull view hides its roof
        public int HighlightedAreaId { get; private set; }

        public void SetHighlightedAreaId(int id) {
            HighlightedAreaId = id;
            if (!Consts.HasComputeShaders) { return; }
            areaVisualization.SetInt(ShaderVars._HighlightedAreaId, id);
            RedrawAreaTexture();
        }

        public void TrySellOrAbandonSection(HashSet<int> isle) {
            if (S.Sys.Combat.IsHostileCombatInProgress) {
                S.Sig.ShowErrorMessage.Send($"{T.OperationForbidden}: {T.CombatOngoing}");
                return;
            }
            foreach (var p in isle) {
                var tile = EntityUtils.ParentObjAt(p);
                if (tile == null) { continue; }
                if (tile.TryGetComponent<BreachCapsuleComp>(out var bc)) {
                    if (!bc.IsOpened) {
                        S.Sig.ShowErrorMessage.Send($"{T.OperationForbidden}: {tile.NameLink}");
                        return;
                    }
                } else if (tile.TryGetComponent<StasisPodComp>(out var sp)) {
                    if (!sp.IsOpened) {
                        S.Sig.ShowErrorMessage.Send($"{T.OperationForbidden}: {tile.NameLink}");
                        return;
                    }
                }
            }
            var port = S.Sys.Ship.FirstReachableNearby(SpaceObjectTypeIdH.Port);
            if (port != null) {
                InitiateSectionSale(isle, port);
            } else {
                if (isle.Count > 20) {
                    var qc = QuickChoice.Create();
                    qc.AddButton(T.Confirm, () => TryAbandonSection(isle));
                    qc.Show();
                } else {
                    TryAbandonSection(isle);
                }
            }
        }
        private void RedrawAreaTexture() {
            if (!Consts.HasComputeShaders) { return; }
            areaTypeBuffer.SetData(AreaTypes);
            var gapFlags = CornerGaps.Flags;
            if (CornerGaps.Version != uploadedGapVersion) {
                uploadedGapVersion = CornerGaps.Version;
                cornerGapBuffer.SetData(gapFlags);
            }

            // Set elsewhere always, for shaders
            //areaIdBuffer.SetData(areaIds);
            areaVisualization.SetBool("isAreaMode", true);
            // "Display Outer Hull" shows the hull view (HullView). This overlay draws above it,
            // so in roof mode it only marks corner gaps
            areaVisualization.SetBool("isRoofMode", isRoofMode);
            areaVisualization.SetBool("highlightSpace", IsSpaceHighlighted);
            areaVisualization.SetBool("showMargin", false);
            areaVisualization.Dispatch(areaKernel, groupsX, groupsY, 1);

        }
        private void RedrawIslandTexture() {
            if (!Consts.HasComputeShaders) { return; }

            islandIdBuffer.SetData(islandIds);
            areaVisualization.SetBool("isAreaMode", false);
            areaVisualization.SetBool("isRoofMode", false);
            areaVisualization.SetBool("highlightSpace", false);
            areaVisualization.SetBool("showMargin", true);

            areaVisualization.Dispatch(areaKernel, groupsX, groupsY, 1);

        }
        private void OnEntityChanged(Entity entity, bool added) {
            if (entity is not Tile tile) { return; }
            if (added && !tile.IsConstructed) { return; }
            if (entity == null || entity.Definition == null) {
                // child
                return;
            }
            var layer = entity.Definition.LayerId;
            if (layer <= WorldLayer.Objects) {
                var p = entity.PosIdx;
                if (tile.Transform.IsMulti) {
                    var posCache = Caches.IntArray128;
                    int cnt = tile.Transform.GetSubtilePos(posCache);
                    for (int i = 0; i < cnt; i++) {
                        var pos = posCache[i];
                        var actualAreaType = ResolveAreaTypeAt(pos);
                        CheckSetAreaType(pos, actualAreaType);
                    }
                } else {
                    var actualAreaType = ResolveAreaTypeAt(p);
                    CheckSetAreaType(p, actualAreaType);
                }
                // Track window tiles for the per-room window count cache.
                // This also covers wall→window replacements where AreaType stays Wall
                // and no area recalc fires, so we must trigger RebuildWindowsByArea.
                if (tile.TryGetComponent<AdmirableComp>(out var adm) && adm.IsWindow) {
                    if (added) {
                        windowPositions.Add(p);
                    } else {
                        windowPositions.Remove(p);
                    }
                    needsWindowRecalc = true;
                }
            }
        }

        private void CheckSetAreaType(int p, AreaType actualAreaType) {
            if (AreaTypes[p] != actualAreaType) {
                var oldType = AreaTypes[p];
                SetAreaType(p, actualAreaType);
                // D.Warn("Needs recalc on area type {0}", entity);
                pendingChanges.Add(new PendingAreaChange(
                    p, oldType, actualAreaType
                ));
            }
        }

        private void OnEntityAdded(Entity entity) {
            OnEntityChanged(entity, true);
        }

        private void OnEntityRemoved(Entity entity) {
            OnEntityChanged(entity, false);
        }

        private void InitiateSectionSale(HashSet<int> positions, SpaceObject port) {
            double estimatedValue = 0f;
            var dip = S.Sys.Diplomacy;
            var eco = S.Sys.Economy;
            var hasOverride = A.HasAsimovOverride;
            int cpuCount = 0;

            // First check only objects, as it will be easier to short circuit
            foreach (var p in positions) {
                var obj = EntityUtils.ParentObjAt(p);
                if (obj != null) {
                    if (!hasOverride && obj.TryGetComponent<StasisArrayComp>(out var stasis)) {
                        if (stasis.BeingCount > 0) {
                            The.Dialog.ShowSimple(IconId.CComputing, T.AsimovViolation1);
                            return;
                        }
                    }
                    estimatedValue = AppendValueOf(estimatedValue, eco, obj);
                    if (obj.TryGetComponent<ShipComputerComp>(out var cpu)) {
                        cpuCount++;
                    }
                }
            }

            var totalCPUs = S.Components.CountOfConstructed<ShipComputerComp>();
            if (!hasOverride) {
                if (cpuCount > 0) {
                    The.Dialog.ShowSimple(IconId.CComputing, T.AsimovViolation3);
                    return;
                }
            } else {
                if (cpuCount >= totalCPUs) {
                    The.Dialog.ShowSimple(IconId.CComputing, T.OperationForbidden);
                    return;
                }
            }

            // if we're still here, let's add value of floors and walls too
            foreach (var p in positions) {
                estimatedValue = AppendValueOf(estimatedValue, eco, EntityUtils.FloorAt(p));
                estimatedValue = AppendValueOf(estimatedValue, eco, EntityUtils.WallAt(p));
            }


            var objs = S.Query.GetAllObjs.Ask();
            foreach (var obj in objs.Values) {
                if (obj.CanSell && positions.Contains(obj.PosIdx(S, out _))) {
                    estimatedValue += eco.WorthOf(obj.Def, out _, true);
                }
            }
            long clockPausedAt = S.Clock.Ticks;
            S.Clock.Pause();
            var standing = dip.RelationshipWith(port.Faction);
            estimatedValue *= Mathf.Clamp(0.3f + standing, 0.05f, 2f);
            WPopup.Create(false, IconId.CStarcreds, T.SellShipSection,
                T.ShipyardSectionOffer_V2.F(port.StarmapLink, Wallet.ToValueString(
                    estimatedValue, true)))
                .WithAction(T.Sell, () => {
                    // Allow some time to pass for no-pause mode
                    if (S.Clock.Ticks > clockPausedAt + Consts.TicksPer5Minutes) {
                        S.Sig.ShowErrorMessage.Send($"{T.OperationAborted}: {T.TimeSensitiveOperation}");
                        return;
                    }
                    S.Clock.ForceStop(ForceStopReason.BulkOperation);
                    S.CmdQ.Enqueue(new CmdAbandonSection(positions, false, () => {
                        S.Wallet.Add(estimatedValue);
                    }, null));
                })
                .WithAction2(T.Cancel, null)
                .Spawn();

        }

        private static double AppendValueOf(double estimatedValue, EconomySys eco, Tile tile) {
            if (tile == null) { return estimatedValue; }
            double tileValue = 25f;
            foreach (var mat in tile.Constructable.RawMaterials) {
                tileValue = eco.WorthOf(mat, true) * 2f;
            }
            // D.Warn("Tile value: {0} -> {1}", tile, tileValue);
            return estimatedValue + tileValue;
        }

        // The ship is the only island, or the island of the main ship computer. Never
        // Islands.First(): islands are numbered from the left edge of the map, so a derelict
        // parked in a left corner comes first.
        public bool TryGetShipIsland(out int islandId) {
            if (IslandCount == 1) {
                foreach (var id in Islands.Keys) {
                    islandId = id;
                    return true;
                }
            }
            var cpu = ShipComputerComp.FindMain(S);
            islandId = cpu == null ? 0 : IslandAt(cpu.Tile.PosIdx);
            return islandId > 0 && Islands.ContainsKey(islandId);
        }

        /// <summary>
        /// Fills `into` with the ids of islands that would be cut loose (abandoned) right now -
        /// any island touching the map margin, unless it is the only island there is (i.e. the
        /// whole ship). Shared by HasDerelicts (warning text) and TryAbandonDerelicts (actually
        /// abandoning them), so both agree on what counts as derelict.
        /// </summary>
        public void CollectDerelictIslands(List<int> into, out bool isShipWithinMargin) {
            into.Clear();
            if (IslandCount == 0) {
                isShipWithinMargin = true;
                return;
            }

            int numIslandsOutsideMargin = 0;
            int numIslandsWithinMargin = 0;
            foreach (var isle in S.Sys.Areas.Islands) {
                bool isTouching = false;
                foreach (var p in isle.Value) {
                    if (!EntityUtils.IsWithinMargin(p, false, false)) {
                        isTouching = true;
                        break;
                    }
                }
                if (isTouching) {
                    into.Add(isle.Key);
                    numIslandsOutsideMargin++;
                } else {
                    numIslandsWithinMargin++;
                }
            }

            isShipWithinMargin = numIslandsWithinMargin > 0;
            if (numIslandsOutsideMargin == 0) {
                into.Clear();
                return;
            }
            if (!isShipWithinMargin && numIslandsWithinMargin == 0 && numIslandsOutsideMargin == 1) {
                into.Clear();
            }
        }

        private readonly List<Tile> orphanDevices = new();

        public bool HasDerelicts(out string abandonWarning) {
            CollectDerelictIslands(Caches.IntList, out _);
            if (Caches.IntList.Count == 0) {
                abandonWarning = null;
                return false;
            }
            var sb = Caches.StringBuilder3;
            sb.Clear();
            sb.AppendFormat("{0}:", T.Sections);
            foreach (var isle in S.Sys.Areas.Islands.OrderByDescending(s => s.Value.Count)) {
                if (!Caches.IntList.Contains(isle.Key)) { continue; }
                var rekt = Pos.Encapsulate(isle.Value);
                sb.AppendFormat("\n {0} {1} ({2}: {3})",
                    GlyphMap.ArrowRight,
                        Texts.LinkToArea($"{T.Section} #{isle.Key}", rekt),
                        T.Size, isle.Value.Count);
            }
            abandonWarning = sb.ToString();
            return !string.IsNullOrEmpty(abandonWarning);
        }

        public bool TryAbandonDerelicts(out bool isShipWithinMargin) {
            return TryAbandonDerelicts(out isShipWithinMargin, false);
        }

        /// <param name="leavingSector">a jump out of the sector also takes the devices left
        /// floating without a section (OrphanSpaceDevices). Not on in-sector flight, which asks
        /// every tick while the engines push, and the scan reads the whole object grid.</param>
        public bool TryAbandonDerelicts(out bool isShipWithinMargin, bool leavingSector) {
            CollectDerelictIslands(Caches.IntList, out isShipWithinMargin);
            orphanDevices.Clear();
            if (leavingSector) {
                OrphanSpaceDevices.Collect(S, orphanDevices);
            }
            if (Caches.IntList.Count == 0 && orphanDevices.Count == 0) {
                return true;
            }

            // Validate every section before enqueueing any: at Unlimited speed Enqueue runs the
            // command right away, and its area rebuild replaces Islands (and may reuse the
            // shared caches)
            var cmds = new List<CmdAbandonSection>(Caches.IntList.Count);
            foreach (var islandId in Caches.IntList) {
                var cmd = new CmdAbandonSection(Islands[islandId], false, null, () => {
                    S.Sys.Ship.ForceStop();
                    if (A.IsInHyperspace) {
                        S.Sys.ShipNav.ExitHyperspace(T.ShipNotInOnePiece);
                    }
                    UISounds.PlayActionDenied();
                });
                cmd.WithState(S);
                if (!cmd.ValidateAsimovLaws()) {
                    return false;
                }
                cmds.Add(cmd);
            }
            if (orphanDevices.Count > 0) {
                var orphanCmd = new CmdAbandonSection(OrphanSpaceDevices.CellsOf(orphanDevices),
                    false, null, null);
                orphanCmd.WithState(S);
                cmds.Add(orphanCmd);
            }
            foreach (var cmd in cmds) {
                S.Clock.ForceStop(ForceStopReason.BulkOperation);
                S.CmdQ.Enqueue(cmd);
            }
            return true;
        }

        private void TryAbandonSection(HashSet<int> island) {
            S.Clock.ForceStop(ForceStopReason.BulkOperation);
            S.CmdQ.Enqueue(new CmdAbandonSection(island, false));
        }

        public string GetName() {
            return Id;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsEnclosed(int idx) {
            return areaIds[idx] > 0;
        }
        public bool IsAdjacentToEnclosed(int p) {
            var n = Pos.N(p);
            var e = Pos.E(p);
            var s = Pos.S(p);
            var w = Pos.W(p);
            var aN = areaIds[n];
            var aS = areaIds[s];
            var aE = areaIds[e];
            var aW = areaIds[w];
            // Adjacent to a room
            if (aN > 0 || aS > 0 || aE > 0 || aW > 0) {
                return true;
            }
            // If it's < -2, we are adjacent to at least 3 walls
            return aN + aS + aE + aW < -2;
        }

        // Floor connectivity only: blind to doors a being may not open and to air it can't
        // breathe. A being picking a target for itself asks BeingReach (BeingReach.md)
        public bool CanReachGroundedIslandToIsland(int islandId, int i2) {
            if (islandId == 0 || i2 == 0) { return false; }
            return path.AreIslandsLinked(islandId, i2);
        }
        public bool CanReachGroundedIslandToPos(int islandId, int pos2) {
            if (islandId == 0) { return false; }
            if (pos2 < 0) {
                D.Err("Can reach grounded island to pos check OOB! {0}", pos2);
                return false;
            }
            var i2 = islandIds[pos2];
            return path.AreIslandsLinked(islandId, i2);
        }
        public bool CanReachGrounded(int pos, int pos2) {
            if (pos < 0 || pos2 < 0) {
                D.Err("Can reach grounded check OOB! {0} -> {1}", pos, pos2);
                return false;
            }
            var i1 = islandIds[pos];
            var i2 = islandIds[pos2];
            return path.AreIslandsLinked(i1, i2);
        }

        public bool IsSameAreaIndoors(int posA, int posB) {
            var a1 = areaIds[posA];
            var a2 = areaIds[posB];
            if (a1 < 1 || a2 < 1) { return false; }
            return a1 == a2;
        }
        public bool IsSameArea(int posA, int posB) {
            var a1 = areaIds[posA];
            var a2 = areaIds[posB];
            return a1 == a2;
        }
        public bool IsSameAreaOrOutside(int posA, int posB) {
            var a1 = areaIds[posA];
            var a2 = areaIds[posB];
            if (a1 == a2) { return true; }
            if (a1 > 0 || a2 > 0) { return false; }
            // space to unenclosed
            if (a1 == 0) {
                if (a2 == 0) { return true; }
                return a2 < -1;
            } else if (a2 == 0) {
                return a1 < -1;
            }
            return a1 < -1 && a2 < -1;
        }

        public int IslandSize(int island) {
            if (island == 0) { return 0; }
            return Islands[island].Count;
        }
        public void NeedsRecalc(int layer) {
            // D.Warn("Needs recalc for layer");
            needsFullRecalc |= layer != WorldLayer.Objects;
            #if !PRODUCTION_BUILD
            if (layer > WorldLayer.Objects) {
                D.Err("Unexpected layer: {0}", layer);
            }
            #endif
        }

        public void NeedsRecalc(int layer, int pos) {
            #if !PRODUCTION_BUILD
            if (layer > WorldLayer.Objects) {
                D.Err("Unexpected layer: {0}", layer);
            }
            #endif
            if (layer > WorldLayer.Objects) { return; }
            var actualAreaType = ResolveAreaTypeAt(pos);
            if (AreaTypes[pos] != actualAreaType) {
                var oldType = AreaTypes[pos];
                SetAreaType(pos, actualAreaType);
                pendingChanges.Add(new PendingAreaChange(
                    pos, oldType, actualAreaType
                ));
            }
        }

        public bool IsSameIsland(int p1, int p2) {
            return islandIds[p1] == islandIds[p2];
        }

        public bool IsAreaReachableAt(int posIdx) {
            return IsAreaReachable(At(posIdx));
        }

        public bool IsAreaReachable(int areaId) {
            if (areaId <= 0) { return true; }
            return !DoorCheck.IsUnreachable(areaId);
        }

        public bool IsInIsland(int islandId, int posIdx) {
            var islId = IslandAt(posIdx);
            return islandId == islId;
        }
        public bool CheckReachable(int p1, int p2) {
            if (!HasUnreachableAreas) { return true; }
            var a1 = At(p1);
            var a2 = At(p2);
            // within the same area
            if (a1 == a2) { return true; }
            if (a1 == -1 || a2 == -1) {
                // we are either inside a wall or trying to reach a wall
                // we don't want to check reachability
                return true;
            }
            // if neither is unreachable or both are unreachable, we are not restricted
            var isToReachable = IsAreaReachable(a1);
            var isFromReachable = IsAreaReachable(a2);
            if (isToReachable != isFromReachable) {
                return false;
            }
            return true;
        }


        public bool IsInOrNearIsland(int islandId, int posIdx) {
            if (!EntityUtils.IsWithinBounds(posIdx)) { return islandId == 0; }
            // each neighbour is compared with the island asked about; overwriting the
            // argument compared neighbours with this tile's island and was almost always true
            if (IslandAt(posIdx) == islandId) { return true; }
            if (IslandAt(Pos.N(posIdx)) == islandId) { return true; }
            if (IslandAt(Pos.S(posIdx)) == islandId) { return true; }
            if (IslandAt(Pos.E(posIdx)) == islandId) { return true; }
            return IslandAt(Pos.W(posIdx)) == islandId;
        }
        private bool hasCachedShipBounds;

        private PosRect shipBounds;

        public PosRect ShipBounds {
            get {
                if (hasCachedShipBounds) { return shipBounds; }
                var minX = int.MaxValue;
                var maxX = int.MinValue;
                var minY = int.MaxValue;
                var maxY = int.MinValue;
                var ic = Islands.Count;
                if (ic == 0) {
                    shipBounds = PosRect.Invalid;
                    hasCachedShipBounds = true;
                    return shipBounds;
                }
                FindShipBounds(ref minX, ref maxX, ref minY, ref maxY,
                    isFallback: false);

                if (minX == int.MaxValue || minY == int.MaxValue) {
                    FindShipBounds(ref minX, ref maxX, ref minY, ref maxY,
                        isFallback: true);
                    if (minX == int.MaxValue || minY == int.MaxValue) {
                        D.Err("Trying to get ship bounds when none exist");
                        shipBounds = PosRect.Invalid;
                    }
                }

                shipBounds = new PosRect(minX, minY, maxX, maxY);
                hasCachedShipBounds = true;
                return shipBounds;
            }
        }

        private void FindShipBounds(ref int minX, ref int maxX, ref int minY,
                ref int maxY, bool isFallback) {
            foreach (var isle in Islands) {
                bool checkedMargin = false;
                foreach (var i in isle.Value) {
                    if (!checkedMargin) {
                        checkedMargin = true;
                        if (!EntityUtils.IsWithinMargin(i, false, false)) {
                            if (isFallback) {
                                var isWM = EntityUtils.IsWithinMargin(i, false, false);
                                D.Warn("Would skip island due to pos out of margin: {0}",
                                    Pos.String(i));
                            } else {
                                break;
                            }
                        }
                    }
                    Pos.ToXY(i, out var x, out var y);
                    if (x < minX) { minX = x; } else if (x > maxX) { maxX = x; }
                    if (y > maxY) { maxY = y; } else if (y < minY) { minY = y; }
                }
            }
        }

        /// <summary>
        /// Like IsOutside, but will return false if in a doorway or walls
        /// </summary>
        public bool IsOutdoors(int idx) {
            #if !PRODUCTION_BUILD
            if (idx < 0 || idx >= gridSize) {
                D.Err("Change this to IsOutdoorsSafe! {0} | {1} | gridSize: {2}",
                    Ready.ShortStatus, Pos.StringD(idx), gridSize);
                return true;
            }
            #endif
            var areaId = areaIds[idx];
            return areaId == 0 || areaId < -1;
        }
        /// <summary>
        /// Includes doors and walls as outside. Use IsOutdoors if walls / doors should be considered inside
        /// </summary>
        public bool IsOutside(int idx) {
            #if !PRODUCTION_BUILD
            if (idx < 0 || idx >= gridSize) {
                D.Err("Change this to IsOutsideSafe! {0}", Ready.ShortStatus);
                return true;
            }
            #endif
            return areaIds[idx] <= 0;
        }
        public bool IsSpace(int idx) {
            #if !PRODUCTION_BUILD
            if (idx < 0 || idx >= gridSize) {
                D.Err("Change this to IsSpaceSafe! {0}", Ready.ShortStatus);
                return true;
            }
            #endif
            if (EntityUtils.IsWallAt(idx)) { return false; }
            return areaIds[idx] == 0;
        }
        public bool IsSpaceSafe(int idx) {
            if (idx < 0 || idx >= gridSize) { return true; }
            if (EntityUtils.IsWallAt(idx)) { return false; }
            return areaIds[idx] == 0;
        }
        public HashSet<int> AreaAt(int posIdx, out int areaId) {
            #if !PRODUCTION_BUILD
            if (posIdx < 0 || posIdx >= gridSize) {
                D.Err("Need to add AreaAtSafe! {0}", Ready.ShortStatus);
                areaId = 0;
                return EmptySet;
            }
            #endif
            areaId = areaIds[posIdx];
            if (areaId == 0) { return EmptySet; }
            if (Areas.TryGetValue(areaId, out var area)) {
                return area;
            }
            return EmptySet;
        }
        /// <returns>Either inside or outside area</returns>
        public HashSet<int> AnyAreaAt(int posIdx) {
            var areaId = areaIds[posIdx];
            if (areaId > 0) {
                if (Areas.TryGetValue(areaId, out var area)) {
                    return area;
                }
            } else if (areaId < 0) {
                if (OuterAreas.TryGetValue(areaId, out var area)) {
                    return area;
                }
            }
            return EmptySet;
        }
        /// <returns>Inside areas only</returns>
        public HashSet<int> AreaAt(int posIdx) {
            #if !PRODUCTION_BUILD
            if (posIdx < 0 || posIdx >= gridSize) {
                D.Err("Need to add AreaAtSafe! {0}", Ready.ShortStatus);
                return EmptySet;
            }
            #endif
            var areaId = areaIds[posIdx];
            if (areaId < 1) { return EmptySet; }
            if (Areas.TryGetValue(areaId, out var area)) {
                return area;
            }
            return EmptySet;
        }

        public int GenRandomSolidPos(Rng rng) {
            return rng.From(SolidArea);
        }
        public int GenRandomSolidPosWithin(Rng rng, PosRect area) {
            Caches.IntList.Clear();
            var c = solidArea.Count;
            for (int i1 = 0; i1 < c; i1++) {
                int i = solidArea[i1];
                if (area.Contains(i)) {
                    Caches.IntList.Add(i);
                }
            }
            return rng.From(Caches.IntList);
        }

        // Seeds windowPositions from the full admirable component list. Called once at load,
        // and again after a ship winch/rotation (see OnShipMoved/OnShipRotated) since those
        // relocate tiles via direct grid writes that bypass EntityAdded/EntityRemoved, the
        // events the O(1) incremental update in OnEntityChanged relies on.
        private void SeedWindowPositions() {
            windowPositions.Clear();
            var admirables = S.Components.AllByType<AdmirableComp>();
            if (Arrays.IsEmpty(admirables)) { return; }
            foreach (AdmirableComp adm in admirables) {
                if (!adm.IsWindow) { continue; }
                if (adm.Tile?.IsConstructed != true) { continue; }
                if (adm.Tile.Transform.IsChild) { continue; }
                windowPositions.Add(adm.Entity.PosIdx);
            }
        }

        // Rebuilds the per-room window count from the sparse windowPositions set.
        // O(numWindows) — typically <30 positions, negligible vs the flood-fill loop.
        private void RebuildWindowsByArea() {
            windowsByArea.Clear();
            foreach (var pos in windowPositions) {
                var areaId = areaIds[pos];
                if (areaId > 0) {
                    // Glass floor inside an enclosed room.
                    windowsByArea.TryGetValue(areaId, out var cur);
                    windowsByArea[areaId] = cur + 1;
                } else if (areaId == -1) {
                    // Window wall: count once toward each distinct adjacent enclosed room.
                    IncrementWindowForNeighborAreas(pos);
                }
                // areaId == 0 or < -1 (open space / outer area): ignore.
            }
        }

        #if !PRODUCTION_BUILD
        /// <summary>
        /// Used for development / tests only! Expensive.
        /// </summary>
        /// <returns></returns>
        public int WindowsByAreaUncached(int areaId) {
            if (areaId <= 0) { return 0; }
            var admirables = S.Components.AllByType<AdmirableComp>();
            if (Arrays.IsEmpty(admirables)) { return 0; }
            D.FIXME(2027, 7, 15, "Remove this if it never happened");
            var countedPositions = Caches.IntSet2;
            countedPositions.Clear();
            int count = 0;
            foreach (AdmirableComp adm in admirables) {
                if (!adm.IsWindow) { continue; }
                var tile = adm.Tile;
                if (tile?.IsConstructed != true || tile.Transform.IsChild) { continue; }
                var pos = adm.Entity.PosIdx;
                var posAreaId = areaIds[pos];
                bool touchesArea = posAreaId == areaId;
                if (!touchesArea && posAreaId == -1) {
                    var n = Pos.N(pos);
                    touchesArea = !Pos.IsOOB(n) && areaIds[n] == areaId;
                    if (!touchesArea) {
                        n = Pos.S(pos);
                        touchesArea = !Pos.IsOOB(n) && areaIds[n] == areaId;
                    }
                    if (!touchesArea) {
                        n = Pos.E(pos);
                        touchesArea = !Pos.IsOOB(n) && areaIds[n] == areaId;
                    }
                    if (!touchesArea) {
                        n = Pos.W(pos);
                        touchesArea = !Pos.IsOOB(n) && areaIds[n] == areaId;
                    }
                }
                if (!touchesArea) { continue; }
                if (countedPositions.Add(pos)) {
                    count++;
                }
            }
            return count;
        }
        #endif

        // Increments windowsByArea for each distinct enclosed room adjacent to a window wall.
        // Cardinal-only by design: a corner window's room only touches it diagonally and
        // isn't actually visible through it, so corner tiles intentionally don't count.
        private void IncrementWindowForNeighborAreas(int pos) {
            int a0 = 0, a1 = 0;
            int n;
            n = Pos.N(pos);
            if (!Pos.IsOOB(n)) { TryAddWindowNeighborArea(areaIds[n], ref a0, ref a1); }
            n = Pos.S(pos);
            if (!Pos.IsOOB(n)) { TryAddWindowNeighborArea(areaIds[n], ref a0, ref a1); }
            n = Pos.E(pos);
            if (!Pos.IsOOB(n)) { TryAddWindowNeighborArea(areaIds[n], ref a0, ref a1); }
            n = Pos.W(pos);
            if (!Pos.IsOOB(n)) { TryAddWindowNeighborArea(areaIds[n], ref a0, ref a1); }
        }

        // Increments the window count for areaId if it's an enclosed room not yet seen for
        // this window position. Tracks up to 2 distinct neighbor rooms (walls touch at most 2).
        private void TryAddWindowNeighborArea(int areaId, ref int seen0, ref int seen1) {
            if (areaId <= 0) { return; }
            if (areaId == seen0 || areaId == seen1) { return; }
            if (seen0 == 0) { seen0 = areaId; }
            else { seen1 = areaId; }
            windowsByArea.TryGetValue(areaId, out var cur);
            windowsByArea[areaId] = cur + 1;
        }

        // Query responder: returns the number of windows touching the given enclosed room.
        private int NumWindowsInArea(int areaId) {
            if (areaId <= 0) { return 0; }
            return windowsByArea.TryGetValue(areaId, out var n) ? n : 0;
        }

        public void SetAreaType(int posIdx, AreaType type) {
            if (isRecalcRunning) {
                #if VERBOSE_AREAS
                D.Warn("Completing FF");
                #endif
                ffJob.Complete();
                AfterFlood();
            }
            SetAreaTypeUnchecked(posIdx, type);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetAreaTypeUnchecked(int pos, AreaType type) {
            #if VERBOSE_AREAS
            if (AreaTypes[pos] != type) {
                D.Log("Area type: {0} -> {1} -> {2}", Pos.StringD(pos), AreaTypes[pos], type);
            }
            #endif
            #if !PRODUCTION_BUILD
            // debug
            if (type == AreaType.Wall && !EntityUtils.IsConstructedWallAt(pos)) {
                D.Err("trying to set a wall area type when wall is not constructed! {0}", Pos.String(pos));
                EntityUtils.DebugPauseFocusOn(EntityUtils.WallAt(pos));
            } else if (type == AreaType.Floor && !EntityUtils.IsConstructedFloorAt(pos)) {
                D.Err("trying to set a floor area type when floor is not constructed! {0}", Pos.String(pos));
                EntityUtils.DebugPauseFocusOn(EntityUtils.FloorAt(pos));
            } else if (type == AreaType.Door && !EntityUtils.IsConstructedWallAt(pos)) {
                D.Err("trying to set a door area type when door is not constructed! {0}", Pos.String(pos));
                EntityUtils.DebugPauseFocusOn(EntityUtils.WallAt(pos));
            }
            #endif
            AreaTypes[pos] = type;
        }

        public List<int> SolidArea => solidArea;
    }
}
