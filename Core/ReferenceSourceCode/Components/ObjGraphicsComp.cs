#if UNITY_EDITOR
// #define DEBUG_RENDERER
//#define DEBUG_GRAPHICS
#endif
using Game.Constants;
using Game.Data;
using Game.Rendering;
using Game.Utils;
using Game.CodeGen;
using Game.Visuals.Filters;
using KL.Utils;
using UnityEngine;
using Game.Systems.Rendering;

namespace Game.Components {
    public sealed class ObjGraphicsComp : BaseComponent<ObjGraphicsComp> {

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new ObjGraphicsComp());
        }
        public ObjGraphicsComp() {
            SkipTypeLookupCache = true;
        }
        private TransformComp transform;
        private string graphicId;
        public string GraphicId => graphicId;
        private string overrideGraphicId;
        public string OverrideGraphicId => overrideGraphicId;
        public AnimatedSpriteSet SpriteSet { get; private set; }
        private int sortOrder;
        public int OriginalSortOrder;
        public int SortOrder => sortOrder;
        private Vector2 offset;
        public Vector2 Size;
        public Vector2 SelectableSize { get; set; }
        public string Renderer;
        public bool hasShadow;
        public bool isAnimated;
        public bool IsHidden { get; private set; }
        public bool doNotCull;
        private bool hasCustomColor;
        private Color color;
        private float selectableSizeRatio;
        public Vector2 Position => SpriteSet?.Position ?? transform.Position;
        public Vector2 SelectPosition => Position;
        private int positionFilterH;
        private bool hoverInSpace;
        private bool hasTemporaryRendererOverride;
        private string temporaryRenderer;
        private int temporarySortOrder;
        public string EntityName;

        protected override void OnConfig() {
            if (Obj == null) {
                D.Err("ObjGraphicsComponent is set on something that doesn't have an Obj! {0}", Entity);
                return;
            }
            graphicId = Config.GetString(PropertyIdH.Graphic);
            var orderName = Config.GetString(PropertyIdH.Shader, "Obj");
            sortOrder = SpriteOrder.ForName(orderName, entity.Definition);
            OriginalSortOrder = sortOrder;
            Renderer = Config.GetString(PropertyIdH.Renderer, null);
            var scale = Config.GetFloat(PropertyIdH.Scale, 1f);
            hoverInSpace = Config.GetBool(PropertyIdH.HoverWhenInSpace, true);
            Size = Config.GetVector2(PropertyIdH.Size, new(scale, scale));
            var positionFilter = Config.GetString(PropertyIdH.PositionFilter, null);
            if (positionFilter != null) {
                //D.Warn("Setting position filter for {0} -> {1}",
                    //entity.DefinitionId, positionFilter);
                if (!PositionFilterTypesH.TryHashFor(positionFilter, out positionFilterH)) {
                    D.Err("Position filter not found by name: {0}",
                        positionFilter);
                    positionFilterH = 0;
                }
            }

            var selectableScale = Config.GetFloat(PropertyIdH.SelectableScale, -1);
            if (selectableScale > 0) {
                SelectableSize = Config.GetVector2(PropertyIdH.SelectableSize,
                    new Vector2(selectableScale, selectableScale));
            } else {
                SelectableSize = Config.GetVector2(PropertyIdH.SelectableSize, Size);
            }
            selectableSizeRatio = SelectableSize.x / Size.y;

            isAnimated = Config.GetBool(PropertyIdH.IsAnimated, false);
            hasShadow = Config.GetBool(PropertyIdH.HasShadow, false);
            doNotCull = Config.GetBool(PropertyIdH.DoNotCull, false);
            offset = Config.GetVector2(PropertyIdH.Offset, Vector2.zero);

            transform = Obj.Transform;
            // FIXME EntityId is not set yet
            // Warning, moving this away from here is much harder than you think
        }

        public void OverrideGraphic(string overrideGraphicId) {
            this.overrideGraphicId = overrideGraphicId;
            ResetRenderer();
        }

        public void ChangeRenderer(string newRenderer, int newOrder) {
            if (OriginalSortOrder != newOrder || Renderer != newRenderer) {
                Renderer = newRenderer;
                sortOrder = newOrder;
                OriginalSortOrder = newOrder;
                ResetRenderer();
            }
        }

        public void ChangeRenderer(string newRenderer) {
            if (Renderer != newRenderer) {
                Renderer = newRenderer;
                ResetRenderer();
            }
        }

        public void SetTemporaryRendererOverride(string renderer,
                int sortOrderOverride) {
            hasTemporaryRendererOverride = true;
            temporaryRenderer = renderer;
            temporarySortOrder = sortOrderOverride;
            ApplyCurrentRenderer();
        }

        public void ClearTemporaryRendererOverride() {
            if (!hasTemporaryRendererOverride) { return; }
            hasTemporaryRendererOverride = false;
            temporaryRenderer = null;
            ApplyCurrentRenderer();
        }

        private void ApplyCurrentRenderer() {
            if (SpriteSet == null) { return; }
            string renderer;
            int order;
            if (hasTemporaryRendererOverride) {
                renderer = temporaryRenderer;
                order = temporarySortOrder;
            } else {
                renderer = Renderer;
                order = sortOrder;
            }
            SpriteSet.SetRenderer(S, 0, renderer, order);
        }

        public void ResetRenderer() {
            if (SpriteSet?.IsShowing == true) {
                SpriteSet.RemoveFromRenderer(S);
            }
            // To be sure that sprite set will actually get recreated from scratch
            SpriteSet = null;
            OnObjsReady();
        }

        public void SetZOffset(float height) {
            SpriteSet.ZOffset = height;
        }

        public void Show(bool show) {
            if (!Obj.IsActive) {
                D.Err("Trying to set visible while obj is inactive: {0}", Obj);
                return;
            }
            IsHidden = !show;
            DoShow();
        }

        private void DoShow() {
            if (SpriteSet == null && !IsHidden && entity.IsActive) {
                BuildAnimatedSpriteSet(entity);
            }
            if (SpriteSet != null) {
                if (IsHidden) {
                    if (SpriteSet.IsShowing) {
                        SpriteSet.RemoveFromRenderer(S);
                    }
                } else {
                    if (!SpriteSet.IsShowing) {
                        SpriteSet.AddToRenderer(S);
                    }
                }
            }
        }

        public void SetSize(Vector2 size) {
            Size = size;
            SelectableSize = size * selectableSizeRatio;
            entity.SelectableSize = SelectableSize;
            if (SpriteSet != null) {
                if (isAnimated) {
                    for (int i = 0; i < SpriteSet.Size; i++) {
                        var sd = SpriteSet.Sprites[i];
                        sd.Scale = Size;
                    }
                    return;
                }
                for (int i = 0; i < SpriteSet.Size; i++) {
                    var sd = SpriteSet.Sprites[i];
                    if (!sd.IsScale(Size)) {
                        SpriteData sd2 = sd;
                        if (!sd.IsTempCopy) {
                            sd2 = sd.Copy();
                            sd2.IsTempCopy = true;
                        }
                        sd2.Scale = Size;
                        SpriteSet.Sprites[i] = sd2;
                    }
                }
            }
        }

        public void OverrideSortOrder(int order) {
            sortOrder = order;
            SpriteSet?.SetSortOrder(S, 0, order);
        }

        public void RestoreSortOrder() {
            OverrideSortOrder(OriginalSortOrder);
        }

        public void SetColor(Color color) {
            hasCustomColor = true;
            this.color = color;
            SpriteSet?.SetColor(0, color);
        }

        public override void Receive(IComponent sender, int message) {
            if (hoverInSpace && message == MsgIdH.InSpaceChanged
                    && SpriteSet != null) {
                UpdateInSpaceHover();
            }
        }

        private void BuildAnimatedSpriteSet(Entity entity) {
            SpriteSet = new AnimatedSpriteSet(this, hasShadow ? 2 : 1,
                entity.Definition.LayerId,
                transform.Position, 0);
            ApplyCurrentRenderer();

            var gfxId = string.IsNullOrWhiteSpace(overrideGraphicId) ?
                graphicId : overrideGraphicId;
            var sd = RenderingService.Sprites.Find(gfxId);
            if (isAnimated) {
                sd = sd.Copy();
            }
            sd.Scale = Size;
            //SpriteSet.Sprites[0] = sd;
            SpriteSet.SetSprite(0, sd, S);

            if (hasShadow) {
                SpriteSet.Sprites[1] = sd; // used to .Copy()
                SpriteSet.Offsets[1] = new Vector2(0, 5);
                SpriteSet.SetRenderer(S, 1, Renderers.Shadows, SpriteOrder.Shadow);
                SpriteSet.SetInitialLayer(1, Renderers.Layer_Shadows);
                SpriteSet.OffsetIgnoresRotation[1] = true;
            }

            if (positionFilterH != 0) {
                //D.Warn("Setting obj jitter filter: {0}",
                    //PositionFilterTypesH.ResolveName(positionFilterH));
                SpriteSet.PositionFilters = new AnimatedSpriteSet.SpritePosFilter[SpriteSet.Size];
                SpriteSet.PositionFilters[0] = PositionFilters.ByNameHash(positionFilterH, EntityId);
                SpriteSet.SkipPositionFilters = false;
            } else if (hoverInSpace) {
                UpdateInSpaceHover();
            } else {
                SpriteSet.SkipPositionFilters = true;
            }

            if (hasCustomColor) {
                SpriteSet.SetColor(0, color);
            }
        }

        private void UpdateInSpaceHover() {
            if (!hoverInSpace) { return; }
            if (Obj.Transform.IsInSpace && Obj.Carrier == null) {
                if (SpriteSet.PositionFilters == null) {
                    SpriteSet.PositionFilters =
                        new AnimatedSpriteSet.SpritePosFilter[SpriteSet.Size];
                    SpriteSet.PositionFilters[0] = PositionFilters.ByNameHash(
                        PositionFilterTypesH.InSpaceHover, EntityId);
                }
                SpriteSet.SkipPositionFilters = false;
            } else {
                SpriteSet.SkipPositionFilters = true;
            }
        }

        public void SetRotation(float rotation) {
            if (SpriteSet != null) {
                SpriteSet.IsRotatable = true;
                SpriteSet.Rotation = rotation;
            }
        }

        public void RemoveShadow() {
            if (hasShadow) {
                hasShadow = false;
                SpriteSet?.RemoveSprite(S, 1);
            }
        }

        public override void UpdatePosIdx(int pos, int dx, int dy) {
            TeleportTo(EntityUtils.CenterOf(pos));
        }

        public void TeleportTo(Vector2 pos) {
            // D.Warn("Moving {0} to {1}", this, pos);
            if (SpriteSet != null) {
                var p = pos + offset;
                SpriteSet.Teleport(p);
            }
        }

        public override string ToString() {
            return string.Format(" * ObjGraphics [{0}] ({1})",
                Position, graphicId);
        }

        // FIXME rethink this
        //private bool isRenderingEnqueued;


        public override void OnLateReady(bool wasLoaded) {
            Obj.SelectableSize = SelectableSize;
            if (wasLoaded) {
                Ready.When(WhenH.ObjsLoaded, OnObjsReady);
            } else {
                entity.AfterInitialize += OnObjsReady;
            }
        }

        public override void OnRemove() {
            if (SpriteSet?.IsShowing == true) {
                SpriteSet.DetachAll();
                SpriteSet.RemoveFromRenderer(S);
            }
        }

        public static Sprite PreviewFor(ComponentConfig c) {
            var graphicId = IconFor(c);
            return RenderingService.Sprites.Find(graphicId).Sprite;
        }

        public static string IconFor(ComponentConfig c) {
            return c.GetString(PropertyIdH.Graphic);
        }

        private void OnObjsReady() {
            if (Obj.Data == null) { return; }
            var col = Obj.Data.Color;
            if (col.a > 0f && col != Consts.ColorWhite) {
                SetColor(col);
            }
            if (!IsHidden && Entity.IsActive) {
                if (SpriteSet == null) {
                    BuildAnimatedSpriteSet(entity);
                }
                SpriteSet.AddToRenderer(S);
            }
            if (SpriteSet != null) {
                TeleportTo(transform.Position);
            }
        }

        public void SetShadowHeight(float v) {
            if (hasShadow) {
                if (SpriteSet == null) {
                    #if !PRODUCTION_BUILD
                    D.Err("Trying to set shadow height too early! {0}", Obj);
                    #endif
                    return;
                }
                //SpriteSet.EnsureMeshes();
                SpriteSet.Offsets[1] = new Vector2(0, -v);
                /*
                // Looks ugly
                var h = Maths.Clamp(v / 15f, 0f, 1f);
                D.Warn("Lerping size to : {0}", h);
                var scale = Vector2.Lerp(Size, Size * 0.75f, h);
                SpriteSet.Sprites[1].Scale = scale;
                */
            }
        }
    }
}
