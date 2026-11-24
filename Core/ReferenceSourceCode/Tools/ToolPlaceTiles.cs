#if !PRODUCTION_BUILD
// Off by default: on, Prevalidate turns from a field into a property, and dev builds would no
// longer have the shape mods bind to (see Clock.Ticks)
// #define DEBUG_PREVALIDATE
#endif
using System.Collections.Generic;
using Game.Commands;
using System.Linq;
using Game.Constants;
using Game.Data;
using KL.Grid;
using UnityEngine;
using KL.Utils;
using Game.Rendering;
using Game.Utils;
using Game.CodeGen;
using Game.UI;
using Game.Input;
using Game.Components;
using Game.Systems.Hints;
using System;
using Game.Systems;
using KL.Randomness;
using Game.Systems.AI;

namespace Game.Tools {
    public sealed class ToolPlaceTiles : TileTool, IModifyTool, IToolWithSubmenu {
        [SelfInit]
        private static void Register() {
            Toolbox.Add(ToolId, () => new ToolPlaceTiles());
        }

        private bool justPlacedTile;
        public bool CanBuild => true;
        public bool CanRemove => false;
        public override ActionType Shortcut => ActionType.ShortcutPlaceTiles;
        public override string[] ShortcutKeys => new[] { "<Keyboard>/q", "<Gamepad>/select" };
        public override bool IsResearchable => true;
        public override bool IsBuild => true;
        /// <summary>
        /// Next time a genius idea to make tool icons colorful comes to mind,
        /// read this:
        /// TOOL ICONS ARE WHITE BECAUSE THEY GET TINTED WHEN TOOL GETS SELECTED.
        /// YOU TRIED CHANGING THIS THREE TIMES ALREADY YOU FUCKING MORON!
        /// </summary>
        public override string MenuIcon => IconId.WHammer;
        public override int Cursor => CursorTypeH.CursorHammer;
        protected override Color markedColor => Consts.ColorCompatiblePlacement;
        public ReinstallableComp Reinstallable;
        public bool IsMassReinstall;
        #if DEBUG_PREVALIDATE
        private Func<int, Tile, bool> prevalidate;
        public Func<int, Tile, bool> Prevalidate {
            get => prevalidate;
            set => D.SetFieldWithDebug(ref prevalidate, value, "Prevalidate");
        }
        #else
        public Func<int, Tile, bool> Prevalidate;
        #endif

        private bool isInitialized;

        protected override void Initialize() {
            if (isInitialized) {
                D.Err("Already initialized tool place tiles!");
                return;
            }
            isInitialized = true;
            S.Sig.ResearchChanged.AddListener(OnResearchChanged);
        }

        private List<string> unlockedParams;

        public override List<string> UnlockedParams {
            get {
                if (unlockedParams != null) { return unlockedParams; }
                if (A.InstantBuild) {
                    unlockedParams = AvailableParams;
                    return unlockedParams;
                }
                unlockedParams = new List<string>();
                foreach (var p in AvailableParams) {
                    var def = The.Defs.Get(p);
                    if (S.Research.IsDefUnlocked(def)) {
                        unlockedParams.Add(p);
                    }
                }
                return unlockedParams;
            }
        }

        private void OnResearchChanged(Def def) {
            unlockedParams = null;
        }

        public static ToolPlaceTiles StartBuilding(string defId) {
            var buildTool = The.Toolbox.Get<ToolPlaceTiles>(ToolId);
            The.Toolbox.SetActive(buildTool, defId);
            UISounds.PlayButtonClick();
            return buildTool;
        }

        private bool IsSingle {
            get {
                if (tilePreview == null) { return true; }
                if (Reinstallable != null) { return true; }
                return tilePreview.Transform.Placement == PlacementType.Single;
            }
        }

        private bool IsRow =>
            tilePreview.Transform.Placement == PlacementType.Row;
        private bool IsRect =>
            tilePreview.Transform.Placement == PlacementType.Rect;

        private float mouseIdleTime;

