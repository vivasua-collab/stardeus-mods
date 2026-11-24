using Game.Components;
using Game.Constants;
using KL.Grid;
using KL.Utils;
using UnityEngine;
using Game.Systems.Rendering;

namespace Game.Data {
    public class InfoBubble : InfoIcon {
        public override InfoItemType Type => InfoItemType.Bubble;
        public override int TypeInt => 3;
        public override string Renderer => Renderers.InfoBubbles;
        public override int RendererHash => Renderers.InfoBubblesHash;
        public override int RendererSortOrder => SpriteOrder.StatusBubble;
        public override bool RendererSorted => true;
        public override int RendererShardSize => 32;

        public override string ToString() {
            return $"InfoBubble [{Entity.DefinitionId}@{Entity.Id}/{Pos.String(Entity.PosIdx)}][{Component.GetName()}][Icon: {Icon}][Key: {InfoKey}]";
        }
        // TODO
        public bool AutoHide;

        public new static InfoBubble For(IComponent comp) {
            var e = comp.Entity;
            var i = new InfoBubble {
                S = e.S,
                Tint = Consts.ColorWhiteV4,
                Entity = e,
                Component = comp,
            };
            if (e is Being b) {
                i.Being = b;
            } else {
                D.Err("InfoBubble is used with non being!");
            }
            return i;
        }

        protected override Vector2 InfoOffset() {
            return Being.Data.Species.LooksDef.BubbleOffset;
        }
    }
}
