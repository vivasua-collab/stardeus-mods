using UnityEngine;
using Game.Data;
using KL.Utils;
using System;
using Game.Rendering;
using Game.Constants;
using Game.Audio;
using Game.UI;
using KL.Randomness;
using System.Collections;
using KL.Clock;
using Game.CodeGen;
using Game.Utils;
using System.Collections.Generic;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using KL.Pool;
using UnityEngine.Rendering;
using Game.Visuals;
using Game.Components;

namespace Game.Input {
    /**
    * README for pixel perfect no gaps:
    * Do not enable anti aliasing. Do not enable texture compression.
    * Use point filter on textures. That's it.
    */
    [DefaultExecutionOrder(-10000)]
    public class CameraControls : MonoBehaviour {
        private static CameraControls instance;
        private const float autoFollowSpeed = 0.15f;
        private GameState S;
        private Camera cam;
        [NonSerialized] public CameraDolly Dolly;
        [NonSerialized] public CameraFocus Focus;
        private float panSpeed;
        private bool isSmoothDrag;
        [SerializeField] private Transform audioListener;
        [SerializeField] private Camera[] childCameras;

        [Range(-1, 8)]
        [SerializeField] private float floorLODOverride;

        private const float zoomSpeed = -0.2f;

        public void AlignWithView(Camera c) {
            c.transform.position = tr.position;
            c.orthographicSize = cam.orthographicSize;
        }

        private float initialOrthoSize = 10;
        private float speedMod = 1f;

        //#if UNITY_EDITOR
        //private readonly Vector2 sizeLimits = new Vector2(1, 240);
        //#else
        private Vector2 sizeLimits = new Vector2(2, 240);
        //#endif
        private float panModifier => cam.orthographicSize / 8f;
        public float WantOrtho;
        public float PreciseOrtho;
        private float lastBeingNameFontSize;
        public Vector3 PrecisePosition;
        public Vector3 WantPosition;
        private Vector3 lastMousePosition;
        private Vector2 lastMouseScreenPosition;
        // READ THIS YOU IDIOT
        // don't overwrite the Z value as if we don't care about it
        // it's used for culling !!!!!!
        private Transform tr;
        private Bindings bindings;
        private float cameraShakeDelay;
        private float cameraShakeDuration;
        private Curve beingNameSizeAtDist;

        private Vector3 ClampCamera(Vector3 pos) {
            if (!Ready.Game || !S.IsLoaded) {
                return pos;
            }
            var center = S.Grid.Center;
            var clampAmt = S.Vars.ShipCombatEnemyCount == 0 ? 0.5f : 1.5f;
            var dblW = S.GridWidth * clampAmt;
            var dblH = S.GridHeight * clampAmt;
            var minX = center.x - dblW;
            var maxX = center.x + dblW;
            var minY = center.y - dblH;
            var maxY = center.y + dblH;
            if (pos.x < minX || pos.x > maxX || pos.y < minY || pos.y > maxY) {
                return new Vector3(
                    Mathf.Clamp(pos.x, minX, maxX),
                    Mathf.Clamp(pos.y, minY, maxY),
                    pos.z);
            }
            return pos;
        }

        private void OnPan(float x, float y) {
            if (BlockEdgePan) { return; }
            WantPosition = new Vector3(WantPosition.x + x * MotionDelta,
                WantPosition.y + y * MotionDelta,
                WantPosition.z);
            MoveWithChildren(WantPosition);
        }

        public void MoveWithChildren(Vector3 pos) {
            RenderUtils.IsCameraMoving = true;
            pos = ClampCamera(pos);
            PrecisePosition = pos;
            if (GameTimer.deltaTime > 0f && cameraShakeDuration > 0 && cameraShakeDelay <= 0f) {
                var shake = (Rng.UInsideUnitCircle() * Mathf.Min(1f,
                    cameraShakeDuration));
                pos += (Vector3) shake;
            }
            /*
            pos = new Vector3(
                Mathf.Round(precisePosition.x),
                Mathf.Round(precisePosition.y),
                Mathf.Round(precisePosition.z));
                */
            tr.position = pos;
            for (int i = 0; i < childCameras.Length; i++) {
                childCameras[i].transform.position = pos;
            }
            NotifyMotion(pos);
        }

        public void ZoomWithChildren(float ortho) {
            RenderUtils.IsCameraMoving = true;
            PreciseOrtho = ortho;
            cam.orthographicSize = ortho;
            for (int i = 0; i < childCameras.Length; i++) {
                childCameras[i].orthographicSize = ortho;
            }
            audioListener.localPosition = new Vector3(0f, 0f, RenderUtils.AudioListenerZ);
        }
        private int texSize;

        private void Start() {
            instance = this;
            tr = transform;
            texSize = CachedPlayerPrefs.GetInt(Res.TextureSizePref, 0);
            Dolly = new CameraDolly(S, this);
            cam = GetComponent<Camera>();
            cam.enabled = true;
            RenderUtils.Camera = cam;
            PostProcessing.ApplyToCamera(cam);
            Focus = new CameraFocus(S, this, cam);
            PrecisePosition = WantPosition = tr.position;
            cam.orthographicSize = PreciseOrtho
                = WantOrtho = initialOrthoSize;

            pauseFrameMaterial = new Material(pauseFrameMaterial);
            fadeEffectMaterial = new Material(fadeEffectMaterial);
            hyperjumpEffectMaterial = new Material(hyperjumpEffectMaterial);

            SetPauseFrameColor();

            cam.transparencySortMode = TransparencySortMode.CustomAxis;
            cam.transparencySortAxis = new(0, 0, 1);
            MouseUtils.WorldCamera = cam;
            cam.farClipPlane = 1000;
            // cam.layerCullSpherical = false;
            bindings = The.Bindings;

            OnSettingChanged(Pref.GZoomSensitivity);
            OnSettingChanged(Pref.GPanSensitivity);
            OnSettingChanged(Pref.GSmoothDrag);

            SetupCullDistances();
            for (int i = 0; i < childCameras.Length; i++) {
                Camera c = childCameras[i];
                c.nearClipPlane = cam.nearClipPlane;
                c.farClipPlane = cam.farClipPlane;
                c.layerCullDistances = cam.layerCullDistances;
                // c.layerCullSpherical = false;
            }

            BuildNameSizeAtDistCurve();
        }