        private string placementTooltip {
            get {
                if (tilePreview == null) { return null; }
                if (tilePreview.Definition.IsTemplate) { return null; }
                var sb = Caches.StringBuilder;
                sb.Clear();

                if (isAbleToPlace) {
                    if (tilePreview.Transform.IsRotatable && !IsRow) {
                        sb.Append(T.RotateTip_V1.F(The.Bindings.GetControlName(
                            ActionType.Rotate)));
                        sb.Append("\n");
                    }
                }

                var pc = tilePreview.Constructable.Placement;
                sb.Append($"placement.tip.{pc}".T());

                if (tilePreview.TryGetComponent<OutdoorsCheckComp>(out var outCheck)) {
                    sb.Append("\n");
                    var isOutdoors = S.Sys.Areas.IsOutside(HoverPos);
                    if (outCheck.MustBeOutdoors) {
                        if (isOutdoors) {
                            sb.Append(Texts.Green(T.MustBeOutdoors));
                        } else {
                            sb.Append(Texts.Red(T.MustBeOutdoors));
                        }
                    } else {
                        if (isOutdoors) {
                            sb.Append(Texts.Red(T.MustBeIndoors));
                        } else {
                            sb.Append(Texts.Green(T.MustBeIndoors));
                        }
                    }
                }
                if (tilePreview.TryGetComponent<HeatProducerComp>(out var heatProd)) {
                    sb.Append("\n");
                    var isOutdoors = S.Sys.Areas.IsOutside(HoverPos);
                    if (isOutdoors) {
                        sb.Append(Texts.Yellow(T.HeatProducer));
                    } else {
                        sb.Append(Texts.Red(T.HeatProducer));
                    }
                }

                if (tilePreview.Transform.HasAnyPort) {
                    sb.Append("\n");
                    sb.Append(T.PlacementTipPorts);
                }

                if (tilePreview.Transform.SpacingCategoryMinGap > 0) {
                    sb.Append("\n");
                    sb.Append(T.PlacementTipSpacingGap_V1.F(
                        tilePreview.Transform.SpacingCategoryMinGap));
                }

                if (!IsSingle) {
                    var cycleControl = The.Bindings.GetControlName(ActionType.CycleToolMode);
                    if (!string.IsNullOrWhiteSpace(cycleControl)) {
                        sb.Append(T.CyclePlacementModeTip_V1.F(cycleControl));
                    }
                }

                // This used to be useful when radial menu was the only option
                // var toolPrev = The.Bindings.GetControlName(ActionType.ToolPrev);
                // if (!string.IsNullOrWhiteSpace(toolPrev)) {
                //     var toolNext = The.Bindings.GetControlName(ActionType.ToolNext);
                //     if (!string.IsNullOrWhiteSpace(toolNext)) {
                //         sb.Append(T.CycleBlueprintTip_V2.F(toolPrev, toolNext));
                //     }
                // }

                tilePreview.Definition.AppendConstructableDesc(sb, true, true);

                return sb.ToString();
            }
        }

        public const string ToolId = "place_tiles";

        public override string Id => ToolId;

        public override bool IsConfigurable => true;

        private List<string> availableParams;
        private List<string> availableParamsDev;
        public override List<string> AvailableParams =>
            availableParams ??= The.Defs.Defs.Values.Where(CanShowInBuildMenu)
                .Select(v => ToolPath(v)).ToList();
        public override List<string> AvailableParamsDev =>
            availableParamsDev ??= The.Defs.Defs.Values.Where(
                v => !v.IsAbstract
                    && WorldLayer.IsDataTileLayer(v.LayerId))
                .Select(v => ToolPath(v)).ToList();

        public static bool CanShowInBuildMenu(Def def) {
            return def != null
                && (!def.SkipToolListing || A.InstantBuild)
                && (def.IsInSeason || A.InstantBuild)
                && !def.IsTemplate && !def.IsAbstract
                && WorldLayer.IsDataTileLayer(def.LayerId);
        }

        public override void UpdateVFX(ParticlesSys ps) {
            if (isHoverHidden) { return; }
            ps.ElectricityVFX.ShowBuildTile(tilePreview, lastHoverPos);
            // A tall graphic also covers the tile above its base, so the outline
            // shows the tiles it will take
            if (!Consts.DisableHoverEffects && tilePreview?.Graphics?.HasHeight == true
                    && !Pos.IsOOB(lastHoverPos)) {
                ps.SelectionVFX.ShowBuildFootprint(tilePreview, lastHoverPos,
                    isAbleToPlace);
            } else {
                ps.SelectionVFX.HideBuildFootprint();
            }
            if (mouseIdleTime < 1f) {
                if (allWorkSpots.Count > 0 || allInPorts.Count > 0 || allOutPorts.Count > 0) {
                    ps.SelectionVFX.ShowWorkSpotsAndPorts(allWorkSpots, allInPorts, allOutPorts);
                } else {
                    ps.SelectionVFX.ShowWorkSpot(tilePreview, lastHoverPos);
                    ps.SelectionVFX.ShowPorts(tilePreview, lastHoverPos);
                }
            }
            ps.SelectionVFX.ShowPreviewRange(tilePreview, lastHoverPos);
            if (Reinstallable != null) {
                ps.SelectionVFX.CreateLine(Reinstallable.Tile.Position,
                    lastCoords);
            }
        }
        public override bool Cancel() {
            ClearTilePreview();
            S.Sys.Particles?.SelectionVFX?.HideAll();
            allWorkSpots.Clear();
            allOutPorts.Clear();
            allInPorts.Clear();
            return base.Cancel();
        }

