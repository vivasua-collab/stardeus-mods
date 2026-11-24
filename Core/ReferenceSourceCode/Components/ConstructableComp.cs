using System.Collections.Generic;
using System.Text;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.Hints;
using Game.Systems.Inventory;
using Game.Systems.Mental;
using Game.Systems.Path;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Nanobots;
using Game.Input;
using Game.Systems.UIUX;
using Game.Systems.Stats;
using System.Linq;
using System;

namespace Game.Components {
    public sealed class ConstructableComp : BaseComponent<ConstructableComp>,
            IHullIntegrityProvider, IUICustomTooltipProvider, IMatRequester,
            IConstructable, INanobotCompatible, IStatProvider,
            IAIGoalPriorityListener, IEntityPanelGoalProvider {
        public bool DoInstantBuild;
        private bool isInstantBuild => DoInstantBuild || A.InstantBuild;
        public bool IsAllWorkDone => WorkDone >= WorkAmount;

        // True when this unfinished tile's task is available for automatic assignment
        // (not forbidden and its availability gate is open). The renderer dims blueprints
        // for which this is false: construction-wave / support-floor gated, or priority 0.
        public bool IsTaskAutoSeeking {
            get {
                if (!TryGetGoal(out var goal)) { return true; }
                if (goal.GoalPriority == 0) { return false; }
                // A courier's hold is not a wait the player should see: the blueprint keeps
                // the look its own rule gives it, and keeps it after the hold ends
                if (IsHeldByCourier) {
                    return goal.IsForceAssigned || IsBuildWorkAvailable(S, goal);
                }
                return goal.IsAvailabilityGateOpen(S);
            }
        }

        // A hauler supplying this blueprint on the same trip as its own task
        // (ConstructionDeliveryBatch). Never saved: holds end with the trip, and a trip
        // resumed after a load forms its batch again.
        private AIAgentComp courier;
        private AIGoal courierGoal;

        // Checked on every read, so a courier that ended without letting go can never
        // strand this blueprint's hauling task
        public bool IsHeldByCourier {
            get {
                if (courier == null) { return false; }
                if (courier.Goal == courierGoal
                        && courierGoal.State == AIGoalState.InProgress
                        && courier.Being.IsActive) {
                    return true;
                }
                courier = null;
                courierGoal = null;
                return false;
            }
        }

        public bool IsHeldBy(AIAgentComp agent) {
            return agent != null && IsHeldByCourier && courier == agent;
        }

        public bool TryHoldForCourier(AIAgentComp agent, AIGoal agentGoal) {
            if (agent == null || agentGoal == null || IsHeldByCourier
                    || !TryGetGoal(out var goal) || goal.JobIdH != JobTypeIdH.Hauling) {
                return false;
            }
            courier = agent;
            courierGoal = agentGoal;
            // Out of the auction and out of the doability recheck of Bidding goals, which
            // would report the material the courier is carrying here as lacking
            if (goal.State == AIGoalState.Bidding) {
                goal.ChangeStateTo(AIGoalState.Available, G.Ticks);
            }
            return true;
        }

        public void ReleaseCourierHold(AIAgentComp agent) {
            if (courier == agent) {
                courier = null;
                courierGoal = null;
            }
        }

        public bool FlyingOnly;
        public Tile ConstructableTile => Tile;
        public bool CanDragRow;
        public bool IsFloorWaveSeed { get; private set; } = true;
        public int FloorWaveId { get; private set; }
        public bool IsFloorDeconstructWaveSeed { get; private set; } = true;
        public int FloorDeconstructWaveId { get; private set; }
        // public for replacement component to be able to transfer the builder
        public AIAgentComp ConstructedBy { get; set;  }

        public PlacementConstraint Placement;
        public string PlacementGroup;

        public static bool IsPlacementInSpace(PlacementConstraint pc) {
            return pc == PlacementConstraint.InSpace
                || pc == PlacementConstraint.InSpaceAttached
                || pc == PlacementConstraint.InSpaceAttachedChained;
        }

        public bool IsBuiltInSpace => IsPlacementInSpace(Placement);
        private MatDeficitEventHolder deficitEv;

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new ConstructableComp());
        }

        private MatRequest cachedMatRequest;
        public MatRequest NextMissingMat {
            get {
                if (IsFinished || IsBrokenDown) { return null; }
                if (rawMaterials == null) {
                    if (Tile.IsActive && !Tile.Health.IsDestroyed) {
                        D.Err("rawMaterials is null for an unfinished tile! {0}. {1}",
                            Tile, Entity.Goal);
                    }
                    return null;
                }
                for (int i = 0; i < rawMaterials.Length; i++) {
                    Mat mat = rawMaterials[i];
                    var diff = mat.Diff;
                    if (diff > 0) {
                        if (cachedMatRequest == null) {
                            cachedMatRequest = new MatRequest();
                            cachedMatRequest.Requester = this;
                            // A blueprint accepts short loads: when stock is scarce the
                            // hauling goal is allowed to run anyway (see MatClaims), and
                            // a worker who could only gather part of it must deliver
                            // what it has instead of dropping the whole trip.
                            cachedMatRequest.IsAmountOptional = true;
                        }
                        cachedMatRequest.Type = mat.Type;
                        cachedMatRequest.Amount = diff;
                        return cachedMatRequest;
                    }
                }
                return null;
            }
        }

        private List<MatRequest> missingMats;
        public List<MatRequest> MissingMats {
            get {
                if (rawMaterials == null) { return null; }
                if (Arrays.IsEmpty(missingMats)) {
                    if (missingMats == null) {
                        missingMats = new();
                    }
                    for (int i = 0; i < rawMaterials.Length; i++) {
                        Mat mat = rawMaterials[i];
                        var diff = mat.Diff;
                        if (diff > 0) {
                            missingMats.Add(new() {
                                Type = mat.Type,
                                Amount = diff,
                                Requester = this,
                                IsAmountOptional = true,
                            });
                        }
                    }
                }
                return missingMats;
            }
        }

        public void ReplaceMaterialsWith(ConstructableComp constructable) {
            rawMaterials = Arrays.Copy(constructable.rawMaterials);
            missingMats = null;
            if (IsFinished) {
                for (int i = 0; i < rawMaterials.Length; i++) {
                    var rm = rawMaterials[i];
                    rm.StackSize = rm.MaxStackSize;
                    rawMaterials[i] = rm;
                }
            }
        }

        public override void OnRemove() {
            FloorConstructionWavePlanner.RecalculateAfterFloorRemoved(Tile);
            PromoteFloorDeconstructWaveNeighbors();
            TryClearGoal();
            deficitEv.Clear();
            if (Tile.Transform.CoverPercent > 0) {
                S.Sig.CoverChanged.Send(Tile, false);
            }
            UpdatePassability();
        }

        public void ConfigureFloorWave(bool isSeed, int waveId) {
            IsFloorWaveSeed = isSeed;
            FloorWaveId = waveId;
        }

        public void ConfigureFloorDeconstructWave(bool isSeed, int waveId) {
            IsFloorDeconstructWaveSeed = isSeed;
            FloorDeconstructWaveId = waveId;
        }

        public void ClearFloorDeconstructWave() {
            IsFloorDeconstructWaveSeed = true;
            FloorDeconstructWaveId = 0;
        }

        private bool IsFloorWaveAvailable(GameState state, AIGoal goal) {
            if (LayerId != WorldLayer.Floor || FloorWaveId == 0) {
                return true;
            }
            // A tile that will never be a section floor once built (e.g. an angled floor)
            // is never a wave's seed (see FloorConstructionWavePlanner.PickNewSeed) and can
            // never be unlocked by the wave's section wavefront either, since it can never
            // satisfy HasCardinalConstructedFloorInWave for its own neighbors. It only opens
            // once adjacent to any already-constructed floor (any wave), so it can never be
            // built floating in space ahead of the rest of the structure.
            // goal.Agent != null - not HasGoal - is the sticky exception: a goal exists (and
            // HasGoal is therefore true) from the instant the tile is placed, since Hauling
            // auto-creates in OnReady well before any worker commits, so HasGoal would have
            // made this whole check a no-op. Once a worker has actually committed (won a bid
            // or was pre-assigned), keep it open regardless, so surroundings changing after
            // that (e.g. its one adjacent floor got deconstructed) can't silently strand it.
            if (!EntityUtils.WillBeSectionFloor(Tile)) {
                return goal.Agent != null
                    || EntityUtils.HasCardinalConstructedFloorAny(Tile.PosIdx);
            }
            if (IsFloorWaveSeed) {
                return true;
            }
            var pos = Tile.PosIdx;
            // Structural connectivity stays cardinal. Islands (and therefore grounded
            // reachability) are flood-filled cardinally, so a floor that only touches the
            // ship diagonally would build into its own detached island - exactly what the
            // wave exists to prevent.
            if (!EntityUtils.HasCardinalConstructedFloor(pos)) {
                return false;
            }
            // Access is Chebyshev: a worker must actually be able to stand next to this tile,
            // but any of the 8 neighbors will do (see HasWalkableNeighbor). Having a cardinal
            // neighbor whose floor happens to be constructed is not enough - one boxed in by a
            // solid wall is no standing spot, even though its floor is finished.
            if (EntityUtils.HasWalkableNeighbor(pos)) {
                return true;
            }
            // Nowhere to stand at all. Keep waiting only while a same-wave cardinal blueprint
            // could still turn into a standing spot; once the wave has finished around this
            // tile nothing will ever open the gate, so hand it to the auction rather than
            // stalling silently forever (a flying worker can still build it, and a grounded one
            // fails visibly and retries). This is the wall-ring corner whose diagonal room
            // interior also ended up carrying a wall.
            // FloorWaveId still matters for bookkeeping (merging/splitting waves, seed
            // tie-breaking, persistence) beyond this availability check.
            return !EntityUtils.HasCardinalUnfinishedFloorInWave(pos, FloorWaveId);
        }

        private bool IsFloorDeconstructWaveAvailable(GameState state, AIGoal goal) {
            if (LayerId != WorldLayer.Floor) {
                return true;
            }
            if (EntityUtils.IsObjAt(Tile.PosIdx) || EntityUtils.IsWallAt(Tile.PosIdx)) {
                return false;
            }
            // Derelict wrecks outside the map's corner margins are never wave-planned
            // (FloorDeconstructionWavePlanner.IsWavePlannedFloorDeconstruction) - salvaging
            // them is bulk, opportunistic work, so the old streamlining chain
            // (AIAction.TryStreamline) should take precedence over waiting for a wave seed.
            // This also reopens floors whose wave state was persisted before that exclusion
            // existed, without requiring the player to re-order them.
            if (!EntityUtils.IsWithinMargin(Tile.PosIdx, false, false)) {
                return true;
            }
            return IsFloorDeconstructWaveSeed || FloorDeconstructWaveId == 0;
        }

        // Closed while any occupied subtile lacks a constructed supporting floor - whether
        // that floor is still an unfinished blueprint or missing entirely - so an OnFloor
        // wall/object, and the hauling of its materials, waits until every supporting floor
        // is built. Placement forbids an OnFloor tile with no floor, and floor removal
        // triggers dangling cleanup (CmdRemoveTile -> CmdCheckDanglingObject)
        // independently, so a floorless blueprint only exists transiently before cleanup -
        // the gate never strands one silently.
        private bool IsSupportFloorAvailable(GameState state, AIGoal goal) {
            return HasConstructedSupportFloor();
        }

        // Floor and wall blueprints can be supplied as extra stops of another blueprint's
        // delivery trip (ConstructionDeliveryBatch)
        public bool CanBeDeliveryStop => LayerId == WorldLayer.Floor
            || LayerId == WorldLayer.Walls;

        // The wave or support floor rule shared by hauling and building
        private bool IsBuildWorkAvailable(GameState state, AIGoal goal) {
            if (LayerId == WorldLayer.Floor) {
                return IsFloorWaveAvailable(state, goal);
            }
            if (Placement == PlacementConstraint.OnFloor) {
                return HasConstructedSupportFloor();
            }
            return true;
        }

        // Closed quietly while a courier is already bringing the materials, so nobody else
        // walks here for them meanwhile
        private bool IsHaulingAvailable(GameState state, AIGoal goal) {
            return !IsHeldByCourier && IsBuildWorkAvailable(state, goal);
        }

        private void AttachFloorWaveGate(AIGoal goal) {
            if (goal.JobIdH == JobTypeIdH.Hauling && CanBeDeliveryStop) {
                goal.AvailabilityGate = IsHaulingAvailable;
                return;
            }
            if (LayerId != WorldLayer.Floor) {
                if (Placement == PlacementConstraint.OnFloor
                        && (goal.JobIdH == JobTypeIdH.Hauling
                            || goal.JobIdH == JobTypeIdH.Construction)) {
                    goal.AvailabilityGate = IsSupportFloorAvailable;
                }
                return;
            }
            if (goal.JobIdH == JobTypeIdH.Hauling
                    || goal.JobIdH == JobTypeIdH.Construction) {
                goal.AvailabilityGate = IsFloorWaveAvailable;
            } else if (goal.JobIdH == JobTypeIdH.Demolition) {
                goal.AvailabilityGate = IsFloorDeconstructWaveAvailable;
            }
        }

        // Priority 0/Forbidden gates automatic assignment the same way the availability
        // gate does, so a priority change must re-resolve the blueprint's hologram shader.
        public void OnAIGoalPriorityChange(AIGoal goal) {
            if (!IsFinished && Tile.IsActive
                    && WorldLayer.IsRenderableTileLayer(LayerId)) {
                S.Map.Grids[LayerId].TriggerChanged(Tile.PosIdx);
            }
        }

        private void PromoteFloorDeconstructWaveNeighbors() {
            if (LayerId != WorldLayer.Floor || FloorDeconstructWaveId == 0) {
                return;
            }
            Pos.ToXY(Tile.PosIdx, out var x, out var y);
            for (int i = 0; i < 4; i++) {
                var offset = Pos.NeighborXY[i];
                var nx = x + offset.Item1;
                var ny = y + offset.Item2;
                if (Pos.IsOOBXY(nx, ny)) {
                    continue;
                }
                var floor = EntityUtils.FloorAt(Pos.FromXY(nx, ny));
                if (floor?.IsConstructed == true
                        && floor.Constructable.FloorDeconstructWaveId
                            == FloorDeconstructWaveId) {
                    floor.Constructable.IsFloorDeconstructWaveSeed = true;
                }
            }
        }

        public bool AddDeconstruction(float workAmount) {
            if (WorkDone <= 0) { return true; }
            Transmit(this, MsgIdH.DeconstructionOngoing);
            WorkDone -= workAmount * deconstructionRate;
            if (WorkDone <= 0) {
                WorkDone = 0;
                Transmit(this, MsgIdH.DeconstructionFinished);
                return true;
            }

            var progress = WorkAmount - WorkDone;
            ShowInfoBar(Consts.ColorConstructBar, 1f - (progress / WorkAmount),
                Mathf.RoundToInt(progress), WorkAmount, "deconstruction.progress");

            return false;
        }

        public bool CanDeconstruct() {
            if (!Tile.IsConstructed || Tile.IsImmutable || Tile.IsPendingMove) {
                return false;
            }
            if (Entity.HasComponent<ShipComputerComp>()) {
                if (S.Components.CountOfConstructed<ShipComputerComp>(2) < 2) {
                    return false;
                }
            }
            if (Entity.TryGetComponent<StasisArrayComp>(out var stasis)) {
                if (!A.HasAsimovOverride && stasis.BeingCount > 0) {
                    return false;
                }
            }
            if (TryGetGoal(out var goal)) {
                if (goal.JobIdH == JobTypeIdH.Demolition) {
                    return false;
                }
            }
            return true;
        }

        public AIGoal CreateDeconstructGoal() {
            if (Tile.IsImmutable || Tile.IsPendingMove) {
                S.Sig.ShowErrorMessage.Send($"{T.OperationForbidden}: {T.DeconstructionNotAllowedTemp_V1.F(entity.Definition.CodexLink)}");
                return null;
            }
            if (entity.Goal != null && entity.Goal.JobIdH != JobTypeIdH.Demolition) {
                // We want to cancel existing task because deconstruction will
                // override this
                AIGoal.Cancel(entity.Goal, G.Ticks, T.Deconstruct);
            }
            if (Entity.HasComponent<ShipComputerComp>()) {
                // Asimov comes first: telling the player to repair a computer they
                // are not allowed to remove anyway reports the wrong blocker
                if (!A.HasAsimovOverride &&
                        S.Components.CountOfConstructed<ShipComputerComp>(2) < 2) {
                    The.Dialog.ShowSimple(Entity.Definition.Preview,
                        T.AsimovViolation3);
                    UISounds.PlayActionDenied();
                    return null;
                }
                if (Tile.Health.IsDamaged) {
                    The.Dialog.ShowSimple(Entity.Definition.Preview,
                        T.ShipCpuDeconstructDamaged);
                    return null;
                }
            }
            if (Entity.TryGetComponent<StasisArrayComp>(out var stasis)) {
                if (!A.HasAsimovOverride && stasis.BeingCount > 0) {
                    S.Sig.ShowErrorMessage.Send(T.AsimovViolation1);
                    return null;
                }
            }
            if (HasAnyGoal) {
                return null;
            }
            if (LayerId == WorldLayer.Floor) {
                ClearFloorDeconstructWave();
            }
            var goal = CreateGoal(JobTypeIdH.Demolition, IconId.CDemolition,
                    T.Deconstruct, this)
                .WithPathFlags(PathReqFlags.AdjNearby)
                .WithWorldToDesired(AIVarsH.IsDeconstructed, false, true)
                .WithRequiredAbilities(AbilityIdH.Build, AbilityIdH.DumbLabor)
                .NanobotCompatible()
                .ByPlayer(true)
                .WriteInitialState();
            AttachFloorWaveGate(goal);
            if (entity.TrySetGoal(goal)) {
                return goal;
            }
            return null;
        }

        public int WorkAmount;
        public float WorkDone;
        public bool IsFinished { get; private set; }
        private string canReplaceGroup;
        public bool CanReplaceInSameLayer;
        private Mat[] rawMaterials;
        public Mat[] RawMaterials => IsFinished ?
            Tile.Definition.ConstructedMats : rawMaterials;
        private BCallback matsChangedB;
        public BCallback MatsChangedB => matsChangedB ??= new BCallback(capacity: RawMaterials.Length);

        private BFloat workDonePctB;
        public BFloat WorkDonePctB => workDonePctB ??= new BFloat(WorkDone / WorkAmount);
        private BBool isFinishedB;
        public BBool IsFinishedB => isFinishedB ??= new BBool(IsFinished);

        public float Integrity => WorkDone == WorkAmount ? 1f :
            WorkDone / WorkAmount * 0.5f;

        public int InPort => Tile.Transform.HasWorkSpot ?
            Tile.Transform.WorkSpot : 0;


        public bool IsReachableForCommonAction => S.Research.IsDefUnlocked(Tile.Definition)
            && (Tile.Definition.IsInSeason || A.InstantBuild);

        public string StatsCategoryName => T.Constructable;

        public int StatsOrder => 0;

        private float tunConstructionSpeed = 1f;
        // Floors and walls come apart fast, so derelicts can be stripped in bulk. Everything
        // else takes as long to remove as to build, but never longer than
        // DeconstructionMaxWork worth of building.
        private float deconstructionRate = 1f;
        public bool CanReplaceWith(int pos, out Tile replacementTarget) {
            var tile = EntityUtils.GetTileInLayer(pos, Tile.Definition.LayerId);
            replacementTarget = tile;
            if (tile == null) { return false; }
            if (tile.Transform.IsChild) { return false; }
            if (tile.DefinitionId == Tile.DefinitionId) {
                if (tile.Transform.Rotation == Tile.Transform.Rotation) {
                    return false;
                }
            }
            if (!CanReplaceInSameLayer && canReplaceGroup == null) {
                return false;
            }
            if (!tile.IsConstructed) { return false; }
            if (tile.PendingReplacement.IsPending) {
                return false;
            }
            if (canReplaceGroup != null &&
                    canReplaceGroup == tile.Constructable.canReplaceGroup) {
                //D.Log("Can replace same group: {0} at {1}", canReplaceGroup, Pos.String(pos));
                return true;
            }
            if (tile.Definition.LayerId != Tile.Definition.LayerId) {
                return false;
            }
            if (tile.Definition.LayerId == WorldLayer.Floor) {
                // Check if there's an object above
                if (!EntityUtils.IsEmptyFloorAt(pos)) {
                    if (!Tile.Integrity.CanHaveFullIntegrity) {
                        return false;
                    }
                }
            }
            return CanReplaceInSameLayer;
        }
        public void AddNanobotsEffort(float amt, out AIGoal goal) {
            if (!TryGetGoal(out goal)) { return; }
            if (goal.JobIdH == JobTypeIdH.Construction) {
                if (!HasConstructedSupportFloor()) { return; }
                if (AddWork(goal.Agent, amt)) {
                    goal.ChangeStateTo(AIGoalState.Completed, G.Ticks);
                }
            } else if (goal.JobIdH == JobTypeIdH.Demolition) {
                if (AddDeconstruction(amt)) {
                    goal.ChangeStateTo(AIGoalState.Completed, G.Ticks);
                    BreakDown(2f, moveMaterialsAside: false, orderHaul:
                        S.Prefs.GetBool(Pref.DeconstructOrderHaul,
                            PrefH.DeconstructOrderHaul, false),
                        autoRebuild: false);
                }
            }
        }

        public bool AddWork(AIAgentComp builder, float amount) {
            if (IsFinished) {
                // D.Warn("Trying to add work on a finished construction: {0}",
                //    this);
                return true;
            }
            WorkDone += amount * tunConstructionSpeed;
            if (WorkDone >= WorkAmount) {
                WorkDone = WorkAmount;
                ConstructedBy = builder;
                FinishConstruction(false);
            } else {
                ShowInfoBar(Consts.ColorConstructBar, WorkDone / WorkAmount,
                    Mathf.RoundToInt(WorkDone), WorkAmount, "construction.progress");
            }
            workDonePctB?.Set(WorkDone / WorkAmount);
            return Maths.Approximately(WorkDone, WorkAmount);
        }
        public bool IsBrokenDown;
        public void BreakDown(float radius,
                bool moveMaterialsAside = false, bool immediate = false,
                bool silent = false, bool skipDanglingCheck = false,
                bool autoRebuild = false, bool orderHaul = false,
                float convertChanceMul = 1f) {
            DoBreakDown(radius, moveMaterialsAside, immediate, silent, skipDanglingCheck,
                autoRebuild, orderHaul, convertChanceMul, isCancel: false);
        }

        // Cancelling an unbuilt tile gives back what was delivered as it came: those materials
        // were never used, so no destroy-time conversion or loss. Every player cancel comes
        // here. Removing the tile directly loses them: a removed tile's goal is cancelled
        // after the tile went inactive, and AIGoal no longer calls OnGoalChange then (STA-1782).
        public void CancelConstruction() {
            if (IsFinished) { return; }
            DoBreakDown(1f, moveMaterialsAside: false, immediate: true, silent: true,
                skipDanglingCheck: false, autoRebuild: false, orderHaul: false,
                convertChanceMul: 1f, isCancel: true);
        }

        private void DoBreakDown(float radius, bool moveMaterialsAside, bool immediate,
                bool silent, bool skipDanglingCheck, bool autoRebuild, bool orderHaul,
                float convertChanceMul, bool isCancel) {
            if (IsBrokenDown) { return; }
            if (!Ready.Gen) {
                autoRebuild = false;
            }
            if (entity.IsImmutable) {
                D.ErrSoft("Trying to break down an invincible tile! {0}", Tile);
                return;
            }
            IsBrokenDown = true;
            //Tile.ENode?.OnRemove();
            if (IsFinished) {
                if (!silent) {
                    Sounds.PlayBreakDown(Tile.Position);
                }
            }
            var spawnMats = true;
            var yieldChance = isCancel ? 0f : S.BB.Get(BBKeyH.DestructionMatYield, 0f);
            if (yieldChance > 0.01f) {
                Pos.ToXY(Tile.PosIdx, out var x, out var y);
                var hash = Hashes.Quick(17, x * 3134, y * -131);
                spawnMats = new Unity.Mathematics.Random((uint) hash).NextFloat() < yieldChance;
            }
            Mat[] mats;
            if (spawnMats) {
                mats = IsFinished ? Tile.Definition.ConstructedMats : rawMaterials;
            } else {
                mats = null;
            }
            if (mats != null && !isCancel) {
                bool isDestroyed = Tile.Health.IsDestroyed;
                bool mustFilter = false;
                for (int i = mats.Length - 1; i >= 0; i--) {
                    Mat mat = mats[i];
                    if ((isDestroyed && mat.Type.IsConsumedOnDestroy)
                            || mat.Type.ConvertToMatOnDestroy != null) {
                        mustFilter = true;
                        break;
                    }
                }
                if (mustFilter) {
                    List<Mat> filteredMats = new();
                    for (int i = mats.Length - 1; i >= 0; i--) {
                        var mat = mats[i];
                        if (isDestroyed && mat.Type.IsConsumedOnDestroy) {
                            continue;
                        }
                        AddDeconstructedMat(filteredMats, mat, S.Rng, convertChanceMul);
                    }
                    mats = filteredMats.ToArray();
                }
            }
            IsFinished = false;
            isFinishedB?.Set(false);
            HideAllInfo();

            rawMaterials = null;
            cachedMatRequest = null;
            if (Tile.IsActive) {
                var destroyCmd = new CmdRemoveTile(Tile, skipDanglingCheck,
                    autoRebuild: autoRebuild);
                if (immediate) {
                    destroyCmd.Execute(S);
                } else {
                    S.CmdQ.Enqueue(destroyCmd);
                }
            }

            // TODO - add some particle effect for dust cloud, etc
            SpawnMats(mats, radius, moveMaterialsAside, orderHaul);
        }

        private void SpawnMats(Mat[] mats, float radius, bool moveMaterialsAside, bool orderHaul) {
            if (Arrays.IsEmpty(mats)) { return; }
            var matpos = Tile.CenterIdx;
            if (moveMaterialsAside) {
                Pos.ForEachAroundRandom(Rng.Unseeded, matpos,
                        Mathf.CeilToInt(Tile.Size.magnitude * 0.5f), false, false, (p) => {
                    if (EntityUtils.IsEmptyFloorAt(p, false)) {
                        matpos = p;
                        return false;
                    }
                    return true;
                });
            }
            // Break down the tile into raw materials and drop them
            // on the ground.
            // Need to spawn a copy because original will be wiped away soon
            S.CmdQ.Enqueue(new CmdSpawnMats(mats, matpos, radius, true,
                orderHaul: orderHaul, mergeIntoBlock: true));
        }

        public static void AddDeconstructedMat(List<Mat> filteredMats, Mat mat, Rng rng, float convertChanceMul = 1f) {
            if (mat.Type.ConvertToMatOnDestroy != null) {
                int amtConverted = 0;
                for (int j = mat.StackSize - 1; j >= 0; j--) {
                    if (rng.Chance(Mathf.Clamp01(mat.Type.ConvertToMatOnDestroyChance * convertChanceMul))) {
                        amtConverted++;
                    }
                }
                int amtRemaining = mat.StackSize - amtConverted;
                if (amtRemaining == 0) {
                    mat.Type = mat.Type.ConvertToMatOnDestroy;
                } else if (amtConverted > 0) {
                    mat.StackSize = amtRemaining;
                    var mat2 = new Mat {
                        Type = mat.Type.ConvertToMatOnDestroy,
                        StackSize = amtConverted,
                        MaxStackSize = amtConverted,
                    };
                    filteredMats.Add(mat2);
                }
                filteredMats.Add(mat);
            } else {
                filteredMats.Add(mat);
            }
        }

        public bool ReplaceWith(Tile tile) {
            Tile.PendingReplacement.IsPending = true;
            if (Tile.PendingReplacement.ConfigureFor(tile)) {
                return true;
            }
            // Don't leave the tile armed as pending without a replacement goal
            Tile.PendingReplacement.CancelReplacement();
            return false;
        }

        public static PlacementConstraint PlacementConstraintFor(ComponentConfig cfg) {
            return Enums.FromString<PlacementConstraint>(
                cfg.GetString(PropertyIdH.Placement, "OnFloor"));
        }

        public static float DeconstructionRateFor(int layerId, int workAmount) {
            float rate = layerId == WorldLayer.Floor || layerId == WorldLayer.Walls
                ? Tunables.DeconstructionSpeedStructure
                : Tunables.DeconstructionSpeedObjects;
            int maxWork = Tunables.DeconstructionMaxWork;
            if (maxWork > 0 && workAmount > maxWork) {
                rate *= (float) workAmount / maxWork;
            }
            return rate;
        }

        protected override void OnConfig() {
            deficitEv = new MatDeficitEventHolder(Tile, this, T.JobTypeConstruction);
            tunConstructionSpeed = Tunables.ConstructionSpeed;

            CanReplaceInSameLayer = Config.GetBool(PropertyIdH.CanReplaceSameLayer, false);
            CanDragRow = Config.GetBool(PropertyIdH.CanDragRow, true);
            canReplaceGroup = Config.GetString(PropertyIdH.CanReplaceGroup, null);
            PlacementGroup = Config.GetString(PropertyIdH.PlacementGroup, null);
            WorkAmount = Config.GetInt(PropertyIdH.WorkAmount, Maths.Max(WorkAmount, 1000));
            deconstructionRate = DeconstructionRateFor(Entity.Definition.LayerId, WorkAmount);
            Placement = PlacementConstraintFor(Config);
            FlyingOnly = Config.GetBool(PropertyIdH.FlyingOnly, IsBuiltInSpace);

            rawMaterials = Config.GetRawMaterialsCopy(PropertyIdH.Contents, true, null);
            #if !PRODUCTION_BUILD
            if (rawMaterials == null && !Entity.Definition.IsTemplate) {
                D.Err("Constructable has null materials: {0}", Entity.DefinitionId);
            }
            #endif
        }

        public void UpdatePassability() {
            // We may call this too early while passability component is not yet
            // initialized
            Tile.Passability?.UpdateBuildable(IsFinished, WorkDone /
                (float) WorkAmount);
        }

        public void InstantBuild() {
            D.Ass(!IsFinished, "Instant-building a finished tile! {0}", this);
            FillMissingMaterials();
            IsFinished = true;
            WorkDone = WorkAmount;
            isFinishedB?.Set(true);
            deficitEv.Clear();
            FinishConstruction(Tile.Reinstallable?.IsMidRelocation == true);
            // InstantBuild bypasses the AI action that normally completes and removes the goal.
            TryClearGoal();
            if (S.Query.IsSelected.Ask(Tile)) {
                UIUtils.RefreshSelectedEntityUI();
            }
        }

        private void FillMissingMaterials() {
            if (rawMaterials != null) {
                for (int i = 0; i < rawMaterials.Length; i++) {
                    var rm = rawMaterials[i];
                    rm.StackSize = rm.MaxStackSize;
                    rawMaterials[i] = rm;
                }
            }
        }

        private void FinishWalls(int pos) {
            Tile.Transform.AutoUpdateRotation();
            Pos.ForEachAround(pos, 1, false, false, (nPos) => {
                if (EntityUtils.IsWithinBounds(nPos)) {
                    S.Map.Grids[LayerId].TriggerChanged(nPos);
                    var nWall = EntityUtils.WallAt(nPos);
                    nWall?.Transform.AutoUpdateRotation();
                }
                return true;
            });
        }

        public void FinishConstruction(bool isRelocating) {
            rawMaterials = null;
            WorkDone = WorkAmount;
            IsFinished = true;
            isFinishedB?.Set(true);
            // Removed, wiped out?
            if (!Entity.IsActive) {
                D.Err("Finishing construction on an inactive entity! {0}", Entity);
                return;
            }
            if (!isRelocating && Tile.Health.IsDamaged) {
                D.Err("Finished construction while tile was damaged! {0}", Tile);
                Tile.Health.Repair(10000f, true);
            }
            UpdatePassability();
            var pos = entity.PosIdx;
            if (pos < 1) {
                D.Err("Finished construction but position is: {0}. Removing: {1}", pos, Tile);
                S.RemoveEntity(Entity);
                return;
            }
            // If game is not ready, we're in the middle of a load
            S.Map.Grids[LayerId].TriggerChanged(pos);

            // Update wall connections
            if (LayerId == WorldLayer.Walls) {
                FinishWalls(pos);
                // This wall/door's own walkability just changed (e.g. a door finished and
                // is now passable) - refresh any unfinished floor next to it,
                // since HasWalkableNeighbor depends on that.
                RefreshUnfinishedFloorNeighborHolograms(pos);
            }

            HideAllInfo();
            Transmit(this, MsgIdH.ConstructionFinished);
            S.Sig.ConstructionCompleted.Send(Tile);
            if (!isRelocating && ConstructedBy != null) {
                PlayStats.TrackConstructionFinished(Tile.DefinitionId);
            }
            S.Sig.RecalculatePassability.Send(pos);
            deficitEv.AssertClear();
            if (Tile.Reinstallable != null) {
                Tile.Reinstallable.SourceId = 0;
                Tile.Reinstallable.IsMidRelocation = false;
            }
            if (Tile.Transform.CoverPercent > 0) {
                AddCover();
            }
            if (LayerId == WorldLayer.Floor) {
                CmdCheckDanglingObject.UpdateInSpaceAt(S, pos);
                // Re-validate the rest of this floor's wave now that this tile is done,
                // so a wave can never end up seedless (FloorWaveId == 0 already excludes
                // gen/instant-built floors; Ready.Gen is belt-and-suspenders).
                if (FloorWaveId != 0 && Ready.Gen) {
                    FloorConstructionWavePlanner.RecalculateAfterFloorConstructed(Tile);
                }
                // A neighbor's construction-wave gate (see IsFloorWaveAvailable) may
                // have just opened purely because this floor became constructed - refresh its
                // hologram even when the neighbor's own wave bookkeeping didn't change. Floor
                // is not a connected tile layer, so MapRenderer's neighbor-cascade does not
                // reach it.
                RefreshUnfinishedFloorNeighborHolograms(pos);
                // A wall/object above this floor may have been waiting on its support-floor
                // gate (see IsSupportFloorAvailable) - refresh its hologram now that it can
                // open. Different grid than Floor, so the connected-layer neighbor refresh
                // above does not reach it.
                var wall = EntityUtils.WallAt(pos);
                if (wall != null && !wall.IsConstructed) {
                    S.Map.Grids[WorldLayer.Walls].TriggerChanged(pos);
                }
                var obj = EntityUtils.ObjAt(pos);
                if (obj != null) {
                    var anchor = obj.Transform.IsChild ? obj.Transform.Parent : obj;
                    if (!anchor.IsConstructed) {
                        S.Map.Grids[WorldLayer.Objects].TriggerChanged(anchor.PosIdx);
                    }
                }
            }
            if (S.Query.IsSelected.Ask(Tile)) {
                // won't work right away
                S.Clock.ExecuteAfterFramesForUI(1, UIUtils.RefreshSelectedEntityUI);
            }
        }

        // Refreshes the hologram of any unfinished floor next to pos, whose
        // IsFloorWaveAvailable gate depends on this position's own walkability (see
        // HasWalkableNeighbor). Used when either a floor itself finishes, or a
        // wall/door built on top of a floor finishes and changes that position's
        // walkability. All 8 neighbors, because the gate accepts a diagonal standing spot.
        private void RefreshUnfinishedFloorNeighborHolograms(int pos) {
            Pos.ToXY(pos, out var fx, out var fy);
            for (int i = 0; i < 8; i++) {
                var offset = Pos.NeighborXY[i];
                var nx = fx + offset.Item1;
                var ny = fy + offset.Item2;
                if (Pos.IsOOBXY(nx, ny)) { continue; }
                var neighborPos = Pos.FromXY(nx, ny);
                var neighborFloor = EntityUtils.FloorAt(neighborPos);
                if (neighborFloor != null && !neighborFloor.IsConstructed) {
                    S.Map.Grids[WorldLayer.Floor].TriggerChanged(neighborPos);
                }
            }
        }

        public override void OnReady(bool wasLoaded) {
            if (wasLoaded) {
                UpdatePassability();
                return;
            }
            if (isInstantBuild) { return; }
            if (!wasLoaded) {
                if (Tile.Reinstallable?.IsPendingQuick != true) {
                    CreateConstructGoal(null);
                }
            }
            UpdatePassability();
        }

        public override void OnLateReady(bool wasLoaded) {
            // If we instant build on ready, other components are not yet
            // initialized, and we will likely create ads with zero entity id
            // for some of them
            if (isInstantBuild && !wasLoaded) {
                InstantBuild();
            }
            if (wasLoaded) {
                if (!IsFinished) {
                    deficitEv.OnLateReady();
                } else {
                    if (Tile.Transform.CoverPercent > 0) {
                        Ready.When(WhenH.CoverSysLoaded, AddCover);
                    }
                }
            }
        }

        private void AddCover() {
            // D.Warn("Sending cover changed for {0}", Tile);
            S.Sig.CoverChanged.Send(Tile, true);
        }

        public void CreateConstructGoal(AIAgentComp forceAgent) {
            if (IsFinished) {
                D.Err("Trying to create construct goal while tile is finished! {0}",
                    Tile);
                return;
            }
            if (entity.Goal != null) {
                D.Err("Trying to create construct goal while a goal already exists: {0}",
                    entity.Goal);
                return;
            }
            var missing = NextMissingMat;
            AIGoal goal;
            if (missing != null) {
                goal = CreateGoal(JobTypeIdH.Hauling,
                        IconId.CHaul, T.HaulMaterials, this)
                    .WithPathFlags(PathReqFlags.AdjOnTop | PathReqFlags.AdjNearby)
                    .NanobotCompatible()
                    .ByPlayer(true)
                    .WithRequiredAbilities(AbilityIdH.Carry, AbilityIdH.DumbLabor)
                    .WithWorldToDesired(AIVarsH.IsResourcesDelivered, false, true)
                    .WriteInitialState();
                goal.CheckDoable = CheckCanHaulMaterials;
            } else { // no missing materials but work not finished
                goal = CreateGoal(JobTypeIdH.Construction,
                        IconId.CHammer, T.Construct, this)
                    .WithPathFlags(PathReqFlags.AdjNearby)
                    .NanobotCompatible()
                    .ByPlayer(true)
                    .WithWorldToDesired(AIVarsH.IsConstructed, false, true)
                    .WithRequiredAbilities(AbilityIdH.Build, AbilityIdH.SkilledLabor)
                    .WriteInitialState();
            }
            AttachFloorWaveGate(goal);
            if (!Jobs.CanDo(forceAgent, JobTypeIdH.Construction)) {
                forceAgent = null;
            }
            if (forceAgent != null && !goal.IsAvailabilityGateOpen(S)) {
                // Don't pre-assign a worker to a goal whose own gate (just attached
                // above) is closed - TrySetGoal would stamp goal.Agent and jump
                // straight to PreAssigned without ever calling agent.SetGoal, and
                // AISys.ProcessGoalPreAssigned bails out silently while the gate
                // stays closed, leaving the goal permanently claimed by a worker
                // who was never actually told to work on it (see
                // FloorConstructionWaves.md).
                forceAgent = null;
            }
            if (!entity.TrySetGoal(goal, forceAgent)) {
                D.Err("Failed creating construction goal for {0}", Tile);
            }
        }

        public AIGoalCheckResult CheckCanHaulMaterials(GameState s, AIGoal goal) {
            if (IsFinished) {
                D.Err("Checking doability a completed constructable: {0} -> {1}",
                    goal, this);
                return AIGoalCheckResult.Completed;
            }
            if (goal != entity.Goal) {
                D.Err("Checking doability for the wrong goal! Expected: {0}, Actual (in AI system): {1}",
                    entity.Goal, goal);
                return AIGoalCheckResult.Error("Wrong goal");
            }
            var nextM = NextMissingMat;
            if (nextM == null) {
                D.Err("CheckHaulMaterials is set on a wrong goal! Nothing to haul! {0} -> {1}",
                    goal, goal.Target);
                return AIGoalCheckResult.Doable;
            }
            return MatClaimGate.CheckHaulGoal(s, this, nextM, goal.GoalPriority);
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetBool("IsFinished", IsFinished);
            if (LayerId == WorldLayer.Floor) {
                data.SetBool("IsFloorWaveSeed", IsFloorWaveSeed);
                data.SetInt("FloorWaveId", FloorWaveId);
                data.SetBool("IsFloorDeconstructWaveSeed", IsFloorDeconstructWaveSeed);
                data.SetInt("FloorDeconstructWaveId", FloorDeconstructWaveId);
            }
            if (!IsFinished) {
                deficitEv.Save(data);
                data.SetMats("Contents", rawMaterials);
                data.SetFloat("WorkDone", WorkDone);
            } else {
                data.SetMats("Contents", null);
            }
        }

        protected override void OnLoad(ComponentData data) {
            IsFinished = data.GetBool("IsFinished", false);
            if (LayerId == WorldLayer.Floor) {
                IsFloorWaveSeed = data.GetBool("IsFloorWaveSeed", true);
                FloorWaveId = data.GetInt("FloorWaveId", 0);
                IsFloorDeconstructWaveSeed = data.GetBool(
                    "IsFloorDeconstructWaveSeed", true);
                FloorDeconstructWaveId = data.GetInt("FloorDeconstructWaveId", 0);
            }
            if (IsFinished) {
                WorkDone = WorkAmount;
                rawMaterials = null;
                return;
            }
            deficitEv.Load(data);
            WorkDone = data.GetFloat("WorkDone", 0);
            var rawMats = data.GetMats("Contents", null);
            if (rawMats != null) {
                rawMaterials = rawMats;
            } else {
                D.Err("Loaded blank raw materials! Will finish construction or make fully unconstructed depending on IsFinished flag, which is: {0}. {1}",
                    IsFinished, Entity);
                if (IsFinished) {
                    FillMissingMaterials();
                    WorkDone = WorkAmount;
                    IsFinished = true;
                } else {
                    WorkDone = 0;
                }
            }
        }

        public override string ToString() {
            var sb = Caches.StringBuilder;
            sb.Clear();
            sb.AppendFormat( " * Constructable (Finished: {0}):", IsFinished);
            if (rawMaterials?.Length > 0) {
                sb.AppendFormat("\n  - Req Materials: {0}",
                    string.Join("\n   - ", rawMaterials));
            }
            sb.AppendFormat("\n  - Work: {0}/{1}", WorkDone, WorkAmount);
            return sb.ToString();
        }

        public bool TryOrderDeconstruct() {
            if (!Tile.IsActive) { return false; }
            S.Sys.Hint.TrackAction(HintMassDeconstruct.Id);
            CreateDeconstructGoal();
            return true;
        }

        private double lastTooltipUpdateAt;
        private const double updateRate = 3d;
        private string lastCurrentTooltip;
        private string cachedTooltip;

        public string ShowCustomTooltip(string currentTooltip, InfoItem item) {
            if (Tile.IsConstructed) {
                cachedTooltip = null;
                lastCurrentTooltip = null;
                return currentTooltip;
            }
            var time = Time.realtimeSinceStartupAsDouble;
            if (lastCurrentTooltip == currentTooltip
                    && lastTooltipUpdateAt + updateRate > time) {
                return cachedTooltip;
            }
            lastCurrentTooltip = currentTooltip;
            lastTooltipUpdateAt = time;
            var sb = new StringBuilder();
            sb.Append(currentTooltip);
            sb.AppendFormat("<color={0}>", Texts.TMPColorYellow);
            // We no longer require flight to build objects in space, just need to reach them
            if (FlyingOnly) {
                sb.AppendFormat("\n <b>{0} {1}</b>", GlyphMap.ArrowRight, T.ReqNavFlight);
            }
            if (!Arrays.IsEmpty(rawMaterials)) {
                sb.AppendFormat("\n <b>{0} {1}</b>", GlyphMap.ArrowRight, T.ContainsMaterials);
                for (int i = 0; i < rawMaterials.Length; i++) {
                    var mat = rawMaterials[i];
                    sb.AppendFormat("\n    - {0}: {1}",
                        mat.Type.Def.CodexLink,
                        Units.XOutOfY(mat.StackSize, mat.MaxStackSize));
                }
            }
            sb.Append("</color>");
            cachedTooltip = sb.ToString();
            return cachedTooltip;
        }

        public string OutputTip() {
            var mats = RawMaterials;
            if (Arrays.IsEmpty(mats)) { return ""; }
            var sb = Caches.StringBuilder3;
            sb.Clear();
            bool hasHeader = false;
            for (int i = 0; i < mats.Length; i++) {
                var mat = mats[i];
                if (mat.MaxStackSize > 0 && mat.Type != null && !mat.Type.IsConsumedOnConstruct) {
                    if (!hasHeader) {
                        sb.Append(Texts.SubTitle(T.ContainsMaterials));
                        hasHeader = true;
                    }
                    sb.Append(mat.Type.Def.CodexLink);
                    sb.Append(' ');
                    sb.AppendLine(Units.XNum(mat.MaxStackSize));
                }
            }
            if (!hasHeader) {
                return null;
            }
            return sb.ToString();
        }

        public bool HasSameMaterialsAs(ConstructableComp other) {
            if (!other.Tile.IsActive) {
                return false;
            }
            var myRM = RawMaterials;
            var otherRM = other.RawMaterials;
            var sameCount = otherRM.Length == myRM.Length;
            if (!sameCount) {
                return false;
            }
            for (int i = otherRM.Length - 1; i >= 0; i--) {
                if (myRM[i].Type != otherRM[i].Type) {
                    return false;
                }
            }
            return true;
        }

        public bool ProvideRequestedMat(UnstoredMatComp pile, Being worker,
                AIGoal goal = null) {
            if (rawMaterials == null) {
                D.Err("Constructable providing mats while rawmaterials is null {0} -> {1}!",
                    worker, pile);
                return true;
            }
            int missingMatCount = 0;
            for (int i = 0; i < rawMaterials.Length; i++) {
                var rm = rawMaterials[i];
                var diff = rm.Diff;
                if (diff <= 0) { continue; }
                if (pile.Type == rm.Type) {
                    int took = pile.Take(pile.Type, null, diff,
                        goal ?? worker?.AIAgent?.Goal);
                    rm.StackSize += took;
                    rawMaterials[i] = rm;
                    if (rm.StackSize == rm.MaxStackSize) {
                        deficitEv.ClearIf(rm.Type);
                    }
                    if (took > 0) {
                        S.Sig.MatConsumed.Send(pile.Type, took);
                    }
                }
                missingMatCount += rm.Diff;
            }

            // clear the cache
            missingMats?.Clear();

            matsChangedB?.Invoke();
            D.Ass(missingMatCount > -1, "Missing mats less than 0 in constructable: {0}",
                D.Dump(rawMaterials));
            if (missingMatCount == 0) {
                deficitEv.AssertClear();
                return true;
            }
            return false;
        }

        public void NotifyMatDeficit(MatType type) {
            deficitEv.Notify(type);
        }

        public void ClearLackNotification(MatType type) {
            deficitEv.ClearIf(type);
        }

        public void OnGoalChange(AIGoal goal, AIGoalState from, AIGoalState to) {
            if (to == AIGoalState.Completed && goal.JobIdH == JobTypeIdH.Hauling) {
                entity.ClearGoal();
                // we delivered the materials just now
                if (!IsFinished) {
                    var prio = goal.GoalPriority;
                    var wasForceAssigned = goal.IsForceAssigned;
                    if (wasForceAssigned && LayerId == WorldLayer.Floor) {
                        IsFloorWaveSeed = true;
                    }
                    CreateConstructGoal(GetAgentIfConstructable(goal.Agent));
                    var nextGoal = entity.Goal;
                    nextGoal?.WithPriority(prio);
                    if (wasForceAssigned && nextGoal?.Agent != null) {
                        nextGoal?.ForceAssigned();
                    }
                }
            }
            if (goal.State == AIGoalState.Cancelled) {
                if (goal.JobIdH == JobTypeIdH.Demolition) {
                    if (LayerId == WorldLayer.Floor) {
                        PromoteFloorDeconstructWaveNeighbors();
                        ClearFloorDeconstructWave();
                    }
                    // Revert partial deconstruction
                    if (WorkAmount > WorkDone) {
                        WorkDone = WorkAmount;
                    }
                    HideInfoBar();
                } else {
                    // we have cancelled either construction or delivery job,
                    // let's remove the tile
                    if (!IsFinished && goal.JobIdH != JobTypeIdH.Cleaning) {
                        CancelConstruction();
                    }
                }
            }
        }

        public AIAgentComp GetAgentIfConstructable(AIAgentComp candidateAgent) {
            return HasConstructedSupportFloor() ? candidateAgent : null;
        }

        // True if this OnFloor object/wall does not need a floor (already on the floor
        // layer, or not placed OnFloor at all), or its supporting floor(s) are constructed.
        public bool HasConstructedSupportFloor() {
            if (Placement != PlacementConstraint.OnFloor ||
                    entity.Definition.LayerId == WorldLayer.Floor) {
                return true;
            }
            if (Tile.Transform.IsMulti) {
                int num = Tile.Transform.GetSubtilePos(Caches.IntArray32);
                for (int i = 0; i < num; i++) {
                    if (!EntityUtils.IsConstructedFloorAt(Caches.IntArray32[i])) {
                        return false;
                    }
                }
                return true;
            }
            return EntityUtils.IsConstructedFloorAt(Tile.Transform.PosIdx);
        }

        public void OnGoalLoad(AIGoal goal) {
            if (!IsFinished && goal.JobIdH == JobTypeIdH.Hauling) {
                goal.CheckDoable = CheckCanHaulMaterials;
            }
            if (!IsFinished || goal.JobIdH == JobTypeIdH.Demolition) {
                AttachFloorWaveGate(goal);
            }
        }

        public bool CreateCleanGoal(AIAgentComp forceAgent = null) {
            var goal = CreateGoal(JobTypeIdH.Cleaning, IconId.CMop, T.CleanFloors, this)
                    .WithVar(AIGoalVars.TargetPos, Tile.PosIdx)
                    .WithWorld(AIVarsH.IsIdle, false)
                    .WithRequiredAbilities(AbilityIdH.CleanFloors, AbilityIdH.DumbLabor)
                    .WithDesired(AIVarsH.IsFloorClean, true);
            goal.ByPlayer(true);
            goal.WriteInitialState();
            if (entity.TrySetGoal(goal)) {
                if (forceAgent != null) {
                    if (goal.TrySetAgent(forceAgent)) {
                        goal.Flags |= AIGoalFlags.ForceAssigned;
                        if (!S.Query.ForceGoal.Ask(goal, true)) {
                            D.Warn("Failed assigning agent to clean goal! {0} -> {1}",
                                goal, forceAgent);
                        } else {
                            goal.Flags &= ~AIGoalFlags.ForceAssigned;
                        }
                    }
                }
                return true;
            }
            return false;
        }

        public void AddStats(Lifetime life, WGroup block, ref int idx) {
            if (!IsFinished) { return; }
            var mats = RawMaterials;
            if (Arrays.IsEmpty(mats)) { return; }
            int count = mats.Count(m => !m.Type.IsConsumedOnConstruct);
            if (count == 0) { return; }
            var listBtn = UISpawn.Widget<WContentListButton>(life,
                UIPrefabId.WContentListButton, block.Container, idx++);
            listBtn.Load(IconId.CConstruct, T.ContainsMaterials, Units.Num(count));
            listBtn.LoadOnToggle(life, (l, container, willShow, wasLoaded) => {
                if (!willShow) { return wasLoaded; }
                if (wasLoaded) { return true; }
                int i = 0;
                for (int m = 0; m < mats.Length; m++) {
                    var mat = mats[m];
                    if (mat.Type.IsConsumedOnConstruct) { continue; }
                    var row = UISpawn.Widget<WIconText2>(l,
                        UIPrefabId.WContentText2, container, i++);
                    row.Load(mat.Type.IconId, mat.Type.NameT, Units.Num(mat.MaxStackSize));
                    var capturedMat = mat;
                    UIUtils.AddTipFunc(l, row, () =>
                        $"{T.OwnedCount}: {S.Sys.Inventory.AvailableCount(capturedMat.Type)}");
                    UIUtils.AddCodexTooltip(l, row.gameObject, row, mat.Type.Def, true);
                }
                return true;
            }, false);
        }
    }
}
