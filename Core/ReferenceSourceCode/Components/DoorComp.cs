using System;
using Game.Constants;
using Game.Data;
using Game.Systems.Motion;
using Game.Systems.Path;
using Game.Systems.UIUX;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;
using Game.Systems.Atmo;
using Game.Systems.Notify;

using Game.Utils;
namespace Game.Components {
    public sealed class DoorComp : BaseComponent<DoorComp>, IHullIntegrityProvider,
            ITickListener, ICopyableComp, IDefPowerProvider {
        private const float DefaultLockedConsumption = 1f;
        private const float DefaultRegularConsumption = 0.1f;
        private const float DefaultOpConsumption = 1f;

        // Idle open or closed .. moving or locked
        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            var locked = config.GetFloat(PropertyIdH.EnergyConsumptionWhenLocked,
                DefaultLockedConsumption);
            var regular = config.GetFloat(PropertyIdH.EnergyConsumptionRegular,
                DefaultRegularConsumption);
            var op = config.GetFloat(PropertyIdH.EnergyConsumptionOperational,
                DefaultOpConsumption);
            power.AddConsumption(Mathf.Min(regular, Mathf.Min(locked, op)),
                Mathf.Max(regular, Mathf.Max(locked, op)));
        }
        private const int ClosedFrame = 0;
        private const int OpenFrame = 3;
        private const long DoorAutoCloseDelay = Consts.TicksPer2Minutes;
        private const float doorOpenSpeed = 1f;
        private const float doorBaseDifficulty = 0.2f;
        public float Integrity {
            get {
                if (framePosition == ClosedFrame) {
                    return 1f;
                }
                if (IsAirlock && IsOperational) {
                    // to prevent oxygen from being squashed
                    return 0.999f;
                }
                return minIntegrity;
            }
        }
        private NavPermission deny;
        public NavPermission EffectiveDeny {
            get {
                if (IsForcedOpen || !Tile.IsConstructed) {
                    return NavPermission.None;
                }
                if (!Tile.ENode.IsPowerable) {
                    return NavPermission.None;
                }
                if (IsStuckOpen) {
                    return deny & ~NavPermission.Enemy;
                }
                return deny;
            }
        }
        // An open door that cannot close (a body in the doorway, a power deficit) does not keep
        // enemies out. They used to see the crew through it and never get in (STA-1657).
        // Friendly restrictions still hold, and someone passing through does not count
        private const long StuckOpenAfterBlockedTicks = Consts.TicksPer15Minutes;
        private long blockedSinceTick = -1;
        private Being blockedBy;
        // Stays until the door closes: an enemy coming through stands in the doorway too, and
        // counting it as a new blocker shut the door on the raid again
        private bool isBlockedOpen;
        private bool IsStuckOpen => currentAnimationFrame != ClosedFrame
            && (isBlockedOpen || !IsOperational);

