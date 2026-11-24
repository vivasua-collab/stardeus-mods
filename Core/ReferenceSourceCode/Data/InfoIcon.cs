using Game.Components;
using Game.Constants;
using KL.Grid;
using Game.Rendering;
using KL.Utils;
using UnityEngine;
using Game.Systems.Rendering;

namespace Game.Data {
    public class InfoIcon : InfoItem {
        protected GPUInfoRenderer renderer;
        public override string Renderer => Renderers.InfoIcons;
        public override int RendererHash => Renderers.InfoIconsHash;
        public override int RendererShardSize => 64;
        public override bool RendererSorted => false;
        public override int RendererSortOrder => SpriteOrder.StatusIcon;
        public override InfoItemType Type => InfoItemType.Icon;
        public override int TypeInt => 0;
        // subclasses can override this to switch the renderer
        public override bool IsBlank => IconH == 0;
        public override string Info => InfoKey;
        [System.NonSerialized] public string Icon;
        [System.NonSerialized] public Vector4 Tint;
        public int IconH;

        //protected AnimatedSpriteSet spriteSet;
        protected GPURenderSlot renderSlot;
        public override string ToString() {
            return $"InfoIcon [{Entity.DefinitionId}@{Entity.Id}/{Pos.String(Entity.PosIdx)}][{Component.GetName()}][Icon: {Icon}][Key: {InfoKey}]";
        }

        public static InfoIcon For(IComponent comp) {
            var e = comp.Entity;
            var s = e.S;
            #if !PRODUCTION_BUILD
            if (!GameState.IsCurrent(s)) {
                D.Err("Trying to show an icon for entity with stale state! Make sure you refreshed the entity after save-load in tests {0}", e);
                return null;
            }
            #endif
            var i = new InfoIcon {
                Entity = e,
                S = s,
                Tint = Consts.ColorWhiteV4,
                Component = comp,
                CanHaveCustomTooltip = true,
            };
            if (e is Tile t) {
                i.Tile = t;
            } else if (e is Being b) {
                i.Being = b;
            } else if (e is Obj o) {
                i.Obj = o;
            }
            return i;
        }

        public bool UpdateIcon(string iconId, string infoKey) {
            return UpdateIcon(iconId, infoKey, Consts.ColorWhiteV4);
        }

        public bool UpdateIcon(string iconId, string infoKey, Vector4 tint) {
            if (Icon != iconId || InfoKey != infoKey || Tint != tint) {
                Icon = iconId;
                Tint = tint;
                if (iconId == null) {
                    IconH = 0;
                } else {
                    IconH = Hashes.S(iconId);
                }
                cachedIcon = null;
                InfoKey = infoKey;
                WasUpdated = true;
                return true;
            }
            return false;
        }

        public override void Hide() {
            // D.Warn("Hiding {0}", this);
            IsShowing = false;
            if (renderSlot != null) {
                renderer.ReleaseSlot(renderSlot);
                renderer = null;
                renderSlot = null;
            }
        }

        public override void Show() {
            if (!WasUpdated) { return; }
            WasUpdated = false;
            if (Icon == null) {
                Hide();
            } else {
                ShowIcon();
                IsShowing = true;
            }
        }

        public override void Clear() {
            Hide();
            Icon = null;
            IconH = 0;
            cachedIcon = null;
        }

        private SpriteData cachedIcon;

        protected void ShowIcon() {
            if (Icon == null) {
                // Icon was cleared while we were waiting for renderer to be
                return;
            }
            cachedIcon ??= RenderingService.Sprites.Find(IconH);
            if (cachedIcon.IdH != IconH) {
                D.Err("Cached icon mismatch! Icon: {0}, Cached: {1}", Icon, cachedIcon);
                cachedIcon = RenderingService.Sprites.Find(IconH);
            }
            // ensure different atlas would not try to reuse the slot
            if (renderer != null) {
                if (renderer.AtlasHash != cachedIcon.AtlasHash) {
                    if (renderSlot != null) {
                        renderer.ReleaseSlot(renderSlot);
                        renderSlot = null;
                    }
                    renderer = null;
                }
            }
            if (renderSlot == null) {
                var h = Hashes.Quick(cachedIcon.AtlasHash, RendererHash);
                renderer = S.Query.GetInfoRenderer.Ask(h, Renderer, RendererShardSize,
                    RendererSortOrder, RendererSorted);

                if (!renderer.TryGetSlotFor(cachedIcon, out renderSlot)) {
                    // We are showing more than 8k icons!?
                    D.Err("Could not get a slot for icon!");
                    return;
                }
            } else {
                renderer.ConfiguredRenderSlotFor(cachedIcon, renderSlot);
            }

            // We will use Scale2 (transform.zw) as offset for info icons
            renderSlot.SetScale(InfoOffset());
            renderSlot.SetPos(Entity.Position, RendererSorted);
            renderSlot.SetColor(Tint);
        }

        public override void Move(Vector2 pos) {
            if (renderSlot != null) {
                renderSlot.SetPos(pos);
            }
        }
        public override void MoveBy(float dx, float dy) {
            if (renderSlot != null) {
                renderSlot.MovePos(dx, dy);
            }
        }

        protected virtual Vector2 InfoOffset() {
            if (Tile == null) {
                return Consts.V2Zero;
            }
            if (Tile.Transform.IsMulti) {
                return Consts.V2Zero;//Tile.Position - EntityUtils.CenterOf(Tile.PosIdx);
            }
            switch (Tile.Definition.LayerId) {
                case WorldLayer.Floor: return new Vector2(-0.25f, -0.25f);
                case WorldLayer.Walls: return new Vector2(-0.25f, 0.25f);
                case WorldLayer.Objects: return new Vector2(0.25f, 0.25f);
            }
            return Consts.V2Zero;
        }

        public override string DebugInfo {
            get {
                return $"InfoIcon: Icon={Icon} Key={InfoKey} Tint={Tint} IsShowing={IsShowing} RenderSlot={renderSlot?.DebugString}";
            }
        }
    }
}