        // Note to self: Don't try to do this manually, you'll fail miserably.
        // It has to be a mechanically built curve
        private void BuildNameSizeAtDistCurve() {
            var curvePoints = new List<float>();
            float dist = 0f;
            int step = 1;
            float prevSize = Equations.BeingNameCameraOrthoSize(6f);
            while (dist < Tunables.CullDistance_Names && step < 50) {
                dist = Equations.BeingNameCameraOrthoStep(step++);
                float size = Equations.BeingNameCameraOrthoSize(dist);

                // end of last step
                curvePoints.Add(dist - Maths.Epsilon);
                curvePoints.Add(prevSize);

                // next step
                curvePoints.Add(dist);
                curvePoints.Add(size);

                // D.Warn("Being name at: {0} -> {1} (#{2})", dist, size, step);
                prevSize = size;
            }
            beingNameSizeAtDist = curvePoints.ToArray();
        }

        private void SetupCullDistances() {
            var distances = new float[32];
            // Cull distance for transparent fx layer
            //distances[1] = 200;

            // Decals
            distances[17] = Tunables.CullDistance_Decals;
            // Decals above
            distances[19] = Tunables.CullDistance_Decals;

            // THESE DON'T WORK with new VFXR
            // // Free entities (objects, beings)
            // distances[21] = Tunables.CullDistance_FreeEntities;
            // THESE DON'T WORK with new VFXR
            // Info
            // distances[29] = Tunables.CullDistance_Info;

            // Info Above (names)
            distances[30] = Tunables.CullDistance_Names;
            cam.layerCullDistances = distances;
        }

        private void OnLoad(GameState state) {
            S = state;
            shipComputerGrayscaleAmt = 0f;
            shipComputerInterlaceAmt = 0f;
            shipComputerGlitchAmt = 0f;
            UpdateInterlace();
            // Ship computers set the FX on AllSystemsLoaded, which comes before this
            if (state.Components.FindFirstByType<ShipComputerComp>() != null) {
                ShipComputerComp.RefreshDamagedFX(state);
            }
            Focus.Reset();
            WantPosition = PrecisePosition = state.CameraPosition;
            WantOrtho = PreciseOrtho = state.CameraZoom;
            sizeLimits.y = Mathf.RoundToInt((state.GridHeight / 2) * 1.15f);
            TeleportTo(state.CameraPosition);
            SetZoomDistance(state.CameraZoom);
            S.Clock.OnSpeedChange.AddListener(OnClockSpeedChange, gameObject);
            S.Clock.OnForceStopChange.AddListener(OnForceStopChange, gameObject);
            S.Sig.TogglePauseFrame.AddListener(TogglePauseFrame, gameObject);
            The.SysSig.ToggleUI.AddListener(OnToggleUI, gameObject);
            The.SysSig.UIThemeReloaded.AddListener(OnUIThemeReloaded, gameObject);
            Ready.WhenGame(this, "RestoreCam", () => {
                // D.Warn("Loading camera pos: {0}", PrecisePosition);
                NotifyMotion(PrecisePosition);
            });
        }

        private void OnForceStopChange(bool yes) {
            OnClockSpeedChange(yes ? ClockSpeed.Stopped : S.Clock.Speed);
        }

        private void OnUIThemeReloaded() {
            SetPauseFrameColor();
        }

        private void SetPauseFrameColor() {
            pauseFrameMaterial.SetColor(ShaderVars._Color,
                The.UITheme.GetColor("PauseFrame", "CameraControls"));
        }

        private void OnToggleUI() {
            // The interlace holds the pass slot; clearing it here dropped the damaged computer
            // screen for good, because the interlace believes it is still showing
            if (isShowingInterlace) { return; }
            TogglePauseFrame(false);
        }

        private void OnClockSpeedChange(ClockSpeed speed) {
            if (isShowingInterlace) { return; }
            if (speed == ClockSpeed.Stopped) {
                TogglePauseFrame(true);
            } else {
                TogglePauseFrame(false);
            }
        }

        private void TogglePauseFrame(bool show) {
            if (show) {
                if (Ready.Save && Ready.Game && hyperjumpCRT == null &&
                        The.Prefs.GetBool(Pref.GPauseFrame, PrefH.GPauseFrame, true)) {
                    FullScreenPassVFXRendererFeature.EnablePass(pauseFrameMaterial);
                }
            } else if (hyperjumpCRT == null) {
                // The hyperjump flash holds the same pass slot for its 3 s fade and the pause
                // frame never shows over it, so there is nothing to hide: clearing the slot cut
                // the flash off on any speed change, unpause or save during the fade.
                FullScreenPassVFXRendererFeature.RemovePass();
            }
        }

        private Vector3 MouseWorldPosition => The.Bindings.Mouse.WorldPosition;
        private Vector2 MouseScreenPosition => The.Bindings.Mouse.ScreenPosition;

        public bool IgnoreInput;

        private bool wasShowingFSW;

        private void Update() {
            if (!GameState.IsCurrent(S)) { return; }
            #if !PRODUCTION_BUILD
            if (CameraDolly.HasMotion && Ready.Game) {
                Dolly.UpdateDollyMotion();
            }
            #endif
            if (GameTimer.deltaTime > 0f) {
                if (cameraShakeDelay > 0f) {
                    cameraShakeDelay -= GameTimer.unscaledDeltaTime;
                } else if (cameraShakeDuration > 0f) {
                    cameraShakeDuration -= GameTimer.unscaledDeltaTime;
                }
            }
            // Resolution change detection lives in ScreenWatcher (Bootstrap.Update)
        }

        private bool wasShowingMainMenu;

        private void LateUpdate() {
            #if !PRODUCTION_BUILD
            if (floorLODOverride > 0) {
                D.Err("Doing FloorLOD override! {0}", floorLODOverride);
                Shader.SetGlobalFloat("_FloorLOD", floorLODOverride);
            }
            #endif

            var isShowingMainMenu = UIShowing.MainMenu;
            if (isShowingMainMenu != wasShowingMainMenu) {
                wasShowingMainMenu = isShowingMainMenu;
                UpdateVFXCulling(prevOrtho, true);
            }

            ConvergeTilt();

            if (!GameState.IsCurrent(S) || !Ready.Game) {
                if (UIShowing.GameSetup && UIShowing.ScrollableWidget) {
                    HandleMotion(false);
                    focusChanged = false;
                }
                return;
            }
            if (!IgnoreInput) {
                if (CameraDolly.HasMotion && Ready.Game) {
                    //Dolly.UpdateDollyMotion();
                } else {
                    Focus.FocusOnFollowedBeings();
                    HandleMotion(false);
                    focusChanged = false;
                }
            }
            RenderUtils.UpdateCameraData(cam, S.GridWidth);
            Focus.UpdateGuidelines();

            // S.Sig.OnAfterLateUpdate.Send();

            S.Sig.OnLateUpdate.Send();
        }