        private readonly List<int> allWorkSpots = new();
        private readonly List<int> allInPorts = new();
        private readonly List<int> allOutPorts = new();

        public override void AddParticles(Dictionary<int, XParticles> x) {
            if (tilePreview == null || isHoverHidden) { return; }
            var ps = GetMouseRectParticles(x);
            var psc = AddModeParticles(ps.Particles, 0);

            if (drawRect) {
                var dpc = dragPositions.Count;
                if (dpc < 400) {
                    if (ps.Particles.Length <= psc + dpc) {
                        psc += dpc;
                    } else {
                        for (int i = 0; i < dpc; i++) {
                            int pos = dragPositions[i];
                            var color = DragCellColor(pos);
                            if (color == null) { continue; }
                            var position = Pos.ToVector(pos);
                            ps.Particles[psc++] = ParticlesSys.ParticleAt(
                                position, color.Value, 1, 0.1f);
                        }
                    }
                } else {
                    // We're fucking insane and trying to select too much
                    var color = !isInDeleteMode ? Consts.GreenRectColor : Consts.RedRectColor;
                    if (ps.Particles.Length > psc) {
                        ps.Particles[psc++] = ParticlesSys.ParticleAt(lastRect.Center(),
                            color, lastRect.Extents, 0.1f);
                    } else {
                        psc++;
                    }
                }
            }

            ps.ShowParticles(psc, false);
        }

        private static string ToolPath(Def def) {
            if (!string.IsNullOrWhiteSpace(def.ToolPath)) {
                var ids = def.Id.Split('/');
                var idEnd = ids[ids.Length - 1];
                return $"{def.ToolPath.TrimEnd('/')}/{idEnd}";
            }
            return def.Id;
        }

        // The build menu tab that lists the def
        public static string MenuTabOf(Def def) {
            var path = string.IsNullOrWhiteSpace(def.ToolPath) ? def.Id : def.ToolPath;
            var slash = path.IndexOf('/');
            return slash < 0 ? path : path.Substring(0, slash);
        }
        private int currentLayer;
        private string currentDef;
        // public set for tests
        public Facing.Type SelectedRotation { get; private set; }
        private Facing.Type lastRotation;
        private Tile tilePreview;
        public Def SelectedDef => tilePreview?.Definition;
        public Tile TilePreview => tilePreview;
        private readonly Dictionary<int, bool> placementAbilityCache = new();
        // Set from the positions of the current drag: the drag reaches empty space,
        // so the floors or walls already in it are left alone
        private bool dragFillsGapsOnly;
        private readonly List<int> modePositions = new();
        private Action<int> addModePosition;

        private bool isAbleToPlace;

        private int AddModeParticles(ParticleSystem.Particle[] p, int offset) {
            if (tilePreview == null) { return offset; }
            offset = DrawGuidelines(p, offset, dragEnd);

            if (isInBucketMode && bucketFill.Count > 0) {
                DrawBucketFill((pos) => {
                    if (p.Length > offset) {
                        p[offset++] = ParticlesSys.ParticleAt(Pos.ToVector(pos),
                            Consts.GreenRectColor, RenderUtils.TilePulse, 0.1f);
                    } else {
                        offset++;
                    }
                });
            } else if (isDragging) {
                offset = DrawGuidelines(p, offset, dragStart);
                if (isInCircleMode || isInLineMode) {
                    modePositions.Clear();
                    addModePosition ??= modePositions.Add;
                    if (isInCircleMode) {
                        DrawCircle(addModePosition);
                    } else {
                        DrawLine(addModePosition);
                    }
                    dragFillsGapsOnly = CmdPlaceTiles.FillsGapsOnly(modePositions,
                        tilePreview.Definition.LayerId);
                    for (int i = 0; i < modePositions.Count; i++) {
                        offset = DrawCellChecked(p, offset, modePositions[i]);
                    }
                }
            }

            return offset;
        }

        private int DrawCellChecked(ParticleSystem.Particle[] p, int offset, int pos) {
            var color = DragCellColor(pos);
            if (color == null) { return offset; }
            if (p.Length > offset) {
                p[offset++] = ParticlesSys.ParticleAt(Pos.ToVector(pos), color.Value,
                    RenderUtils.TilePulse, 0.1f);
            } else {
                offset++;
            }
            return offset;
        }

        // What a drag will do at pos, decided the way CmdPlaceTiles decides it. No color
        // means the drag leaves the tile alone
        private Color? DragCellColor(int pos) {
            if (isInDeleteMode) {
                return Consts.RedRectColor;
            }
            if (dragFillsGapsOnly && CmdPlaceTiles.HasStructAt(pos,
                    tilePreview.Definition.LayerId)) {
                return null;
            }
            return IsAbleToPlace(pos, true) ? Consts.GreenRectColor : Consts.RedRectColor;
        }

