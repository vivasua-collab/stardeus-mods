#if !PRODUCTION_BUILD
#define DEBUG_SPACE
#endif
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Utils;
using Game.CodeGen;
using KL.UI;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Game.Rendering;
using KL.Pool;
using KL.Collections;
using Game.Data.Factions;
using Game.Visuals;
using Game.Systems.UIUX;

namespace Game.UI {
    /// <summary>
    /// Handles Space Objects in Starmap
    /// <seealso cref="StarfieldSO"/>
    /// </summary>
    public sealed class UISpaceObject : WBase, ICustomPoolable {
        [SerializeField] private UIClickableHoverable button;
        [SerializeField] private UITooltip nameTooltip;
        [SerializeField] private Slider progressSlider;
        [SerializeField] private Image image;
        [SerializeField] private Image statusIcon;
        [SerializeField] private Image extraIcon;
        [SerializeField] private UISpaceObjectLabel label;
        [SerializeField] private RectTransform iconContainer;
        private UIAutoscaleByParent tooltipAutoscale;
        private UIAutoscaleByParent selfAutoscale;
        private UIAutoscaleByParent extraIconAutoscale;
        public Image Image => image;
        public Image StatusIcon => statusIcon;
        public UITooltip NameTooltip => nameTooltip;
        private SpaceObject so;
        public Vector2 StarmapSize;
        private UIVFXHolder vfxHolder;
        public UIVFXHolder VFX => vfxHolder;
        public bool IsStarmapDecoration => so.Type.StarmapDecoration;
        public SpaceObject SpaceObject => so;
        private RectTransform rekt;
        public RectTransform Rekt => rekt;
        private static readonly UISpaceObjectChoice objChoiceUI = new();
        public float ScaleFactor;
        private bool isShowingCombatColors;
        private static Color combatFlashColor;
        private Vector2 lastLoc;
        private float spread;
        private Vector2 mapCenter;
        private UICircle trajectory;
        private SpaceObjectLooks looks;
        // Owns the tooltips; UniversePreviewWidget spawns us without a lifetime
        private Lifetime lifetime;
        private UISpaceObjectIcons icons;

        private void Start() {
            button.OnClick = OnClick;
            label.Button = button;
        }

        private void SetDecoration() {
            //tooltipAutoscale.enabled = false;
            //nameTooltip.gameObject.SetActive(false);
            statusIcon.gameObject.SetActive(false);
            image.raycastTarget = false;
            button.enabled = false;
            tooltipCg.interactable = false;
            tooltipCg.blocksRaycasts = false;
        }

        private void SetInteractive() {
            //tooltipAutoscale.enabled = true;
            statusIcon.gameObject.SetActive(true);
            image.raycastTarget = true;
            //nameTooltip.gameObject.SetActive(true);
            button.enabled = true;
            tooltipCg.interactable = true;
            tooltipCg.blocksRaycasts = true;
        }

        private void LateUpdate() {
            if (!UIShowing.Starmap && !UIShowing.GameSetup) { return; }
            UpdateScale();
            if (lastLoc != so.Location) {
                UpdatePosition();
            } else if (so.Type.IdH == SpaceObjectTypeIdH.Player) {
                if (so.S?.Sys?.Ship.IsRotating == true) {
                    UpdatePosition();
                }
            }
            // NO you idiot
            // if (A.Starmap?.IsPlayerParent(so) == true) {
            //     Image.transform.localRotation = Quaternion.Euler(0f, 0f, 45f * RenderUtils.SinUnscaledTime);
            // }
            if (icons.HaveMatsChanged()) {
                Refresh();
            }

            if (so.Ship != null) {
                if (so.Ship.IsInCombat) {
                    isShowingCombatColors = true;
                    image.color = Color.Lerp(Consts.ColorWhite,
                        combatFlashColor, RenderUtils.Pulse);
                } else if (isShowingCombatColors) {
                    image.color = Consts.ColorWhite;
                    isShowingCombatColors = false;
                }
            }
        }