        private Vector2 moveInertia = Consts.V2Zero;
        private bool wasDraggingLastFrame;
        // A press of a drag button only becomes a camera drag once the cursor has left the
        // press point: a right click is also cancel, deselect or an order (up to the cancel
        // drag distance, see UserInputEscHandler), and a twitch during it must not throw the view
        private bool isDragButtonHeld;
        private bool isDragStarted;
        private bool isDragByCancel;
        private Vector2 dragPressScreenPos;
        private readonly DragVelocity dragVelocity = new();
        private const float MiddleDragStartPx = 4f;
        private const float ReferenceScreenHeight = 1080f;

        private bool IsPastDragStart(Vector2 msp) {
            float threshold;
            if (isDragByCancel) {
                threshold = The.Prefs.GetInt(Pref.GCancelDragDistancePixels,
                    PrefH.GCancelDragDistancePixels, Consts.DefaultCancelDragDistancePx);
            } else {
                threshold = MiddleDragStartPx * RenderUtils.ScreenHeight / ReferenceScreenHeight;
            }
            return Vectors.SqrDistance(dragPressScreenPos, msp) >= threshold * threshold;
        }

        // The throw continues the drag at the speed the map moved under the cursor. Velocity
        // is measured in pixels of a 1080 px tall screen, so the limit means the same on 4K.
        private Vector2 ReleaseInertia(bool isInWidget) {
            // Camera moves against the cursor
            var vel = -dragVelocity.Estimate();
            if (isInWidget) {
                vel = Vector2.ClampMagnitude(vel, Tunables.SmoothDragInertiaWidgetLimit);
                return vel * 0.016666f * Tunables.SmoothDragMultiplierWidget;
            }
            var screenH = Mathf.Max(1f, RenderUtils.ScreenHeight);
            var refVel = vel * (ReferenceScreenHeight / screenH);
            refVel = Vector2.ClampMagnitude(refVel, Tunables.SmoothDragInertiaLimit);
            // Inverse of MotionDelta: world units per second at this zoom, in move units
            return refVel * (16f / (ReferenceScreenHeight * Mathf.Max(0.01f, panSpeed)))
                * Tunables.SmoothDragMultiplier;
        }

