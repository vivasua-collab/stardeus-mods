#if !PRODUCTION_BUILD
// #define DEBUG_HYPERJUMPS
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using KL.Grid;
using KL.Utils;
using UnityEngine;
using System.Buffers;
using Unity.Mathematics;
using Game.Input;
using Game.Systems.Space;
using Game.Commands;
using Game.Systems.Ships;
using Game.Rendering;
using KL.Randomness;
using Game.Data.Economy;
using Game.Systems.UIUX;

namespace Game.Systems {
    public sealed class ShipNavSys : GameSystem, IOverlayProvider,
            ISaveable {
        public const string SysId = "ShipNavSys";
        public override string Id => SysId;
        public bool IsFlightReady;
        public bool CanDoShortHyperjump = false;
        public bool CanDoLongHyperjump = false;
        public bool CanDoPlanet = false;
        public float EnginePower;
        public bool WantsHyperspace { get; private set; }
        public bool AutoForwardDirectionEnabled { get; private set; } = true;
        private HyperjumpQueue hyperjumpQueue;
        private readonly HyperspaceState hyperspaceState = new();
        /// <summary>
        /// For tests
        /// </summary>
        public HyperspaceState HyperspaceState => hyperspaceState;
        private readonly ShipFlightCondition flightCondition = new();
        private Facing.Type shipDir;
        /// <summary>
        /// When this is set to true, entering / exiting hyperspace will be instant.
        /// Used for regenerating space
        /// </summary>
        public bool IsQuickHyperspace;
        public Facing.Type ShipDirection {
            get => shipDir;
            set {
                // D.Warn("Setting ship direction: {0}->{1}", shipDir, value);
                shipDir = value;
            }
        }
        public Vector2 ShipForward {
            get {
                switch(ShipDirection) {
                    case Facing.Type.Down: return Consts.V2Down;
                    case Facing.Type.Up: return Consts.V2Up;
                    case Facing.Type.Right: return Consts.V2Right;
                    case Facing.Type.Left: return Consts.V2Left;
                }
                return Consts.V2Right;
            }
        }
        public float ShipDefaultRot {
            get {
                switch (ShipDirection) {
                    case Facing.Type.Down: return 180f;
                    case Facing.Type.Up: return 0f;
                    case Facing.Type.Right: return 90f;
                    case Facing.Type.Left: return 270f;
                }
                return 90f;
            }
        }

        private RenderTexture noBuildZoneTexture;
        private ComputeShader noBuildZoneViz;
        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new ShipNavSys());
        }
        public List<OverlayInfo> Overlays { get; } = new();
        private bool wasCameraMovedWithOverlayOpen;

        public bool HasBridgeControls() {
            var lastPing = S.BB.Get(BBKeyH.BridgeControlsPing, (long) -Consts.TicksPerDay);
            if (lastPing == 0L) { return false; }
            var pingDiff = G.Ticks - lastPing;
            // D.Warn("BC ping diff: {0}", pingDiff);
            return pingDiff <= Consts.TicksPer60Minutes;
        }

        public bool HasBridgeControlsOperator() {
            var lastPing = S.BB.Get(BBKeyH.BridgeControlsOperatorPing, (long) -Consts.TicksPerDay);
            if (lastPing == 0L) { return false; }
            return G.Ticks - lastPing <= Consts.TicksPer30Minutes;
        }

        public Entity Entity => null;
        public readonly List<ShipEngineComp> Engines = new();
        public void KillNonHyperdrives() {
            for (int i = Engines.Count - 1; i >= 0; i--) {
                var e = Engines[i];
                // if (e.Hyperdrive == null) {
                e.Kill(true, false);
                // }
            }
        }

        public float EnginePush() {
            float push = 0;
            for (int i = Engines.Count - 1; i >= 0; i--) {
                var e = Engines[i];
                push += e.ActualPush;
            }
            return push;
        }

        public bool CanMoveIn(string layer) {
            if (!IsInOnePiece) {
                S.Sig.CameraFocus.Send(S.Grid.Center,
                    new(S.Grid.Width / 2,
                        S.Grid.Height / 2), 0);
                if (UIShowing.Overlay) {
                    S.Sig.HideOverlay.Send();
                }
                The.Dialog.ShowSimple(IconId.CComputing,
                    T.FlightImpossibleIslands_V1.F(S.Sys.Areas.IslandCount));
                S.Sig.ShowOverlay.Send("Islands");
                return false;
            }

            if (Engines.Count == 0) {
                The.Dialog.ShowSimple(IconId.CComputing,
                    T.FlightImpossibleNoEngines);
                return false;
            }
            var hasSuitableEngines = false;
            for (int i = Engines.Count - 1; i >= 0; i--) {
                var e = Engines[i];
                if (e.WorksIn(layer)) {
                    hasSuitableEngines = true;
                    break;
                }
            }
            if (!hasSuitableEngines) {
                The.Dialog.ShowSimple(IconId.CComputing,
                    T.FlightImpossibleNoSuitableEngines);
                return false;
            }

            return true;
        }

        public HyperspaceLayerState HyperspaceStateFor(SpaceObject parent) {
            if (parent == null) {
                D.Err("Trying to get hyperspace state for null parent!");
                return hyperspaceState.Universe;
            }
            if (parent.Layer == UniverseLayer.Universe) {
                return hyperspaceState.Universe;
            }
            return hyperspaceState.Region;
        }

        public HyperspaceLayerState HyperspaceStateForLayer(string layer) {
            if (layer == UniverseLayer.Universe) {
                return hyperspaceState.Universe;
            }
            return hyperspaceState.Region;
        }

        // Like HyperspaceStateFor, but accounts for an active hyperjump relay: a Universe-layer
        // jump made through a relay is powered by the ship's Region (short-range) drives, so report
        // the Region layer state instead of the (0-when-no-FTL) Universe state. Mirrors the relay
        // substitution in HyperjumpQueue.OnTick and the Universe->Region downgrade in
        // HasEnginesForHyperspace. HyperspaceStateFor stays raw for the FTL-specific and per-target
        // movement logic that must not see this substitution.
        public HyperspaceLayerState EffectiveHyperspaceStateFor(SpaceObject parent) {
            if (S.Universe.IsUsingHyperspaceRelay && parent != null
                    && parent.Layer == UniverseLayer.Universe) {
                return hyperspaceState.Region;
            }
            return HyperspaceStateFor(parent);
        }

        // Number of reachable (green) legs to draw for a path/queue at this layer. A relay performs a
        // single region hop before deactivating on arrival, so at most one leg is reachable through it.
        public int PathJumpsFor(SpaceObject parent) {
            var jumps = EffectiveHyperspaceStateFor(parent).Jumps.Value;
            if (S.Universe.IsUsingHyperspaceRelay && parent != null
                    && parent.Layer == UniverseLayer.Universe) {
                jumps = math.min(jumps, 1);
            }
            return jumps;
        }

        public void AddEngine(ShipEngineComp engine) {
            if (!Engines.AddUniqueInefficient(engine)) {
                D.Err("Adding duplicate engine! {0}", engine);
            }
        }

        public void RemoveEngine(ShipEngineComp engine) {
            if (!Engines.Remove(engine)) {
                D.Err("Failed to remove engine: {0}", engine);
            }
        }

        protected override void OnInitialize() {
            hyperjumpQueue = new HyperjumpQueue(S, this);
            Overlays.Clear();
            S.Sig.ToggleOverlay.AddListener(OnToggleOverlay);
            S.Sig.AfterLoadState.AddListener(OnLoadSave);
            S.Sig.NewGameInitialized.AddListener(OnGameInit);
        }

        private void OnGameInit() {
            Ready.WhenGameLate(this, "Recharge engines", () => {
                // recharge any hyperdrives
                foreach (var eng in Engines) {
                    if (eng.Hyperdrive != null) {
                        eng.Hyperdrive.Charge = 1f;
                    }
                }
            });
        }

        private void OnLoadSave(GameState state) {
            S.Query.GetShipFlightCondition.SetResponder(() => flightCondition);
            PrepareCompute();
            Ready.WhenGameLate(this, "HyperspaceState", UpdateCapabilities);
            Ready.WhenGameLate(this, "RefreshHyperjumpQueue", () =>
                S.Sig.HyperjumpQueueChanged.Send(hyperjumpQueue, false));
            Ready.WhenGameLate(this, "ValidateLocation",
                () => SOUtils.ValidateLocation(S));
        }

        private int groupsX;
        private int groupsY;
        private int vizKernel;

        private void PrepareCompute() {
            this.noBuildZoneViz = Resources.Load<ComputeShader>(
                    "Shaders/Compute/NoBuildZoneViz");
            try {
                vizKernel = this.noBuildZoneViz.FindKernel("CSNoBuildZoneViz");
            } catch (Exception ex) {
                D.LogEx(ex, "Could not load NoBuildZoneViz compute shader");
                return;
            }


            this.noBuildZoneViz.GetKernelThreadGroupSizes(0,
                out var x, out var y, out var _);
            groupsX = S.GridWidth / (int) x;
            groupsY = S.GridHeight / (int) y;

            noBuildZoneTexture = new RenderTexture(S.GridWidth, S.GridHeight, 0,
                RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            noBuildZoneTexture.enableRandomWrite = true;
            noBuildZoneTexture.name = "NoBuildZoneTex";
            noBuildZoneTexture.Create();
            Overlays.Add(new OverlayInfo(OverlayType.Controls, OverlayId.ShipNav,
                IconId.WSpaceShip, ref noBuildZoneTexture));
            Overlays.Add(new OverlayInfo(OverlayType.Hidden, OverlayId.NoBuildZone,
                null, ref noBuildZoneTexture));

            noBuildZoneViz.SetTexture(vizKernel, "tex", noBuildZoneTexture);

            noBuildZoneViz.SetInt("Margin", Consts.MapMargin);
            noBuildZoneViz.SetInt("CornerMargin", Consts.CornerMargin);
            noBuildZoneViz.SetVector("colNoBuild", Consts.ColorNoBuildZone);
            noBuildZoneViz.SetVector("colNoFly", Consts.ColorNoFlyZone);
        }

        private bool isShowingOverlay;
        private bool isShowingNoBuildZones;
        private Vector3 camPos;
        private float camOrtho;

        private void OnToggleOverlay(OverlayInfo info, bool on) {
            if (on && info.IdH == OverlayIdH.NoBuildZone && !isShowingNoBuildZones) {
                isShowingNoBuildZones = true;
                RedrawTexture();
                if (isShowingOverlay) {
                    HideOverlay();
                }
            } else if (on && info.IdH == OverlayIdH.ShipNav && !isShowingOverlay) {
                isShowingNoBuildZones = false;
                isShowingOverlay = true;
                RedrawTexture();
                EntityUtils.CameraFocusOnMap();
                camPos = RenderUtils.CameraPosition;
                camOrtho = RenderUtils.CameraOrtho;
                S.Clock.OnUpdate60.AddListener(CheckCameraMoved);
            } else if (isShowingOverlay) {
                HideOverlay();
            } else if (isShowingNoBuildZones) {
                isShowingNoBuildZones = false;
            }
        }

        private void HideOverlay() {
            if (wasCameraMovedWithOverlayOpen) {
                wasCameraMovedWithOverlayOpen = false;
            } else {
                // focus back on where we were focused before opening
                // the overlay
                S.Sig.CameraFocus.Send(camPos, Consts.V2Zero, camOrtho * 2);
            }
            S.Clock.OnUpdate60.RemoveListener(CheckCameraMoved);
            isShowingOverlay = false;
        }

        private void CheckCameraMoved() {
            var b = The.Bindings;
            if (
                b.IsPressed(ActionType.CameraDrag) ||
                b.IsPressed(ActionType.CameraZoom) ||
                b.IsPressed(ActionType.CameraMove)
            ) {
                wasCameraMovedWithOverlayOpen = true;
            }
        }

        private void RedrawTexture() {
            noBuildZoneViz.Dispatch(vizKernel, groupsX, groupsY, 1);
        }

        public override void Unload() {
            noBuildZoneTexture?.Release();
            Engines.Clear();
        }



        public void SetAutoForwardDirection(bool enabled) {
            AutoForwardDirectionEnabled = enabled;
            if (enabled) {
                AutoSetForwardDirection(A.IsInHyperspace);
            }
        }

        public void SetForwardDir(Facing.Type dir) {
            var oldDir = ShipDirection;
            ShipDirection = dir;
            S.Sys.Ship.ChangeForwardDirection(oldDir, dir);
            D.Warn("Setting ship forward: {0} -> {1}", oldDir, dir);
        }
        // Only the ship itself must stay out of the corners. Derelicts may reach past
        // the margin line (STA-1681: a derelict fragment on it blocked every jump).
        public bool IsWithinMargin(out int pos) {
            var shipIsland = S.Sys.Areas.TryGetShipIsland(out var isl) ? isl : 0;
            var gh = S.GridHeight;
            var gw = S.GridWidth;
            pos = Pos.Invalid;
            var m = Consts.CornerMargin;
            for (int i = 0; i < Consts.CornerMargin; i++) {
                // top left right
                // x = m - 1
                // y = gh - i - 1
                pos = Pos.FromXY(m - 1, gh - i - 1);
                if (IsShipFloorAt(pos, shipIsland)) {
                    return false;
                }

                // top left down
                // x = i
                // y = gh - m
                pos = Pos.FromXY(i, gh - m);
                if (IsShipFloorAt(pos, shipIsland)) {
                    return false;
                }

                // top right left
                // x = gw - m
                // y = gh - i - 1
                pos = Pos.FromXY(gw - m, gh - i - 1);
                if (IsShipFloorAt(pos, shipIsland)) {
                    return false;
                }

                // top right down
                // x = gw - i - 1
                // y = gh - m
                pos = Pos.FromXY(gw - i - 1, gh - m);
                if (IsShipFloorAt(pos, shipIsland)) {
                    return false;
                }

                // bot left right
                // x = m - 1
                // y = i
                pos = Pos.FromXY(m - 1, i);
                if (IsShipFloorAt(pos, shipIsland)) {
                    return false;
                }

                // bot left up
                // x = i
                // y = m - 1
                pos = Pos.FromXY(i, m - 1);
                if (IsShipFloorAt(pos, shipIsland)) {
                    return false;
                }

                // bot right left
                // x = gw - m
                // y = m - 1
                pos = Pos.FromXY(gw - m, i);
                if (IsShipFloorAt(pos, shipIsland)) {
                    return false;
                }

                // bot right up
                // x = gw - i - 1
                // y = m - 1
                pos = Pos.FromXY(gw - i - 1, m - 1);
                if (IsShipFloorAt(pos, shipIsland)) {
                    return false;
                }
            }
            return true;
        }

        // Without a known ship island every floor counts, as before
        private bool IsShipFloorAt(int pos, int shipIsland) {
            if (!EntityUtils.IsFloorAt(pos)) { return false; }
            return shipIsland == 0 || S.Sys.Areas.IslandAt(pos) == shipIsland;
        }

        public bool IsInOnePiece {
            get {
                if (S.Sys.Areas.IslandCount == 1) {
                    return true;
                }
                bool isBuilding = S.Situation.OngoingBuildTasks > 0;
                var minIslandSize = 30;
                int isleCount = 0;
                foreach (var isle in S.Sys.Areas.Islands) {
                    var pos = isle.Value.First();
                    if (!EntityUtils.IsWithinMargin(pos, false, false)) {
                        // D.Warn("Skipping derelict");
                        // derelict
                        continue;
                    }
                    if (isBuilding) {
                        if (isle.Value.Count > minIslandSize) {
                            isleCount++;
                        }
                    } else {
                        isleCount++;
                    }
                }
                return isleCount == 1;
            }
        }

        public bool CheckFlightReady(
                out bool inOnePiece,
                out bool hasBridge,
                out bool enginesPowered,
                out bool isWithinMargin) {
            EnginePower = 0;
            CanDoShortHyperjump = false;
            CanDoLongHyperjump = false;
            CanDoPlanet = false;
            enginesPowered = false;
            var curLayer = S.Universe.Player.Parent.Layer;
            foreach (var e in Engines) {
                if (!e.IsPowered) { continue; }
                if (e.WorksIn(curLayer)) {
                    EnginePower += e.PowerWithUpgrades;
                    enginesPowered |= true;
                }
                CanDoShortHyperjump |= e.WorksIn(UniverseLayer.Region);
                CanDoLongHyperjump |= e.WorksIn(UniverseLayer.Universe);
                CanDoPlanet |= e.WorksIn(UniverseLayer.Sector);
            }

            if (inOnePiece = IsInOnePiece) {
                isWithinMargin = IsWithinMargin(out var _);
            } else {
                // FIXME
                isWithinMargin = true;
            }
            hasBridge = HasBridgeControls();

            var wasFlightReady = IsFlightReady;

            IsFlightReady = inOnePiece
                && hasBridge && isWithinMargin;

            if (wasFlightReady != IsFlightReady) {
                S.Sig.FlightReadyChanged.Send(IsFlightReady);
            }
            return IsFlightReady;
        }

        public void ExitHyperspace(string reason) {
            #if DEBUG_HYPERJUMPS
            D.Warn("Exiting hyperspace ({0}): {1}", reason, S.Universe.HyperspaceLocation);
            #endif
            SetWantHyperspace(false);
            // restore solar power
            S.BB.Set(BBKeyH.SolarOutputMultiplier, 1f);
            if (!S.Sys.Ship.TryEnterObject(S.Universe.HyperspaceLocation,
                    S.Universe.Hyperspace)) {
                S.BB.Set(BBKeyH.SolarOutputMultiplier, 0f);
                UISounds.PlayActionDenied();
                return;
            }
            if (!IsQuickHyperspace) {
                UISounds.PlayHyperjump();
            }
            if (!IsQuickHyperspace) {
                string title = T.LeftHyperspace;
                if (reason != null) {
                    title = $"{Texts.Red(title)}: {reason}";
                }
                S.Sig.ShowFanfare.Send(title);
                S.Sys.Log.AddLine(title);
            }
            S.Universe.HyperspaceLocation = null;
            S.Universe.StepsInHyperspace = 0;
            S.Universe.CurrentRelay = null;
            AutoSetForwardDirection(false);
            if (UIShowing.Starmap) {
                S.UIQuery.RebuildStarmap.Ask(true, true);
                // if (A.Starmap != null) {
                //     A.Starmap.PrepareForRebuild($"Exit Hyperspace | {reason}");
                //     A.Starmap.ClearHover(true);
                // }
            }
            ScanSurroundings();
            // A.Starmap.SwitchLayer(UniverseLayer.Sector);
            S.Sig.EnteredHyperspace.Send(false);
            UpdateCapabilities();
            var ship = S.Universe.Player.Ship;
            ship.IsInHyperspace = false;
            ship.Sector = S.Universe.Player.Parent.Sector;
            S.Sig.NPCShipEnteredSector.Send(S.Universe.Player.Ship, ship.Sector, true);
            ShowHyperjumpRelayArrivalPopup();
        }

        private void ShowHyperjumpRelayArrivalPopup() {
            if (IsQuickHyperspace) {
                return;
            }
            var playerSector = A.PlayerSector;
            if (playerSector?.Sector?.IsHyperjumpRelay != true) {
                return;
            }
            if (!playerSector.HasChildren) {
                return;
            }
            SpaceObject relay = null;
            var children = playerSector.Children;
            for (int i = 0; i < children.Count; i++) {
                var child = children[i];
                if (child.Type.IdH == SpaceObjectTypeIdH.HyperjumpRelay) {
                    relay = child;
                    break;
                }
            }
            if (relay == null) {
                return;
            }
            if (!SOActionTravelTo.IsAvailable(S, S.Universe.Player, relay)) {
                return;
            }

            var subtitle = T.HyperjumpRelayArrivedPrompt_V1.F(
                Texts.Accented(T.SpaceObjectHyperjumprelay));
            string text = null;
            var toll = SOUtils.HyperRelayToll(S, relay, out _, out _, out _);
            if (toll > 0f) {
                text = T.HyperjumpRelayTollPrompt_V2.F(
                    Texts.Accented(Texts.LinkToFactionTooltip(relay.Faction), bold: false),
                    Texts.Accented(Wallet.ToValueString(toll)));
            }
            WPopup.Create(false, IconId.CHyperspaceEnter,
                    T.SpaceObjectHyperjumprelay, subtitle, text)
                .WithAction(T.SpaceObjectApproach, () => {
                    if (UIShowing.Starmap) {
                        S.UIQuery.StarmapFocusOn.Ask(relay, true);
                    }
                    SOActionTravelTo.BeginTravel(S, S.Universe.Player, relay);
                })
                .WithAction2(T.Cancel, null)
                .Spawn(center: true);
        }

        // What a jump out of the sector leaves behind, after the derelicts were cut loose
        // (AreaSys.TryAbandonDerelicts): whatever drifts in open space and the enemies out
        // there. Hyperjumps and warps (black holes, Void Ripper) share it, so a warp no longer
        // takes a derelict along (STA-1891).
        internal void LeaveSectorBehind() {
            S.Sys.Ship.RemoveLooseObjects();
            S.Sys.Combat.LeaveBehindOnJump();
        }

        public bool EnterHyperspace(SpaceObject parent, out string failReason) {
            if (hyperjumpQueue.IsPaused) {
                // Invariant: SetPaused clears WantsHyperspace while outside hyperspace, so
                // nothing should ever reach here paused. Refuse loudly rather than jumping and
                // immediately bouncing back out via the stuck-in-hyperspace safety net.
                D.Err("Tried to enter hyperspace while the hyperjump queue is paused!");
                failReason = $"{T.NounHyperjump}: {T.StatusOnHold}";
                return false;
            }
            D.Warn("Entering hyperspace from {0}", parent);
            if (!CanMoveInHyperspace(true, UniverseLayer.Region, out failReason)) {
                return false;
            }

            if (!S.Sys.Areas.TryAbandonDerelicts(out var isShipWithinMargin, leavingSector: true)) {
                return false;
            }

            if (!S.Sys.Ship.TryEnterObject(S.Universe.Hyperspace, parent)) {
                failReason = T.Error;
                return false;
            }

            S.Vars.HyperspaceEnterGameTime = GameTimer.time;

            LeaveSectorBehind();
            S.Sys.Scan.OnHyperspaceAction(0.1f);
            // S.Sys.NPCShip.CreateHyperTrail(S.Universe.Player.Ship, parent.Sector);

            if (A.IsInHyperspace) {
                D.Err("Hyperspace location was already set when entering hyperspace! {0}. Now trying to set: {1}",
                    S.Universe.HyperspaceLocation, parent);
            } else {
                S.Universe.StepsInHyperspace = 0;
            }
            S.Universe.HyperspaceLocation = parent;
            S.Situation.SectorDifficulty = parent?.SpaceEntity?.Biome?.Difficulty ?? 0f;

            // no solar panels in hyperspace, but we will reduce the output over time
            // S.BB.Set(BBKeyH.SolarOutputMultiplier, 0f);
            // if (!UIShowing.Starmap) {
            //     S.UIQuery.ShowStarmap.Ask(true);
            // }

            AutoSetForwardDirection(true);

            if (UIShowing.Starmap) {
                A.StarmapCtx.SwitchLayer(hyperjumpQueue.QueueLayer);
                // if (A.StarmapCtx.Layer != hyperjumpQueue.QueueLayer) {
                //     A.Starmap.SwitchLayer(hyperjumpQueue.QueueLayer, "Enter HS with queue layer");
                // }
                // if (S.Universe.IsUsingHyperspaceRelay) {
                //     A.Starmap.SwitchLayer(UniverseLayer.Universe, "NavSys EnterHS with relay");
                // } else if (A.StarmapCtx.Layer == UniverseLayer.Sector) {
                //     A.Starmap.SwitchLayer(UniverseLayer.Region, "NavSys EnterHyperspace w/o relay");
                // }
            }

            failReason = null;
            // D.Warn("Entering hyperspace: {0}", parent);
            S.Universe.Player.Ship.IsInHyperspace = true;
            S.Sig.NPCShipEnteredSector.Send(S.Universe.Player.Ship, parent.Sector, false);
            S.Sig.EnteredHyperspace.Send(true);

            if (S.Sys.Areas.IslandCount == 1) {
                var center = Pos.Encapsulate(
                    S.Sys.Areas.Islands.First().Value).CenterPos;
                if (!isShipWithinMargin || Pos.SqrDistance(center, S.Grid.CenterPos) > 20 * 20) {
                    S.CmdQ.Enqueue(new CmdRecenterShip(center));
                }
            }

            return true;
        }

        private void UpdateCapabilities() {
            var curHS = hyperspaceState;
            var curCond = flightCondition;

            var prevFlags = curHS.Flags;
            curHS.Flags = HyperspaceStateFlags.None;
            float prevMaxCharge = curHS.Region.MaxCharge;

            if (WantsHyperspace) {
                curHS.Flags |= HyperspaceStateFlags.WantsHyperspace;
            }
            if (A.IsInHyperspace) {
                curHS.Flags |= HyperspaceStateFlags.InHyperspace;
            }

            int enginesSec = 0;
            int enginesReg = 0;
            int enginesUni = 0;
            int enginesWarpReg = 0;
            int enginesWarpUni = 0;
            int enginesReadyReg = 0;
            int enginesReadyUni = 0;
            int enginesChargeableReg = 0;
            int enginesChargeableUni = 0;
            float maxChargeReg = 0f;
            float maxChargeUni = 0f;
            int jumpsReg = 0;
            int jumpsUni = 0;
            int warpsReg = 0;
            int warpsUni = 0;

            foreach (var engine in Engines) {
                if (!engine.ENode.IsConnected) { continue; }
                if (!engine.ENode.IsSwitchedOn) { continue; }
                if (engine.ENode.HasDeficit) { continue; }
                if (engine.Hyperdrive == null) {
                    enginesSec++;
                    continue;
                }
                var isShort = engine.WorksIn(UniverseLayer.Region);
                var isLong = engine.WorksIn(UniverseLayer.Universe);
                var isWarp = engine.Hyperdrive.IsWarp;

                if (!isShort && !isLong) {
                    D.Err("Misconfigured hyperdrive, neither short nor long range: {0}",
                        engine.Tile);
                    continue;
                }

                if (isShort) {
                    enginesReg++;
                    if (isWarp) {
                        enginesWarpReg++;
                    }
                }
                if (isLong) {
                    enginesUni++;
                    if (isWarp) {
                        enginesWarpUni++;
                    }
                }
                if (!engine.ENode.IsPowerable || !engine.ENode.IsReachable) {
                    continue;
                }
                if (engine.HasPush) {
                    if (isShort) {
                        enginesReadyReg++;
                    }
                    if (isLong) {
                        enginesReadyUni++;
                    }
                }
                if (engine.Hyperdrive.HasFuel) {
                    if (isShort) {
                        enginesChargeableReg++;
                    }
                    if (isLong) {
                        enginesChargeableUni++;
                    }
                }
                var ch = engine.Hyperdrive.Charge;
                if (isShort) {
                    var curMax = maxChargeReg;
                    maxChargeReg = math.max(curMax, ch);
                    if (ch >= 1f) {
                        jumpsReg++;
                        if (isWarp) {
                            warpsReg++;
                        }
                    }
                }
                if (isLong) {
                    var curMax = maxChargeUni;
                    maxChargeUni = math.max(curMax, ch);
                    if (ch >= 1f) {
                        jumpsUni++;
                        if (isWarp) {
                            warpsUni++;
                        }
                    }
                }
            }
            if (jumpsReg > 0) {
                curHS.Flags |= HyperspaceStateFlags.CanEnterHyperspace;
            }

            curHS.Region.Engines.Set(enginesReg);
            curHS.Region.EnginesReady.Set(enginesReadyReg);
            curHS.Region.EnginesChargeable.Set(enginesChargeableReg);
            curHS.Region.EnginesWarp.Set(enginesWarpReg);
            curHS.Region.MaxCharge.Set(maxChargeReg);
            curHS.Region.Jumps.Set(jumpsReg);
            curHS.Region.Warps.Set(warpsReg);

            curHS.Universe.Engines.Set(enginesUni);
            curHS.Universe.EnginesReady.Set(enginesReadyUni);
            curHS.Universe.EnginesChargeable.Set(enginesChargeableUni);
            curHS.Universe.EnginesWarp.Set(enginesWarpUni);
            curHS.Universe.MaxCharge.Set(maxChargeUni);
            curHS.Universe.Jumps.Set(jumpsUni);
            curHS.Universe.Warps.Set(warpsUni);

            curCond.HasEnginesSector.Set(enginesSec > 0);
            curCond.HasEnginesRegion.Set(enginesReg > 0);
            curCond.HasEnginesUniverse.Set(enginesUni > 0);
            curCond.HasBridge.Set(HasBridgeControls());
            curCond.HasBridgeOperator.Set(HasBridgeControlsOperator());
            curCond.IsInOnePiece.Set(IsInOnePiece);

            if (prevFlags != curHS.Flags || prevMaxCharge != maxChargeReg) {
                S.Sig.HyperspaceStateChanged.Send(curHS);
            }
        }

        public bool CanEnterHyperspaceNow(bool errorWithEngineList,
                out string failReason, string hsLayer) {
            if (A.InstantBuild) {
                failReason = null;
                return true;
            }
            var sb = Caches.StringBuilder;
            sb.Clear();
            bool isReady = CheckFlightReady(out var inOnePiece,
                out var hasBridge, out var enginesPowered, out var isWithinMargin);

            if (!HasEnginesForHyperspace(requireRunning: false, requireCharged: true, errorWithEngineList,
                    out var engineReason, hsLayer)) {
                sb.AppendFormat("\n- {0}", engineReason);
                isReady = false;
            } else {
                if (!A.Player.Ship.CombatState.CanHyperjump) {
                    sb.AppendFormat("\n- {0}: {1}", T.NounCombatMode, Texts.Red(T.HyperspaceNotCharged));
                    isReady = false;
                }
            }
            if (!inOnePiece) {
                sb.AppendFormat("\n- {0}", Texts.Red(T.ShipNotInOnePiece));
            }
            if (!enginesPowered) {
                sb.AppendFormat("\n- {0}", Texts.Red(T.ShipNoEngines));
            }
            if (!hasBridge) {
                sb.AppendFormat("\n- {0}", Texts.Red(T.FlightNoBridgeControls));
            }
            if (!isWithinMargin) {
                sb.AppendFormat("\n- {0}", Texts.Red(T.ShipInNoBuildZone));
            }
            if (S.Sys.Expedition.Expeditions.Count > 0) {
                // check if any expeditions are in a different location
                bool canRecall = false;
                foreach (var exp in S.Sys.Expedition.Expeditions) {
                    if (exp.CanRecall) {
                        canRecall = true;
                        break;
                    }
                }
                if (canRecall) {
                    sb.AppendFormat("\n- {0}", Texts.Red(T.HyperjumpTipExpedition));
                    isReady = false;
                }
            }
            if (S.Sys.Copters.Missions.Count > 0) {
                sb.AppendFormat("\n- {0}", Texts.Red(T.HyperjumpTipCopter));
                isReady = false;
            }
            var trades = S.Query.GetOngoingTrades.Ask();
            if (!Arrays.IsEmpty(trades)) {
                if (trades.Count > 1) {
                    sb.AppendFormat("\n- {0} {1}", Texts.Red(T.OngoingTrade),
                        Texts.Red(Units.XNum(trades.Count)));
                } else {
                    sb.AppendFormat("\n- {0}", Texts.Red(T.OngoingTrade));
                }
                isReady = false;
            }
            if (!isReady) {
                failReason = sb.ToString().Trim();
            } else {
                failReason = null;
            }
            return isReady;
        }

        public void SetWantHyperspace(bool yes, bool silent = false) {
            if (WantsHyperspace == yes) { return; }
            // D.Warn("Setting want hyperspace: {0} => {1}", WantsHyperspace, yes);
            DoSetWantHyperspace(yes, silent);
        }

        private void DoSetWantHyperspace(bool yes, bool silent = false) {
            var didWantHyperspace = WantsHyperspace;
            WantsHyperspace = yes;
            // may be null on load
            if (A.Player.Ship != null) {
                A.Player.Ship.SetWantsHyperspace(yes);
            }
            if (yes) {
                if (S.Sys.Ship.HasDestination(out _)) {
                    S.Sys.Ship.CancelFlight();
                }
                if (!didWantHyperspace && !silent) {
                    S.Sig.ShowFanfare.Send(T.PreparingForHyperjump);
                }
            } else {
                if (!A.IsInHyperspace && !silent) {
                    S.Sig.ShowFanfare.Send(T.HyperjumpCancelled);
                }
                if (UIShowing.Starmap) {
                    S.UISig.OnHoverUISpaceObject.Send(null, false);
                }
            }
            UpdateCapabilities();
            enterHyperspaceTimeoutTicks = null;
        }

        public void DoEnterHyperspace() {
            var nextJumpTarget = S.Query.GetNextHyperjumpTarget.Ask();
            if (nextJumpTarget == null) {
                // Every jump has somewhere to go. Entering with nowhere to go is how an open
                // starmap rebuilt mid-entry and stranded the ship in hyperspace (STA-1783).
                D.Warn("Wanted hyperspace without a jump target, cancelling");
                SetWantHyperspace(false);
                return;
            }
            if (UIShowing.Starmap) {
                var wantedLayer = UniverseLayer.Region;
                if (nextJumpTarget.SO.Parent != A.PlayerSector.Parent) {
                    wantedLayer = UniverseLayer.Universe;
                }
                A.StarmapCtx.SwitchLayer(wantedLayer);
            }
            if (EnterHyperspace(S.Universe.Player.Parent, out var failReason)) {
                if (!IsQuickHyperspace) {
                    UISounds.PlayHyperjump();
                }
                if (!IsQuickHyperspace) {
                    S.Sig.ShowFanfare.Send(T.EnteredHyperspace);
                    S.Sys.Log.AddLine(T.EnteredHyperspace);
                }
            } else {
                if (!IsQuickHyperspace) {
                    D.Warn("Enter hyperspace failed: {0}", failReason);
                    var title = $"{T.EnterHyperspaceFailed}\n{failReason}";
                    S.Sig.ShowErrorMessage.Send(title);
                    S.Sys.Log.AddLine(title);
                }
                SetWantHyperspace(false);
                hyperjumpQueue.SetPaused(true);
            }
        }

        private readonly Dictionary<string, string> supportedEnginesCache = new();

        public string SupportedEngineListFor(string layer) {
            if (supportedEnginesCache.TryGetValue(layer, out var msg)) {
                return msg;
            }
            Caches.StringBuilder2.Clear();
            int count = 0;
            foreach (var d in The.Defs.Defs) {
                var engineCfg = d.Value.ComponentConfigFor(ComponentIdH.ShipEngine, false);
                if (engineCfg == null) { continue; }
                var worksIn = engineCfg.GetStringSet(PropertyIdH.WorksIn, null);
                if (worksIn != null && Arrays.Contains(worksIn, layer)) {
                    count++;
                    Caches.StringBuilder2.AppendFormat("\n - {0}", d.Value.CodexLink);
                }
            }
            if (count == 0) {
                var err = $"No engine can work in Starmap layer: {layer}! Was this layer added by a mod?";
                supportedEnginesCache.Add(layer, err);
                D.Err(err);
                return err;
            }
            var compTypes = T.CompatibleEngineTypes_V2.F(
                UniverseLayer.FlightType(layer),
                Caches.StringBuilder2.ToString());

            supportedEnginesCache.Add(layer, compTypes);
            return compTypes;
        }

        public string GetName() {
            return Id;
        }
        private ComponentData data;

        private ComponentData Data => data ??= new(0, GetName());

        public ComponentData Save() {
            var d = Data;
            d.SetInt("Direction", (int) ShipDirection);
            d.SetBool("AutoForwardDirection", AutoForwardDirectionEnabled);
            d.SetBool("WantsHyperspace", WantsHyperspace);
            hyperjumpQueue.OnSave(d);
            return d;
        }

        public void Load(ComponentData data) {
            ShipDirection = (Facing.Type) data.GetInt("Direction",
                (int) Facing.Type.Right);
            AutoForwardDirectionEnabled = data.GetBool("AutoForwardDirection", true);
            var wantHS = data.GetBool("WantsHyperspace", false);
            WantsHyperspace = wantHS;
            this.data = data;
            Ready.When(WhenH.AllSystemsLoaded, RestoreData);
        }

        private void RestoreData() {
            hyperjumpQueue.OnLoad(data);
            if (WantsHyperspace && hyperjumpQueue.IsPaused && !A.IsInHyperspace) {
                // Repair saves written before the pause/WantsHyperspace invariant existed.
                D.Warn("Loaded a paused hyperjump queue that wanted hyperspace, clearing WantsHyperspace");
                WantsHyperspace = false;
            }
            if (WantsHyperspace && !A.IsInHyperspace && !hyperjumpQueue.HasTarget) {
                // A legacy warp target is not loaded, don't enter hyperspace with nowhere to go
                D.Warn("Loaded a hyperjump preparation without a target, clearing WantsHyperspace");
                WantsHyperspace = false;
            }
            UpdateCapabilities();
        }


        // Queueing a jump is fine while the drive charges or the bridge is switched off -
        // those sort themselves out and the queued jump fires when they do. A ship in pieces,
        // or one without bridge controls or any engine for this kind of jump, would keep the
        // jump waiting on something the player still has to build, and the abandon warning
        // shown while queueing would be about a jump that cannot happen. So these refuse.
        public bool CanQueueHyperjump(string hsLayer, out string failReason) {
            failReason = null;
            if (A.InstantBuild || A.IsInHyperspace) { return true; }
            if (S.Universe.IsUsingHyperspaceRelay && hsLayer == UniverseLayer.Universe) {
                return true;
            }
            var sb = new System.Text.StringBuilder();
            if (!IsInOnePiece) {
                sb.AppendFormat("\n- {0}", Texts.Red(T.ShipNotInOnePiece));
            }
            if (!HasBridgeControlsDevice()) {
                sb.AppendFormat("\n- {0}", Texts.Red(T.FlightNoBridgeControls));
            }
            if (!HasEngineDeviceFor(hsLayer)) {
                sb.AppendFormat("\n- {0}", Texts.Red(T.HyperspaceMissingEngine));
            }
            if (sb.Length == 0) { return true; }
            failReason = sb.ToString();
            return false;
        }

        // Built, whether or not it is switched on or powered
        private bool HasBridgeControlsDevice() {
            var all = S.Components.AllByType<BridgeControlsComp>();
            if (all == null) { return false; }
            foreach (BridgeControlsComp bc in all) {
                if (bc.Entity.IsActive && bc.Tile.IsConstructed) { return true; }
            }
            return false;
        }

        // Built, whether or not it is powered or charged
        private bool HasEngineDeviceFor(string hsLayer) {
            foreach (var engine in Engines) {
                if (engine.Tile.IsConstructed && engine.WorksIn(hsLayer)) { return true; }
            }
            return false;
        }

        public bool CanMoveInHyperspace(bool errorWithEngineList, string hsLayer,
                out string failReason) {
            if (A.InstantBuild) {
                failReason = null;
                return true;
            }

            if (S.Universe.IsUsingHyperspaceRelay && hsLayer == UniverseLayer.Universe) {
                failReason = null;
                // D.Warn("Can enter HS now: true (using relay)");
                return true;
            }

            if (A.IsInHyperspace) {
                return HasEnginesForHyperspace(requireRunning: true, requireCharged: true,
                        errorWithEngineList, out failReason, hsLayer);
            } else {
                return CanEnterHyperspaceNow(errorWithEngineList, out failReason, hsLayer);
            }
        }

        public bool CanStayInHyperspace(string hsLayer, out string failReason) {
            return HasEnginesForHyperspace(requireRunning: true, requireCharged: false,
                errorWithEngineList: false, out failReason, hsLayer);
        }

        /// <summary>
        /// Charge/running-independent check: does the ship have any engine at all
        /// capable of operating in this hyperspace layer? Use this to validate a
        /// jump before it's enqueued, so we don't wait forever for a non-existent engine.
        /// </summary>
        public bool HasEngineForLayer(string hsLayer, out string failReason) {
            return HasEnginesForHyperspace(requireRunning: false, requireCharged: false,
                errorWithEngineList: true, out failReason, hsLayer);
        }

        private bool HasEnginesForHyperspace(bool requireRunning, bool requireCharged,
                bool errorWithEngineList, out string failReason, string hsLayer) {
            bool hasEngine = false;
            bool hasCharge = false;
            if (A.InstantBuild) {
                failReason = null;
                return true;
            }

            if (hsLayer == UniverseLayer.Universe) {
                if (S.Universe.IsUsingHyperspaceRelay) {
                    hsLayer = UniverseLayer.Region;
                }
            }

            foreach (var engine in Engines) {
                if (engine.IsPowered && engine.ENode.IsReachable &&
                        !engine.ENode.HasDeficit && engine.WorksIn(hsLayer)) {
                    hasEngine = true;
                } else {
                    continue;
                }
                var charged = engine.Hyperdrive?.IsCharged == true;
                var running = engine.HasPush;
                if (charged) {
                    hasCharge = true;
                }
                // A jump is always performed by one single drive (see TryUseHyperdrive), so
                // charge and push have to come from the *same* engine. Accumulating them
                // separately reports a jump as possible while every charged drive is idle and
                // every running drive is flat, and the jump then fails for real.
                if ((!requireCharged || charged) && (!requireRunning || running)) {
                    failReason = null;
                    return true;
                }
            }

            if (!hasEngine) {
                failReason = T.HyperspaceMissingEngine;
                if (errorWithEngineList) {
                    var engines = SupportedEngineListFor(hsLayer);
                    failReason = $"{failReason} {engines}";
                }
            } else if (requireCharged && !hasCharge) {
                failReason = T.HyperspaceNotCharged;
                if (errorWithEngineList) {
                    var engines = SupportedEngineListFor(hsLayer);
                    failReason = $"{failReason} {engines}";
                }
                if (G.ClockSpeed == ClockSpeed.Stopped) {
                    var timeWarning = Texts.Accented(T.WarningTimeStopped);
                    failReason = $"{failReason}{GlyphMap.BRHR}{timeWarning}";
                }
            } else {
                // Either nothing is running at all, or charge and push exist but never
                // together on one drive.
                failReason = T.HyperdriveNotRunning;
            }

            if (requireCharged || requireRunning) {
                return false;
            }
            // D.Warn("Has engines for hyperspace (uncharged): {0}", hasEngine);
            return hasEngine;
        }

        public bool TryMoveInHyperspace(string hsLayer, bool isWarp,
                out string failReason) {
            if (!CanMoveInHyperspace(true, hsLayer, out failReason)) {
                return false;
            }

            if (S.Universe.IsUsingHyperspaceRelay && !isWarp
                    && hsLayer == UniverseLayer.Universe) {
                // pay the toll
                var toll = SOUtils.HyperRelayToll(S, S.Universe.CurrentRelay, out var baseCost, out var tax, out var disc);
                if (toll > 0f) {
                    if (!S.Wallet.TryPay(toll, null)) {
                        SOActionUseRelay.ShowCannotAffordPopup(S, S.Universe.CurrentRelay);
                        return false;
                    } else {
                        var msg = $"{Texts.Accented(T.PaidToll)}: {Wallet.ToValueString(toll)} ({T.NounTax}: {Units.Percentage(tax)}, {T.NounDiscount}: {Units.Percentage(disc)})";
                        S.Sig.ShowFanfare.Send(msg);
                        S.Sys.Log.AddLine(msg);
                    }
                }
                D.Warn("TryMove success (relay)");

                return true;
            }

            if (!isWarp) {
                // try to use non-warp drive first, falling back to a warp drive below
                if (TryUseHyperdrive(hsLayer, false, ref failReason, hasFallback: true)) {
                    return true;
                }
            }
            return TryUseHyperdrive(hsLayer, true, ref failReason);
        }

        /// <param name="hasFallback">Set when this is the speculative first attempt and the
        /// caller will try the other drive type next. Failing then is an expected outcome
        /// (e.g. only the warp drives are running), not an inconsistency worth reporting.</param>
        public bool TryUseHyperdrive(string hsLayer, bool warp, ref string failReason,
                bool hasFallback = false) {
            var pool = ArrayPool<HyperdriveComp>.Shared;
            var runningDrives = pool.Rent(Engines.Count);
            int usableCount = 0;
            int runningCount = 0;

            foreach (var engine in Engines) {
                if (!engine.WorksIn(hsLayer)) {
                    continue;
                }
                if (engine.Hyperdrive != null) {
                    if (engine.Hyperdrive.IsWarp != warp) { continue; }
                    usableCount++;
                    if (engine.HasPush) {
                        runningDrives[runningCount++] = engine.Hyperdrive;
                    } else {
                        D.Warn("Hyperdrive has no push: {0} | F:{1}, C:{2}",
                            engine.Tile, engine.Hyperdrive.Fuel.CurrentStock, engine.Hyperdrive.Charge);
                    }
                }
            }

            if (usableCount == 0 || runningCount == 0) {
                pool.Return(runningDrives);
                if (A.InstantBuild) {
                    failReason = null;
                    return true;
                }
                if (usableCount == 0) {
                    failReason = T.HyperspaceMissingEngine;
                    if (!hasFallback) {
                        D.Err("CanMoveInHyperspace did not catch missing engine! warp: {0}", warp);
                    }
                } else {
                    failReason = T.HyperdriveNotRunning;
                    if (!hasFallback) {
                        D.ErrSoft("CanMoveInHyperspace did not catch inactive engine! warp: {0}", warp);
                    }
                }
                return false;
            }

            var startIdx = Rng.URange(0, runningCount);

            for (int i = startIdx + 1; i < runningCount + startIdx + 1; i++) {
                var idx = i % runningCount;
                #if DEBUG_HYPERJUMPS
                D.Warn("Trying hyperdrive: #{0} (Warp: {1}). startIdx: {2}, runningCount: {3}",
                    idx, warp, startIdx, runningCount);
                #endif
                var drive = Arrays.SafeGet(runningDrives, idx);
                if (drive == null) {
                    D.Err("Drive was null: {0} | {1}", idx, runningCount);
                    continue;
                }
                if (drive.TryJump()) {
                    #if DEBUG_HYPERJUMPS
                    D.Warn("Jumping with hyperdrive: #{0} (Warp: {1})", idx, warp);
                    #endif
                    pool.Return(runningDrives);
                    S.Universe.StepsInHyperspace++;
                    return true;
                }
            }

            pool.Return(runningDrives);
            if (A.InstantBuild) {
                failReason = null;
                return true;
            }
            #if DEBUG_HYPERJUMPS
            D.Warn("Failed to use hyperdrive: {0} (warp; {1}) | {2}", hsLayer, warp, failReason);
            #endif
            return false;
        }

        private int? enterHyperspaceTimeoutTicks;
        private bool CanWarp {
            get {
                string hsLayer = SOUtils.CurrentStarmapLayer(S, true);
                HyperspaceLayerState lState;
                if (hsLayer == UniverseLayer.Universe) {
                    lState = hyperspaceState.Universe;
                } else {
                    lState = hyperspaceState.Region;
                }
                return lState.EnginesWarp > 0;
            }
        }

        public void OnTick(long ticks) {
            // We have to call this regardless if we have a destination or not, we may
            // be drifting in space
            RunHyperdrives();
            TickHyperspace();
            hyperjumpQueue.OnTick(ticks, hyperspaceState);
            ScanSurroundings();
        }

        private void TickHyperspace() {
            CheckEnterHyperspace();
            UpdateCapabilities();
            // UpdateHyperspaceUI(true);
        }

        private void RunHyperdrives() {
            bool hasOperator = S.Sys.ShipNav.HasBridgeControlsOperator() || A.InstantBuild;
            // run all hyperdrives
            for (int i = Engines.Count - 1; i >= 0; i--) {
                ShipEngineComp e = Engines[i];
                if (e.Hyperdrive == null) { continue; }
                if (!e.IsPowered) { continue; }
                if (WantsHyperspace) {
                    if (hasOperator) {
                        // after requesting motion it will return the amount
                        // of heading that is remaining
                        e.RequestMotion(true);
                    }
                } else {
                    e.Kill(false, true);
                }
            }
            if (A.IsInHyperspace) {
                if (!WantsHyperspace) {
                    ExitHyperspace(null);
                    return;
                }
                // Safety net: an indefinitely-paused queue will never move us or manage an
                // exit. If we also can no longer stay in hyperspace (e.g. a power deficit
                // killed every hyperdrive), force an exit instead of getting stuck forever.
                if (hyperjumpQueue.IsPaused && hyperjumpQueue.PausedUntil == 0
                        && !CanStayInHyperspace(S.Universe.HyperspaceLocation.Layer,
                            out var stayReason)) {
                    D.Warn("Stuck in hyperspace with queue paused and no way to stay, exiting");
                    ExitHyperspace(stayReason);
                    return;
                }
                // reduce solar output a bit to phase the solar panels away
                var curOutput = S.BB.Get(BBKeyH.SolarOutputMultiplier, 1f);
                // in case we were near a star or something else boosted it
                if (curOutput > 0f) {
                    if (curOutput > 1f) { curOutput = 1f; }
                    var phaseOutRate = 1f /
                        (Tunables.SolarPanelHyperspacePhaseOutHours
                            * Consts.TicksPerHour);
                    // D.Warn("Phasing out solar: {0} -> {1}",
                    //     curOutput, curOutput - phaseOutRate);
                    curOutput -= phaseOutRate;
                    if (curOutput < 0f) {
                        curOutput = 0f;
                    }
                    S.BB.Set(BBKeyH.SolarOutputMultiplier, curOutput);
                }
            }
        }

        private void CheckEnterHyperspace() {
            // not in hyperspace yet, but want to be
            if (!WantsHyperspace || A.IsInHyperspace) {
                return;
            }
            if (!S.Sys.ShipNav.HasBridgeControlsOperator() && !A.InstantBuild) {
                return;
            }
            // We have to check the region layer here because the player can fuck around
            // and change things in the starmap while hyperjump is preparing
            if (HasEnginesForHyperspace(requireRunning: true, requireCharged: true,
                    true, out var failReason, UniverseLayer.Region)) {
                DoEnterHyperspace();
            } else {
                if (enterHyperspaceTimeoutTicks == null) {
                    enterHyperspaceTimeoutTicks = Consts.TicksPerHour;
                }
                if (enterHyperspaceTimeoutTicks-- < 0) {
                    SetWantHyperspace(false);
                    string title = T.EnterHyperspaceFailed;
                    if (failReason != null) {
                        title = $"{Texts.Red(title)}: {failReason}";
                    }
                    S.Sig.ShowFanfare.Send(title);
                    S.Sys.Log.AddLine(title);
                }
            }
        }

        public void AutoSetForwardDirection(bool forHyperspace) {
            if (!AutoForwardDirectionEnabled) {
                return;
            }
            var directionCounts = new Dictionary<Facing.Type, int>();
            foreach (var engine in Engines) {
                if (engine.IsManeuvering) { continue; }
                if (!engine.ENode.IsPowerable) { continue; }
                if (engine.Hyperdrive == null) {
                    if (forHyperspace) { continue; }
                } else {
                    if (!forHyperspace) { continue; }
                    // warp drives don't affect direction
                    if (engine.Hyperdrive.IsWarp) { continue; }
                }
                var dir = engine.Tile.Transform.Rotation;
                dir = Facing.Opposite(dir);
                // increments the directionCounts by 1 for given dir
                directionCounts.GetIncrement(dir);
            }
            // Find the direction that has the most engines
            var maxDirection = ShipDirection;
            var maxCount = 0;
            var minCount = int.MaxValue;
            foreach (var kvp in directionCounts) {
                if (kvp.Value > maxCount) {
                    maxDirection = kvp.Key;
                    maxCount = kvp.Value;
                }
                if (kvp.Value < minCount) {
                    minCount = kvp.Value;
                }
            }
            if (directionCounts.Count == 1) {
                // otherwise it will never change the direction
                minCount = 0;
            }
            // Update the ShipDirection if it is different and if there are more engines
            // in one direction than others
            if (maxCount > minCount && maxDirection != ShipDirection) {
                ShipDirection = maxDirection;
                D.Warn("Auto-set ship forward direction to: {0}", ShipDirection);
            }
        }

        private void ScanSurroundings() {
            if (!S.Sys.Sec.IsRadarActive()) {
                // D.Warn("radar inactive");
                return;
            }
            // we have to know where we are in the region and if radar is active, ensure
            // that we see the surroundings
            var loc = S.Universe.HyperspaceLocation ?? S.Universe.Player.Parent;
            if (loc.Sector == null) {
                // we are in region in hyperspace
                // D.Warn("not in sector");
                return;
            }
            var u = S.Sys.Universe;
            bool generated = u.GenerateSector(loc, false);
            if (generated) {
                S.Sig.SpaceObjectChanged.Send(loc);
            }
            foreach (var link in loc.Sector.Links) {
                var neighbor = link.OtherSideOf(loc.Sector);
                if (u.GenerateSector(neighbor.SO, false)) {
                    // D.Warn("generated {0}", neighbor.SO);
                    S.Sig.SpaceObjectChanged.Send(neighbor.SO);
                    generated = true;
                }
            }
            // if (generated && UIShowing.Starmap) {
            //     S.Sig.RebuildStarmap.Send(false);
            // }
        }

    }
}