        private Vector2 lastCoords;

        protected override void HideHover() {
            ToolGrid.TrySet(lastHoverPos, null);
            base.HideHover();
            var sp = S.Sys.Particles;
            sp.ElectricityVFX?.EndBuildPreview();
            HidePreviewMarkers();
            if (Reinstallable != null) {
                sp.SelectionVFX.GoalLinks.Stop();
            }
        }

        public override void OnHover(Vector2 coords) {
            isHoverHidden = false;
            lastCoords = coords;
            var rw = 1;
            var rh = 1;
            if (tilePreview != null) {
                var tr = tilePreview.Transform;
                rw = tr.RotatedWidth;
                rh = tr.RotatedHeight;
            }
            var margin = tilePreview?.Definition.LayerId == WorldLayer.Objects ?
                0 : Consts.MapMargin;
            var pos = ClampPos(EntityUtils.ToPosSafe(coords), rw, rh, margin);
            OnHoverCommon(coords);

            if (isDragging) {
                if (!IsSingle) {
                    CheckDragModes();
                }
                for (int i = 0; i < dragPositions.Count; i++) {
                    int p = dragPositions[i];
                    IsAbleToPlace(p, true);
                }
                isAbleToPlace = true;
                if (pos != lastHoverPos) {
                    UpdateMarkedTiles(pos);
                }
                if (ToolGrid.IsWithinBounds(lastHoverPos)) {
                    ToolGrid.TrySet(lastHoverPos, null);
                    lastHoverPos = Pos.Invalid;
                }
                return;
            } else if (!IsSingle) {
                CheckNonDragModes(pos);
            }

            dragEnd = pos;
            var oldMouseIdleTime = mouseIdleTime;
            mouseIdleTime += GameTimer.unscaledDeltaTime;

            if (pos != lastHoverPos || SelectedRotation != lastRotation
                    || tilePreview == null) {
                mouseIdleTime = 0;
                ToolGrid.TrySet(lastHoverPos, null);
                if (tilePreview == null) {
                    tilePreview = BuildTile();
                }
                isAbleToPlace = IsAbleToPlace(pos, false);
                if (isDragPlacement) {
                    if (isAbleToPlace) {
                        PlaceTile(pos, false);
                        isAbleToPlace = false;
                    }
                }

                The.SysSig.HideTooltip.Send();
                allWorkSpots.Clear();
                allInPorts.Clear();
                allOutPorts.Clear();
                if (tilePreview != null) {
                    tilePreview.PlacementCheckResult = isAbleToPlace;
                    if (tilePreview.Definition.LayerId > WorldLayer.Floor) {
                        var size = tilePreview.Transform.Size.magnitude + 2;
                        var rad = tilePreview.Transform.IsWorkSpotCompatible ? 10 : size;
                        Pos.ForEachInRadius(pos, rad, true, (p) => {
                            var obj = EntityUtils.ObjAt(p);
                            if (obj == null) { return true; }
                            if (obj.Transform.HasWorkSpot) {
                                if (!tilePreview.Transform.IsWorkSpotCompatible
                                        || obj.HasComponent<OperatableDeviceComp>()) {
                                    allWorkSpots.Add(obj.Transform.WorkSpot);
                                }
                            }
                            return true;
                        });
                        Pos.ForEachInRadius(pos, size, true, (p) => {
                            var obj = EntityUtils.ObjAt(p);
                            if (obj == null) { return true; }
                            if (obj.Transform.HasInPort) {
                                allInPorts.Add(obj.Transform.InPort);
                            }
                            if (obj.Transform.HasOutPort) {
                                allOutPorts.Add(obj.Transform.OutPort);
                            }
                            return true;
                        });
                        if (tilePreview.Transform.HasWorkSpot) {
                            allWorkSpots.Add(
                                tilePreview.Transform.RotatedWorkSpot(pos));
                        }
                        if (tilePreview.Transform.HasInPort) {
                            allInPorts.Add(
                                tilePreview.Transform.RotatedInPort(pos));
                        }
                        if (tilePreview.Transform.HasOutPort) {
                            allOutPorts.Add(
                                tilePreview.Transform.RotatedOutPort(pos));
                        }
                    }
                }

                ClearPreview();
                ToolGrid.TrySet(pos, tilePreview);
            }

            if (The.Bindings.IsPressed(ActionType.CtrlModifier)) {
                The.SysSig.HideTooltip.Send();
            } else {
                var ttDelay = isAbleToPlace ? 1f : 0.1f;
                // Show the tooltip 2 seconds later
                if (oldMouseIdleTime < ttDelay && mouseIdleTime > ttDelay) {
                    The.SysSig.ShowTooltip.Send(placementTooltip, false);
                }
            }

            lastHoverPos = pos;
            lastRotation = SelectedRotation;
        }