        private void HandleMotion(bool uiOnly) {
            if (UIShowing.MainMenu) {
                if (UIShowing.GameSetup && UIShowing.ScrollableWidget) {
                    // starmap setup = allow motion
                } else {
                    return;
                }
            }
            bool isOverGameView = bindings.Mouse.IsOverGameView;
            RenderUtils.IsCameraMoving = false;
            #if !PRODUCTION_BUILD
            if (The.TestsAreRunning) {
                isOverGameView = true;
            }
            #endif

            var isShowingFSW = UIShowing.FullScreenWidget;
            if (isShowingFSW != wasShowingFSW) {
                wasShowingFSW = isShowingFSW;
                moveInertia = Consts.V2Zero;
            }

            // Grab motion
            var moveVal = Ready.TextInput && Ready.TextInputFocus && Ready.UserInput
                && !UIShowing.MinigameConsumesMovement ?
                bindings.GetValue<Vector2>(ActionType.CameraMove)
                    : Consts.V2Zero;
            moveVal *= Tunables.CameraWASDSpeed;

            Vector2 dragAmt = Consts.V2Zero;
            bool hasWidgetDrag = false;

            var isDragHeld = bindings.IsEitherPressed(ActionType.CameraDrag, ActionType.ClickCancel);
            if (!isDragHeld) {
                isDragButtonHeld = false;
            }

            if (!focusChanged && Ready.UserInput && isOverGameView) {
                if (!isShowingFSW && The.Toolbox.IsFreeSelect && bindings.IsDoubleClicked(ActionType.CameraDrag)) {
                    OnCameraGoto(MouseWorldPosition, PreciseOrtho);
                    wasDraggingLastFrame = false;
                    dragAmt = Consts.V2Zero;
                    moveInertia = Consts.V2Zero;
                    moveVal = Consts.V2Zero;
                    return;
                }
                var msp = MouseScreenPosition;
                if (isDragHeld) {
                    if (!isDragButtonHeld) {
                        isDragButtonHeld = true;
                        isDragStarted = false;
                        isDragByCancel = !bindings.GetBinding(ActionType.CameraDrag).IsPressed();
                        dragPressScreenPos = msp;
                        dragVelocity.Clear();
                    }
                    dragAmt = lastMouseScreenPosition - msp;
                    // Damp
                    // This used to "catch" a flicked sliding view and slow it into the cursor
                    // gently. We probably don't need that
                    moveInertia = Vector2.MoveTowards(moveInertia, Consts.V2Zero,
                        Time.unscaledDeltaTime * 4f);
                    if (!isDragStarted && IsPastDragStart(msp)) {
                        isDragStarted = true;
                    }
                    if (isDragStarted) {
                        // Still frames are samples too: holding still before the release
                        // brings the throw speed down to zero
                        dragVelocity.Add(Time.unscaledTime, msp);
                    }
                    if (isDragStarted && dragAmt.sqrMagnitude > 0.025f) {
                        if (!isShowingFSW) {
                            Drag();
                            Focus.UnfollowEntity();
                        } else {
                            hasWidgetDrag = true;
                        }
                        wasDraggingLastFrame = true;
                    } else {
                        dragAmt = Consts.V2Zero;
                    }
                } else {
                    if (wasDraggingLastFrame) {
                        wasDraggingLastFrame = false;
                        if (isSmoothDrag) {
                            moveInertia = ReleaseInertia(isShowingFSW);
                        }
                    } else {
                        // Faster damping of drag-release smooth camera throw
                        if (isSmoothDrag && moveInertia.sqrMagnitude > 0.01f
                                && moveVal.sqrMagnitude < 0.001f) {
                            var dm = Mathf.Pow(Tunables.SmoothDragDamp,
                                Time.unscaledDeltaTime * 60f);
                            moveInertia *= dm;
                        }
                    }
                }
            }

            var moveSqm = moveVal.sqrMagnitude;
            float moveAccel;
            if (moveSqm > 0.001f) {
                var moveMag = Mathf.Sqrt(moveSqm);
                // Cancel inertia that opposes the new direction for snappy reversal
                if (Vector2.Dot(moveInertia, moveVal) < 0f) {
                    moveInertia = Consts.V2Zero;
                }
                // Don't carry drag momentum past keyboard speed cap
                if (moveInertia.sqrMagnitude > moveSqm) {
                    moveInertia = moveInertia.normalized * moveMag;
                }
                // Scale accel so ramp-up always takes CameraWASDAccelTime seconds
                moveAccel = moveMag / Tunables.CameraWASDAccelTime * Time.unscaledDeltaTime;
            } else {
                moveAccel = Time.unscaledDeltaTime * 4f;
            }
            moveInertia = Vector2.MoveTowards(moveInertia, moveVal, moveAccel);
            var moveInSqrm = moveInertia.sqrMagnitude;
            // final clamp for frame drops and shit
            if (moveInSqrm > 16f) {
                moveInertia = moveInertia.normalized * 4f;
            }
            var move = moveInertia;

            bool hasMotion = (moveSqm + moveInSqrm) > 0.000001f;
            bool isInPanArea = false;
            if (!hasMotion) {
                move = ApplyEdgePan(out isInPanArea);
                hasMotion = isInPanArea;
            }

            if (isOverGameView) {
                lastMousePosition = MouseWorldPosition;
                lastMouseScreenPosition = MouseScreenPosition;
            }

            // Grab zoom
            float zoomChange = 0;
            speedMod = 1f;
            if (Ready.UserInput && Ready.TextInput) {
                zoomChange = bindings.GetValue<float>(ActionType.CameraZoom);
            }
            // if (bindings.IsPressed(ActionType.ShiftModifier)) {
            //     speedMod = 2f;
            // } else if (bindings.IsPressed(ActionType.CtrlModifier)) {
            //     speedMod = 0.5f;
            // }

            bool hasZoomInput = Math.Abs(zoomChange) > 0.001f;
            //var isOverScollableUI = isShowingFSW
                //|| (UIShowing.ScrollableWidget && bindings.Mouse.IsOverScrollableUI);
            var isOverScollableUI = isShowingFSW || UIShowing.ScrollableWidget;
                //&& bindings.Mouse.IsOverScrollableUI;
            /*
            if (UIShowing.ScrollableWidget && (hasZoomInput || hasMotion)
                    && !isInPanArea) {
                ProfilerUtils.StartProfile("IsOverScrollableUI");
                isOverScollableUI = bindings.Mouse.IsOverScrollableUI;
                ProfilerUtils.EndProfile();
            }
            */

            // We still have to call even if there is no zoom input, because we
            // may be auto zooming
            var playerZoomAmt = HandleZoom(isOverScollableUI, zoomChange, uiOnly);

            if (hasMotion || hasZoomInput) {
                S.Vars.LastInputTime = GameTimer.realtimeSinceStartupAsDouble;
            }

            if (isOverScollableUI || isShowingFSW) {
                if (hasWidgetDrag) {
                    S.Sig.OnUIScrollDrag.Send(dragAmt);
                } else if (hasMotion || !Maths.Approximately(0f, playerZoomAmt)) {
                    // We'll navigate inside the scroll rect instead of the world
                    S.Sig.OnUIScrollNav.Send(move * ScrollWidgetMotionDelta, playerZoomAmt);
                }
            }
            if (!Ready.Game) { return; }
            if (hasMotion) {
                if (!isOverScollableUI && !isShowingFSW) {
                    Focus.UnfollowEntity();
                    MoveBy(move * MotionDelta);
                    WantPosition = PrecisePosition;
                }
            } else {
                var dist = Vectors.Distance(WantPosition, PrecisePosition);
                if (dist > 0.1f) {
                    // Vector3 for Z value & culling!
                    TeleportTo(Vector3.MoveTowards(PrecisePosition,
                        WantPosition, MotionDelta * dist * autoFollowSpeed), false);
                } else if (cameraShakeDuration > 0) {
                    MoveWithChildren(PrecisePosition);
                }
            }
        }

        private readonly Vector2 zero = new(0, 0);
        private float secondsInPanArea;

        private bool BlockEdgePan => lostFocus //|| The.Bindings.Mouse.IsOverUI
                    || UIShowing.MainMenu || UIShowing.FullScreenWidget
                    || !Ready.Input || !Ready.TextInput || !Ready.UserInput;