        private void UpdateScale() {
            if (!IsConstantSizeInStarmap) {
                tooltipAutoscale.DoUpdate();
                extraIconAutoscale.DoUpdate();
            } else {
                selfAutoscale.DoUpdate();
            }
        }

        public void UpdatePosition() {
            if (so.Parent == null) { return; }
            float posMul = 1f;
            if (so.Parent.Type.IdH == SpaceObjectTypeIdH.Universe) {
                posMul = Tunables.StarmapPositionMultiplier_Region;
            } else if (so.Parent.Type.IdH == SpaceObjectTypeIdH.Region) {
                posMul = Tunables.StarmapPositionMultiplier_Sector;
            }
            posMul *= spread;
            ScaleFactor = posMul;
            var targetX = so.Location.x * posMul + mapCenter.x;
            var targetY = so.Location.y * posMul + mapCenter.y;
            rekt.RepositionTopLeft(targetX, targetY, 0.5f, 0.5f);
            var dir = so.Location - lastLoc;
            lastLoc = so.Location;
            UpdateDirection(dir);
        }

        private double canUpdateDirectionAfter;

        private void UpdateDirection(Vector2 dir) {
            if (!UIShowing.Starmap) { return; }
            so.S.Sig.UpdateSpaceObjectUI.Send(so, (uiobj, looks) => {
                if (uiobj == null) {
                    // Obj can take a while to show up
                    return;
                }
                if (so.Type.IdH == SpaceObjectTypeIdH.Player) {
                    var rot = so.S.Sys.Ship.Rotation;
                    var facing = Facing.FromAngle(rot);
                    var dir = Facing.ToOffset(facing, 1f);
                    looks.UpdateDirection(uiobj, dir);
                    return;
                }
                if (GameTimer.realtimeSinceStartupAsDouble > canUpdateDirectionAfter) {
                    canUpdateDirectionAfter = GameTimer.realtimeSinceStartupAsDouble + 0.5d;
                    // 1. get the direction in which shuttle is moving
                    // 2. update the looks to use the right sprite for the dir
                    looks.UpdateDirection(uiobj, dir);
                }
            });
        }

        public void OnClick(PointerEventData data) {
            if (so == null) {
                D.Err("Clicking UISpaceObject with null SO!");
                return;
            }
            if (data.button == PointerEventData.InputButton.Middle) {
                return;
            }
            if (!UIShowing.Starmap) {
                if (UIShowing.MainMenu) {
                    A.S.Sig.SpaceObjectClicked.Send(so);
                }
                return;
            }
            if (so == null) {
                D.Err("Trying to click UI of a null SO!");
                return;
            }
            if (so.Ship != null && so.Ship.IsInHyperspace) {
                D.Err("Ship is in hyperspace but has starmap SO! {0}", so.Ship);
                so.Parent.RemoveChild(so);
                A.S.Sig.SpaceObjectRemoved.Send(so);
                return;
            }
            if (data.button == PointerEventData.InputButton.Right) {
                if (so.Type.IdH == SpaceObjectTypeIdH.Player) {
                    return;
                }
                if (A.StarmapCtx.Layer == UniverseLayer.Sector) {
                    // right click
                    SOActionTravelTo.BeginTravel(so.S, so);
                    return;
                } else {
                    if (so == A.PlayerSector) {
                        return;
                    }
                    // D.Warn("Right click on ui so: {0}", so);
                    var q = so.S.Query.GetHyperjumpQueue.Ask();
                    if (q.Queue.Contains(so.SpaceEntity)) {
                        q.Dequeue(so.SpaceEntity);
                        UISounds.PlayDigitalCancel();
                    } else {
                        if (so.Region != null) {
                            if (SOActionHyperjumpRegionRelay.CanRelayTo(so)) {
                                SOActionHyperjumpRegionRelay.DoEnqueueRelay(so, so.S);
                            } else if (SOActionHyperjumpRegionFTL.CanFTLTo(so)) {
                                SOActionHyperjumpRegionFTL.DoEnqueueFTL(so, so.S);
                            } else {
                                UISounds.PlayActionDenied();
                            }
                        } else if (so.Sector != null) {
                            SOActionHyperjumpEnqueue.DoEnqueue(so, so.S);
                        }
                    }
                    return;
                }
            }
            // TODO:
            // 1. figure out if there are any close space objects near this one
            List<UISpaceObject> closeUIItems = A.S.UIQuery.StarmapCloseTo.Ask(this);
            // 2. if there are, show a choice context menu to select which one we want
            if (closeUIItems.Count < 2) {
                // so.S.Sig.ToggleContextMenu.Send(this);
                SpaceObjectUICtx.ShowQuickChoiceFor(so, this);
            } else {
                objChoiceUI.SetObjects(closeUIItems);
                var opts = QuickChoice.Create(T.Select);
                foreach (var obj in closeUIItems) {
                    var name = ChoiceName(obj.SpaceObject, obj.ListName);
                    var btn = new QuickChoice.IconButton() {
                        Icon = Images.Sprite(obj.SpaceObject.Type.Icon),
                        AutoClose = false,
                        OnClick = () => {
                            SpaceObjectUICtx.ShowQuickChoiceFor(obj.SpaceObject, obj);
                        },
                        TipFunc = () => {
                            so.S.UISig.OnHoverUISpaceObject.Send(obj, true);
                            return null;
                        }
                    };
                    opts.AddIconButton(name, btn);
                }
                so.S.UIQuery.ShowQuickChoice.Ask(opts);
            }
            // 3. if there none, show the context menu for this one
        }