        private bool IsAbleToPlace(int pos, bool cached, bool warn = false) {
            if (tilePreview == null) { return false; }
            if (!cached || !placementAbilityCache.TryGetValue(
                    pos, out bool res)) {
                if (Reinstallable != null) {
                    res = CmdPlaceTile.CanPlace(pos, tilePreview,
                        tilePreview.Transform.LayerId, warn, Reinstallable.Tile,
                            isMassReinstall: IsMassReinstall);
                } else {
                    res = CmdPlaceTile.CanPlace(pos, tilePreview,
                        tilePreview.Transform.LayerId, warn);
                }
                placementAbilityCache[pos] = res;
            }
            return res;
        }

        public override bool HandlesRotate => true;
        public override void OnRotate(int direction) {
            if (tilePreview == null) { return; }
            var tr = tilePreview.Transform;
            if (tr?.IsRotatable != true) { return; }
            var newRot = Facing.Rotate(
                SelectedRotation, direction, tr.ValidRotations);
            SetRotation(newRot);
        }

        public void SetRotation(Facing.Type rot) {
            SelectedRotation = rot;
            // D.Log("Setting rotation: {0}", SelectedRotation);
            SelectedRotation = rot;
            tilePreview = null;
            placementAbilityCache.Clear();
        }

        public override void Deactivate() {
            S?.Sys?.Particles?.SelectionVFX?.HideAll();
            Reinstallable = null;
            IsMassReinstall = false;
            wasClickedDown = false;
            isDragPlacement = false;
            placementAbilityCache.Clear();
            if (S != null) {
                if (S.Map?.Grids != null) {
                    ToolGrid?.TrySet(lastHoverPos, null);
                }
                ClearTilePreview();
            }
            mouseIdleTime = 0;
            The.SysSig?.HideTooltip.Send();
            base.Deactivate();
        }

        private void ClearTilePreview() {
            // Also without a preview: it is dropped after every placement, while
            // the electricity lines may still be waiting for the grid to come back
            S.Sys?.Particles?.ElectricityVFX?.EndBuildPreview();
            if (tilePreview != null) {
                HidePreviewMarkers();
                tilePreview = null;
            }
        }

        private void HidePreviewMarkers() {
            var sp = S.Sys?.Particles;
            if (sp != null) {
                sp.SelectionVFX.ShowWorkSpot(null, 0);
                sp.SelectionVFX.ShowPorts(null, 0);
                sp.SelectionVFX.HideBuildFootprint();
                // Will clear preview range;
                sp.SelectionVFX.ShowMarkedTiles(0);
            }
        }

        private bool isDragPlacement;

        public override void OnClickDown(Vector2 coords) {
            justPlacedTile = false;
            wasClickedDown = true;
            var pos = EntityUtils.ToPosSafe(coords);
            if (Prevalidate?.Invoke(pos, tilePreview) == false) {
                #if PRODUCTION_BUILD
                wasClickedDown = false;
                UISounds.PlayActionDenied();
                return;
                #else
                if (A.InstantBuild) {
                    D.Err("Ignoring prevalidate");
                } else {
                    wasClickedDown = false;
                    UISounds.PlayActionDenied();
                    return;
                }
                #endif
            }
            if (!IsSingle) { return; } // drag to place, like a wall
            if (!isAbleToPlace) {
                // we could be clicking a solar panel above another solar panel
                var topmost = EntityUtils.GetTopmostParentTile(pos);
                if ((topmost == null || topmost.IsConstructed)
                        && !HasCancellableBuildTask(CurrentTileInBuildLayerAt(pos))) {
                    IsAbleToPlace(pos, false, warn: true);
                }
                return;
            }
            if (Reinstallable != null) {
                Reinstall(pos);
                S.Toolbox.Clear();
                return;
            } else {
                PlaceTile(pos, true);
            }
            mouseIdleTime = 0;
            isDragPlacement = true;
        }

        public static void ClearDebrisFor(Tile tile, int pos, AIAgentComp agent) {
            if (tile == null) { return; }
            if (tile.Definition.LayerId == WorldLayer.Floor) { return; }
            if (!Ready.Gen) { return; }
            var cnt = tile.Transform.GetSubtilePos(pos, Caches.PosIdx);
            for (int i = 0; i < cnt; i++) {
                if (ClearDebrisAt(tile.S, Caches.PosIdx[i], agent)) {
                    agent = null;
                }
            }
        }

