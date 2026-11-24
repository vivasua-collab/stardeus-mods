using System;
using Game.Components;
using Game.Constants;
using KL.Grid;
using Game.Utils;
using Game.CodeGen;
using Game.Rendering;
using KL.Text;
using KL.Utils;
using UnityEngine;
using Game.VFXR;
using Unity.Mathematics;
using Game.Systems.Rendering;

namespace Game.Data {
    public class InfoBar : InfoIcon {
        protected GPUInfoBarRenderer barRenderer;
        public override InfoItemType Type => InfoItemType.Bar;
        public override int TypeInt => 2;
        public override string Info => IsPretranslated ? InfoKey : InfoKey.T(value, max);
        public bool IsPretranslated;
        private float3 color;
        private float pct;
        private int value;
        private int max;
        public override bool IsBlank => InfoKey == null;

        public override string ToString() {
            return $"InfoBar [{Component.GetName()}][Icon: {Icon}][Key: {InfoKey}]";
        }

        public new static InfoBar For(IComponent comp) {
            var e = comp.Entity;
            var i = new InfoBar {
                S = e.S,
                Entity = e,
                Component = comp,
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
        private void ShowBar() {
            if (renderSlot == null) {
                if (barRenderer == null) {
                    barRenderer = S.Query.GetInfoBarRenderer.Ask();
                }
                if (!barRenderer.TryGetSlotFor(out renderSlot)) {
                    // We are showing more than 8k icons!?
                    D.Err("Could not get a slot for icon!");
                    return;
                }
            }

            // renderSlot.Pos.w = 1f;
            renderSlot.SetColor(new float4(color.x, color.y, color.z, pct));
            //renderSlot.Color.w = pct;
            Move(Entity.Position);
        }
        public override void Hide() {
            IsShowing = false;
            if (renderSlot != null) {
                barRenderer.ReleaseSlot(renderSlot);
                renderSlot = null;
            }
        }

        public override void Move(Vector2 pos) {
            if (renderSlot != null) {
                Vector2 offset = Being != null ?
                    Being.Data.Species.LooksDef.BarOffset : new Vector2(0, -0.25f);
                renderSlot.SetPos(pos + offset, false);
            }
        }

        public override void Show() {
            if (!WasUpdated) { return; }
            WasUpdated = false;
            if (!IsShowing) {
                ShowBar();
                IsShowing = true;
            }
            if (renderSlot != null) {
                renderSlot.SetColor(new float4(color.x, color.y, color.z, pct));
            }
        }

        public bool UpdateBar(Color color, float pct, int value, int max, string infoKey) {
            if (value != this.value || max != this.max || InfoKey != infoKey) {
                // FIXME lame
                this.color = new(color.r, color.g, color.b);
                this.pct = pct;
                this.value = value;
                this.max = max;
                InfoKey = infoKey;
                WasUpdated = true;
                return true;
            }
            if (renderSlot != null) {
                renderSlot.SetAlpha(pct);
            }
            return false;
        }
    }
}