        // Two ships close together are two ship names in the Select list, and a playtester
        // could not tell which one was theirs (New Beginning starts next to a derelict)
        private static string ChoiceName(SpaceObject so, string name) {
            if (so == A.Player) {
                return $"{name} {Texts.GreyParentheses(T.OurShip)}";
            }
            if (so.Ship?.IsDerelict == true) {
                return $"{name} {Texts.GreyParentheses(T.SpaceObjectShipderelict)}";
            }
            return name;
        }

        public void UpdateLooks() {
            looks?.ApplyInStarmap(this);
        }

        public Vector2 UIPosition => rekt.position;
        public string ListName => so.ListName;

        private void OnZoom(float uiZoomScale) {
            if (uiZoomScale > 0) {
                rekt.localScale = Consts.V3One / uiZoomScale;
            }
        }

        public bool IsConstantSizeInStarmap;
        [SerializeField] private CanvasGroup tooltipCg;
        private RectTransform container;

        public void Load(RectTransform container,
                SpaceObject obj, SpaceObjectLooks looks,
                Vector2 center, float spread) {
            so = obj;
            this.container = container;
            this.looks = looks;
            this.spread = spread;
            this.mapCenter = center;
            button.OnChange = OnHover;
            if (obj.Ship != null) {
                combatFlashColor = The.UITheme.GetColor("Red", this);
            }
            lifetime?.Dispose();
            lifetime = Lifetime.Create("UISpaceObject");
            icons ??= new UISpaceObjectIcons(this, statusIcon, iconContainer);
            // Pooled: the starmap may have hidden the icons of the previous object
            ShowIcons(true);
            // Through TooltipHandler rather than OnHover: the status icons are inside the label,
            // and only TooltipHandler lets the most deeply nested hovered tooltip win
            UIUtils.AddHover(lifetime, this, OnTooltipHover);
            Reload();
            LoadTrajectory();
        }