        private Vector2 ApplyEdgePan(out bool hasPanMotion) {
            #if !PRODUCTION_BUILD
            if (The.TestsAreRunning) {
                hasPanMotion = false;
                secondsInPanArea = 0;
                return zero;
            }
            #endif
            if (BlockEdgePan || The.Bindings.Mouse.IsOverUI) {
                hasPanMotion = false;
                secondsInPanArea = 0;
                return zero;
            }
            var edgePanSpeed = The.Prefs.GetFloat(Pref.GEdgePanSpeed,
                PrefH.GEdgePanSpeed, Consts.DefaultEdgePanSpeed);
            if (edgePanSpeed < 0.001f) {
                hasPanMotion = false;
                return zero;
            }
            var pos = The.Bindings.Mouse.ScreenPositionUnclamped;
            // fuck new unity input system
            // Prevents camera jumping after refocus because unity input system
            // sets mouse coordinates as 0:0 when we are defocused
            if (focusChanged) {
                if (pos.sqrMagnitude < 0.01f) {
                    hasPanMotion = false;
                    return zero;
                }
            }
            int dirX = 0;
            int dirY = 0;
            //
            var screenW = RenderUtils.ScreenWidth;
            var screenH = RenderUtils.ScreenHeight;
            float panInner = Mathf.Min(screenW, screenH)
                * Consts.PanAreaScreenPercent;
            // it's terrible if it's not 0
            int panOuter = 0;
            if (pos.x >= -panOuter && pos.x <= panInner) {
                dirX = -1;
            } else if (pos.x >= screenW - panInner && pos.x <= screenW + panOuter) {
                dirX = 1;
            } else if (pos.x < -panOuter || pos.x > screenW + panOuter) {
                hasPanMotion = false;
                secondsInPanArea = 0;
                return zero;
            }
            if (pos.y >= -panOuter && pos.y <= panInner) {
                dirY = -1;
            } else if (pos.y >= screenH - panInner && pos.y <= screenH + panOuter) {
                dirY = 1;
            } else if (pos.y <= -panOuter || pos.y >= screenH + panOuter) {
                hasPanMotion = false;
                secondsInPanArea = 0;
                return zero;
            }

            var udt = Time.unscaledDeltaTime;
            if (udt > 0.5f) {
                // We were alt-tabbed without "run in background"
                hasPanMotion = false;
                secondsInPanArea = 0;
                return zero;
            }

            hasPanMotion = dirX != 0 || dirY != 0;
            if (hasPanMotion) {
                secondsInPanArea += udt;
                edgePanSpeed *= Mathf.Clamp01(
                    (secondsInPanArea - Tunables.EdgePanDelay)
                        * Tunables.EdgePanAcceleration);
                return new(dirX * edgePanSpeed, dirY * edgePanSpeed);
            } else {
                secondsInPanArea = 0;
                return zero;
            }
        }
        private float mouseScrollAmt;
        private float MotionDelta => Time.unscaledDeltaTime * speedMod *
            panModifier * panSpeed;
        private float ScrollWidgetMotionDelta => Time.unscaledDeltaTime * speedMod *
            panSpeed;

        public bool PlayerZooming;
        private float zoomSens;
        private float lastZoomChange;
        private float zoomCooldownTime;
        private Vector2 lastDragVector = Consts.V2Zero;

        ///<returns>Zoom that was produced by player scrolling. Auto or no zoom returns 0</returns>
        private float HandleZoom(bool isOverScrollableUI, float zoomChange, bool uiOnly) {
            // Auto open starmap when zooming out - this feels bad
            // if (zoomChange < 0f && WantOrtho > sizeLimits.y * 0.8f) {
            //     if (!UIShowing.Starmap) {
            //         S.UIQuery.ShowStarmap.Ask(true);
            //         A.Starmap.FocusOn(S.Universe.Player);
            //         return WantOrtho;
            //     }
            // }
            if (VirtualMouseInput.IsVScrollInUse) {
                return HandleControllerZoom(isOverScrollableUI, zoomChange, uiOnly);
            } else {
                if (zoomChange == 0f) {
                    if (zoomCooldownTime > 0f) {
                        zoomCooldownTime -= Time.unscaledDeltaTime;
                        zoomChange = lastZoomChange;
                    }
                } else {
                    lastZoomChange = zoomChange;
                    zoomCooldownTime = 0.1f;
                }
                return HandleMouseZoom(isOverScrollableUI, zoomChange, uiOnly);
            }
        }
        private float HandleMouseZoom(bool isOverScrollableUI, float zoomChange, bool uiOnly) {
            zoomChange = Mathf.Clamp(zoomChange, -1, 1);

            if (TryScrollableUI(zoomChange, out var ignoreUI)) {
                zoomChange = 0;
                mouseScrollAmt = 0;
                lastZoomChange = 0;
            }
            if (uiOnly) {
                return 0f;
            }

            if (!ignoreUI && !UIShowing.FullScreenWidget && MouseUtils.IsOverUI) {
                zoomChange = 0f;
            }

            bool isSmoothZoom = The.Prefs.GetBool(Pref.GSmoothZoom, PrefH.GSmoothZoom,
                Consts.DefaultSmoothZoom);

            if (isSmoothZoom) {
                var zoomAccel =
                    (zoomChange > 0.0001f && mouseScrollAmt < zoomChange) || // Increasing +zoom
                    (zoomChange < -0.0001f && mouseScrollAmt > zoomChange)  // Increasing -zoom
                        ? Time.unscaledDeltaTime * 5f : Time.unscaledDeltaTime * 2f;
                mouseScrollAmt = Mathf.MoveTowards(mouseScrollAmt, zoomChange, zoomAccel);
            } else {
                mouseScrollAmt = Mathf.MoveTowards(mouseScrollAmt, zoomChange, 1f);
            }

            if (mouseScrollAmt != 0f) {
                if (!isOverScrollableUI && bindings.Mouse.IsOverGameView) {
                    var zoomAmt = mouseScrollAmt * zoomSpeed * speedMod * zoomSens
                        * Time.unscaledDeltaTime;
                    if (!isSmoothZoom) {
                        zoomAmt *= 3f;
                    }
                    // Prevent zoom going way too far if frame drops happen
                    if (zoomAmt > 0.1f) {
                        zoomAmt = 0.1f;
                    } else if (zoomAmt < -0.1f) {
                        zoomAmt = -0.1f;
                    }
                    var size = WantOrtho;
                    size *= 1 + zoomAmt;
                    #if UNITY_EDITOR
                    WantOrtho = Mathf.Clamp(size, 0.1f, sizeLimits.y);
                    #else
                    WantOrtho = Mathf.Clamp(size, sizeLimits.x, sizeLimits.y);
                    #endif
                    PlayerZooming = true;
                    SetZoomDistance(WantOrtho);
                }
            } else {
                PlayerZooming = false;
            }

            var camOrtho = PreciseOrtho;
            var orthoDiff = Mathf.Abs(camOrtho - WantOrtho);
            if (orthoDiff > 0.01f) {
                if (isSmoothZoom) {
                    camOrtho = Mathf.MoveTowards(camOrtho, WantOrtho,
                        Time.unscaledDeltaTime * orthoDiff * 3f);
                } else {
                    camOrtho = WantOrtho;
                }
                SetZoomDistance(camOrtho);
            }

            return mouseScrollAmt;
        }

        // private bool isWaitingForNoZoomAfterScrollReactive;