        private static bool ClearDebrisAt(GameState s, int p, AIAgentComp agent) {
            if (!s.Objs.ByPos.TryGetValue(p, out var objs)) { return false; }
            bool assigned = false;
            for (int i = objs.Count - 1; i >= 0; i--) {
                Obj ob = objs[i];
                var mat = ob.Pile;
                if (mat == null) {
                    continue;
                }
                if (mat.Entity.IsCarriedByAny) {
                    continue;
                }
                var curGoal = mat.Entity.Goal;
                if (curGoal == null) {
                    curGoal = mat.CreateStoringGoal(true, false);
                    if (curGoal != null) {
                        if (mat.Entity.TrySetGoal(curGoal.WithPriority(4), agent)) {
                            assigned = true;
                            agent = null;
                        }
                    } else {
                        D.Err("Cannot create a hauling goal for a pile: {0}",
                            mat);
                        continue;
                    }
                } else {
                    if (curGoal.GoalPriority < 4) {
                        curGoal.WithPriority(4);
                    }
                    AIGoal.TryRetry(curGoal, G.Ticks, true, true);
                    if (agent != null && curGoal.TrySetAgent(agent)) {
                        curGoal.ChangeStateTo(AIGoalState.PreAssigned, G.Ticks);
                        agent = null;
                        assigned = true;
                    }
                }
            }
            return assigned;
        }

        public override void OnDrag(Vector2 start, Vector2 end, float dragDistance) {
            if (!wasClickedDown) { return; }
            if (IsSingle) { return; }
            OnDragCommon(start, end);
            The.SysSig.HideTooltip.Send();
            if (isInPencilMode) {
                if (lastPencilTile == Pos.Invalid) {
                    PlaceTile(dragEnd, false);
                } else if (lastPencilTile != dragEnd) {
                    DrawLineCustom(EntityUtils.CenterOf(lastPencilTile),
                        end, PlaceTile);
                }
                lastPencilTile = dragEnd;
                return;
            }
            if (isInCircleMode || isInLineMode) { return; }
            PosRect rect = PosRect.Invalid;
            if (isInDeleteMode || IsRect) {
                rect = DragRect(start, end);
            } else if (IsRow) {
                rect = DragRow(start, end);
            }
            HandleDragRect(rect, Consts.GreenRectColor);
            dragFillsGapsOnly = CmdPlaceTiles.FillsGapsOnly(dragPositions,
                tilePreview.Definition.LayerId);
        }

        public override bool Configure(string param) {
            Reinstallable = null;
            tilePreview = null;
            currentDef = param;
            if (param != null && !The.Defs.Contains(currentDef)
                    && !The.Defs.ToolPathsContains(currentDef)) {
                D.Err("Trying to configure place tiles tool with missing def / tool path: {0}",
                    param);
                currentDef = null;
                return false;
            }
            SelectedRotation = 0;
            lastRotation = 0;
            placementAbilityCache.Clear();
            return param != null;
        }

        private readonly List<int> tempPositions = new();

        public override string CurrentParam => currentDef;

        protected override bool WallOrFloorCheck(int idx) {
            if (idx < Consts.MapMargin || idx >= S.GridSize - Consts.MapMargin) {
                return false;
            }
            var isFloor = S.Map.Grids[WorldLayer.Floor].Data[idx] == null;
            return (currentLayer == WorldLayer.Walls ? isFloor : !isFloor) ||
                S.Map.Grids[WorldLayer.Walls].Data[idx] != null ||
                S.Map.Grids[WorldLayer.Objects].Data[idx] != null;
        }