        private void LoadTrajectory() {
            D.Ass(trajectory == null, "Trajectory not null!");
            if (so.Type.IdH != SpaceObjectTypeIdH.Moon) {
                return;
            }
            var prefab = ResourceLibrary.FindPrefab<UICircle>("UICircle");
            trajectory = PrefabPool.Spawn(prefab, Consts.V3Zero, Consts.QId,
                rekt.parent);
            trajectory.name = $"{name}_trajectory";
            // D.Warn("Loading traj: {0}", name);
            UIRect.CopyRT(rekt, trajectory.Rect);
            trajectory.Rect.anchoredPosition = mapCenter;
            var thick = Tunables.StarmapTrajectoryThickness;
            var rp = trajectory.Rect.localPosition;
            var p = rekt.localPosition;
            var r = new Vector2(rp.x - p.x, rp.y - p.y).magnitude;
            trajectory.Load(r, thick, 2.5f, false,
                The.UITheme.GetColor("StarmapRing", this));
            trajectory.Rect.SetSiblingIndex(0);
        }

        private void OnHover(bool isHovering) {
            label.OnHover(isHovering);
            so.S?.UISig.OnHoverUISpaceObject.Send(this, isHovering);
        }

        private void OnTooltipHover(bool isHovering) {
            // TooltipHandler hides the tooltip itself when nothing is hovered, and it calls this
            // after it has shown the next target's tooltip, so hiding here would erase that
            if (!isHovering) { return; }
            string str = so.Info.UITooltip(true);
            if (string.IsNullOrWhiteSpace(str)) {
                The.SysSig.HideTooltip.Send();
            } else {
                The.SysSig.ShowTooltip.Send(str, true);
            }
        }



        public void UpdateExplorationState() {
            var isReg = so.Region != null;
            var isSec = so.Sector != null;
            if (!isReg && !isSec) {
                progressSlider.gameObject.SetActive(false);
                return;
            }
            int visited, total;
            if (so.CalculateExplorationState(out visited, out total)) {
                progressSlider.gameObject.SetActive(true);
                if (total < 1) {
                    progressSlider.value = 1f;
                } else {
                    progressSlider.value = visited / (float) total;
                }
            } else {
                progressSlider.gameObject.SetActive(false);
            }
        }


        public void Reload() {
            var isPlayer = so.Type.IdH == SpaceObjectTypeIdH.Player;
            bool isSectorView = so.Parent.Type.IdH == SpaceObjectTypeIdH.Region;
            IsConstantSizeInStarmap = so.Looks(isSectorView)?.IsConstantSizeInStarmap != false;
            // override ship fixed size
            if (so.IsShip) {
                IsConstantSizeInStarmap = Tunables.ShipFixedSizeInStarmap;
            }
            rekt ??= GetComponent<RectTransform>();
            image.transform.localRotation = Consts.QId;
            if (IsConstantSizeInStarmap) {
                if (selfAutoscale == null) {
                    selfAutoscale = new UIAutoscaleByParent(rekt, container);
                } else {
                    selfAutoscale.SetScaleParent(container);
                }
            } else {
                if (tooltipAutoscale == null) {
                    tooltipAutoscale = new UIAutoscaleByParent(nameTooltip.transform, rekt.parent);
                } else {
                    tooltipAutoscale.SetScaleParent(rekt.parent);
                }
                if (extraIconAutoscale == null) {
                    extraIconAutoscale = new UIAutoscaleByParent(extraIcon.transform, rekt.parent);
                } else {
                    extraIconAutoscale.SetScaleParent(rekt.parent);
                }
            }
            rekt.localScale = Consts.V3One;
            UpdatePosition();
            if (IsStarmapDecoration) {
                SetDecoration();
            } else {
                SetInteractive();
                ShowStatusIcon(so);
            }
            string name = so.DisplayName;
            if (so.Type.InheritParentLooks) {
                name = so.Parent.DisplayName;
            }
            if (so.Type.StarmapDecoration) {
                name = Texts.Accented(name);
            }
            #if !PRODUCTION_BUILD
            //nameTooltip.SetText(so.Faction != null ?  name + "*" : name);
            gameObject.name = $"UISpaceObject_{name}";
            #endif
            if (name != null) {
                if (so.Ship?.IsDerelict == false) {
                    name = so.Ship.NameWithTag;
                }
                D.FIXME_LATER("Won't work anymore");
                // if (nameTooltipCanvas == null) {
                //     nameTooltipCanvas = nameTooltip.GetComponent<Canvas>();
                // }
                // nameTooltipCanvas.sortingOrder = 1 + so.Type.TooltipOrder;
                nameTooltip.SetText(name);
                nameTooltip.Show(0f, 0f);
            } else {
                nameTooltip.Hide();
            }
            selfAutoscale?.ResetScale();
            tooltipAutoscale?.ResetScale();
            var starmapSizeMul = so.Looks(isSectorView).StarmapSizeMul;
            if (starmapSizeMul == 0f) {
                starmapSizeMul = 1f;
            }
            extraIconAutoscale?.SetUnscaledSize(1f / Tunables.ShipStarmapSizeMul / starmapSizeMul);
            extraIconAutoscale?.ResetScale();
            looks.ApplyInStarmap(this);
            if (isPlayer) {
                extraIconAutoscale?.SetPulse(0.1f, 2f);
                extraIcon.sprite = Images.Sprite(GraphicsId.ObjectsDevicesShipComputer);
                extraIcon.gameObject.SetActive(true);
                // if player's ship will be fixed size, we need to divide by image.rectTransform.localScale.X
                extraIcon.rectTransform.sizeDelta = new Vector2(64, 64); /*/ image.rectTransform.localScale.x; */
            } else {
                extraIconAutoscale?.ClearPulse();
                extraIcon.gameObject.SetActive(false);
            }

            if (IsConstantSizeInStarmap && UIShowing.Starmap) {
                var ctx = A.S.UIQuery.GetStarmapContext.Ask();
                OnZoom(ctx.StarmapScale);
            }
            name = so.ToString();
            UpdateExplorationState();
        }

