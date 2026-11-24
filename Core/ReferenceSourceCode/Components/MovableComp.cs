using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Tools;
using Game.Utils;
using Game.CodeGen;
using UnityEngine;
using KL.Utils;
using Game.Systems.AI;
using KL.Grid;
using Game.Systems.Path;
using Game.Systems.Creatures;
using Game.Systems.Proxy;

namespace Game.Components {
    public sealed class MovableComp : BaseComponent<MovableComp>, IEntityPanelGoalProvider {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new MovableComp());
        }

        public bool IsForbidden;
        public bool IsPendingMove;
        public Vector2 MoveTarget;
        public int MoveTargetPos => Pos.FromVector(MoveTarget);
        private EntityProxy proxy;
        private string proxyTooltip => T.RelocationPending;

        protected override void OnConfig() {
            IsForbidden = Config.GetBool(PropertyIdH.IsForbidden, false);
        }

        public override void OnSave() {
            var data = GetOrCreateData();

            data.SetBool("IsPendingMove", IsPendingMove);
            if (IsPendingMove) {
                data.SetFloat("MoveTargetX", MoveTarget.x);
                data.SetFloat("MoveTargetY", MoveTarget.y);
            }
        }

        protected override void OnLoad(ComponentData data) {
            IsPendingMove = data.GetBool("IsPendingMove", false);
            if (IsPendingMove) {
                var x = data.GetFloat("MoveTargetX");
                var y = data.GetFloat("MoveTargetY");
                if (x < 1f && y < 1f) {
                    // bad coordinate!
                    IsPendingMove = false;
                    D.Err("Refusing to load bad MoveTarget: {0}:{1}. Entity: {2}",
                        x, y, Entity);
                } else {
                    MoveTarget = new(x, y);
                }
            }
        }

        public bool IsReachableForCommonAction =>
            !IsForbidden && !HasAnyGoal
            //&& !entity.HasCancellableAd
            && (Being == null || canMoveBeing)
            && !entity.IsCarriedByAny;
        private bool canMoveBeing {
            get {
                var b = Being;
                // No being, we can move
                // FIXME: this was returning true, comment above said can move - why?
                if (b == null) { return false; }
                if (b.Data.HasStateFlag(BeingState.InDevice)) {
                    // A downed bot on a repair station is the only one that can be
                    // carried out of a device (STA-1724)
                    return (b.IsDead || b.Motion.IsImmobilized)
                        && RepairStationComp.TryGetAt(b.PosIdx, out _);
                }
                // Being dead, we can move
                if (b.IsDead) { return true; }
                if (b.Motion.IsImmobilized) { return true; }
                if (b.AIAgent.Goal?.IsNonInterruptable == true) {
                    D.Warn("Can't move being due non-interruptable goal: {0}: {1}",
                        b, b.AIAgent.Goal);
                    return false;
                }
                // Alien faction, unless a guest stranded on a wreck
                if (!b.Data.EffectiveFaction.IsPlayer
                        && !PassengerUtils.IsGuestOnDerelict(b.Data)) {
                    return false;
                }
                // Flying workers should not be relocatable unless they are offline
                return b.Data.Species.IsBiological || b.Motion.IsGrounded;
            }
        }

        public void BeginRelocation() {
            if (IsForbidden
            // || entity.HasCancellableAd
            ) { return; }
            ToolChoosePos.BeginObj(new List<Vector2>(1) { entity.Position },
                (vec) => {
                    bool ordered = OrderMove(vec);
                    return ordered ? ChoosePosResult.Success : ChoosePosResult.Fail;
                }, CanMoveTo);
        }

        public bool OrderMove(Vector2 pos) {
            if (IsForbidden || !entity.IsActive || entity.Goal != null) {
                return false;
            }
            var canMove = CanMoveTo(pos);
            if (!canMove.yes) {
                return false;
            }
            pos = canMove.adjPos;
            // Don't auto-haul stuff that has been moved by the player
            if (Obj != null) {
                if (Obj.StorableObj != null) {
                    Obj.StorableObj.IsForbidden = true;
                } else if (Obj.Pile != null) {
                    Obj.Pile.IsForbidden = true;
                }
            }
            MoveTarget = pos;
            IsPendingMove = true;
            var goal = CreateGoal(JobTypeIdH.Hauling, IconId.CMove, T.Relocate, this)
                // .WithWorld(AIVarsH.IsNear, false)
                .WithWorldToDesired(AIVarsH.IsMoved, false, true)
                .WithPathFlags(PathReqFlags.AdjNearby)
                .WithRequiredAbilities(AbilityIdH.Carry, AbilityIdH.DumbLabor)
                .ByPlayer(true)
                .WriteInitialState();
            if (Being != null) {
                goal.WithFlag(AIGoalFlags.IgnoreDeadBodies);
            }
            CreateProxyAt(pos);
            return entity.TrySetGoal(goal);
        }

        private void CreateProxyAt(Vector2 pos) {
            if (proxy != null) {
                D.Err("Trying to create a duplicate entity proxy for movable! {0} -> {1}. New pos: {2}",
                    entity, proxy, pos);
                return;
            }
            proxy = EntityProxy.CreateFor(S, entity, pos, proxyTooltip);
        }


        public (bool yes, Vector2 adjPos) CanMoveTo(Vector2 pos) {
            if (pos.x < 1f && pos.y < 1f) { return (false, pos); }
            if (!EntityUtils.IsWithinBounds(pos)) { return (false, pos); }
            var posIdxTo = EntityUtils.ToPos(pos);
            var posIdxFrom = entity.PosIdx;
            if (posIdxTo == posIdxFrom) {
                return (false, pos);
            }
            // why did I have this?
            // if (Vectors.SqrDistance(Entity.Position, pos) < 4) {
            //     return false;
            // }
            if (Being != null) {
                bool isAsimovPass = A.HasAsimovOverride || !Being.Species.IsBiological
                    || Being.IsDead;
                bool canRelocateToSpace = canMoveBeing && isAsimovPass;
                var p = EntityUtils.ToPos(pos);
                if (!EntityUtils.IsEmptyFloorAt(p, canRelocateToSpace)) {
                    var obj = EntityUtils.ParentObjAt(p);
                    if (obj == null || !obj.Passability.IsPassable) {
                        if (obj != null && Being != null &&
                                Being.Data.Species.IsBiological &&
                                Being.Data.Body.IsInjured &&
                                obj.Transform.HasInPort &&
                                obj.HasComponent<AutoDocComp>()) {
                            var adjPos = Pos.ToVector(obj.Transform.InPort);
                            return (true, adjPos);
                        }
                        return (false, pos);
                    }
                }
                if (!isAsimovPass && !Being.Data.Safety.IsActuallySafeAt(EntityUtils.ToPos(pos))) {
                    return (false, pos);
                }
            } else {
                if (!EntityUtils.IsEmptyFloorAt(EntityUtils.ToPos(pos), true)) {
                    return (false, pos);
                }
            }
            //D.Log("Can move to {0}: {1}", pos, res);
            return (true, pos);
        }

        public void UpdateProxyAndTarget(Vector2 newTarget) {
            MoveTarget = newTarget;
            proxy?.UpdatePosition(S, newTarget);
        }

        public EntityProxy RemoveProxy() {
            var pr = proxy;
            proxy = null;
            return pr;
        }

        public void SetProxy(EntityProxy pr) {
            if (proxy != null) {
                D.Err("Trying to set a new EntityProxy on movable! {0} -> {1} | Existing: {2}",
                    entity, proxy, pr);
                return;
            }
            proxy = pr;
            pr.Entity = entity;
            pr.Tooltip = proxyTooltip;
        }

        public void OnGoalChange(AIGoal goal, AIGoalState from, AIGoalState to) {
            if (AIGoal.IsCompleted(to) || AIGoal.IsCancelled(to)) {
                IsPendingMove = false;
                DestroyProxy();
            }
        }

        private void DestroyProxy() {
            if (proxy != null) {
                proxy.Destroy(S);
                proxy = null;
            }
        }

        public override void OnRemove() {
            DestroyProxy();
        }

        public void OnGoalLoad(AIGoal goal) {
            if (!IsPendingMove) {
                D.Err("Loading move goal when move is not pending, cancelling: {0} | {1}",
                    goal, Entity);
                AIGoal.Cancel(goal, G.Ticks, "Invalid state");
            } else {
                CreateProxyAt(MoveTarget);
            }
            // IsPendingMove = true;
        }

        public override string ToString() {
            return " * Movable";
        }
    }
}
