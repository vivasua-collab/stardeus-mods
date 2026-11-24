using System.Collections.Generic;
using System.Text;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.AI;
using Game.Systems.Inventory;
using Game.Systems.Mental;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Path;
using Game.Systems.Nanobots;

namespace Game.Components {
    public sealed class PendingReplacementComp : BaseComponent<PendingReplacementComp>,
            IConstructable, IUICustomTooltipProvider, IEntityPanelGoalProvider,
            INanobotCompatible {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new PendingReplacementComp());
        }
        public int InPort => Tile.Transform.HasAnyPort ?
            Tile.Transform.AnyPortInFirst : 0;

        public Tile ConstructableTile => Tile;
        private MatDeficitEventHolder deficitEv;
        public bool IsPending;
        public bool WasSuccessfullyReplaced;
        private string targetDefId;
        public string TargetSpacingCategory;
        private Def targetDef;
        public Def TargetDef => targetDef ??=
            targetDefId == null ? null : The.Defs.Get(targetDefId);
        public float WorkDone;
        public int WorkAmount;
        public bool IsFinished => WorkDone >= WorkAmount;
        private Facing.Type facing;
        public AIAgentComp ConstructedBy { get; private set; }
        private Mat[] rawMaterials;
        // public Action<Tile> AfterReplace;
        private MatRequest cachedMatRequest;

        public override void OnRemove() {
            deficitEv.Clear();
        }
        public MatRequest NextMissingMat {
            get {
                if (rawMaterials == null) { return null; }
                for (int i = 0; i < rawMaterials.Length; i++) {
                    Mat mat = rawMaterials[i];
                    var diff = mat.Diff;
                    if (diff > 0) {
                        if (cachedMatRequest == null) {
                            cachedMatRequest = new MatRequest();
                            cachedMatRequest.Requester = this;
                            // Short loads are delivered rather than dropped - see the
                            // same note in ConstructableComp.NextMissingMat
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
                            missingMats.Add(new MatRequest() {
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

        public bool CanShowDetailsWhenUnconstructed => true;

        public bool CanShowDetailsWhenUnreachable => true;

        protected override void OnConfig() {
            deficitEv = new MatDeficitEventHolder(Tile, this, T.JobTypeConstruction);
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetBool("IsPending", IsPending);
            if (IsPending) {
                deficitEv.Save(data);
                data.SetString("TargetSpacingCat", TargetSpacingCategory);
                data.SetString("TargetDefId", targetDefId);
                data.SetInt("Facing", (int) facing);
                data.SetFloat("WorkDone", WorkDone);
                data.SetInt("WorkAmt", WorkAmount);
                data.SetMats("RawMats", rawMaterials);
            }
        }

        protected override void OnLoad(ComponentData data) {
            IsPending = data.GetBool("IsPending", false);
            if (IsPending) {
                deficitEv.Load(data);
                TargetSpacingCategory = data.GetString("TargetSpacingCat", null);
                targetDefId = data.GetString("TargetDefId", null);
                WorkDone = data.GetFloat("WorkDone", 0f);
                WorkAmount = data.GetInt("WorkAmt", Maths.Max(WorkAmount, 500));
                facing = (Facing.Type) data.GetInt("Facing", 0);
                rawMaterials = data.GetMats("RawMats", null);
                if (rawMaterials == null) {
                    D.Err("Failed loading raw materials!");
                }
            }
        }

        public override string ToString() {
            return " * PendingReplacementComp";
        }

        public override void OnLateReady(bool wasLoaded) {
            tunConstructionSpeed = Tunables.ConstructionSpeed;
            if (IsPending) {
                deficitEv.OnLateReady();
                S.Clock.OnNextUpdate(ValidatePending);
            }
        }

        private bool isSwappingGoal;

        private void ValidatePending() {
            if (IsPending) {
                if (!HasGoal) {
                    D.Err("Pending reinstall, but no goal! {0} / {1}",
                        this, Entity);
                    CancelReplacement();
                } else if (TryGetGoal(out var goal)
                        && goal.JobIdH == JobTypeIdH.Construction
                        && NextMissingMat != null) {
                    // Saved by a version that started building with materials missing
                    D.Warn("Replacement was building with materials missing, hauling them first: {0}",
                        Tile);
                    isSwappingGoal = true;
                    try {
                        AIGoal.Cancel(goal, G.Ticks);
                    } finally {
                        isSwappingGoal = false;
                    }
                    if (entity.Goal == goal) {
                        entity.ClearGoal();
                    }
                    WorkDone = 0f;
                    HideInfoBar();
                    if (!CreateHaulGoal()) {
                        CancelReplacement();
                    }
                }
            }
        }

        private int[] previousConnections;

        public void FinishReplacement() {
            Transmit(this, MsgIdH.DeconstructionFinished);
            // 1. remove the original
            int prevConCount = A.Grid.ConnectionsFor(EntityId, Caches.IntArray32);
            if (prevConCount > 0) {
                previousConnections = Arrays.Copy(Caches.IntArray32, prevConCount);
                A.Grid.DisconnectAll(Tile.ENode);
            }
            WasSuccessfullyReplaced = true;
            if (A.InstantBuild) {
                new CmdRemoveTile(Tile, false, false).Execute(S);
            } else {
                Tile.Constructable.BreakDown(1, true, skipDanglingCheck: true);
            }
            A.Grid.RebuildGrid(false);

            var newTile = EntityUtils.CreatePrototype<Tile>(targetDefId);
            newTile.Constructable.ConstructedBy = ConstructedBy;
            newTile.Transform.Rotation = facing;

            // var afterAction = Arrays.IsEmpty(previousConnections) ? AfterReplace :
            //     AfterReplaceWithReconnect;
            S.CmdQ.Enqueue(
                new CmdPlaceTile(Tile.PosIdx, newTile, warn: false, true, true,
                    (newTile) => AfterReplace(Tile, newTile)));
        }

        private void AfterReplace(Tile oldTile, Tile t) {
            if (previousConnections != null) {
                if (t.ENode == null) {
                    // Should be fine if we replace say a door with a window
                    D.Warn("New tile has no Enode after replacement {0}",
                        t.DefinitionId);
                    return;
                }
                if (!t.ENode.IsRegistered) {
                    D.Err("New tile has not initialized Enode: {0}", t.DefinitionId);
                    return;
                }
                for (int i = previousConnections.Length - 1; i >= 0; i--) {
                    int con = previousConnections[i];
                    var node = S.Components.Find<ElectricNodeComp>(con);
                    if (!t.ENode.TryConnectTo(node, out var failR)) {
                        D.Err("Failed restoring a connection after replacement: {0}: {1}->{2}",
                            failR, t, node);
                    }
                }
            }
            previousConnections = null;
            // copy config
            foreach (var comp in oldTile.Components) {
                if (comp is ICopyableComp copyable) {
                    var comp2 = t.GetComponent(comp.NameHash);
                    if (comp2 != null && comp2 is ICopyableComp copyable2) {
                        if (copyable.CanCopyTo(copyable2)) {
                            // D.Warn("Copying config from {0} to {1}", copyable, copyable2);
                            copyable.CopyConfigTo(copyable2);
                        } else {
                            D.Warn("Cannot copy config from {0} to {1} when replacing {2} with {3}",
                                copyable, copyable2, oldTile, t);
                        }
                    }
                }
            }
        }

        public bool ProvideRequestedMat(UnstoredMatComp pile, Being worker,
                AIGoal goal = null) {
            if (rawMaterials == null) {
                D.Err("Providing materials in replacement comp while rawmaterials = null. {0} -> {1}",
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

            D.Ass(missingMatCount > -1, "Missing mats less than 0 in constructable: {0}",
                D.Dump(rawMaterials));
            return missingMatCount < 1;
        }

        public void CancelReplacement() {
            IsPending = false;
            TargetSpacingCategory = null;
            WorkDone = 0f;
            HideInfoBar();
            if (TryGetGoal(out var goal)) {
                // this can get stuck in rare cases
                if (AIGoal.IsCancelled(goal)) {
                    entity.ClearGoal();
                }
            }
            if (Arrays.IsEmpty(rawMaterials)) { return; }
            var matpos = Tile.PosIdx;
            Pos.ForEachAroundRandom(Rng.Unseeded, matpos, 1, false, false, (p) => {
                if (EntityUtils.IsEmptyFloorAt(p, false)) {
                    matpos = p;
                    return false;
                }
                return true;
            });
            foreach (var mat in rawMaterials) {
                if (mat.StackSize < 1) { continue; }
                S.CmdQ.Enqueue(new CmdSpawnMat(mat, matpos, 2f));
            }
            rawMaterials = null;
        }

        private float tunConstructionSpeed = 1f;

        public bool AddWork(AIAgentComp builder, float amount) {
            if (IsFinished) { return true; }
            WorkDone += amount * tunConstructionSpeed;
            Transmit(this, MsgIdH.DeconstructionOngoing);
            if (WorkDone >= WorkAmount) {
                WorkDone = WorkAmount;
                ConstructedBy = builder;
                FinishReplacement();
                return true;
            } else {
                ShowInfoBar(Consts.ColorConstructBar, WorkDone / WorkAmount,
                    Mathf.RoundToInt(WorkDone), WorkAmount, "construction.progress");
            }
            return false;
        }

        public void AddNanobotsEffort(float amt, out AIGoal goal) {
            if (!TryGetGoal(out goal)) { return; }
            if (goal.JobIdH != JobTypeIdH.Construction) { return; }
            if (AddWork(goal.Agent, amt)) {
                goal.ChangeStateTo(AIGoalState.Completed, G.Ticks);
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

        public bool ConfigureFor(Tile tile) {
            rawMaterials = Arrays.Copy(tile.Constructable.RawMaterials);
            targetDefId = tile.DefinitionId;
            targetDef = null;
            facing = tile.Transform.Rotation;
            WorkAmount = tile.Constructable.WorkAmount;
            TargetSpacingCategory = tile.Transform.SpacingCategory;
            deficitEv.Clear();

            if (entity.Goal != null) {
                return false;
            }
            if (!A.InstantBuild) {
                return CreateHaulGoal();
            }
            return true;
        }

        private bool CreateHaulGoal() {
            var g = CreateGoal(JobTypeIdH.Hauling,
                    IconId.CReconstruct, T.Replace, this)
                .WithPathFlags(PathReqFlags.AdjNearby)
                .ByPlayer(true)
                .NanobotCompatible()
                .WithRequiredAbilities(AbilityIdH.Carry, AbilityIdH.DumbLabor)
                .WithWorldToDesired(AIVarsH.IsResourcesDelivered, false, true)
                .WriteInitialState();
            g.CheckDoable = CheckCanHaulMaterials;
            return entity.TrySetGoal(g);
        }

        private double lastTooltipUpdateAt;
        private const double updateRate = 3d;
        private string lastCurrentTooltip;
        private string cachedTooltip;
        public string ShowCustomTooltip(string currentTooltip, InfoItem item) {
            var time = GameTimer.realtimeSinceStartupAsDouble;
            if (lastCurrentTooltip == currentTooltip
                    && lastTooltipUpdateAt + updateRate > time) {
                return cachedTooltip;
            }
            lastTooltipUpdateAt = time;
            lastCurrentTooltip = currentTooltip;
            var sb = Caches.StringBuilder;
            sb.Clear();
            sb.Append(currentTooltip);
            sb.Append("\n");
            AppendReplacementInfo(sb);
            cachedTooltip = sb.ToString();
            return cachedTooltip;
        }

        public void AppendReplacementInfo(StringBuilder sb) {
            if (TargetDef == null) { return; }
            sb.Append(Texts.SubTitle(T.ReplacementInProgress));
            sb.AppendFormat(" {0} {1} {0} {2}", GlyphMap.ArrowRight,
                entity.Definition.CodexLink, TargetDef.CodexLink);
            if (Arrays.IsEmpty(rawMaterials)) { return; }
            sb.Append("\n\n");
            sb.Append(Texts.SubTitle(T.ContainsMaterials));
            for (int i = 0; i < rawMaterials.Length; i++) {
                var mat = rawMaterials[i];
                var amt = Units.XOutOfY(mat.StackSize, mat.MaxStackSize);
                amt = mat.StackSize >= mat.MaxStackSize ? Texts.Green(amt) : Texts.Red(amt);
                sb.AppendFormat("{0}: {1}\n", mat.Type.Def.CodexLink, amt);
            }
            sb.AppendLine();
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
                // A trip also ends as done when it brought part of the materials and the
                // colony has no more (AIActionGatherMats), so the rest is requested again
                if (IsPending && NextMissingMat != null) {
                    if (!CreateHaulGoal()) {
                        D.Err("Failed to order more materials for replacement: {0}", Tile);
                        CancelReplacement();
                    }
                } else if (IsPending) {
                    if (!CreateConstructGoal(goal.Agent)) {
                        D.Err("Failed to order construction - another task was there?");
                        CancelReplacement();
                    }
                }
            }
            // only cancelled is ok here
            if (AIGoal.IsCancelled(goal.State)) {
                if (IsPending && !isSwappingGoal) {
                    CancelReplacement();
                }
            }
        }

        private bool CreateConstructGoal(AIAgentComp forceAgent) {
            if (!IsPending) {
                D.Err("Trying to create construct goal while tile replacment is not pending! {0}",
                    Tile);
                return false;
            }
            if (entity.Goal != null) {
                D.Err("Trying to create construct goal while a goal already exists: {0}",
                    entity.Goal);
                return false;
            }
            var g = CreateGoal(JobTypeIdH.Construction,
                        IconId.CReconstruct, T.Replace, this)
                    .WithPathFlags(PathReqFlags.AdjNearby)
                    .ByPlayer(true)
                    .NanobotCompatible()
                    .WithOverwritePriority(-1)
                    .WithRequiredAbilities(AbilityIdH.Build, AbilityIdH.SkilledLabor)
                    .WithWorldToDesired(AIVarsH.IsConstructed, false, true)
                    .WriteInitialState();
            if (!Jobs.CanDo(forceAgent, JobTypeIdH.Construction)) {
                forceAgent = null;
            }
            if (WorkDone > 0f) {
                D.Warn("Resetting work done for pending replacement comp: {0}", Tile);
                WorkDone = 0f;
            }
            if (!entity.TrySetGoal(g, forceAgent)) {
                D.Err("Failed creating construction goal for {0}", Tile);
                return false;
            }
            return true;
        }

        public void OnGoalLoad(AIGoal goal) {
            if (IsPending) {
                if (goal.JobIdH == JobTypeIdH.Hauling) {
                    goal.CheckDoable = CheckCanHaulMaterials;
                }
            }
        }
    }
}
