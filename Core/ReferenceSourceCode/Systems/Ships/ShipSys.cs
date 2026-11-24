#if UNITY_EDITOR
// #define DEBUG_FLIGHT
// #define DEBUG_ROTATION
#endif
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Game.Achievements;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Input;
using Game.Systems.Suggestions;
using Game.Systems.Motion;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using Game.Visuals;
using KL.Pool;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Creatures;
using Game.Commands;
using Game.Systems.UIUX;

namespace Game.Systems {
    // [Obsolete("TODO: Refactor / rewrite. Don't add anything new here")]
    public sealed class ShipSys : GameSystem, ISaveable {
        public const string SysId = "ShipSys";
        public override string Id => SysId;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new ShipSys());
        }

        private StarfieldVFX starfield;
        private ShipNavSys nav;
        private StarfieldSO soPrefab;
        public float ExitRadius;

        private SpaceObject player => S.Universe.Player;
        public SpaceObject Player => player;
        bool hasArrived;
        private SpaceObject destination;
        public SpaceObject DestinationSO => destination;
        private Action onReachShip;
        private Vector2 shipMotionVector;
        private Vector2 destinationLoc;
        private Vector2 visualPos;
        private Vector2 looseObjDirection;
        private bool hasDestination;
        private bool isApproachFlight;
        private bool isCapturingNebula;
        public bool NeedsOperator => hasDestination || nav.WantsHyperspace;
        // For tests
        public int OvershootCount;
        public int UndershootCount;
        public readonly List<ShipEngineComp> NeededEngines = new();
        public bool HasDestination(out Vector2 loc) {
            loc = destinationLoc;
            return hasDestination;
        }
        /// <summary>
        /// True while the current flight is a derelict approach towards `so` - the boarding
        /// itself only happens on arrival, so cancelling the flight cancels the approach.
        /// </summary>
        public bool IsApproaching(SpaceObject so) {
            return isApproachFlight && hasDestination && destination == so;
        }
        private Vector2 lastLoc = Consts.V2Zero;
        private Vector2 currentLoc = Consts.V2Zero;
        private readonly List<SpaceObject> currentlyNearby = new();
        private readonly List<SpaceObject> currentlyVisible = new();
        private bool isShipCombatOngoing;

        public void TeleportTo(Vector2 vector) {
            player.Location = vector;
            ForceStop();
            UpdateVisualPosition();
            FullUpdateNearbyObjects();
        }

        private float engineForce;
        public List<SpaceObject> Nearby => currentlyNearby;
        public SpaceObject FirstReachableNearby(int typeH) {
            foreach (var obj in currentlyNearby) {
                if (obj.Type.IdH == typeH && obj.IsReachableFrom(player.Location)) {
                    return obj;
                }
            }
            return null;
        }
        // This is our inertia
        private Vector2 velocity;
        private float rotSpeed;
        private float wantedRotation;
        public float WantedRotation => wantedRotation;
        private float rotation;
        public float Rotation => rotation;
        public void ForceRotation(float rot) {
            startRotDist = 0f;
            rotation = wantedRotation = rot;
            RotateStarfield();
        }
        // For tests
        public void ForceVelocity(Vector2 vel) {
            velocity = vel;
        }

        // Before order of magnitude?
        private float acceleration => Tunables.SpaceTravelAcceleration;
        private float deceleration => Tunables.SpaceTravelDeceleration;
        public bool IsMoving => velocity.sqrMagnitude > 0f || player.Parent.Type.IdH == SpaceObjectTypeIdH.Hyperspace;
        private long travelStartTime;
        private long nextCheckTime;
        private bool isRotating;
        private bool isRotationAligned;
        // for tests
        public bool IsRotating => isRotating;
        public bool IsDecelerating;
        private int discoveredObjects;
        public int ShipMass;
        // heading is the vector that points to where we want to go
        private Vector2 heading;

        public float SpeedInCurrentLayer => Tunables.SpaceTravelFlightSpeed;

        public float BGSpeedInCurrentLayer {
            get {
                switch (S.Universe.Player.Parent.Layer) {
                    case UniverseLayer.Hyperspace: return Tunables.SpaceBGMotionSpeed_Hyperspace;
                    default: return Tunables.SpaceBGMotionSpeed;
                }
            }
        }

        public bool SetDestination(SpaceObject obj, Action onReachShip = null) {
            if (obj == null) {
                CancelFlight();
                return false;
            }
            if (onReachShip != null && obj?.Ship == null) {
                D.Err("OnReachShip callback set, but no ship found on {0}", obj);
                onReachShip = null;
            }
            var loc = obj.LocationApprox(player.Location);
            if (SetDestination(loc, obj.Parent, onReachShip)) {
                lastSqrDistToDest = float.MaxValue;
                destination = obj;
                var dist = Vectors.Distance(obj.Location, player.Location);
                S.Sys.Log.AddLine(T.FlightInitiated_V2.F(
                    obj.StarmapLink, Units.DistanceInSpace(
                        dist, obj.Parent.Type.DistanceUnitsT)));
                return true;
            }
            return false;
        }

        /// <summary>
        /// Flight towards a derelict that gets boarded on arrival. Unlike a regular ship
        /// destination the target position is fixed (DerelictApproach decides it), so it is
        /// not re-approximated to a standoff distance on every tick.
        /// </summary>
        public bool SetApproachDestination(SpaceObject so, Vector2 loc) {
            if (!SetDestination(loc, player.Parent)) {
                return false;
            }
            lastSqrDistToDest = float.MaxValue;
            destination = so;
            isApproachFlight = true;
            S.Sys.Log.AddLine(T.ApproachingTarget_V1.F(so.StarmapLink));
            return true;
        }

        private string NotEnoughPowerInfo(string layer) {
            var msg = T.ShipNoEngines;
            var compTypes = nav.SupportedEngineListFor(layer);
            return $"{msg}.\n{compTypes}";
        }

        // Each failed flight order explains itself, so repeating the order (RMB again, the
        // Approach action) must replace the open explanation rather than stack another one
        private const string FlightImpossibleId = "FlightImpossible";

        public bool ValidateFlightReadiness(string layer) {
            if (A.InstantBuild) { return true; }
            if (!nav.CheckFlightReady(
                    out var inOnePiece,
                    out var hasBridge,
                    out var isPowered, out var isWithinMargin)) {
                var why = Caches.StringList;
                why.Clear();
                if (!isPowered) {
                    why.Add(NotEnoughPowerInfo(layer));
                }
                if (!inOnePiece) {
                    why.Add($"<b>{T.ShipNotInOnePiece}</b>: {T.ShipNotInOnePieceTip_V1.F(T.OverlayTipIslands)}");
                }
                if (!hasBridge) {
                    why.Add(T.FlightNoBridgeControls);
                }
                if (!isWithinMargin) {
                    if (!nav.IsWithinMargin(out var pos)) {
                        if (UIShowing.Starmap) {
                            S.UIQuery.ShowStarmap.Ask(false);
                        }
                        EntityUtils.CameraFocusOn(pos);
                        why.Add(T.ShipInNoBuildZone);
                    }
                }
                var popup = WPopup.Create(false, IconId.CComputing, T.OperationFailed, null,
                    T.ShipFlightImpossible_V1.F(string.Join(GlyphMap.BRHR, why)));
                if (!inOnePiece) {
                    popup.WithAction(T.OpenSectionsOverlay, () => {
                        S.Sig.ShowOverlay.Send(OverlayId.Islands);
                    });
                    popup.WithAction2(T.Ok, null);
                }
                popup.Unique(FlightImpossibleId).Spawn();
                return false;
            }
            if (!nav.CanMoveIn(layer)) {
                return false;
            }
            return true;
        }

        /// <param name="cameraShift">camera offset from center of the map, possibly with some multiplier to reduce it</param>
        /// <returns></returns>
        public Vector2 GetScreenPos(Vector2 cameraShift) {
            var localPos = cameraShift;
            var rotatedPos = Vectors.Rotate(localPos, rotation);
            //D.Warn("ScreenPos: {0} -> {1} -> {2}. Ship rot: {3}", worldPos,
                //rotatedPos, player.Location + rotatedPos, rotation);
            return player.Location + rotatedPos;
        }

        public bool SetDestination(Vector2 loc, SpaceObject parent, Action onReachShip = null) {
            this.onReachShip = onReachShip;
            destination = null;
            isApproachFlight = false;

            if (isShipCombatOngoing) {
                S.Sig.ShowErrorMessage.Send($"{T.MenuSpacetravel}: {T.NotAvailableDuringShipCombat}");
                return false;
            }
            if (nav.WantsHyperspace) {
                S.Sig.ShowErrorMessage.Send(T.PreparingForHyperjump);
                return false;
            }

            if (!ValidateFlightReadiness(parent.Layer)) {
                hasDestination = false;
                return false;
            }

            var dist = Vectors.Distance(loc, player.Location);
            if (dist < 0.05f) {
                UISounds.PlayActionDenied();
                hasDestination = false;
                return false;
            }
            rotSpeed = 0f;
            currentLoc = lastLoc = player.Location;
            hasArrived = false;
            nextCheckTime = travelStartTime = G.Ticks;
            nav.AutoSetForwardDirection(false);

            hasDestination = true;
            destinationLoc = loc;
            return true;
        }

        public void ToggleVisuals(bool on) {
            foreach (var vis in currentlyVisible) {
                if (vis.Visual != null) {
                    if (vis.Visual.gameObject != null) {
                        vis.Visual.gameObject.SetActive(on);
                    }
                }
            }
        }

        public bool IsNearby(SpaceObject obj) {
            return currentlyNearby.Contains(obj);
        }

        protected override void OnInitialize() {
            starfield = new StarfieldVFX(S);

            nav = S.Sys.Get<ShipNavSys>(ShipNavSys.SysId);
            S.Sig.AfterLoadState.AddListener(OnLoadSave);
            S.Sig.SpaceObjectNearby.AddListener(OnSpaceObjNearby);
            S.Sig.SpaceObjectRemoved.AddListener(OnSpaceObjRemoved);
            The.SysSig.StarfieldCapture.AddListener(OnStarfieldCapture);
            currentLoc = lastLoc = velocity = Consts.V2Zero;
            // we start the game with ship oriented towards the right
            rotation = 90f;
        }

        private void AddVisualSO(SpaceObject obj) {
            //D.Warn("Now visible: {0}", obj);
            //S.Clock.Pause();
            if (obj.Visual != null) {
                D.Log("SpaceObject had visual already: {0}", obj);
                return;
            }
            soPrefab ??= ResourceLibrary.FindPrefab<StarfieldSO>("StarfieldSO");
            var inst = PrefabPool.Spawn(soPrefab, Consts.V3Zero, Consts.QId,
                ResourceLibrary.Root);
            inst.Load(S, obj, player);
            obj.Visual = inst;
            if (isCapturingNebula) {
                // we will reactivate it later
                inst.gameObject.SetActive(false);
            }
            RotateStarfield();
        }

        private void RemoveVisualSO(SpaceObject obj) {
            var vis = obj.Visual;
            if (vis == null) {
                //D.Err("No visual already on space obj: {0}", obj);
                return;
            }
            obj.Visual = null;
            // A pooled space object would keep its effect and particle budget until reused
            vis.UnloadVFX();
            PrefabPool.Despawn(vis.gameObject);
        }

        private void OnSpaceObjRemoved(SpaceObject obj) {
            RemoveVisualSO(obj);
            currentlyNearby.Remove(obj);
        }

        private void OnSpaceObjNearby(SpaceObject obj, bool added) {
            if (!added || !Ready.Game || obj == null) { return; }
            // obj.Behavior?.OnPlayerNearby(player);
            if (S.Sys.Sec.IsRadarActive() && !obj.Knowledge.IsFullyScanned) {
                SOActionScan.ScanObject(obj);
            }
            if (obj.Type.IdH == SpaceObjectTypeIdH.HyperjumpRelay) {
                if (added) {
                    if (!S.Universe.IsUsingHyperspaceRelay) {
                        // Entering range is the only moment the relay gets activated, so it
                        // must not depend on the ship being ready to jump right now (drive
                        // still charging, trade open, etc) - the jump itself checks that.
                        S.Universe.CurrentRelay = obj;
                        if (nav.CanEnterHyperspaceNow(false, out _, UniverseLayer.Region)) {
                            // D.Warn("Auto-using hyperjump relay");
                            // S.Universe.IsUsingHyperspaceRelay = true;
                            var playerParent = SOUtils.GetPlayerSector(S)?.SO?.Parent;
                            if (playerParent != null) {
                                S.Sig.ShowFanfare.Send($"{T.SpaceObjectHyperjumprelay}: {Texts.Green(Texts.LinkToStarmap(playerParent, T.Ready))}");
                            }
                        }
                    }
                } else {
                    if (!nav.WantsHyperspace) {
                        if (S.Universe.IsUsingHyperspaceRelay) {
                            D.Warn("Auto-not-using hyperjump relay");
                            // S.Universe.IsUsingHyperspaceRelay = false;
                            S.Universe.CurrentRelay = null;
                            S.Sig.ShowFanfare.Send($"{T.SpaceObjectHyperjumprelay}: {Texts.Yellow(T.LinkInactive)}");
                        }
                    }
                }
            }
        }

        private void OnSpaceObjEnter(SpaceObject obj, SpaceObject prevObj) {
            S.Sys.Universe.EnterObject(obj, prevObj);
            ExitRadius = obj.ExitDist;
            if (obj.Sector != null) {
                S.Situation.SectorDifficulty = obj.Sector.Biome.Difficulty;
            }
            ForceStop();

            CleanLists();

            if (obj.Type.IsScannable && obj.HasChildren) {
                if (!obj.Knowledge.IsFullyScanned) {
                    SOActionScan.ScanObject(obj);
                }
                foreach (var child in obj.Children) {
                    if (child.Type.IsScannable && !child.Knowledge.IsFullyScanned) {
                        SOActionScan.ScanObject(child);
                    }
                }
            }

            FullUpdateNearbyObjects();
            //state.ShipLog.AddScopeChange(obj, prevObj);
            if (UIShowing.Starmap) {
                visualPositionToRestore = Consts.V2Zero;
                needsToRestoreVisualPosition = false;
            }
            // UISounds.Play("FX/DingDingDing", 0.5f);
            ChangeStarmap(obj);
            S.Sig.SpaceObjectEnter.Send(obj, prevObj);
            SpaceAchievements.OnSpaceObjectEnter(obj);
        }

        public static void FullScan(SpaceObject obj) {
            if (obj.Name == "Scylla") {
                D.Warn("Scanning scylla");
            }
            if (obj.Sector == null) { return; }
            if (obj.Knowledge.IsFullyScanned) {
                return;
            }
            SOActionScan.ScanObject(obj);
            if (!obj.HasChildren) {
                return;
            }
            foreach (var child in obj.Children) {
                if (!child.Knowledge.IsFullyScanned) {
                    SOActionScan.ScanObject(child);
                }
            }
        }

        private void FullUpdateNearbyObjects() {
            currentlyNearby.Clear();
            currentlyVisible.Clear();
            UpdateNearbyObjects();
        }

        private void ChangeStarmap(SpaceObject next) { //SpaceObject obj) {
            starfield.SetLooks(next.SpaceLooks());
            SpaceSky.Apply(next, true);
            starfield.ClearShipVector();
            S.Sig.OnShipRotate.Send(rotation);
        }

        private Vector2 visualPositionToRestore;
        private bool needsToRestoreVisualPosition;
        private bool needsToUpdateStarfieldRot;
        private bool hasTiltedShipView;

        public void UpdateVisualPosition() {
            foreach (var vis in currentlyVisible) {
                vis.Visual?.UpdatePosition();
            }
            if (isRotating) {
                var rotNow = Quaternion.Euler(0, 0, -rotation);
                var rotWant = Quaternion.Euler(0, 0, -wantedRotation).normalized;
                var tr = S.Clock.TicksPerUpdate;
                var adjRotSpeed = rotSpeed * tr;
                var rotated = Quaternion.RotateTowards(rotNow, rotWant,
                    adjRotSpeed).normalized;
                var eulerZ = -rotated.eulerAngles.z;
                #if DEBUG_ROTATION
                D.Warn("Rotating ship: from {0} to {1} by {2}. wanted: {3}",
                    rotation, eulerZ, rotSpeed, wantedRotation);
                #endif
                if (rotSpeed > 0) {
                    var rotDir = Mathf.Sign(eulerZ - rotation);
                    var visualRotOfShip = rotDir * rotSpeed * 20f;
                #if DEBUG_ROTATION
                    D.Log("Visual angle: {0} -> {1}", rotSpeed, visualRotOfShip);
                #endif
                    var rot = Mathf.Clamp(visualRotOfShip, -2f, 2f);
                    hasTiltedShipView = true;
                    S.Sig.RotateGameCam.Send(rot);
                }

                rotation = eulerZ;
                needsToUpdateStarfieldRot = true;
                if (Vectors.AngleEquals(rotation, wantedRotation)) {
                    // make sure we're positive
                    if (rotation < 0f) {
                        rotation += 360f;
                    }
                    // Equalize the sign;
                    wantedRotation = rotation;
                    rotSpeed = 0f;
                    startRotDist = 0f;
                    isRotating = false;
                    hasTiltedShipView = false;
                    S.Sig.RotateGameCam.Send(0f);
                }
            }
            //rotation = Vector2.SignedAngle(nav.ShipForward, heading);

            var ts = S.Clock.TimeScale;
            if (ts > 5f) {
                ts = 5f;
            }

            // Adjust velMag to be smoother
            float velMag = AdjustedVelocityMag();
            var adjVelMag = velMag * ts;
            if (adjVelMag > 10f * ts) {
                adjVelMag = 10f * ts;
            }
            //var visualDir = nav.ShipForward * adjVelMag;
            var visualDir = RotationAdjustedPos(-nav.ShipForward);
            visualDir *= -adjVelMag;

            looseObjDirection = visualDir;

            if (!The.Prefs.GetBool(Pref.GAnimatedBackground,
                    PrefH.GAnimatedBackground, true)) {
                return;
            }
            visualPos += visualDir * BGSpeedInCurrentLayer;
            if (UIShowing.FullScreenWidget) {
                //need to accumulate tha actual shift, so that when we get back
                // the starfield will not look as if it was paused
                needsToRestoreVisualPosition = true;
                visualPositionToRestore = visualPos;
                if (hasStoredTempPosition) {
                    tempPosition = visualPos;
                }
                return;
            }
            SetFTLFX(velMag);
            UpdateStarfieldPos();
        }

        public void UpdateStarfieldPos() {
            if (needsToRestoreVisualPosition) {
                visualPos = visualPositionToRestore;
                needsToRestoreVisualPosition = false;
                RotateStarfield();
            }
            MoveStarfieldTo(visualPos);
        }

        private float AdjustedVelocityMag() {
            var velMag = velocity.magnitude;
            if (player.Parent.Type.IdH == SpaceObjectTypeIdH.Hyperspace) {
                var timeDiff = GameTimer.time - S.Vars.HyperspaceEnterGameTime;
                var t = timeDiff / 10f;
                velMag = Mathf.Lerp(0f, 5f, t);
            } else {
                velMag = Mathf.Sqrt(velMag * 10f);
            }
            return velMag;
        }

        public void RotateStarfield() {
            //D.Warn("Rotating starfield to: {0}", rotation);
            S.Sig.OnShipRotate.Send(rotation);
        }
        public Vector2 RotateSpaceObj(float angle, Vector2 velocity,
                Vector2 position, ref float lastRotation) {
            if (lastRotation == 0f) {
                lastRotation = angle;
                return velocity;
            }
            if (lastRotation == angle) { return velocity; }
            var delta = lastRotation - angle;
            lastRotation = angle;
            return Vectors.Rotate(velocity, position, delta * 0.01f);
        }

        private Vector2 lastStarFieldPos = Consts.V2Zero;

        public void MoveStarfieldTo(Vector2 pos) {
            // If you ever see the starfield moving in a wrong direction diagonally
            // it is 99% due to float precision loss with high numbers
            // if (lastStarFieldPos != pos) {
            lastStarFieldPos = pos;
            starfield.MoveTo(pos);
            // }
            // S.Sig.VisualWorldPosChanged.Send(pos);
        }

        private bool hasStoredTempPosition;
        private Vector2 tempPosition;

        public void TempMoveStarfieldTo(Vector2 pos) {
            if (!hasStoredTempPosition) {
                tempPosition = lastStarFieldPos;
                hasStoredTempPosition = true;
            }
            starfield.ClearShipVector();
            MoveStarfieldTo(pos);
        }

        private void SetFTLFX(float velMag) {
            if (player.Parent.Layer == UniverseLayer.Hyperspace) {
                starfield.SetShipVector(nav.ShipForward, velMag);
            }
        }

        private void SetFTLFX() {
            SetFTLFX(AdjustedVelocityMag());
        }

        public void RestoreStarfieldPos() {
            if (hasStoredTempPosition) {
                hasStoredTempPosition = false;
                if (player.Parent == null) {
                    D.Err("Player parent is null when trying to restore starfield pos! This would crash the logic");
                    return;
                }
                visualPos = tempPosition;
                SetFTLFX();
                NormalizeVisualPos();
                MoveStarfieldTo(visualPos);
                RotateStarfield();
            }
        }

        private void OnLoadSave(GameState state) {
            // Destination and velocity are going to be loaded already
            // destination = null;
            heading = Consts.V2Zero;
            starfield.SetLooks(player.Parent.SpaceLooks());
            SpaceSky.OnGameLoaded(player.Parent);
            state.Clock.OnTick.AddListener(OnTick);
            state.Clock.OnTickProgress.AddListener(OnTickProgress);
            currentLoc = lastLoc = player.Location;
            state.Sig.ShipCombatChanged.AddListener(OnShipCombatChange);
            ForceRotation(rotation);
            UpdateVisualPosition();
            Ready.WhenGameLate(this, "UpdateNearby", () => UpdateNearbyObjects());
        }

        private void OnShipCombatChange(bool isCombat) {
            isShipCombatOngoing = isCombat;
            if (isCombat) {
                CancelFlight();
            }
        }

        public void CancelFlight() {
            nav.KillNonHyperdrives();
            warnedAboutBridgeControls = false;
            lastSqrDistToDest = 0f;
            isRotating = false;
            startRotDist = 0f;
            wantedRotation = rotation;
            isRotationAligned = true;
            rotSpeed = 0f;
            hasArrived = true;
            destination = null;
            hasDestination = false;
            isApproachFlight = false;
            onReachShip = null;
            destinationLoc = player.Location;
            heading = Consts.V2Zero;
            IsDecelerating = false;
        }

        private void OnTickProgress(float progress) {
            if (!IsMoving) {
                starfield.ClearShipVector();
            }
            if (IsMoving || isRotating || !isRotationAligned) {
                if (S.Universe.HyperspaceLocation == null) {
                    if (G.ClockSpeed > ClockSpeed.Fast2) {
                        player.Location = currentLoc;
                    } else {
                        #if !PRODUCTION_BUILD
                        if (float.IsNaN(progress)) {
                            D.Err("progress is NaN!");
                        }
                        #endif
                        // D.Warn("Player moving: {0}. rot: {1}. rotAligned: {2}", IsMoving, isRotating, isRotationAligned);
                        player.Location = Vector2.Lerp(lastLoc, currentLoc, progress);
                    }
                    MoveLooseObjects(velocity.magnitude);
                } else {
                    MoveLooseObjects(50f);
                }
                UpdateVisualPosition();
            } else if (hasTiltedShipView) {
                hasTiltedShipView = false;
                S.Sig.RotateGameCam.Send(0f);
            }
            if (needsToUpdateStarfieldRot) {
                needsToUpdateStarfieldRot = false;
                RotateStarfield();
            }

            if (hasDestination && destination?.Ship != null) {
                if (destination.Parent != S.Universe.Player.Parent) {
                    // the ship is already not there anymore
                    ForceStop();
                } else if (!isApproachFlight) {
                    destinationLoc = destination.Ship.NearbyPosFrom(
                        S.Universe.Player.Location, out var isNear);
                    if (destination.Ship.HasLocalTarget) {
                        var futurePos = destination.Ship.GetFuturePosition(10);
                        if (Vectors.SqrDistance(currentLoc, futurePos) <
                                Vectors.SqrDistance(currentLoc, destinationLoc)) {
                            destinationLoc = futurePos;
                        }
                    }
                }
            }
        }

        private bool HasReachedTargetShip() {
            if (!hasDestination) { return false; }
            if (destination?.Ship == null) { return false; }
            if (destination.Ship.IsMoving) { return false; }
            return destinationLoc == currentLoc;
        }

        private float DistanceToFullStop(Vector2 velocity) {
            return velocity.sqrMagnitude / (2f * deceleration) * 0.9f;
        }

        internal void RemoveLooseObjects() {
            var objs = S.Objs;
            var objCount = objs.All.Count;

            for (int i = objCount - 1; i >= 0; i--) {
                Obj o = objs.All[i];
                if (!o.IsActive || !o.Transform.IsInSpace || o.IsCarriedByAny) {
                    continue;
                }
                objs.Destroy(o);
            }
        }
        private void MoveLooseObjects(float speed) {
            // We need to transform this into visual direction!
            //var dir = shipMotionVector;
            //var dir = looseObjDirection;
            var dir = nav.ShipForward * speed;
            if (Maths.Approximately(0, dir.sqrMagnitude)) {
                return;
            }
            dir *= S.Clock.TicksPerUpdate;
            dir *= Tunables.LooseObjectFloatSpeed;
            S.Sys.PendingBodies?.OnShipMove(dir);
            var objs = S.Objs;
            var objCount = objs.All.Count;

            for (int i = objCount - 1; i >= 0; i--) {
                Obj o = objs.All[i];
                if (!o.IsActive || !o.Transform.IsInSpace
                        || o.Transform.SkipMoveLoose || o.IsCarriedByAny) {
                    continue;
                }
                var pos = o.Position - dir;
                if (!EntityUtils.IsWithinBounds(pos)) {
                    objs.Destroy(o);
                } else {
                    if (o.TryGetComponent<IWithTrajectory>(out var traj)) {
                        traj.OnShipMove(dir);
                    } else {
                        o.Transform.TeleportTo(pos);
                        o.Graphics.TeleportTo(pos);
                    }
                }
            }
            var beings = S.Beings.All;
            var bCount = beings.Count;
            for (int i = bCount - 1; i >= 0; i--) {
                Being b = beings[i];
                if (!ShouldFloatInSpace(b)) { continue; }
                var pos = b.Position - dir;
                if (!EntityUtils.IsWithinBounds(pos)) {
                    BeingUtils.Eliminate(S, b.Data, T.DeathAbandonedInSpace, G.Ticks);
                } else {
                    b.Motion.TeleportTo(pos);
                    // b.Graphics.TeleportTo(pos);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ShouldFloatInSpace(Being b) {
            // invisible / inactive
            if (!b.IsActive) { return false; }
            // on solid ground
            if (!b.Motion.Flags.HasFlag(MotionFlags.InSpace)) {
                return false;
            }
            // Carried by somebody
            if (b.Carrier != null) { return false; }
            // flying living non-immobilized being, should not float
            if (b.IsAlive && !b.Motion.WillFloat) {
                return false;
            }
            return true;
        }
        public bool HasCommunicator() {
            var lastPing = S.BB.Get(BBKeyH.CommunicatorPing, (long) -Consts.TicksPerDay);
            return G.Ticks - lastPing <= Consts.TicksPerHour || A.InstantBuild;
        }

        private bool warnedAboutBridgeControls;

        private bool ValidateFlight() {
            if (!hasDestination) { return false; }
            //if (actualPush < 0.0001f) { return; }
            if (!nav.HasBridgeControls()) {
                if (!warnedAboutBridgeControls) {
                    warnedAboutBridgeControls = true;
                    The.Dialog.ShowSimple(IconId.CComputing,
                        T.FlightNoBridgeControls);
                }
                return false;
            }
            if (Ready.Areas) {
                if (!nav.IsInOnePiece) {
                    var ic = S.Sys.Areas.IslandCount;
                    The.Dialog.ShowSimple(IconId.CComputing,
                        T.FlightCancelledIslands_V1.F(ic));
                    CancelFlight();
                    return false;
                }
                // if (nav.EnginePower > 0f) {
                //     var maxPush = nav.EnginePower / ShipMass;
                //     if (maxPush < nav.MinPushToMove) {
                //         if (!warnedAboutLackOfPower) {
                //             warnedAboutLackOfPower = true;
                //             The.Dialog.ShowSimple(IconId.CComputing,
                //                 NotEnoughPowerInfo(player.Parent.Layer));
                //         }
                //         return false;
                //     }
                // }
            }

            return true;
        }

        private bool hasValidatedInitialReadiness;
        private float lastSqrDistToDest;
        private float startRotDist;

        public void ChangeForwardDirection(Facing.Type from, Facing.Type to) {
            if (from == to) { return; }
            var rotFrom = Facing.ToAngle(from);
            var rotTo = Facing.ToAngle(to);
            var diff = rotFrom - rotTo;
            var rot = (rotation + diff) % 360f;
            ForceRotation(rot);
            foreach (var o in currentlyVisible) {
                o.Visual?.UpdateLooks();
            }
            S.Sig.ShipForwardDirChanged.Send(from, to);
        }

        private void OnTick(long ticks) {
            nav.OnTick(ticks);

            // 1. can we even fly?
            ShipMass = S.Sys.Areas.TotalAreaSize;
            if (ShipMass == 0) { return; }
            if (!hasValidatedInitialReadiness) {
                hasValidatedInitialReadiness = true;
                nav.CheckFlightReady(out _, out _, out _, out _);
            }
            engineForce = nav.EnginePush();
            //var actualPush = engineForce / ShipMass;

            float actualPush;
            if (engineForce > 0f) {
                actualPush = Equations.LocalSpacePush(engineForce, ShipMass);
                #if DEBUG_FLIGHT
                D.Log("Actual push: {0} / {1} = {2}", engineForce, ShipMass, actualPush);
                #endif
            } else {
                actualPush = 0f;
            }
            var canFly = ValidateFlight();

            heading = Consts.V2Zero;


            if (hasDestination) {
                heading = (destinationLoc - currentLoc).normalized;
                wantedRotation = Vectors.SignedAngleClockwise(
                    Consts.V2Up, heading);
                heading *= actualPush;
                if (actualPush > 0f) {
                    if (!S.Sys.Areas.TryAbandonDerelicts(out var isShipWithinMargin)) {
                        ForceStop();
                    } else if (!isShipWithinMargin
                            && CmdRecenterShip.TryFindShipCenter(S, out var shipCenter)) {
                        S.CmdQ.Enqueue(new CmdRecenterShip(shipCenter));
                    }
                }
            }

            isRotationAligned = Vectors.AngleEquals(rotation, wantedRotation, out var diff);
            if (!isRotationAligned) {
                if (!isRotating && velocity.sqrMagnitude > 0.04f && diff > 30f) {
                    #if DEBUG_FLIGHT
                    // we want to stop first;
                    D.Warn("Angle is not equal, let's stop and recalibrate! {0} != {1}",
                        rotation, wantedRotation);
                    #endif
                    heading = Consts.V2Zero;
                    IsDecelerating = true;
                } else {
                    var aDist = Vectors.DistanceBetweenAngles(rotation, wantedRotation);
                    if (!isRotating) {
                        startRotDist = aDist;
                    }
                    var acc = Vectors.AccelerateDecelerate(startRotDist, aDist, 0.1f);
                    rotSpeed = Maths.Clamp(actualPush *
                        Tunables.SpaceTravelRotationSpeed * acc, 0.005f, 1f);
                    #if DEBUG_ROTATION
                    D.Log("Rotating from {0} to {1} with speed {2}",
                        rotation, wantedRotation, rotSpeed);
                    #endif
                    isRotating = true;
                }
            }

            var wantedVelocity = Consts.V2Zero;
            if (hasDestination && !IsDecelerating) {
                wantedVelocity = heading;
            }

            var velBefore = velocity;

            if (isRotationAligned ||
                    wantedVelocity.sqrMagnitude < velocity.sqrMagnitude) {
                velocity = Vector2.MoveTowards(velocity, wantedVelocity,
                    (IsDecelerating || actualPush == 0f)
                        ? deceleration : acceleration);
            }

            if (!hasDestination && isRotating || (velBefore.sqrMagnitude > 0f
                    && velocity.sqrMagnitude == 0)) {
                // We just stopped, let's make sure engines are off
                isRotating = false;
                nav.KillNonHyperdrives();
            }

            UpdateNearbyObjects();

            float shipSpeedMul = Tunables.SpaceTravelBaseSpeed
                * SpeedInCurrentLayer;

            shipMotionVector = velocity * shipSpeedMul;
            // Actually move the ship in the universe
            if (S.Universe.HyperspaceLocation == null && shipMotionVector.sqrMagnitude > 0f) {
                player.Location = lastLoc = currentLoc;
                currentLoc = lastLoc + shipMotionVector;
            }


            // Prevent overshoot if we are aligned with dest and going too fast
            if (hasDestination && isRotationAligned) {
                var sqrLastDistToDest = Vectors.SqrDistance(lastLoc, destinationLoc);
                var sqrCurDistToDest = Vectors.SqrDistance(currentLoc, destinationLoc);
                // We are about to overshoot!
                if (sqrCurDistToDest > sqrLastDistToDest && sqrCurDistToDest < 0.01f) {
                    D.Err("Stopping the ship or we would overshoot! Velocity: {0}", velocity);
                    currentLoc = destinationLoc;
                    velocity = Consts.V2Zero;
                    shipMotionVector = Consts.V2Zero;
                }
            }

            // We're not going anywhere
            if (ShipMass == 0) { return; }

            if (!hasArrived && !hasDestination) {
                hasArrived = true;
            }

            // 1. figure out if we want to move anywhere at all
            if (!hasArrived) {
                var pLoc = currentLoc;
                var dLoc = destinationLoc;
                var fullStopDistBeforeDot = DistanceToFullStop(velocity);
                var fullStopDist = fullStopDistBeforeDot
                    * Vector2.Dot(velocity.normalized, heading.normalized);

                //#if DEBUG_FLIGHT
                //D.Log("Full stop dist {0} after dot {1}.", fullStopDistBeforeDot, fullStopDist);
                //#endif

                var dist = Vectors.Distance(pLoc, dLoc) / shipSpeedMul;
                if (!IsDecelerating && dist > 0f && dist <= fullStopDist) {
                    #if DEBUG_FLIGHT
                    D.Log("Begin decelerate at dist: {0}. Full stop dist: {1}", dist, fullStopDist);
                    #endif
                    IsDecelerating = true;
                } else if (isRotationAligned && fullStopDist > dist + 0.05f) {
                    var ratio = dist / fullStopDist;
                    var oldVel = velocity;
                    velocity *= ratio;
                    var newFullStopDist = DistanceToFullStop(velocity);
                    #if DEBUG_FLIGHT
                    D.ErrSoft("We would overshoot, reducing velocity! {0} -> {1}. Old stop dist to new {2} -> {3}",
                        oldVel, velocity, fullStopDist, newFullStopDist);
                    #endif
                    fullStopDist = newFullStopDist;
                }
                #if DEBUG_FLIGHT
                D.Log("Velocity: {0} / Dist: {1} / To stop: {2}",
                    velocity, dist, fullStopDist);
                #endif

            }

            #if DEBUG_FLIGHT
            D.Log("Velocity: {0} / Is Decelerating: {1}", velocity, IsDecelerating);
            #endif

            if (hasArrived && !IsDecelerating) { return; }

            // Attack on reach ship, etc
            if (onReachShip != null) {
                if (destination?.Ship == null) {
                    D.Err("Destination ship is null when onReachShip is not! {0}",
                        onReachShip.Target);
                    onReachShip = null;
                } else {
                    destination.Ship.NearbyPosFrom(currentLoc, out var isNear, 1f);
                    if (isNear) {
                        onReachShip();
                        onReachShip = null;
                        ForceStop();
                        travelStartTime = 0;
                        return;
                    }
                }
            }

            if (CheckArrival()) { return; }

            if (!IsDecelerating) {
                if (!canFly) {
                    nav.KillNonHyperdrives();
                    return;
                }
                NeededEngines.Clear();
                var canPush = QueryEngines(ticks);

                if (canPush) {
                    ActivateEngines();
                }
            }
        }

        private bool CheckArrival() {
            if (isRotating) { return false; }
            if (!IsDecelerating) { return false; }

            if (HasReachedTargetShip()) {
                // won't do any more checks
            } else {
                // If we multiply by oomag, the distance will be way way too distant
                var pLoc = currentLoc;
                var dLoc = destinationLoc;
                var sqrDist = Vectors.SqrDistance(pLoc, dLoc);
                if (sqrDist < 0.001f || (lastSqrDistToDest > 0f
                        && lastSqrDistToDest < sqrDist)) {
                    // extra stop force
                    #if DEBUG_FLIGHT
                    D.Log("Applying extra stop force!");
                    #endif
                    velocity *= 0.99f;
                }
                var velSqr = velocity.sqrMagnitude;
                lastSqrDistToDest = sqrDist;
                if (velSqr > 0f) { return false; }
                if (sqrDist > 1f) {
                // if (sqrDist > 0.025f) {
                    if (isRotationAligned) {
                        #if !PRODUCTION_BUILD
                        D.ErrSoft("Undershooting still happens!");
                        #endif
                        UndershootCount++;
                        D.Warn("We undershot, accelerating again. Vsqrmag: {0}. DistSqr: {1}",
                            velSqr, sqrDist);
                    }
                    IsDecelerating = false;
                    return false;
                }
            }
            var approached = isApproachFlight ? destination : null;
            var destName = destination != null ?
                destination.StarmapLink : T.NounCustomLocation;
            var msg = T.FlightArrived_V2.F(Texts.Green(destName),
                Texts.NoBr(Units.TicksToTime(G.Ticks - travelStartTime)));
            S.Sys.Log.AddLine(msg);
            if (approached == null) {
                // the boarding brings its own fanfare right after this
                S.Sig.ShowFanfare.Send(msg);
                UISounds.Play("FX/DingDingDing", 0.5f);
            }

            if (destination != null && S.Situation.ExpeditionsCompleted < 1
                    && destination.Type.IdH == SpaceObjectTypeIdH.ResourceUnknown
                    && destination.IsReachableFrom(player.Location)) {
                S.Sig.AddSuggestion.Send(SuggestionStartExpedition.SuggId);
            }

            CancelFlight();

            travelStartTime = 0;
            if (approached != null) {
                // out of the tick that ended the flight
                S.ActQ.Enqueue(() => DerelictApproach.Execute(S, approached));
            }
            return true;
        }
        private bool checkEnginesNow;

        private bool QueryEngines(long ticks) {
            // how much can we push ourselves to X and Y
            var canPush = false;
            var head = destinationLoc - currentLoc;
            for (int i = 0; i < nav.Engines.Count; i++) {
                ShipEngineComp e = nav.Engines[i];
                if (!e.IsPowered) { continue; }
                if (!e.WorksIn(player.Parent.Layer)) { continue; }
                NeededEngines.Add(e);
                canPush = true;
            }

            checkEnginesNow = false;
            if (ticks > nextCheckTime) {
                if (nextCheckTime != 0) {
                    checkEnginesNow = true;
                }
                nextCheckTime = ticks + 20;
            }

            if (!canPush && checkEnginesNow) {
                The.Dialog.ShowSimple(IconId.CComputing,
                    T.FlightImpossibleNoSuitableEngines);
                CancelFlight();
            }
            return canPush;
        }

        private void ActivateEngines() {
            var hasOperator = S.Sys.ShipNav.HasBridgeControlsOperator();
            // We will have to loop through needed engines and request
            // their contribution to the actualHeading in next tick
            // BUT we need to distribute the amount of force produced
            // so we do not overuse engines
            for (int i = NeededEngines.Count - 1; i >= 0; i--) {
                ShipEngineComp e = NeededEngines[i];
                if (!e.IsPowered) { continue; }
                // after requesting motion it will return the amount
                // of heading that is remaining
                if (hasOperator) {
                    e.RequestMotion(false);
                } else {
                    e.Kill(true, false);
                }
            }
        }

        private bool UpdateNearbyObjects() {
            bool changed = false;
            var pLoc = player.Location;
            for (int i = 0; i < player.Parent.Children.Count; i++) {
                SpaceObject obj = player.Parent.Children[i];
                if (obj == player) { continue; }

                if (obj.IsVisibleFrom(pLoc)) {
                    if (!currentlyVisible.Contains(obj)) {
                        currentlyVisible.Add(obj);
                        AddVisualSO(obj);
                    }
                } else {
                    if (currentlyVisible.Contains(obj)) {
                        currentlyVisible.Remove(obj);
                        RemoveVisualSO(obj);
                    }
                }

                if (obj.IsReachableFrom(pLoc)) {
                    if (!currentlyNearby.Contains(obj)) {
                        if (obj.Type.CanBeOwned) {
                            // instantly reveal the faction for ownable nearby objects
                            obj.Knowledge.FactionId = obj.FactionId;
                        }

                        currentlyNearby.Add(obj);
                        // new object nearby!
                    S.Sig.SpaceObjectNearby.Send(obj, true);
                        obj.UI?.Refresh();
                        changed = true;
                    }
                } else {
                    // Bye bye
                    if (currentlyNearby.Contains(obj)) {
                        S.Sig.SpaceObjectNearby.Send(obj, false);
                        currentlyNearby.Remove(obj);
                        changed = true;
                    }
                }
            }
            return changed;
        }

        public override void Unload() {
            NeededEngines.Clear();
            IsDecelerating = false;
            destination = null;
            hasDestination = false;
            heading = velocity = Consts.V2Zero;
            The.SysSig.StarfieldCapture.RemoveListener(OnStarfieldCapture);
            starfield?.Destroy();
            CleanLists();
            currentlyNearby.Clear();
            currentlyVisible.Clear();
        }

        private void OnStarfieldCapture(bool isStarting) {
            isCapturingNebula = isStarting;
            ToggleVisuals(!isStarting);
        }

        public string GetName() {
            return Id;
        }

        private ComponentData data;

        private ComponentData Data => data ??= new(0, GetName());

        public Entity Entity => null;

        public ComponentData Save() {
            var d = Data;
            d.SetLong("BridgePing", S.BB.Get(BBKeyH.BridgeControlsPing, (long) -Consts.TicksPerDay));
            d.SetLong("BridgeOpPing", S.BB.Get(BBKeyH.BridgeControlsOperatorPing, (long) -Consts.TicksPerDay));
            d.SetFloat("Rotation", rotation);
            d.SetVector2("Velocity", velocity);
            d.SetFloat("ExitRadius", ExitRadius);
            d.SetFloat("VisualPosX", visualPos.x);
            d.SetFloat("VisualPosY", visualPos.y);
            var dId = (destination?.Id) ?? 0;
            d.SetBool("HasDestination", hasDestination);
            d.SetBool("IsApproach", isApproachFlight);
            d.SetInt("Destination", dId);
            d.SetFloat("DestinationX", destinationLoc.x);
            d.SetFloat("DestinationY", destinationLoc.y);
            d.SetLong("TravelStartTime", travelStartTime);
            d.SetInt("DiscoveredObjects", discoveredObjects);
            return d;
        }

        public void Load(ComponentData data) {
            // The map was never rotated before
            rotation = data.GetFloat("Rotation", 90f);
            wantedRotation = rotation;
            rotSpeed = 0f;
            velocity = data.GetVector2("Velocity", Consts.V2Zero);
            ExitRadius = data.GetFloat("ExitRadius", 0f);
            var dId = data.GetInt("Destination");
            if (dId > 0) {
                destination = S.Universe.Find(dId);
                travelStartTime = data.GetLong("TravelStartTime");
            }
            hasDestination = data.GetBool("HasDestination", destination != null);
            isApproachFlight = data.GetBool("IsApproach", false);
            if (hasDestination) {
                // an approach flies to a fixed position that is not the derelict's own
                if (destination != null && !isApproachFlight) {
                    destinationLoc = destination.Location;
                } else {
                    float dx = data.GetFloat("DestinationX", 0f);
                    float dy = data.GetFloat("DestinationY", 0f);
                    destinationLoc = new(dx, dy);
                }
            }
            visualPos = new Vector2(
                data.GetFloat("VisualPosX", 0),
                data.GetFloat("VisualPosY", 0));
            NormalizeVisualPos();
            discoveredObjects = data.GetInt("DiscoveredObjects", 0);
            S.Situation.SpaceObjectsDiscovered = discoveredObjects;

            var bping = data.GetLong("BridgePing", 0L);
            if (bping > 0) {
                S.BB.Set(BBKeyH.BridgeControlsPing, bping);
            }
            var bopping = data.GetLong("BridgeOpPing", 0L);
            if (bopping > 0) {
                S.BB.Set(BBKeyH.BridgeControlsOperatorPing, bopping);
            }

            this.data = data;
        }

        private void NormalizeVisualPos() {
            // prevent floating point from going too far out
            if (visualPos.sqrMagnitude > 1000 * 1000) {
                visualPos = visualPos.normalized * Rng.URange(2f, 5f);
            }
        }

        public void ForceStop() {
            velocity = Consts.V2Zero;
            hasDestination = false;
            isApproachFlight = false;
            hasArrived = true;
            nav.KillNonHyperdrives();
            currentLoc = lastLoc = heading = player.Location;
            lastSqrDistToDest = 0f;
            destination = null;
            onReachShip = null;
            IsDecelerating = false;
        }

        public bool TryEnterObject(SpaceObject obj, SpaceObject current) {
            if (obj.Type.IdH == SpaceObjectTypeIdH.Region) {
                D.Err("Trying to enter a space region! {0}", obj);
                return false;
            }
            OnSpaceObjEnter(obj, current);
            if (obj.Type.IdH == SpaceObjectTypeIdH.BlackHole
                    && current.Type.IdH == SpaceObjectTypeIdH.BlackHole) {
                // hole to hole = emerge in center
                TeleportTo(Consts.V3Zero);
            }
            return true;
        }

        public void CleanLists() {
            foreach (var vis in currentlyVisible) {
                RemoveVisualSO(vis);
            }
        }

        public void DestroyAllVisuals() {
            CleanLists();
            currentlyVisible.Clear();
        }
        public Vector2 RotationAdjustedPos(Vector2 pos) {
            var adj = Vectors.Rotate(pos, rotation);
            //D.Warn("Adjusting pos by rot: {0} => {1} ({2})", pos, adj, rotation);
            return adj;
        }

        public Vector3 RotationAdjustedPos(Vector3 pos) {
            var rotated = Vectors.Rotate(pos, S.Grid.Center, rotation);
            return Vectors.SetZ(rotated, pos.z);
        }

    }
}
