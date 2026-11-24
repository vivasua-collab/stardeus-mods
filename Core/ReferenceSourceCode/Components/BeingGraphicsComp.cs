using System;
using System.Collections;
using Game.Constants;
using Game.Data;
using KL.Randomness;
using Game.Rendering;
using KL.Utils;
using UnityEngine;
using Game.Utils;
using Game.CodeGen;
using Game.Systems.AI;
using Game.Systems.Motion;
using Game.Systems.Rendering;
using Game.Visuals;

namespace Game.Components {
    // CarryController manages multi-carry state for beings
    using Game.Systems.Creatures;
    public sealed class BeingGraphicsComp : BaseComponent<BeingGraphicsComp> {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new BeingGraphicsComp());
        }
        private BeingLooksComp looks;
        public SpeciesLooksDef LooksDef;
        private bool isVertical;
        public bool IsVertical => isVertical;
        private Vector2 lastPosition;
        private Vector2 lastPositionWithoutOffset;
        // Where plants were last brushed (PlantBrush), so it happens about once per tile walked
        private Vector2 lastBrushPos;
        private const float BrushSpacing = 0.8f;
        private Facing.Type facing = Facing.Type.Down;
        public Facing.Type CurrentFacing => facing;
        private Facing.Type lastFacing = Facing.Type.Down;
        public AnimatedSpriteSet SpriteSet;
        private float rotation = 0;
        private bool isHidden;
        public bool IsHidden => isHidden;
        public Vector2 SelectableSize => LooksDef.SelectSize;
        public Vector2 HeadPosition => SpriteSet?.LastRenderedPos ?? Position;
        private static readonly Vector2 horizontalRange = new Vector2(-10, 10);
        private AIAction CurrentAct => Being.AIAgent.CurrentAction;
        private float distSinceLastFrameChange;
        private const float frameChangeDistance = 0.15f;
        /// <summary>
        /// Rider, Target, start/stop
        /// </summary>
        public Action<Being, Being, bool> OnRideChange;
        public void ChangeAnimationFrame(float dist) {
            // D.Warn("dist: {0} [{2}] ({1})", dist, Being, Time.frameCount);
            if (isVertical && SpriteSet != null) {
                distSinceLastFrameChange += dist;
                if (distSinceLastFrameChange >= frameChangeDistance) {
                    distSinceLastFrameChange = 0;
                    // D.Warn("Changing frame for {0} | {1}", Time.frameCount, Being);
                    looks.NextFrame();
                    looks.RebuildSprites();
                    RebuildLooks();
                }
            }
        }
        private Entity carrying;
        public CarryController CarryCtrl { get; private set; }
        // For tests
        public Entity Carrying => carrying;
        public bool IsCarrying() {
            return carrying?.IsActive == true;
        }
        public void ClearCarrying() {
            if (carrying?.IsActive == true) {
                // D.Err("Can only clear carrying if it's inactive, it's not! {0} / {1} / {2}",
                //     carrying, Being, CurrentAct);
            } else {
                carrying = null;
                CarryCtrl?.OnPrimaryCleared();
            }
        }
        // who are we riding
        private Being ridee;
        private Being rider;
        private Vector2 riderOffset;
        private bool isFailed;
        public bool HasRider => rider != null;
        // private float riderZ;
        public bool StartRiding(Being being) {
            // D.Warn("Riding: {0} started riding on {1}", Being, being);
            if (ridee == null) {
                ridee = being;
                being.Graphics.rider = Being;
            } else {
                D.Err("Trying to ride someone while riding someone else already! riding: {0}, trying: {1}",
                    ridee, being);
                return false;
            }
            riderOffset = being.Graphics.LooksDef.RiderOffset;
            if (SpriteSet != null) {
                SpriteSet.RideOffset = riderOffset;
                // SpriteSet.ZOffset = riderZ;
            // } else {
                // this.riderOffset = riderOffset;
                // this.riderZ = riderZ;
            }
            Being.Motion.Flags |= MotionFlags.Riding;
            OnRideChange?.Invoke(Being, being, true);
            return true;
        }

        public void StopRiding() {
            D.Warn("Riding: {0} stopped riding on {1}", Being, ridee);
            if (ridee != null) {
                OnRideChange?.Invoke(Being, ridee, false);
                ridee.Graphics.rider = null;
                ridee = null;
            }
            rotation = 0f;
            SpriteSet.Detach();
            SpriteSet.RideOffset = Consts.V2Zero;
            SpriteSet.ZOffset = 0;
            Being.Motion.Flags &= ~MotionFlags.Riding;
            Being.Motion.TeleportTo(lastPositionWithoutOffset);
        }
        public bool Carry(Being b) {
            #if DEBUG_CARRY
            D.Warn("Started carrying: {0} => {1}", b);
            #endif
            if (b == null) {
                D.Err("Trying to carry a null being! {0} | {1}",
                    CurrentAct, Being.AIAgent.Goal);
                return false;
            }
            if (!b.IsActive) {
                D.Err("Trying to carry a inactive being! {0} {1} | {2}",
                    b, CurrentAct, Being.AIAgent.Goal);
                return false;
            }
            if (carrying == b) {
                D.Err("Trying to carry same being twice! {0} | agent: {1}", b, Being.AIAgent.Goal);
                return true;
            }
            if (b.AIAgent.Goal != null) {
                if (!AIGoal.Interrupt(b.AIAgent.Goal, G.Ticks)) {
                    // may still be stuck in open space
                    if (!b.Motion.IsGrounded || S.Sys.Areas.IslandAt(b.PosIdx) != 0) {
                        // refusing to carry a being with a dropdown
                        D.Warn("Failed to carry a being with a non-interruptable task: {0}/ {1}",
                            b, b.AIAgent.Goal);
                        return false;
                    }
                }
            }
            b.Motion.Abort(T.AdRejInterrupted);
            if (!b.SetCarrier(Being)) {
                D.Err("Failed to set carrier on being: {0}", b);
                return false;
            }
            if (b.TryGetComponent<ParticleTrailComp>(out var tr)) {
                tr.SetHidden();
            }
            b.Motion.Immobilize(ImmobilizeReasonIdH.Carried, true);
            b.Motion.ReleaseClaimedPos();
            b.Motion.Flags |= MotionFlags.Carried;
            b.Graphics.SpriteSet.AttachTo(SpriteSet);
            carrying = b;
            CarryCtrl?.OnPrimarySet(b);
            return true;
        }

        public bool Carry(Entity e) {
            if (e is Being b) {
                return Carry(b);
            } else if (e is Obj o) {
                return Carry(o, false, false);
            }
            D.Err("Trying to carry neither a being nor an obj: {0}", e);
            return false;
        }
        public bool Carry(Obj o, bool skipInventoryCheck, bool ignoreMaxCarryAmount) {
            #if DEBUG_CARRY
            D.Warn("Started carrying: {0} => {1}", Being, obj);
            #endif
            if (o == null) {
                D.Err("Trying to carry a null obj! {0} | {1}",
                    CurrentAct, Being.AIAgent.Goal);
                return false;
            }
            if (!o.IsActive) {
                D.Err("Trying to carry a inactive object! {0} {1} | {2}",
                    o, CurrentAct, Being.AIAgent.Goal);
                return false;
            }
            if (carrying == o) {
                D.Err("Trying to carry same object twice! {0} | agent: {1}", o, Being.AIAgent.Goal);
                return true;
            }
            // FIXME extract common interface for Graphics
            if (o.Pile != null) {
                var pile = o.Pile;
                if (!skipInventoryCheck && pile.IsAddedToInventory && !pile.IsFullyReserved) {
                    D.Err("Carrying pile that is still added to inventory but not fully reserved {0}",
                        CurrentAct);
                    return false;
                }
                if (!ignoreMaxCarryAmount) {
                    // Always happens in relocation tasks
                    D.Ass(pile.StackSize <= Being.Data.StatsBeing.CarryCapacity.ValueInt,
                        "Worker trying to pick up pile that is too big! {0}",
                            CurrentAct);
                }
            } else {
                // Sanity check
                if (!o.Data.IsAvailableIgnoreReservation) {
                    D.Err("Cannot carry an obj that has ObjData with state != Active | Stored! {0} | {1} | {2}", o.Data, o, Being.AIAgent);
                    return false;
                } else if (o.Data.Reservation?.IsForGoal() == true) {
                    if (!o.Data.Reservation.IsFor(Being.AIAgent.Goal)) {
                        D.Err("Cannot carry an obj that has ObjData that is reserved for a different goal! {0}, Agent's goal: {1} | {2}. Reservation: {3}",
                            o.Data, Being.AIAgent.Goal, Being.AIAgent, o.Data.Reservation);
                        return false;
                    }
                }
            }
            if (!o.SetCarrier(Being)) { return false; }
            o.Graphics.OverrideSortOrder(LooksDef.CarriedObjSortOrder(facing));
            o.Graphics.SpriteSet.AttachTo(SpriteSet);
            o.HideAllInfo();
            carrying = o;
            CarryCtrl?.OnPrimarySet(o);
            return true;
        }

        private void UpdateCarriedItem() {
            if (carrying == null) { return; }
            var off = CarryOffset(out var zOff);
            SpriteSet.CarryOffset = off;
            MoveCarriedItem(off, zOff);
            // Being.Worker.UpdateCarryInfo(carrying, true);
            #if !PRODUCTION_BUILD
            if (G.ClockSpeed < ClockSpeed.Unlimited) {
                if (Vectors.SqrDistance(Position, carrying.Position) > 4) {
                    D.Err("Carried object too far away from the carrier! {0} {1} {2} {3}. CombatUnit: {4}",
                        carrying.Id, carrying.Position, Position, CurrentAct,
                        Being.AIAgent.CombatUnit);
                }
            }
            #endif
        }

        public bool IsRiding => ridee != null;

        private void UpdateRiding() {
            if (ridee == null || isFailed) { return; }
            // D.Warn("Ride offset: {0}/{1}", riderOffset, SpriteSet?.RideOffset);
            if (Rng.UChance(0.01f)) {
                rotation = Rng.URange(-10f, 10f);
            }
            if (Being == null) {
                D.Err("Trying to UpdateRiding while Being is null!");
                return;
            }
            if (!Being.IsActive) {
                D.Err("Trying to UpdateRiding while Being is inactive!");
                return;
            }
            if (Being.Motion.IsBusy) {
                D.Err("Riding something while having a path! {0}. Goal: {1}. CurAct: {2}",
                    Being.DefinitionId, Being.AIAgent.Goal, Being.AIAgent.CurrentAction);
                Being.Motion.Fail("Riding something");
            }
            if (ridee == null || !ridee.IsActive) {
                ridee = null;
                return;
            }
            if (ridee.Graphics == null) {
                D.Err("Trying to UpdateRiding while ridee.Graphics is null!");
                return;
            }
            if (ridee == null) {
                D.Err("Ridee is no longer present after MoveBeing! {0}", Being);
                return;
            }
            if (SpriteSet != null && ridee.Graphics.SpriteSet != null) {
                SpriteSet.AttachTo(ridee.Graphics.SpriteSet);
            }
        }


        private void MoveCarriedItem(Vector2 offset, float zOff) {
            var po = Position + offset;

            if (carrying is Being b) {
                b.Motion.TeleportTo(po);
                // MoveBeing(po, b);
            } else if (carrying is Obj o) {
                MoveObject(po, zOff, o);
            }
            CarryCtrl?.UpdateAdditionalPositions(po, zOff);
        }

        private void MoveObject(Vector2 po, float zOff, Obj o) {
            o.Transform.TeleportTo(po);
            o.Graphics.TeleportTo(po);
            o.Graphics.SetZOffset(zOff);
            o.Graphics.SpriteSet.SetSortOrder(S, 0,
                LooksDef.CarriedObjSortOrder(facing));
        }

        public void DropIfCarrying() {
            if (carrying != null) {
                Drop(carrying);
            }
        }

        public void Drop(Entity obj, bool skipInventoryCheck = false) {
            // One of the additional carried items (e.g. destroyed in flight): let go of it
            // alone, dropping the whole stack would leave the primary item attached but
            // no longer tracked as carried
            if (obj != null && obj != carrying && obj is Obj extra
                    && CarryCtrl?.ReleaseAdditional(extra) == true) {
                return;
            }
            CarryCtrl?.DropAllAdditional(skipInventoryCheck);
            if (carrying == null) {
                D.Err("Trying to drop an object that we are not carrying: {0}. Being: {1}", obj, Being);
                return;
            }
            if (carrying != obj) {
                D.Err("Trying to drop something we are not carrying! {0} != {1}. Being: {2}",
                    obj, carrying, Being);
            }
            #if DEBUG_CARRY
            D.Warn("Dropping carried: {0} => {1}", Being, obj);
            #endif

            if (obj == null) {
                D.Err("Trying to drop a null obj! Carrying: {0}. Being: {1}", carrying, Being);
            } else if (!obj.IsActive) {
                D.Warn("Dropping inactive carried obj: {0}. Being: {1}", carrying, Being);
            } else {
                // We may be dropping this while being is hidden
                if (isHidden) {
                    UnhideCarriedObj();
                }
                MoveCarriedItem(Consts.V2Zero, 0);
                if (obj is Obj o) {
                    o.Graphics.RestoreSortOrder();
                    o.Graphics.SpriteSet?.Detach();
                    o.SetCarrier(null);
                    o.StorableObj?.UpdateForbiddenIcon();
                    if (!skipInventoryCheck && o.IsActive) {
                        var mat = o.Pile;
                        if (mat != null) {
                            if (!mat.IsAddedToInventory && mat.StackSize > 0) {
                                D.Err("Dropping something that is not in inventory: {0} -> {1}",
                                    obj, mat);
                                mat.AddToInventory();
                            }
                            if (!EntityUtils.TryMerge(mat)) {
                                o.Transform.UpdateInSpace(true);
                                mat.UpdateForbiddenIcon(true);
                            }
                        }
                    } else {
                        o.Transform.UpdateInSpace(true);
                    }
                } else if (obj is Being b) {
                    b.Graphics.SpriteSet?.Detach();
                    b.SetCarrier(null);
                    b.AIAgent.ClearRoamFailure();
                    b.Motion.Immobilize(ImmobilizeReasonIdH.Carried, false);
                    b.Motion.Flags &= ~MotionFlags.Carried;
                    if (!b.Motion.IsImmobilized) {
                        if (b.TryGetComponent<ParticleTrailComp>(out var tr)) {
                            tr.SetVisible();
                        }
                    }
                    b.Motion.UpdateInSpace(false);
                } else {
                    D.Err("Carried obj is neither Obj nor Being! {0}", obj);
                    obj.SetCarrier(null);
                }
            }
            if (SpriteSet != null) {
                SpriteSet.CarryOffset = Consts.V2Zero;
            }
            carrying = null;
            CarryCtrl?.OnPrimaryCleared();
        }

        public Vector2 CarryOffset(out float zOff) {
            zOff = LooksDef.CarryZ(facing);
            return CarryOffset();
        }

        private Vector2 CarryOffset() {
            var p = LooksDef.CarryOffset;
            return facing switch {
                // FIXME obj should be in front of being
                Facing.Type.Down => p + new Vector2(0f, -0.8f),
                Facing.Type.Up => p + new Vector2(0, -0.2f),
                Facing.Type.Left => p + new Vector2(-0.33f, -0.5f),
                Facing.Type.Right => p + new Vector2(0.33f, -0.5f),
                _ => p,
            };
        }

        // FIXME set different vertical / horizontal reasons
        public void SetVertical(bool isVertical) {
            this.isVertical = isVertical;
            rotation = isVertical ? 0f : Rng.URange(horizontalRange);
            if (SpriteSet != null) {
                RebuildLooks();
                UpdateSpriteSet();
            }
        }

        public void SetRotation(float degrees, bool rebuildLooks) {
            rotation = degrees;
            if (rebuildLooks) {
                UpdatePositionAndFacing(lastPositionWithoutOffset);
            }
        }

        private Vector2 positionOffset = Vector2.zero;
        // How far the being is drawn from where it stands (SetOffset); walking clears it
        public Vector2 PositionOffset => positionOffset;

        public void SetOffset(Vector2 p) {
            // D.Warn("Setting graphics offset for {0}: {1}", Being, p);
            positionOffset = p;
            UpdatePositionAndFacing(Being.Motion.Position.Exact);
            UpdateBeingPosition(Position);
        }

        private void UpdateBeingPosition(Vector2 pos) {
            Being.Position = pos;
            Being.SelectPosition = pos + LooksDef.SelectOffset;
        }

        public Vector2 Position {
            get {
                // if (SpriteSet != null) { return SpriteSet.PositionLerped; }
                return lastPosition;
                // return Being.Motion.Position.Exact;
            }
        }

        private bool IsAnimatingAction;

        public bool PunchAtWithCallback(Vector2 vec, float amt, float inTime,
                float outTime, Action onPeak) {
            if (IsAnimatingAction) {
                return false;
            }
            if (G.ClockSpeed < ClockSpeed.Unlimited) {
                IsAnimatingAction = true;
                vec *= amt;
                Coroutines.StartDirect(PunchAsync(vec, inTime, outTime, EndPunch, onPeak));
            } else {
                onPeak();
            }
            return true;
        }

        public bool Punch(Vector2 vec, float amt, float inTime, float outTime) {
            if (IsAnimatingAction) {
                return false;
            }
            if (G.ClockSpeed < ClockSpeed.Unlimited) {
                IsAnimatingAction = true;
                vec *= amt;
                Coroutines.StartDirect(PunchAsync(vec, inTime, outTime, EndPunch));
            }
            return true;
        }

        public bool Strafe(Vector2 facingDir, float amt,
                float leftTimeIn, float rightTimeIn,
                float leftTimeOut, float rightTimeOut) {
            if (IsAnimatingAction) {
                return false;
            }
            if (G.ClockSpeed < ClockSpeed.Unlimited) {
                facingDir = facingDir.normalized * amt;
                IsAnimatingAction = true;
                Coroutines.StartDirect(StrafeAsync(facingDir,
                    leftTimeIn, rightTimeIn, leftTimeOut, rightTimeOut,
                    EndPunch));
            }
            return true;
        }

        private void EndPunch() {
            IsAnimatingAction = false;
        }
        public IEnumerator StrafeAsync(Vector2 facingDir,
                float leftTimeIn, float rightTimeIn,
                float leftTimeOut, float rightTimeOut, Action onEnd) {
            var z = Consts.V2Zero;
            Vector2 off;
            float punchTime = 0f;
            // Center to Left
            var curDir = Vectors.Rotate(facingDir, z, -90);
            var startState = S;
            while (punchTime < leftTimeIn) {
                punchTime += GameTimer.deltaTime;
                var a = Equations.CubicInOut(punchTime / leftTimeIn);
                off = Vector2.Lerp(z, curDir, a);
                SpriteSet?.SetTempOffset(off);
                if (G.ClockSpeed == ClockSpeed.Unlimited) { yield break; }
                yield return new WaitForEndOfFrame();
                if (!GameState.IsCurrent(startState)) { yield break; }
            }
            punchTime = 0f;
            // Left to center
            while (punchTime < leftTimeOut) {
                punchTime += GameTimer.deltaTime;
                var a = Equations.CubicIn(punchTime / leftTimeOut);
                off = Vector2.Lerp(curDir, z, a);
                SpriteSet?.SetTempOffset(off);
                if (G.ClockSpeed == ClockSpeed.Unlimited) { yield break; }
                yield return new WaitForEndOfFrame();
                if (!GameState.IsCurrent(startState)) { yield break; }
            }
            punchTime = 0f;
            SpriteSet?.SetTempOffset(z);
            // Center to Right
            curDir = Vectors.Rotate(facingDir, z, 90);
            while (punchTime < rightTimeIn) {
                punchTime += GameTimer.deltaTime;
                var a = Equations.CubicOut(punchTime / rightTimeIn);
                off = Vector2.Lerp(z, curDir, a);
                SpriteSet?.SetTempOffset(off);
                if (G.ClockSpeed == ClockSpeed.Unlimited) { yield break; }
                yield return new WaitForEndOfFrame();
                if (!GameState.IsCurrent(startState)) { yield break; }
            }
            punchTime = 0f;
            // Right to center
            while (punchTime < rightTimeOut) {
                punchTime += GameTimer.deltaTime;
                var a = Equations.CubicInOut(punchTime / rightTimeOut);
                off = Vector2.Lerp(curDir, z, a);
                SpriteSet?.SetTempOffset(off);
                if (G.ClockSpeed == ClockSpeed.Unlimited) { yield break; }
                yield return new WaitForEndOfFrame();
                if (!GameState.IsCurrent(startState)) { yield break; }
            }
            SpriteSet?.SetTempOffset(z);
            onEnd();
        }

        private IEnumerator PunchAsync(Vector2 dir, float inTime, float outTime,
                Action onEnd, Action onPeak = null) {
            var z = Consts.V2Zero;
            Vector2 off;
            float punchTime = 0f;
            var startState = S;
            inTime *= Rng.URange(0.8f, 1.2f);
            while (punchTime < inTime) {
                punchTime += GameTimer.deltaTime;
                var a = Equations.CubicInOut(punchTime / inTime);
                off = Vector2.Lerp(z, dir, a);
                SpriteSet?.SetTempOffset(off);
                if (G.ClockSpeed == ClockSpeed.Unlimited) { yield break; }
                yield return new WaitForEndOfFrame();
                if (!GameState.IsCurrent(startState)) { yield break; }
            }
            if (onPeak != null) {
                onPeak();
            }
            punchTime = 0f;
            outTime *= Rng.URange(0.8f, 1.2f);
            while (punchTime < outTime) {
                punchTime += GameTimer.deltaTime;
                var a = Equations.CubicInOut(punchTime / outTime);
                off = Vector2.Lerp(dir, z, a);
                SpriteSet?.SetTempOffset(off);
                if (G.ClockSpeed == ClockSpeed.Unlimited) { yield break; }
                yield return new WaitForEndOfFrame();
                if (!GameState.IsCurrent(startState)) { yield break; }
            }
            SpriteSet?.SetTempOffset(z);
            onEnd();
        }

        public override void OnLateReady(bool wasLoaded) {
            // transform ??= Being.Transform;
            looks = Being.Looks;
            CarryCtrl ??= new CarryController(Being);
            if (!wasLoaded) {
                RebuildLooks();
            }
        }

        public void SetHidden() {
            isHidden = true;
            Transmit(this, MsgIdH.GraphicHidden);
            HideCarriedObj();
            if (SpriteSet != null) {
                if (SpriteSet.IsShowing) {
                    SpriteSet.RemoveFromRenderer(S);
                }
            }
        }

        private void HideCarriedObj() {
            if (carrying != null) {
                if (carrying is Obj o) {
                    o.Graphics.Show(false);
                } else if (carrying is Being b) {
                    b.Graphics.SetHidden();
                }
            }
            if (CarryCtrl != null) {
                for (int i = 0; i < CarryCtrl.AdditionalCount; i++) {
                    CarryCtrl.AdditionalCarried[i]?.Graphics.Show(false);
                }
            }
        }

        public void SetVisible() {
            if (!Being.IsActive) {
                D.Err("Trying to set visible while being is inactive: {0}", Being);
                return;
            }
            UnhideCarriedObj();
            isHidden = false;
            Transmit(this, MsgIdH.GraphicUnhidden);
            if (SpriteSet == null) {
                RebuildLooks();
            } else {
                if (!SpriteSet.IsShowing) {
                    SpriteSet.AddToRenderer(S);
                }
            }
        }

        private void UnhideCarriedObj() {
            if (carrying != null) {
                if (carrying is Obj o) {
                    o.Graphics.Show(true);
                } else if (carrying is Being b) {
                    b.Graphics.SetVisible();
                }
            }
            if (CarryCtrl != null) {
                for (int i = 0; i < CarryCtrl.AdditionalCount; i++) {
                    CarryCtrl.AdditionalCarried[i]?.Graphics.Show(true);
                }
            }
        }

        private bool requestedCustomFacing;
        private Facing.Type customFacing;
        private bool requestedRebuild;

        public void SetFacing(Facing.Type type) {
            // D.Warn("Setting facing: {0}->{1} | {2}", facing, type, Being);
            requestedCustomFacing = true;
            customFacing = type;
            requestedRebuild = true;
            if (type != facing) {
                UpdateSpriteSet();
            }
        }

        public void RebuildLooks(bool force = false) {
            // D.Warn("Rebuilding looks: {0}", Being);
            // if lastFacing is not the same, looks will be rebuilt anyway!
            requestedRebuild = true;
            if (lastFacing == facing) {
                lastFacing = Facing.Opposite(facing);
            }
            if (force) {
                looks.Looks.IsDirty = true;
            }
            // FIXME this produces a bug where the avatar is on the side
            // if (IsAnimatingAction) {
            //     D.Warn("Rebuilding looks while animating action! {0}", Being);
            // }
            if (force) {
                if (Being.Data.Name == "Allen") {
                    D.Warn("Rebuilding looks for Allen!");
                }
                if (SpriteSet == null) {
                    looks.RebuildSprites();
                    UpdateSpriteSet();
                }
                Update(Position - positionOffset);
            }
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.HealthWentToZero) {
                SetVertical(false);
                if (ridee != null) {
                    StopRiding();
                }
            } else if (message == MsgIdH.ImmobilizeStarted) {
                SetVertical(false);
            } else if (message == MsgIdH.ImmobilizeEnded) {
                if (Being.IsAlive && !Being.Data.HasCondition(ConditionIdH.Sleeping)) {
                    SetVertical(true);
                }
            } else if (message == MsgIdH.BeingFinishedLoading) {
                lastPosition = Being.Motion.Position.Exact;
                RebuildLooks(true);
            }
        }

        private float LeaningRot(float velocity, float posDiffX) {
            // Shadow will not rotate
            if (CurrentFacing == Facing.Type.Right) {
                return -5f * velocity;
            } else if (CurrentFacing == Facing.Type.Left) {
                return 5f * velocity;
            }
            var posSign = Math.Sign(posDiffX);
            if (posSign > 0) {
                return -2f * velocity;
            } else if (posSign < 0) {
                return 2f * velocity;
            }
            // The caller adds this to the base rotation, returning it here doubled the tilt
            return 0f;
        }

        private bool wasVertical = true;

        private void ResolveFacing(Vector2 pos, Vector2 lastPosition) {
            if (GameTimer.time < canChangeFacingAfter) {
                return;
            }
            canChangeFacingAfter = GameTimer.time + 0.1f;
            var cu = Being.AIAgent.CombatUnit;
            if (cu.ShouldFaceTarget(pos, lastFacing, out Facing.Type f)) {
                facing = f;
                return;
            }
            if (pos != lastPosition) {
                facing = Facing.FromDirection(pos - lastPosition, lastFacing);
            }
        }

        private float canChangeFacingAfter;
        public int PosInFrontOf {
            get {
                var offset = Facing.ToOffset(CurrentFacing, 1f);
                var pos = Position + offset;
                return EntityUtils.ToPos(pos);
            }
        }

        public void UpdatePositionAndFacing(Vector2 pos) {
            if (isRemoved) {
                D.ErrSoft("Trying to update position and facing when removed {0}", Being);
                return;
            }
            lastPositionWithoutOffset = pos;
            pos += positionOffset;
            /*
            Dwarf fortress mode!
            pos = new Vector2(
                Mathf.Round(pos.x) + 0.5f,
                Mathf.Round(pos.y) + 0.5f);
            */
            if (isVertical) {
                if (lastPosition.x == 0f && lastPosition.y == 0f) {
                    lastPosition = pos;
                } else {
                    ResolveFacing(pos, lastPosition);
                }
            }
            UpdateSpriteSet();
            var velocity = Being.Motion.VelocityPercent;
            var walk = looks.Walk;
            if (walk != null) {
                walk.IsWalking = velocity > 0.0000001f;
            }
            if (velocity > 0.1f && positionOffset != Consts.V2Zero) {
                // D.ErrSoft("Being offset is not zero while moving! [{1}] {0} | {2}",
                //     Being, Pos.String(Being.PosIdx), Being.AIAgent.CurrentAction);
                positionOffset = Consts.V2Zero;
            }
            if (velocity > 0.0000001f) {
                var posDiffX = pos.x - lastPosition.x;
                SpriteSet.Teleport(pos, rotation + LeaningRot(velocity, posDiffX));
                BrushPlants(pos);
            } else {
                SpriteSet.Teleport(pos, rotation);
            }
            lastPosition = pos;
        }

        // Plants next to a walking being sway along its step (PlantBrush). Spaced by distance
        // walked, not by tile: a being wobbling across a tile edge would brush twice.
        private void BrushPlants(Vector2 pos) {
            var step = pos - lastPosition;
            // Further than half a tile in one update is a teleport, not a step
            if (step.sqrMagnitude > 0.25f) {
                lastBrushPos = pos;
                return;
            }
            if ((pos - lastBrushPos).sqrMagnitude < BrushSpacing * BrushSpacing) { return; }
            lastBrushPos = pos;
            PlantBrush.OnStep(pos, step);
        }

        private void UpdateSpriteSet() {
            if (isRemoved) {
                D.Err("Trying to update sprite set after being was removed! {0}", Being);
                return;
            }
            if (requestedCustomFacing) {
                requestedCustomFacing = false;
                facing = customFacing;
            }
            if (requestedRebuild || (lastFacing != facing) || (isVertical != wasVertical)) {
                requestedRebuild = false;
                // if (lastFacing != facing) {
                //     D.Warn("Facing changed: {0}->{1} | {2}", lastFacing, facing, Being);
                // }
                lastFacing = facing;
                wasVertical = isVertical;
                if (SpriteSet != null) {
                    var newSet = looks.SpriteSetFor(facing, isVertical);
                    if (newSet != SpriteSet) {
                        // when SetVertical(true) is invoked, rotation does not
                        // immediately get copied until being starts moving somewhere
                        SpriteSet.Rotation = rotation;
                        SpriteSet.TransferMotion(S, newSet);
                        if (SpriteSet.IsShowing) {
                            SpriteSet.RemoveFromRenderer(S);
                        }
                        SpriteSet = newSet;
                    }
                } else {
                    InitSpriteSet();
                }
                if (!isHidden) {
                    SpriteSet.AddToRenderer(S);
                }
                if (carrying != null) {
                    SpriteSet.CarryOffset = CarryOffset();
                }
            }
            if (SpriteSet == null) {
                InitSpriteSet();
                if (!isHidden) {
                    SpriteSet.AddToRenderer(S);
                }
            }
        }


        private void InitSpriteSet() {
            if (looks.IsFailed) {
                D.Err("Looks failed to build for being {0}, won't init sprite set!",
                    Being);
                isFailed = true;
                return;
            }
            SpriteSet = looks.SpriteSetFor(facing, isVertical);
            if (ridee != null) {
                SpriteSet.RideOffset = riderOffset;
                // SpriteSet.ZOffset = riderZ;
            }
        }

        public void Update() {
            Update(Position - positionOffset);
        }

        public void Update(Vector2 pos) {
            if (looks.Looks.IsDirty) {
                looks.Rebuild();
            }
            UpdatePositionAndFacing(pos);
            UpdateBeingPosition(Position);
            UpdateCarriedItem();
            UpdateRiding();
        }

        public override string ToString() {
            if (!entity.IsActive) {
                return " * BeingGraphics [Inactive]";
            }
            return string.Format(" * BeingGraphics [{0}] (Facing {1})",
                Position, facing);
        }

        private bool isRemoved;

        public override void OnRemove() {
            isRemoved = true;
            // D.Warn("Removing sprite set from renderer: {0}", SpriteSet);
            if (SpriteSet?.IsShowing == true) {
                SpriteSet.RemoveFromRenderer(S);
                SpriteSet = null;
            // } else {
            //     D.Warn("Sprite set was not showing, so not going to remove it: {0}", SpriteSet);
            }
        }

        public void ClearFacingLock() {
            canChangeFacingAfter = 0;
        }



        #if !PRODUCTION_BUILD
        public string GetGraphicsDebugInfo() {
            var sb = Caches.StringBuilder3;
            sb.Clear();
            sb.AppendFormat("=== GRAPHICS DUMP FOR BEING {0} ===\n", Being.Id);
            sb.AppendFormat("Being: {0}\n", Being);
            sb.AppendFormat("BeingData: {0}\n", Being.Data);
            sb.AppendFormat("Graphics.IsHidden: {0}\n", IsHidden);
            sb.AppendFormat("Graphics.IsVertical: {0}\n", IsVertical);
            sb.AppendFormat("Looks: {0}\n", looks.Looks);
            foreach (var lp in looks.Looks.Parts) {
                sb.AppendFormat(" -> {0}\n", lp);
            }
            sb.AppendFormat("SpriteSet: {0}\n", SpriteSet?.DebugInfo);
            if (SpriteSet != null) {
                sb.AppendFormat("Render Slots:\n");
                for (int i = 0; i < SpriteSet.Slots.Length; i++) {
                    var slot = SpriteSet.Slots[i];
                    sb.AppendFormat(" -> Render Slot #{0}: {1}\n", i, slot?.DebugString);
                    GPUSpriteRenderer renderer = null;
                    if (SpriteSet.Sprites[i] != null) {
                        renderer = SpriteSet.GetRendererFor(S, i);
                    }
                    if (renderer != null) {
                        // var shard = renderer.GetShardExisting(slot.Idx, out var slotIdxInShard);
                        sb.AppendFormat(" -> Renderer #{0}: {1}\n", i, renderer.DebugInfo);
                        // sb.AppendFormat(" -> Sprite Renderer #{0}: {1}\n", i, renderer.DebugInfoSpriteRend);
                        // sb.AppendFormat(" -> Renderer Shard: {0}\n", shard.DebugInfo);
                        // sb.AppendFormat(" -> SlotIdx in Shard: {0}\n", slotIdxInShard);
                        // sb.AppendFormat(" -> Slot in Shard: {0}\n", shard.slots[slotIdxInShard].DebugString);
                    } else {
                        sb.AppendFormat(" -> Renderer #{0}: {1}\n", i, null);
                    }
                }
            }
            sb.AppendFormat("=== END GRAPHICS DUMP ===\n");
            return sb.ToString();
        }

        private void AttemptFix() {
            looks.Rebuild(force: true, clearCache: true);
            RebuildLooks(force: true);
        }

        public void AttemptGraphicsFix() {
            looks.Rebuild(force: true, clearCache: true);
            RebuildLooks(force: true);
        }

        public void ClearLooksCache() {
            looks.ClearCache();
        }

        public Vector2 DebugPositionOffset => positionOffset;
        #endif
    }
}