        public override void OnClickUp(Vector2 startPos, Vector2 position,
                float dragDistance) {
            isDragPlacement = false;
            if (!wasClickedDown) { return; }
            wasClickedDown = false;
            if (isInPencilMode) {
                lastPencilTile = Pos.Invalid;
            }
            if (isInDeleteMode) {
                // So that single clicking on a tile would work
                if (dragPositions.Count == 0) {
                    dragPositions.Add(lastHoverPos);
                }
                if (A.InstantBuild) {
                    for (int i = 0; i < dragPositions.Count; i++) {
                        int p = dragPositions[i];
                        S.CmdQ.Enqueue(new CmdRemoveTile(p, currentLayer));
                    }
                } else {
                    for (int i = 0; i < dragPositions.Count; i++) {
                        int p = dragPositions[i];
                        if (!Pos.IsOOB(p)) {
                            var t = EntityUtils.TileAt(p, currentLayer);
                            if (t?.IsConstructed == false) {
                                var unbuilt = EntityUtils.ParentObj(t);
                                if (unbuilt.Reinstallable?.IsPending == true
                                        || unbuilt.Constructable == null) {
                                    // a relocation ghost, nothing was delivered to it
                                    S.CmdQ.Enqueue(new CmdRemoveTile(p, currentLayer));
                                } else {
                                    unbuilt.Constructable.CancelConstruction();
                                }
                            } else {
                                var parent = EntityUtils.ParentObj(t);
                                if (parent?.PendingReplacement?.IsPending == true) {
                                    parent.ClearGoal("Removed by tool place tiles");
                                }
                            }
                        }
                    }
                }
                ClearDragPositions();
                placementAbilityCache.Clear();
                isDragging = false;
                return;
            }
            if (isInBucketMode) {
                PlaceTiles(bucketFill);
                bucketFill.Clear();
                isDragging = false;
                ClearDragPositions();
                return;
            }
            if (!justPlacedTile && (IsSingle || !isDragging)) {
                var pos = EntityUtils.ToPos(position);
                if (RemoveCurrentBuildTask(pos)) {
                    placementAbilityCache.Remove(pos);
                    tilePreview = null;
                    wasClickedDown = false;
                    return;
                }
            }
            if (IsSingle) {
                // remove preview
                tilePreview = null;
                return;
            }
            if (isDragging) {
                if (Prevalidate != null) {
                    var pos = EntityUtils.ToPos(position);
                    if (!Prevalidate(pos, tilePreview)) {
                        UISounds.PlayActionDenied();
                        ClearDragPositions();
                        placementAbilityCache.Clear();
                        isDragging = false;
                        return;
                    }
                }
                if (isInCircleMode) {
                    var circumference = Mathf.RoundToInt(2 * Mathf.PI * radius);
                    tempPositions.Clear();
                    DrawCircle(tempPositions.Add);
                    PlaceTiles(tempPositions);
                    tempPositions.Clear();
                    drawRect = false;
                } else if (isInLineMode) {
                    tempPositions.Clear();
                    DrawLine(tempPositions.Add);
                    PlaceTiles(tempPositions);
                    tempPositions.Clear();
                } else {
                    int placeablePositions = 0;
                    for (int i = 0; i < dragPositions.Count; i++) {
                        int p = dragPositions[i];
                        if (IsAbleToPlace(p, true)) {
                            placeablePositions++;
                        }
                    }

                    if ((lastRect.Width == 1 && lastRect.Height > 6) ||
                            // We just drew a long 1 tile thin line
                            (lastRect.Height == 1 && lastRect.Width > 6)) {
                        S.Sys.Hint.TrackAction(HintDrawLine.Id);
                    } else if (dragPositions.Count > 15) {
                        S.Sys.Hint.TrackAction(HintDrawFill.Id);
                    }
                    PlaceTiles(dragPositions);
                }
            } else {
                if (Rng.UChance(0.05f)) {
                    S.Sys.Hint.TrackAction(HintDrawDelete.Id);
                }
                if (Rng.UChance(0.05f)) {
                    S.Sys.Hint.TrackAction(HintDrawPencil.Id);
                }
                if (Rng.UChance(0.05f)) {
                    S.Sys.Hint.TrackAction(HintDrawCircle.Id);
                }
                PlaceTile(EntityUtils.ToPos(position), true);
            }
            ClearDragPositions();
            placementAbilityCache.Clear();
            isDragging = false;
        }

        private Tile CurrentTileInBuildLayerAt(int pos) {
            if (tilePreview == null || Pos.IsOOB(pos)) {
                return null;
            }
            switch (tilePreview.Definition.LayerId) {
                case WorldLayer.Objects: {
                    return EntityUtils.ParentObjAt(pos);
                }
                case WorldLayer.Floor: {
                    return EntityUtils.ParentObj(EntityUtils.FloorAt(pos));
                }
                case WorldLayer.Walls: {
                    return EntityUtils.ParentObj(EntityUtils.WallAt(pos));
                }
            }
            return null;
        }

        // Clicking a tile that already has a build order cancels it instead of
        // ordering another one: an unfinished blueprint, or a constructed tile
        // that is queued to be replaced with a different one.
        private static bool HasCancellableBuildTask(Tile current) {
            if (current == null) {
                return false;
            }
            return !current.IsConstructed
                || current.PendingReplacement?.IsPending == true;
        }

        private bool RemoveCurrentBuildTask(int pos) {
            if (tilePreview == null) {
                D.Err("Tile preview is null while on click down!");
                return false;
            }

            var current = CurrentTileInBuildLayerAt(pos);
            if (!HasCancellableBuildTask(current)) {
                return false;
            }

            // We will make an assumption that if prevalidation is active,
            // we are in the tutorial, and removing the blueprint we are
            // trying to build would break the tutorial
            if (Prevalidate != null) {
                UISounds.PlayActionDenied();
                return false;
            }
            current.ClearGoal(
                "Removed by tool place tiles");
            return true;
        }