        private bool TryScrollableUI(float zoomChange, out bool ignoreUI) {
            ignoreUI = false;
            // This was here because of old pagination
            // if (isWaitingForNoZoomAfterScrollReactive) {
            //     if (zoomChange == 0f) {
            //         isWaitingForNoZoomAfterScrollReactive = false;
            //     }
            //     return true;
            // }

            // // No zoom change, let's not query
            // if (zoomChange == 0f) {
            //     isWaitingForNoZoomAfterScrollReactive = false;
            //     return false;
            // }

            if (zoomChange == 0f) { return false; }

            var first = The.Bindings.Mouse.FirstUnderCursor;
            if (first == null) {
                return false;
            }
            //D.Warn("First under cursor: {0}", first);
            var scr = first.GetComponentInParent<IReactsToScroll>();
            if (scr == null) {
                return false;
            }
            //D.Warn("Trying to scroll: {0} ({1})", D.GOPath(first.transform), scr);
            if (VirtualMouseInput.IsVScrollInUse) {
                zoomChange *= -1;
            }
            if (The.Prefs.GetBool(Pref.GUIScrollInvert,
                    PrefH.GUIScrollInvert, Consts.DefaultUIScrollInverted)) {
                zoomChange *= -1;
            }
            if (scr.Scroll(zoomChange)) {
                // isWaitingForNoZoomAfterScrollReactive = true;
                UISounds.PlayButtonHover(UISounds.HoverType.Lowest);
            }
            var passThrough = scr.PassThroughZoomOnScroll;
            if (passThrough) {
                ignoreUI = true;
                return false;
            }
            return true;
        }

        private float HandleControllerZoom(bool isOverScrollableUI, float zoomChange, bool uiOnly) {
            // set it back to 0-1
            //zoomChange /= VirtualMouseInput.ScrollMultiplier;
            // Controller mouse zoom works unintuitively by default in game zoom
            // but the direction is ok in UI scrolls, so let's invert it here
            zoomChange *= -1;

            return HandleMouseZoom(isOverScrollableUI, zoomChange, uiOnly);
        }


        private float prevOrtho;

        public void SetZoomDistance(float camOrtho) {
            var p = PrecisePosition;
            if (Focus.TryFollowEntity(camOrtho)) {
                // Nothing to do here
            } else if (PlayerZooming) {
                // Zoom into focal point
                Vector3 mouseWB = The.Bindings.Mouse.WorldPosition;
                ZoomWithChildren(camOrtho);
                Vector3 mouseWA = The.Bindings.Mouse.WorldPosition;
                Vector3 posDiff = (mouseWB - mouseWA);

                // Only when not above UI
                if (The.Bindings.Mouse.FirstUnderCursor == null) {
                    WantPosition = new Vector3(p.x + posDiff.x, p.y + posDiff.y, -camOrtho);
                }

                MoveWithChildren(WantPosition);
            } else {
                ZoomWithChildren(camOrtho);
                MoveWithChildren(new Vector3(p.x, p.y, -camOrtho));
            }
            WantPosition = Vectors.SetZ(WantPosition, p.z);
            S.BB.Set(BBKeyH.CameraZoom, camOrtho);
            S.Sig.CamOrthoSizeChanged.Send(camOrtho);
            Shader.SetGlobalFloat(ShaderVars._CamOrtho, camOrtho);
            UpdateFloorLOD();
            UpdateVFXCulling(camOrtho, false);
            TrackBeingNameSizeAtZoom(camOrtho);
            prevOrtho = camOrtho;
        }

        private void UpdateVFXCulling(float ortho, bool force) {
            var d = Tunables.CullDistance_Info;
            if (UIShowing.MainMenu) {
                ortho = d + 1f;
            }
            if (d < ortho && ((d >= prevOrtho) || force)) {
                // need to cull
                cam.cullingMask &= ~(1 << 29); // layer 30, or infoabove
                cam.cullingMask &= ~(1 << 30); // layer 30, or infoabove
                // D.Warn("CULL: ON. Dist: {0}, Ortho; {1}->{2}. Mask: {3}", d, prevOrtho, ortho, cam.cullingMask);
            } else if (d >= ortho && ((d < prevOrtho) || force)) {
                // need to show
                cam.cullingMask |= 1 << 29; // layer 29, or info
                cam.cullingMask |= 1 << 30; // layer 30, or infoabove
                // D.Warn("CULL: OFF. Dist: {0}, Ortho; {1}->{2}. Mask: {3}", d, prevOrtho, ortho, cam.cullingMask);
            }
        }
        private float GetBeingNameFontSize() {
            if (lastBeingNameFontSize == 0) {
                TrackBeingNameSizeAtZoom(PreciseOrtho);
            }
            return lastBeingNameFontSize;
        }

        private void TrackBeingNameSizeAtZoom(float ortho) {
            var fontSize = beingNameSizeAtDist.Evaluate(ortho);
            if (fontSize < 1f) {
                fontSize = 1f;
            }
            if (fontSize != lastBeingNameFontSize) {
                lastBeingNameFontSize = fontSize;
                if (RenderUtils.ScreenHeight > 800) {
                    fontSize *= 0.75f;
                }
                S.Sig.BeingNameFontSizeChanged.Send(fontSize);
            }
        }

        private void UpdateFloorLOD() {
            float floorLOD = 0;
            if (PreciseOrtho > 250) {
                floorLOD = 6;
            } else if (texSize > 0 && PreciseOrtho > 150) {
                floorLOD = 5;
            } else if (PreciseOrtho > 70) {
                floorLOD = 4;
            } else if (PreciseOrtho > 30) {
                floorLOD = 3;
            } else if (PreciseOrtho > 15) {
                floorLOD = 2;
            } else if (PreciseOrtho > 10) {
                floorLOD = 1;
            }
            if (floorLOD > 3 && texSize == 0) {
                floorLOD = 3;
            }
            #if !PRODUCTION_BUILD
            if (floorLODOverride > 0) {
                floorLOD = floorLODOverride;
            }
            #endif

            //// D.Log("Setting Floor LOD: {0} -> {1}", preciseOrtho, floorLOD);

            Shader.SetGlobalFloat(ShaderVars._FloorLOD, floorLOD);
        }

        private void Drag() {
            if (lastMousePosition.x == 0f && lastMousePosition.y == 0f) {
                D.Warn("Last mouse position wasn't set during drag, fixing");
                lastMousePosition = MouseWorldPosition;
            }

            var newPos = PrecisePosition + (lastMousePosition - MouseWorldPosition);
            MoveWithChildren(newPos);
            WantPosition = newPos;
        }

        private void MoveBy(Vector3 vec) {
            MoveWithChildren(PrecisePosition + vec);
        }