        private void ClearBlocked() {
            blockedSinceTick = -1;
            blockedBy = null;
            isBlockedOpen = false;
        }
        private int wantedAnimationFrame;
        private int currentAnimationFrame;
        private float framePosition;
        // Door graphics should have 4 frames, from fully closed to fully open
        private TileGraphicsComp graphics;
        public DoorLockState State;
        public bool IsForcedOpen;
        public int OpenCount;
        public int CloseCount;
        public bool IsAirlock;
        private long openedRequestedAtTick;
        private float minIntegrity;

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new DoorComp());
        }
        public bool IsExposedToOpenSpace() {
            if (!Tile.IsConstructed) { return true; }
            if (IsClosed) { return false; }
            var p = Tile.PosIdx;
            var n = Pos.N(p);
            var e = Pos.E(p);
            var s = Pos.S(p);
            var w = Pos.W(p);
            var a = S.Sys.Areas;
            var aN = a.At(n);
            if (aN == 0) { return true; }
            var aW = a.At(w);
            if (aW == 0) { return true; }
            var aE = a.At(e);
            if (aE == 0) { return true; }
            var aS = a.At(s);
            if (aS == 0) { return true; }
            return false;
        }

        public bool IsLocked => State == DoorLockState.Locked;
        private BBool lockedB;
        public BBool LockedB => lockedB ??= new(IsLocked);
        public bool IsOperational => elNode == null || (elNode.IsPowerable && !elNode.HasDeficit);
        public NavPermission Deny => deny;
        public bool ArePermissionsUnlocked => S.BB.Get(BBKeyH.DoorPermissionsUnlocked, false) || A.InstantBuild;

        public void Lock(bool check) {
            if (check && !IsOperational) {
                WPopup.Warning(T.CannotChangeLockState, T.DoorNotPowered);
                return;
            }
            State = DoorLockState.Locked;
            IsForcedOpen = false;
            wantedAnimationFrame = ClosedFrame;
            if (Tile.IsConstructed) {
                Tile.Passability.Update(false, false, 1);
                EntityUtils.TriggerChange(Tile);
                UpdateInfoIcon();
            }
            Tile.Passability.UpdateDeny(EffectiveDeny);
            lockedB?.Set(IsLocked);
        }

        private void UpdateInfoIcon() {
            if (IsLocked) {
                ShowInfoIcon(IconId.CLocked, T.InfoDoorLocked);
            } else {
                var d = EffectiveDeny;
                if (d != NavPermission.None && d != NavPermission.Enemy) {
                    ShowInfoIcon(IconId.CKey, PathNode.DenyStr(deny));
                } else {
                    HideInfoIcon();
                }
            }
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetInt("Deny", (int) deny);
            data.SetInt("WantedAnimFrame", wantedAnimationFrame);
            data.SetInt("CurrentAnimFrame", currentAnimationFrame);
            data.SetInt("State", (int) State);
            data.SetBool("IsForcedOpen", IsForcedOpen);
        }

        // For procgen
        public void SetClosed(bool closed) {
            var frame = closed ? ClosedFrame : OpenFrame;
            // currentAnimationFrame is left to UpdateDoorAnimationFrame: set here, the update
            // saw no change and skipped the sprite, cover and hull integrity, so a door closed
            // during play kept leaking like an open one
            framePosition = wantedAnimationFrame = frame;
            if (closed) {
                IsForcedOpen = false;
            }
            UpdateDoorAnimationFrame();
        }

        protected override void OnLoad(ComponentData data) {
            deny = (NavPermission) data.GetInt("Deny", 0);
            wantedAnimationFrame = data.GetInt("WantedAnimFrame", OpenFrame);
            currentAnimationFrame = data.GetInt("CurrentAnimFrame",
                wantedAnimationFrame);
            State = (DoorLockState) data.GetInt("State", (int) DoorLockState.Unlocked);
            IsForcedOpen = data.GetBool("IsForcedOpen", false);
        }

        public void Unlock(bool check) {
            if (check && !IsOperational) {
                WPopup.Warning(T.CannotChangeLockState, T.DoorNotPowered);
                return;
            }
            State = DoorLockState.Unlocked;
            Tile.Passability.Update(true, true, doorBaseDifficulty);
            EntityUtils.TriggerChange(Tile);
            UpdateInfoIcon();
            lockedB?.Set(IsLocked);
        }

        public bool IsOpen => graphics.AnimationFrame == OpenFrame;
        // 0 closed .. 1 fully open, by animation frame
        public float OpenFraction => Mathf.Clamp01(currentAnimationFrame / (float) OpenFrame);
        public bool IsClosed => graphics.AnimationFrame == ClosedFrame;


        private float lockedConsumption;
        private float regularConsumption;
        private float opConsumption;
        public float MaxConsumption { get; private set; }
        public bool IsCopyable => true;
        public string CopyConfigText => T.Door;
        private int forceOpenTicks;

        public bool TryOpen(MotionControl opener) {
            if (IsLocked) { return false; }
            if (opener?.CheckPermissions(EffectiveDeny) == false) {
                return false;
            }
            if (wantedAnimationFrame != OpenFrame) {
                wantedAnimationFrame = OpenFrame;
                OpenCount++;
            }
            openedRequestedAtTick = G.Ticks;
            if (!IsOperational) {
                // This is bad because then animals get stuck as PF system
                // still sees the door as passable for them
                // if (opener?.Being.Species.IsAnimal == true) {
                //     return false;
                // }
                if (forceOpenTicks++ > Consts.TicksPerMinute) {
                    ConvergeDoor();
                    forceOpenTicks = 0;
                }
            }
            return true;
        }

        private bool TryClose() {
            var beingInDoorway =
                S.Beings.FindClosestVisual(
                    entity.Position, boundsSize: 0.9f, includeProxy: false);
            if (beingInDoorway == null) {
                hasSomeoneInDoorway = false;
                ClearBlocked();
                CloseCount++;
                wantedAnimationFrame = ClosedFrame;
                forceOpenTicks = 0;
                RefreshDeny();
                return true;
            } else {
                hasSomeoneInDoorway = true;
                // checked every 10 ticks: two people passing at the wrong moments are not a blocker
                if (blockedSinceTick < 0 || blockedBy != beingInDoorway) {
                    blockedSinceTick = G.Ticks;
                    blockedBy = beingInDoorway;
                } else if (G.Ticks - blockedSinceTick >= StuckOpenAfterBlockedTicks) {
                    isBlockedOpen = true;
                }
            }
            return false;
        }

        private void RefreshDeny() {
            if (Tile.Passability.UpdateDeny(EffectiveDeny)) {
                UpdateInfoIcon();
            }
        }

        private int registeredPos;

        public bool IsDeniedFor(MotionControl motion) {
            return !motion.CheckPermissions(EffectiveDeny);
        }

        private void RegisterDoor() {
            if (registeredPos == 0) {
                registeredPos = Tile.PosIdx;
                S.Sys.Areas.DoorCheck.RegisterDoor(registeredPos, this);
            }
        }

        public void UpdateRegisteredPos() {
            registeredPos = Tile.PosIdx;
        }

        public void MarkUnregistered() {
            registeredPos = 0;
        }

        private void UnregisterDoor() {
            if (registeredPos != 0) {
                S.Sys.Areas.DoorCheck.UnregisterDoor(registeredPos, this);
                registeredPos = 0;
            }
        }

        public override void OnReady(bool wasLoaded) {
            if (currentAnimationFrame != ClosedFrame) {
                Tile.Transform.CoverPercent = 25;
            }
            graphics = Tile.Graphics;
        }

        public override void OnLateReady(bool wasLoaded) {
            extraInfo = Entity.GetRequiredComponent<ExtraInfoComp>(this);
            framePosition = currentAnimationFrame;
            var minInsulAirtight = Mathf.Min(Tile.Integrity.MaxInsulation, Tile.Integrity.MaxAirtight);
            if (minInsulAirtight > 0f) {
                minIntegrity = 0.05f / minInsulAirtight;
            }
            if (IsConstructed) {
                RegisterDoor();
                UpdateDoorAnimationFrame(true);
            }
            if (wasLoaded && IsConstructed) {
                if (State == DoorLockState.Locked) {
                    Lock(false);
                } else {
                    Unlock(false);
                    if (IsForcedOpen) {
                        ForceOpen(true);
                    }
                }
            }
        }

        public void CheckDenyDefaults() {
            if (deny == NavPermission.None) {
                if (Tile.IsConstructed && S.BB.Get(BBKeyH.DoorPermissionsUnlocked, false)) {
                    deny = NavPermission.Enemy;
                }
            }
            Tile.Passability.UpdateDeny(EffectiveDeny);
        }


        protected override void OnConfig() {
            IsAirlock = Config.GetBool(PropertyIdH.IsAirlock, false);
            lockedConsumption = Config.GetFloat(PropertyIdH.EnergyConsumptionWhenLocked,
                DefaultLockedConsumption);
            regularConsumption = Config.GetFloat(PropertyIdH.EnergyConsumptionRegular,
                DefaultRegularConsumption);
            opConsumption = Config.GetFloat(PropertyIdH.EnergyConsumptionOperational,
                DefaultOpConsumption);
            MaxConsumption = Mathf.Max(lockedConsumption, opConsumption);
        }

        private bool isShowingDeficitInfo;

        private bool hasSomeoneInDoorway;

        public void DetectBeing(Being being) {
            hasSomeoneInDoorway = true;
        }

        private long nextBlockCheckTick;
        private bool isUnblocked = true;

        private bool CheckIfUnblocked(long ticks) {
            if (ticks < nextBlockCheckTick) { return isUnblocked; }
            if (!Ready.Pathfinding) { return isUnblocked; }
            nextBlockCheckTick = ticks + Rng.URange(Consts.TicksPerHour, Consts.TicksPer2Hours);
            var pos = Tile.PosIdx;
            int passableNeighbors = 0;
            for (int i = 0; i < 4; i++) {
                if (IsPassable(Pos.NeighborAt(pos, i))) {
                    passableNeighbors++;
                }
            }
            isUnblocked = passableNeighbors > 1;
            return isUnblocked;
        }

        private NotificationHandle blockedNotification;
        private ExtraInfoComp extraInfo;

        private bool IsPassable(int pos) {
            return S.Sys.Path.Nodes[pos].IsPassable;
        }

        // FIXME optimize. what to optimize?
        public void Tick(long ticks) {
            D.Ass(Tile.ENode != null, "Enode is null! {0}", Tile);
            if (!Tile.IsConstructed) { return; }
            if (CheckIfUnblocked(ticks)) {
                if (blockedNotification.IsValid) {
                    Notification.Remove(ref blockedNotification);
                    extraInfo?.HideAllInfo();
                }
            } else {
                if (!blockedNotification.IsValid) {
                    blockedNotification = Notification.BuildFor(Tile, null, T.BlockedDoor,
                        0, NotificationType.Warning)
                        .Group("door_blk")
                        .Sticky()
                        .Show();
                    extraInfo?.ShowInfoIcon(IconId.CWarning, T.BlockedDoor);
                }
            }
            var isOp = IsOperational;
            if (isOp) {
                if (hasSomeoneInDoorway && wantedAnimationFrame == ClosedFrame) {
                    wantedAnimationFrame = OpenFrame;
                }
                ConvergeDoor();
            }
            // a deficit starting or ending, and a door staying blocked, send no message
            RefreshDeny();

        }

        private void ConvergeDoor() {
            if (wantedAnimationFrame == currentAnimationFrame) {
                return;
            }
            framePosition = Mathf.MoveTowards(framePosition,
                wantedAnimationFrame, doorOpenSpeed);
            // Play door open sound if camera zoom is very close
            // FIXME: the sound is horrible, and the logic isn't great.
            // if there's going to be a new sound, likely the distance should be
            // shorter, and the sound should only be played if clock speed is
            // normal, definitely not at 10x
            // if (RenderUtils.CameraOrtho < 10f) {
            //     Sounds.PlayDoorOpen(Tile.Position);
            // }
            UpdateDoorAnimationFrame();
        }

        private void UpdateDoorAnimationFrame(bool force = false) {
            var newFrame = Mathf.RoundToInt(framePosition);
            if (!force && currentAnimationFrame == newFrame) {
                return;
            }
            currentAnimationFrame = newFrame;
            graphics?.SetAnimationFrame(currentAnimationFrame);
            if (currentAnimationFrame == ClosedFrame) {
                ClearBlocked();
                Tile.Transform.CoverPercent = 100;
                S.Sig.CoverChanged.Send(Tile, true);
            } else if (currentAnimationFrame == OpenFrame) {
                Tile.Transform.CoverPercent = 25;
                S.Sig.CoverChanged.Send(Tile, true);
            }
            Transmit(this, MsgIdH.HullIntegrityChanged);
            // Light through the doorway follows the door as it opens and closes
            S.Sys.Areas?.LightPass?.Refresh(Tile);
            if (Tile.Passability.UpdateDeny(EffectiveDeny)) {
                UpdateInfoIcon();
            }
            if (currentAnimationFrame != ClosedFrame) {
                OxygenateDoor();
            }
        }

        private void OxygenateDoor() {
            // We are opening the door
            // let's fill it up with oxygen
            var p = Tile.PosIdx;
            if (IsAirlock && IsOperational) {
                FillAirlock(p);
                return;
            }
            // D.Warn("oxygenating door at ({0})", Pos.String(p));
            switch (Tile.Transform.Rotation) {
                case Facing.Type.Down:
                case Facing.Type.Up: {
                        var oxyN = S.GetO2(Pos.N(p));
                        var oxyS = S.GetO2(Pos.S(p));
                        if (oxyN > 0 || oxyS > 0) {
                            // D.Warn("Oxygenating door at {0} with N{1} or S{2}", Pos.String(p), oxyN, oxyS);
                            S.SetO2(p, Math.Max(oxyN, oxyS));
                        }
                        break;
                    }
                case Facing.Type.Right:
                case Facing.Type.Left: {
                        var oxyE = S.GetO2(Pos.E(p));
                        var oxyW = S.GetO2(Pos.W(p));
                        if (oxyE > 0 || oxyW > 0) {
                            // D.Warn("Oxygenating door at {0} with E{1} or E{2}", Pos.String(p), oxyE, oxyW);
                            S.SetO2(p, Math.Max(oxyE, oxyW));
                        }
                        break;
                    }
            }
        }

        public override string ToString() {
            var animFrame = graphics != null ? graphics.AnimationFrame : -1;
            return string.Format(" * Door: {0} (Frame: c{1} w{2})",
                State, animFrame, wantedAnimationFrame);
        }

        public bool SetLockedByUI(bool locked) {
            if (locked) {
                Lock(true);
            } else {
                Unlock(true);
            }
            return true;
        }

        public bool TogglePermission(NavPermission perm) {
            deny ^= perm;
            Tile.Passability.UpdateDeny(EffectiveDeny);
            UpdateInfoIcon();
            return true;
        }

        private string IconForPerm(NavPermission perm) {
            if ((deny & perm) == perm) {
                return IconId.CCross;
            }
            return IconId.CCheck;
        }

        public bool IsPermOn(NavPermission perm) {
            return !IsLocked && (deny & perm) != perm;
        }

        public string IconForPermission(NavPermission perm) {
            return IconForPerm(perm);
        }

        // public for tests
        public bool ForceOpen(bool open) {
            IsForcedOpen = open;
            Tile.Passability.UpdateDeny(EffectiveDeny);
            if (open) {
                TryOpen(null);
                ShowInfoIcon(IconId.COpenDoor, T.InfoDoorHeldOpen);
            } else {
                UpdateInfoIcon();
            }
            return true;
        }

        public override void OnRemove() {
            UnregisterDoor();
        }

        private ElectricNodeComp elNode;
        public ElectricNodeComp ENode => elNode;

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp elnode) {
                    elNode = elnode;
                    elnode.AddAfterTick(this, AfterTickGrid);
                    elnode.SetConsumerWantedInput(wantedConsumption);
                    AfterTickGrid();
                    if (Tile.Passability.UpdateDeny(EffectiveDeny)) {
                        UpdateInfoIcon();
                    }
                }
            } else if (message == MsgIdH.ConstructionFinished) {
                RegisterDoor();
                var wantedFrame = Ready.Gen ? OpenFrame : ClosedFrame;
                framePosition = wantedAnimationFrame = currentAnimationFrame = wantedFrame;
                UpdateDoorAnimationFrame(force: true);
                CheckDenyDefaults();
                UpdateInfoIcon();
            } else if (message == MsgIdH.ElectricNodeSwitchedOff
                    || message == MsgIdH.ElectricNodeConnectionChanged
                    || message == MsgIdH.ElectricNodeSwitchedOn) {
                if (Tile.Passability.UpdateDeny(EffectiveDeny)) {
                    UpdateInfoIcon();
                }
                Transmit(this, MsgIdH.HullIntegrityChanged);
            }
        }

        private float wantedConsumption =>
            IsLocked ? lockedConsumption :
                ((IsClosed || IsOpen) ? regularConsumption : opConsumption);
        private void AfterTickGrid() {
            if (!elNode.IsPowerable) { return; }
            if (IsOpen && !IsForcedOpen &&
                    G.Ticks > openedRequestedAtTick + DoorAutoCloseDelay) {
                TryClose();
            }


            var consumption = wantedConsumption;

            if (IsAirlock && framePosition != ClosedFrame && IsOperational) {
                // re-oxygenate constantly if it's an airlock
                OxygenateDoor();
            }

            if (elNode.Node.Consumed > consumption - math.EPSILON) {
                if (isShowingDeficitInfo) {
                    isShowingDeficitInfo = false;
                    //HideInfoIcon();
                    if (IsLocked) {
                        ShowInfoIcon(IconId.CLocked, T.InfoDoorLocked);
                    } else if (IsForcedOpen) {
                        ShowInfoIcon(IconId.COpenDoor, T.InfoDoorHeldOpen);
                    }
                }
                // keeping the door locked
                elNode.SetConsumerWantedInput(consumption);
                return;
            }
            elNode.SetConsumerWantedInput(consumption);
        }

        public bool CanCopyTo(ICopyableComp target) {
            return target is DoorComp;
        }

        public bool CopyConfigTo(ICopyableComp target) {
            if (target is not DoorComp door || !door.IsOperational) {
                return false;
            }
            bool copied = false;
            if (door.State != State) {
                copied = true;
                if (target.IsConstructed) {
                    if (IsLocked) {
                        door.Lock(false);
                    } else {
                        door.Unlock(false);
                    }
                }
            }
            if (door.IsForcedOpen != IsForcedOpen) {
                copied = true;
                if (target.IsConstructed) {
                    door.ForceOpen(IsForcedOpen);
                }
            }
            if (door.deny != deny) {
                door.deny = deny;
                door.Tile.Passability.UpdateDeny(door.EffectiveDeny);
                copied = true;
            }
            if (copied) {
                if (target.IsConstructed) {
                    door.UpdateInfoIcon();
                }
            }
            return copied;
        }

        public void OnAtmoRecalculated(AtmoLayer layer) {
            if (IsAirlock && IsOperational && framePosition != ClosedFrame) {
                FillAirlock(Tile.PosIdx);
            }
        }

        private void FillAirlock(int p) {
            S.SetO2(p, Consts.MaxAtmosphereOxygenSaturation);
            S.SetTempAtLeast(p, Consts.MinBiologicalComfyTemp);
        }
    }
}