        private void Reinstall(int pos) {
            ClearDebrisFor(tilePreview, pos, null);
            if (!IsWithinBounds(pos)) { return; }
            var ticks = G.Ticks;
            var r = Reinstallable;
            if (r.IsPending) {
                // Re-targeting: drop the old ghost before the new one links to the source,
                // its cancellation clears the source's TargetId
                r.CancelPendingRelocation();
            }
            if (A.InstantBuild || pos == r.Tile.PosIdx) {
                Reinstallable.Tile.Transform.RelocateToInstant(pos, SelectedRotation);
            } else {
                bool aboveSameObj = false;
                // scan below reinstallable
                var cnt = tilePreview.Transform.GetSubtilePos(pos, Caches.PosIdx);
                for (int i = 0; i < cnt; i++) {
                    var p = Caches.PosIdx[i];
                    var pp = EntityUtils.ParentObjAt(p, r.Tile.Definition.LayerId);
                    if (pp != null && pp.Id == r.EntityId) {
                        aboveSameObj = true;
                    }
                }

                if (aboveSameObj) {
                    Reinstallable.Tile.Transform.RelocateToInstant(pos, SelectedRotation);
                } else {
                    var t = BuildTile();
                    t.Reinstallable.SourceId = r.EntityId;
                    var cmd = new CmdPlaceTile(pos, t, warn: true, false, true, (tile) => {
                        r.TargetId = tile.Id;
                        r.ShowInfoIcon(IconId.CMove, T.RelocationPending);
                    });
                    cmd.Execute(S);
                    // relocating stuff from the derelict
                    if (!VirtualMouseInput.IsCurrent && !EntityUtils.IsWithinMargin(Reinstallable.Tile.PosIdx, false, false)) {
                        S.Sys.Hint.Show(HintCameraMemory.Id, false);
                    }
                }
            }
            Reinstallable = null;
            Sounds.PlayConstructionOrder(The.Bindings.Mouse.WorldPosition);
            justPlacedTile = true;
        }

        private void PlaceTile(int pos) {
            PlaceTile(pos, false);
        }

        private void PlaceTile(int pos, bool replaceStruct) {
            if (!replaceStruct) {
                // This controls object drag in a row
                if (tilePreview.Definition.LayerId == WorldLayer.Floor
                        || EntityUtils.IsFloorAt(pos)) {
                    if (!tilePreview.Constructable.CanDragRow) {
                        if (!The.Bindings.GetBinding(ActionType.CtrlModifier).IsPressed()) {
                            return;
                        }
                    }
                }
                if (EntityUtils.IsWallAt(pos)) { return; }
            }
            if (!IsWithinBounds(pos)) { return; }
            ClearDebrisFor(tilePreview, pos, null);
            bool instant = A.InstantBuild;
            S.CmdQ.Enqueue(
                new CmdPlaceTile(pos, BuildTile(), warn: true, instant));
            if (!instant && Reinstallable == null) {
                PlayStats.TrackBuildOrdered(currentDef, 1);
            }
            Sounds.PlayConstructionOrder(The.Bindings.Mouse.WorldPosition);
            justPlacedTile = true;
        }

        private void PlaceTiles(List<int> pos) {
            bool instant = A.InstantBuild;
            for (int i = 0; i < pos.Count; i++) {
                ClearDebrisFor(tilePreview, pos[i], null);
            }
            S.CmdQ.Enqueue(new CmdPlaceTiles(
                pos, SelectedRotation, The.Defs.Get(currentDef), instant));
            if (!instant) {
                PlayStats.TrackBuildOrdered(currentDef, pos.Count);
            }
            Sounds.PlayConstructionOrder(The.Bindings.Mouse.WorldPosition);
        }

        private Tile BuildTile() {
            var tile = EntityUtils.CreatePrototype<Tile>(currentDef);
            if (tile == null) {
                S.Toolbox.Clear();
                return null;
            }
            tile.Transform.Rotation = SelectedRotation;
            tile.Graphics.OnReady(false);
            currentLayer = tile.Definition.LayerId;
            return tile;
        }

        public override string ToString() {
            if (currentDef != null) {
                return string.Format("{0} ({1})", Id, currentDef);
            } else {
                return string.Format("{0} (unconfigured)", Id);
            }
        }

        public override Def DefFor(string param) {
            return The.Defs.Get(param);
        }
        public override string MenuNameFor(string param) {
            //var parts = param.Split('/');
            //return parts[parts.Length - 1];
            return The.Defs.Get(param).NameT;
        }

        public override string MenuIconFor(string param) {
            return The.Defs.Get(param).Preview;
        }

        private readonly Dictionary<string, int> toolMenuOrder = new();
        public int OrderForSubmenu(string submenu) {
            if (!toolMenuOrder.TryGetValue(submenu, out int order)) {
                if (!Tunable.TryInt($"ToolOrder_PlaceTiles_{submenu}", out order)) {
                    D.Err($"ToolOrder_PlaceTiles_{submenu} is not defined tunables!");
                    order = int.MaxValue;
                }
                toolMenuOrder[submenu] = order;
            }
            return order;
        }
    }
}