        public void TeleportTo(Vector3 vec, bool updateWantedPos = true) {
            MoveWithChildren(vec);
            if (updateWantedPos) {
                WantPosition = vec;
            }
        }

        private void NotifyMotion(Vector3 pos) {
            // config view?
            if (!Ready.Game) { return; }
            if (The.Prefs.GetBool(Pref.GAnimatedBackground,
                    PrefH.GAnimatedBackground, true)) {
                Shader.SetGlobalVector(ShaderVars._GameView, pos);
                S.BB.Set(BBKeyH.GameView, pos);
            }
            S.BB.Set(BBKeyH.CameraPosition, pos);
            var rotAdjPos = S.Sys.Ship.RotationAdjustedPos(pos);
            S.Sig.OnMoveCamera.Send(pos, rotAdjPos);
            SoundMixer.CameraZ = pos.z;
        }

        public void OnCameraGoto(Vector2 point, float dist) {
            WantPosition = point;
            WantOrtho = dist;
            PlayerZooming = false;
        }

        public void TeleportTo(Vector2 pos, float zoom) {
            // D.Warn("Teleporting camera to {0}@{1}", pos, zoom);
            WantPosition = pos;
            WantOrtho = zoom;
            MoveWithChildren(pos);
            SetZoomDistance(zoom);
            ZoomWithChildren(zoom);
        }

        private void OnShakeCamera(float delay, float duration) {
            if (The.Prefs.GetBool(Pref.GScreenShake,
                    PrefH.GScreenShake, true)) {
                cameraShakeDelay = delay;
                cameraShakeDuration = duration;
            }
        }
        [SerializeField] private Material pauseFrameMaterial;
        [SerializeField] private Material fadeEffectMaterial;
        [SerializeField] private Material hyperjumpEffectMaterial;
        private bool isShowingInterlace;
        private float fadeGrayscaleAmt;
        private float fadeInterlaceAmt;
        private float shipComputerGrayscaleAmt;
        private float shipComputerInterlaceAmt;
        // Glitch bursts belong to a failing computer, so pause never adds any
        private float fadeGlitchAmt;
        private float shipComputerGlitchAmt;
        private float pauseGrayscaleAmt;
        private float pauseInterlaceAmt;
        private CoroutineItem hyperjumpCRT;
        public void ShowInterlace() {
            isShowingInterlace = true;
            // The hyperjump flash holds the pass slot until it ends, and its end hands the slot
            // back to the interlace
            if (hyperjumpCRT != null) { return; }
            FullScreenPassVFXRendererFeature.EnablePass(fadeEffectMaterial);
        }
        private void ShowHyperjump() {
            FullScreenPassVFXRendererFeature.EnablePass(hyperjumpEffectMaterial);
        }

        public static void ShowPauseInterlace() {
            if (instance == null) { return; }
            instance.pauseGrayscaleAmt = 1f;
            instance.pauseInterlaceAmt = 0.1f;
            instance.UpdateInterlace();
        }

        public static void HidePauseInterlace() {
            if (instance == null) { return; }
            instance.pauseGrayscaleAmt = 0f;
            instance.pauseInterlaceAmt = 0f;
            instance.UpdateInterlace();
        }

        public static void SetFocusGuidelinesHidden(bool hidden) {
            if (instance?.Focus == null) { return; }
            instance.Focus.SetHidden(hidden);
        }

        public static void SetShipComputerDamagedFX(float damagePercent) {
            if (instance == null) { return; }
            damagePercent = Mathf.Clamp01(damagePercent);
            instance.shipComputerGrayscaleAmt = damagePercent * 3f;
            instance.shipComputerInterlaceAmt = damagePercent;
            instance.shipComputerGlitchAmt = damagePercent;
            instance.UpdateInterlace();
        }

        private void SetFadeInterlaceValues(float grayscaleAmt, float interlaceAmt) {
            fadeGrayscaleAmt = Mathf.Max(0f, grayscaleAmt);
            fadeInterlaceAmt = Mathf.Max(0f, interlaceAmt);
            fadeGlitchAmt = fadeInterlaceAmt;
            UpdateInterlace();
        }

        private void UpdateInterlace() {
            var grayscaleAmt = Mathf.Max(fadeGrayscaleAmt,
                Mathf.Max(shipComputerGrayscaleAmt, pauseGrayscaleAmt));
            var interlaceAmt = Mathf.Max(fadeInterlaceAmt,
                Mathf.Max(shipComputerInterlaceAmt, pauseInterlaceAmt));
            var glitchAmt = Mathf.Max(fadeGlitchAmt, shipComputerGlitchAmt);
            if (grayscaleAmt > 0f || interlaceAmt > 0f) {
                if (!isShowingInterlace) {
                    ShowInterlace();
                }
                SetInterlaceValues(grayscaleAmt, interlaceAmt, glitchAmt);
            } else if (isShowingInterlace) {
                HideInterlace();
            }
        }

        public void HideInterlace() {
            isShowingInterlace = false;
            SetInterlaceValues(0, 0, 0);
            if (hyperjumpCRT != null) { return; }
            FullScreenPassVFXRendererFeature.RemovePass();
            if (S.Clock.IsStopped) {
                TogglePauseFrame(true);
            }
        }
        public void HideHyperjumpEffect() {
            if (hyperjumpCRT == null) { return; }
            if (!hyperjumpCRT.IsFinished) {
                Coroutines.Cancel(hyperjumpCRT);
            }
            hyperjumpCRT = null;
            SetHyperjumpEffectValues(0);
            // The grey screen of a damaged computer was under the flash all along
            if (isShowingInterlace) {
                FullScreenPassVFXRendererFeature.EnablePass(fadeEffectMaterial);
                return;
            }
            FullScreenPassVFXRendererFeature.RemovePass();
            if (S?.Clock?.IsStopped == true) {
                TogglePauseFrame(true);
            }
        }

        private void OnEnterHyperspace(bool entered) {
            if (hyperjumpCRT != null) {
                HideHyperjumpEffect();
            }
            if (S.Sys.ShipNav.IsQuickHyperspace) { return; }
            hyperjumpCRT = Coroutines.Start(ShowHyperjumpEffect(), this,
                "HyperjumpFX", HideHyperjumpEffect, false, false);
        }