        public void Refresh() {
            ShowStatusIcon(so);
            UpdateExplorationState();
        }

        private void ShowStatusIcon(SpaceObject obj) {
            icons.Show(lifetime, obj);
        }

        public void ShowIcons(bool show) {
            iconContainer.gameObject.SetActiveFast(show);
        }

        public override string ToString() {
            return $"UISpaceObject [{so}]";
        }


        private void OnEnable() {
            UpdateLooks();
        }

        public void PoolDeactivate() {
            gameObject.SetActive(false);
            StarmapSize = Consts.V2Zero;
            looks = null;
            if (so != null) {
                so.SetUI(null);
            }
            icons?.Clear();
            lifetime?.Dispose();
            lifetime = null;
            so = null;
            if (vfxHolder != null) {
                vfxHolder.Destroy();
                vfxHolder = null;
            }
            if (trajectory != null) {
                PrefabPool.Despawn(trajectory.gameObject);
                trajectory = null;
            }
        }

        public void PoolActivate() {
            gameObject.SetActive(true);
        }

        public void LoadVFX(string visualEffect, float size) {
            if (vfxHolder != null) {
                if (vfxHolder.VFXName == visualEffect) {
                    // Same effect reused on this pooled instance - refresh its render size
                    // instead of leaving it stuck with whatever size it was first built at.
                    vfxHolder.SetSize(StarmapSize);
                    vfxHolder.VFX.SetFloat(ShaderVars.Size, size);
                    return;
                }
                UnloadVFX();
            }
            if (visualEffect != null) {
                // D.Warn("Loading VFX: {0}", visualEffect);
                int vfxLayer = LayerMask.NameToLayer("TransparentFX");
                vfxHolder = new UIVFXHolder(visualEffect, transform, StarmapSize, vfxLayer);
                vfxHolder.VFX.SetFloat(ShaderVars.Size, size);
                vfxHolder.Show();
            }
        }

        public void UnloadVFX() {
            vfxHolder?.Destroy();
            vfxHolder = null;
        }
    }
}
