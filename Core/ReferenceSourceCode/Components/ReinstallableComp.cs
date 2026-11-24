using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Tools;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Utils;
using UnityEngine;
using Game.Input;
using System;
using Game.Systems.AI;

namespace Game.Components {
    public sealed class ReinstallableComp : BaseComponent<ReinstallableComp>,
            IAIGoalProvider,
            IUICustomTooltipProvider {
        [SelfInit]
        public static void Register() {
            AddComponentPrototype(new ReinstallableComp());
        }
        public bool IsPendingQuick => SourceId != 0 || TargetId != 0;
        public bool IsPending => (SourceId != 0 && Source != null)
            || (TargetId != 0 && Target != null);
        private bool isMidRelocation;
        public bool IsMidRelocation {
            get => isMidRelocation;
            set {
                if (isMidRelocation != value) {
                    isMidRelocation = value;
                    Transmit(this, MsgIdH.RelocationStateChanged);
                }
            }
        }
        public int SourceId;
        public int TargetId;
        public bool IsForbidden;
        public bool IsAdvanced;
        private ReinstallableComp target;
        public ReinstallableComp Target {
            get {
                if (target == null && TargetId != 0) {
                    target = S.Components.Find<ReinstallableComp>(TargetId);
                    if (target == null) {
                        D.ErrSoft("reinstallable target not found by id: {0}", TargetId);
                        CancelMoveOnSource();
                    }
                }
                if (target != null && !target.Entity.IsActive) {
                    CancelMoveOnSource();
                }
                return target;
            }
        }

        private ReinstallableComp source;
        public ReinstallableComp Source {
            get {
                if (source == null && SourceId != 0) {
                    source = S.Components.Find<ReinstallableComp>(SourceId);
                    if (source == null) {
                        D.Err("Source not found by id: {0}.", SourceId);
                        SourceId = 0;
                        return null;
                    }
                    if (source.Target == null) {
                        // D.Warn("Reinstallable source target is null, restoring! {0}-{1}", source.Tile, Tile);
                        source.TargetId = EntityId;
                    }
                }
                return source;
            }
        }

        protected override void OnConfig() {
            IsForbidden = Config.GetBool(PropertyIdH.IsForbidden, false);
            IsAdvanced = Config.GetBool(PropertyIdH.IsAdvanced, false);
        }

        public static void CopyRelocatable(Tile source, Tile target) {
            for (int i = 0; i < source.Components.Length; i++) {
                IComponent c = source.Components[i];
                if (c is IRelocatable r) {
                    try {
                        r.RelocateTo(target);
                    } catch (Exception ex) {
                        D.LogEx(ex, "Failed copying relocatable comp: {0}",
                            ComponentState.DumpComponentInfo(c));
                    }
                }
            }
        }

        private void CancelMoveOnSource() {
            D.Warn("Cancelling move on source tile: {0}", Tile);
            SetSourceUnbuiltOverride(false);
            if (target != null) {
                if (target.Entity.Goal != null) {
                    AIGoal.Cancel(target.Entity.Goal, G.Ticks);
                }
                target.RemoveTile();
            }
            target = null;
            TargetId = 0;
            HideInfoIcon();
        }

        public void SetSourceUnbuiltOverride(bool active) {
            if (Tile.Passability.IsUnbuiltOverrideActive == active) {
                return;
            }
            Tile.Passability.SetUnbuiltOverride(active);
            var positions = Caches.IntArray128;
            int count = Tile.Transform.GetSubtilePos(positions);
            for (int i = 0; i < count; i++) {
                int pos = positions[i];
                S.Sys.Areas.NeedsRecalc(Tile.Definition.LayerId, pos);
                S.Sig.RecalculatePassability.Send(pos);
            }
        }

        public void FinalizeMove() {
            var src = Source;
            // D.Warn("Finalizing relocation of {0} from {1} to {2}", src.Entity.Definition.Id,
            //     Pos.String(src.Tile.PosIdx), Pos.String(Tile.PosIdx));
            S.Sig.TileRelocated.Send(src.Tile.Id, Tile.Id);
            CopyRelocatable(src.Tile, Tile);
            src.TargetId = 0;
            src.target = null;
            SourceId = 0;
            source = null;
            Tile.Constructable.InstantBuild();
            if (Tile.ENode != null) {
                S.Sys.Particles.ElectricityVFX.RefreshView();
                if (!Tile.ENode.IsConnected) {
                    Tile.ENode.UpdateConnIcon();
                }
            }
            if (Tile.Health.IsDamaged) {
                Tile.Health.CreateRepairGoal();
            }
            new CmdRemoveTile(src.Tile).Execute(S);
            if (S.Query.DeselectIfSelected.Ask(Tile)) {
                EntityUtils.SelectOrFocus(Tile);
            }
            HideInfoIcon();
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetInt("SourceId", SourceId);
            data.SetInt("TargetId", TargetId);
        }

        protected override void OnLoad(ComponentData data) {
            SourceId = data.GetInt("SourceId", SourceId);
            TargetId = data.GetInt("TargetId", TargetId);
        }

        public override string ToString() {
            return "Reinstallable";
        }

        public override void OnLateReady(bool wasLoaded) {
            if (SourceId == 0 && TargetId == 0) {
                return;
            }
            if (!wasLoaded) {
                TryCreateRelocateGoal();
            } else if (TargetId == 0) {
                S.Clock.OnNextUpdate(ValidateSource);
            }
        }

        private void ValidateSource() {
            if (Source == null) {
                D.Err("Source is missing for reinstallable target at {0}, removing",
                    Pos.String(Tile.PosIdx));
                RemoveTile();
            }
        }

        private bool TryCreateRelocateGoal() {
            if (entity.Goal != null) { return false; }
            if (Tile.IsImmutable) { return false; }
            if (Source == null) {
                D.Err("Reinstallable Source is null {0} {1}!", Entity, this);
                RemoveTile();
                return false;
            }
            // Goal is created on the target tile (this), but reachability must be evaluated
            // at the SOURCE: the worker walks to the source first, and picking it up clears
            // any blockage (e.g. a door sealed by the source object) that would otherwise
            // make the target unreachable. See AIGoal.ReachEntity.
            var goal = CreateGoal(JobTypeIdH.Hauling, IconId.CMove, T.Relocate, this)
                .WithWorldToDesired(AIVarsH.IsReinstalled, false, true)
                .ByPlayer(true)
                .WithRequiredAbilities(AbilityIdH.Carry, AbilityIdH.DumbLabor)
                .WithReachEntity(Source.Entity)
                .WriteInitialState();
            return entity.TrySetGoal(goal);
        }

        public override void OnRemove() {
            if (SourceId != 0) {
                var relocationSource = source
                    ?? S.Components.Find<ReinstallableComp>(SourceId);
                if (relocationSource?.Entity.IsActive == true) {
                    relocationSource.SetSourceUnbuiltOverride(false);
                }
            }
            if (IsPending && Target != null) {
                // somoene removed the source while relocation was pending
                D.Warn("Cancelling goal on target: {0}", Pos.String(
                        Target.Tile.PosIdx));
                var tg = Target.entity.Goal;
                if (tg != null && tg.JobIdH == JobTypeIdH.Hauling) {
                    AIGoal.Cancel(tg, G.Ticks);
                }
            } else {
                // Backup recovery
                var found = S.Components.GenFindAllMatching<ReinstallableComp>(
                    r => r.SourceId == EntityId);
                if (found != null) {
                    foreach (var existing in found) {
                        // A cancelled ghost is already queued for removal
                        if (existing.isTileRemoved) { continue; }
                        //D.ErrSoft("Removing existing reinstallable at {0}",
                            //Pos.String(existing.Tile.PosIdx));
                        existing.RemoveTile();
                    }
                }
            }
        }

        public bool IsReachableForCommonAction => CanOrderRelocationNow &&
            (!IsAdvanced || S.Research.IsUnlocked(DefIdsH.ResearchConstructionRelocationAdvanced));
        public bool CanOrderRelocationNow => !IsForbidden && !HasGoal
            && !Tile.IsImmutable && Tile.IsConstructed && !IsMidRelocation;

        // Called on the source. The ghost removes itself and unlinks the source in OnGoalChange
        public void CancelPendingRelocation() {
            if (Target?.TryGetGoal(out var goal) == true) {
                AIGoal.Cancel(goal, G.Ticks, T.AdRejCancelled);
            }
        }

        private bool isTileRemoved;

        private void RemoveTile() {
            if (!Ready.Rendering) {
                D.Err("Check if tile was removed: {0}", Tile);
                EntityUtils.CameraFocusOn(Tile);
                // Ready.WhenRendering(this, "Remove tile when rendering",
                //     RemoveTile);
            }
            if (isTileRemoved) {
                D.Err("Removing tile twice in reinstallable! {0}", Tile);
                return;
            }
            isTileRemoved = true;
            S.CmdQ.Enqueue(new CmdRemoveTile(
                entity.PosIdx, entity.Definition.LayerId));
        }

        // private void BeginRelocation() {
        //     if (IsAdvanced) {
        //         var canRelocateAdvanced = A.InstantBuild || S.BB.Get(BBKeyH.RelocationAdvanced, false);
        //         if (!canRelocateAdvanced) {
        //             S.Sig.ShowErrorMessage.Send(
        //                 T.ResearchRequired_V1.F(Texts.LinkToResearch(
        //                     The.Defs.FastGet(DefIdsH.ResearchConstructionRelocationAdvanced))));
        //             return;
        //         }
        //     }
        //     BeginRelocation(-1);
        // }
        public bool BeginRelocation(int pos = -1, bool isMassRelocation = false,
                Facing.Type? targetRotation = null) {
            if (!CanOrderRelocationNow) {
                UISounds.PlayActionDenied();
                return false;
            }
            if (IsAdvanced) {
                var canRelocateAdvanced = A.InstantBuild || S.BB.Get(BBKeyH.RelocationAdvanced, false);
                if (!canRelocateAdvanced) {
                    var sb = Caches.StringBuilder2;
                    sb.Clear();
                    sb.Append(T.OperationForbidden);
                    sb.Append(": ");
                    sb.Append(T.Relocate);
                    sb.Append(": ");
                    sb.Append(Tile.Definition.CodexLink);
                    sb.Append(GlyphMap.BRHR);
                    sb.AppendFormat(T.ResearchRequired_V1, S.Research.TreePathFor(
                        The.Defs.FastGet(DefIdsH.ResearchConstructionRelocationAdvanced)));
                    S.Sig.ShowErrorMessage.Send(sb.ToString());
                    return false;
                }
            }
            if (Tile.TryGetComponent<IWithFaction>(out var withFaction)) {
                if (withFaction.Faction?.IsPlayer == false) {
                    UISounds.PlayActionDenied();
                    return false;
                }
            }
            if (!Tile.Constructable.IsAllWorkDone) {
                UISounds.PlayActionDenied();
                return false;
            }
            // A pending relocation is replaced once the new spot is placed (ToolPlaceTiles.Reinstall)
            var tool = ToolPlaceTiles.StartBuilding(entity.DefinitionId);
            tool.Reinstallable = this;
            tool.IsMassReinstall = isMassRelocation;

            if (Tile.Transform.IsRotatable) {
                tool.SetRotation(targetRotation ?? Tile.Transform.Rotation);
            }

            if (pos != -1) {
                var p = EntityUtils.CenterOf(pos);
                tool.OnHover(p);
                tool.OnClickDown(p);
                tool.OnClickUp(p, p, 0);
                return true;
            }
            return false;
        }

        private string cachedTooltip;
        private string lastCurrentTooltip;
        private double lastTooltipUpdateAt;
        private const double updateRate = 1d;
        public string ShowCustomTooltip(string currentTooltip, InfoItem item) {
            if (!IsPending || entity.Goal == null) {
                cachedTooltip = null;
                lastCurrentTooltip = null;
                return currentTooltip;
            }
            var time = Time.realtimeSinceStartupAsDouble;
            if (lastCurrentTooltip == currentTooltip
                    && lastTooltipUpdateAt + updateRate > time) {
                return cachedTooltip;
            }
            lastTooltipUpdateAt = time;
            var sb = Caches.StringBuilder;
            sb.Clear();
            sb.Append(currentTooltip);
            sb.AppendFormat("<color={0}>", Texts.TMPColorYellow);
            if (Tile.Constructable.FlyingOnly) {
                sb.Append("\n  * ");
                sb.Append(T.ReqNavFlight);
            }
            sb.Append("</color>");
            cachedTooltip = sb.ToString();
            return cachedTooltip;
        }

        public void OnGoalChange(AIGoal goal, AIGoalState from, AIGoalState to) {
            if (AIGoal.IsCancelled(to)) {
                var src = Source;
                if (src != null) {
                    src.SetSourceUnbuiltOverride(false);
                    src.TargetId = 0;
                    // A stale cache would make the next relocation's Target return this ghost
                    src.target = null;
                    src.HideInfoIcon();
                    RemoveTile();
                }
            }
        }

        public void OnGoalLoad(AIGoal goal) {
            var src = Source;
            if (src != null) {
                src.ShowInfoIcon(IconId.CMove, T.RelocationPending);
                // ReachEntity is transient — restore it so the bidding/auction gates
                // evaluate reachability at the source, not this (target) tile.
                goal.ReachEntity = src.Entity;
            } else {
                D.Err("Failed to find source on goal load! {0}. Relocation may be inconsistent!",
                    Entity);
                RemoveTile();
            }
        }
    }
}