        public void SetInterlaceValues(float grayscaleAmt, float interlaceAmt, float glitchAmt) {
            fadeEffectMaterial.SetFloat(ShaderVars._GrayscaleAmount, grayscaleAmt);
            fadeEffectMaterial.SetFloat(ShaderVars._InterlaceAmount, interlaceAmt);
            fadeEffectMaterial.SetFloat(ShaderVars._GlitchAmount, glitchAmt);
        }

        public void SetHyperjumpEffectValues(float amt) {
            hyperjumpEffectMaterial.SetFloat("_Amount", amt);
        }

        public static IEnumerator FadeCamera() {
            #if !PRODUCTION_BUILD
            if (The.TestsAreRunning) {
                yield break;
            }
            #endif
            if (instance == null) { yield break; }
            var gsAmt = 3f;
            var ilAmt = 1f;
            while (ilAmt >= 0f || gsAmt >= 0f) {
                instance.SetFadeInterlaceValues(gsAmt, ilAmt);
                yield return null;
                if (instance == null) { yield break; }
                gsAmt -= Time.unscaledDeltaTime / 5f;
                ilAmt -= Time.unscaledDeltaTime / 15f;
            }
            instance.SetFadeInterlaceValues(0f, 0f);
        }
        private static IEnumerator ShowHyperjumpEffect() {
            #if !PRODUCTION_BUILD
            if (The.TestsAreRunning) {
                yield break;
            }
            #endif
            instance.ShowHyperjump();
            var amt = 1f;
            // fade for 10 seconds
            while (amt > 0f) {
                instance.SetHyperjumpEffectValues(amt);
                yield return null;
                if (instance == null) {
                    yield break;
                }
                amt -= Time.unscaledDeltaTime / 3f;
            }
        }

        private bool lostFocus;
        private bool focusChanged;

        private void OnApplicationFocus(bool focus) {
            #if PRODUCTION_BUILD
            D.Warn("App focus: {0}", focus);
            #endif
            focusChanged = true;
            lostFocus = !focus;
            secondsInPanArea = 0;
        }

        private void OnSettingChanged(string name) {
            if (name == Pref.GZoomSensitivity) {
                zoomSens = The.Prefs.GetFloat(Pref.GZoomSensitivity,
                    PrefH.GZoomSensitivity,
                    Consts.DefaultZoomSensitivity);
            } else if (name == Pref.GPanSensitivity) {
                panSpeed = The.Prefs.GetFloat(Pref.GPanSensitivity,
                    PrefH.GPanSensitivity, Consts.DefaultPanSensitivity);
            } else if (name == Pref.GSmoothDrag) {
                isSmoothDrag = The.Prefs.GetBool(Pref.GSmoothDrag,
                    PrefH.GSmoothDrag,
                    Consts.DefaultSmoothDrag);
            } else if (name == Pref.GPauseFrame) {
                var on = The.Prefs.GetBool(Pref.GPauseFrame, PrefH.GPauseFrame, true);
                TogglePauseFrame(on);
            }
        }

        private float wantedTilt;
        private float currentTilt;

        private void RotateCam(float angle) {
            wantedTilt = angle;
        }

        private void ConvergeTilt() {
            if (!Maths.Approximately(currentTilt, wantedTilt)) {
                if (The.Prefs.GetBool(Pref.GScreenShake, PrefH.GScreenShake, true)) {
                    currentTilt = Mathf.MoveTowards(currentTilt, wantedTilt,
                        GameTimer.deltaTime * 0.33f);
                        // Cannot use Caches since this has to persist multiple frames
                    tr.localRotation = Quaternion.Euler(0, 0, currentTilt);
                } else {
                    wantedTilt = currentTilt;
                }
            }
        }

        private void OnEnable() {
            The.SysSig.PanCamera.AddListener(OnPan, gameObject);
            The.SysSig.SettingChanged.AddListener(OnSettingChanged, gameObject);
            S = The.R.State;
            S.Sig.EnteredHyperspace.AddListener(OnEnterHyperspace, gameObject);
            S.Sig.RotateGameCam.AddListener(RotateCam, gameObject);
            S.Sig.OnShowFullScreenWidget.AddListener(OnFSW, gameObject);
            S.Sig.AfterLoadState.AddListener(OnLoad, gameObject);
            S.Sig.CameraGoto.AddListener(OnCameraGoto, gameObject);
            S.Sig.ShakeCamera.AddListener(OnShakeCamera, gameObject);
            S.Sig.ToggleRendering.AddListener(OnToggleRendering, gameObject);
            S.Query.GetBeingNameFontSize.SetResponder(GetBeingNameFontSize);
        }

        private void OnToggleRendering(bool on) {
            cam.enabled = on;
        }

        private void OnDisable() {
            The.SysSig.PanCamera.RemoveListener(OnPan);
            The.SysSig.SettingChanged.RemoveListener(OnSettingChanged);
            The.SysSig.ToggleUI.RemoveListener(OnToggleUI);
            The.SysSig.UIThemeReloaded.RemoveListener(OnUIThemeReloaded);
            if (S != null) {
                if (S.Sig != null) {
                    S.Sig.RotateGameCam.RemoveListener(RotateCam);
                    S.Sig.OnShowFullScreenWidget.RemoveListener(OnFSW);
                    S.Sig.AfterLoadState.RemoveListener(OnLoad);
                    S.Sig.EnteredHyperspace.RemoveListener(OnEnterHyperspace);
                    S.Sig.CameraGoto.RemoveListener(OnCameraGoto);
                    S.Sig.ShakeCamera.RemoveListener(OnShakeCamera);
                    S.Sig.ToggleRendering.RemoveListener(OnToggleRendering);
                    S.Sig.TogglePauseFrame.RemoveListener(TogglePauseFrame);
                }
                S.Clock?.OnSpeedChange.RemoveListener(OnClockSpeedChange);
                // added with OnSpeedChange in OnLoad, but was never removed
                S.Clock?.OnForceStopChange.RemoveListener(OnForceStopChange);
                S.Query?.GetBeingNameFontSize.RemoveResponder();
                S = null;
            }
        }

        private void OnFSW(bool on) {
            cam.enabled = !on;
        }

        private void OnDestroy() {
            instance = null;
            Dolly?.Unsubscribe();
            Focus?.Unsubscribe();
            Dolly = null;
            Focus = null;
        }
    }
}
