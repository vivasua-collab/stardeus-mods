using System.Collections.Generic;
using System.Text;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Modifiers;
using Game.Utils;
using Game.Visuals;
using UnityEngine;

namespace Game.Tools {
    // Hovering a zoomed out being marker (BeingMarkers) says who is there; clicking it selects
    // the being and makes the camera follow it in. Runs before the being and tile handlers and
    // takes the click only when it lands on a marker.
    public sealed class FreeSelectBeingMarkerHandler : FreeSelectHandler {
        public const string HandlerId = "FreeSelectBeingMarker";
        private const int MaxListed = 5;

        [SelfInit]
        private static void Register() {
            Register(HandlerId, new FreeSelectBeingMarkerHandler());
        }

        public override int Order => 2;
        // The markers are drawn above the roofs
        public override bool WorksOverRoofs => true;

        private readonly List<Being> picked = new();

        public override void OnHover(Vector2 pos) {
            var markers = MapRenderer.Current?.Markers;
            if (markers == null || Pick(markers, pos) == 0) { return; }
            markers.SetHovered(picked[0]);
            // Points at the hovered being's top bar avatar, the reverse of the avatar's
            // own hover link to the being
            var avatar = S.UIQuery.GetBeingAvatar.Ask(picked[0].Data);
            if (avatar != null) {
                S.Sig.HoverUITarget.Send(avatar);
            }
            ctx.WantedCursor = CursorTypeH.CursorFinger;
            var sb = ctx.BeginTooltipBlock();
            if (picked.Count == 1) {
                var b = picked[0];
                sb.Append(Texts.SubTitle(b.Data.Name));
                sb.Append(' ');
                sb.Append(GlyphMap.ArrowRight);
                sb.Append(' ');
                AppendWho(sb, b);
                return;
            }
            var limit = Mathf.Min(picked.Count, MaxListed);
            for (int i = 0; i < limit; i++) {
                if (i > 0) {
                    sb.AppendLine();
                }
                var b = picked[i];
                sb.Append(' ');
                sb.Append(GlyphMap.ArrowRight);
                sb.Append(' ');
                sb.Append(b.Data.Name);
                sb.Append(" (");
                AppendWho(sb, b);
                sb.Append(')');
            }
            if (limit < picked.Count) {
                sb.AppendLine();
                sb.Append(Texts.Grey($"+{Units.Num(picked.Count - limit)}"));
            }
        }

        public override void OnClickUp(Vector2 startPos, Vector2 pos, float dragDistance) {
            if (ctx.IsDragging || ctx.IsShiftPressed || ctx.IsCtrlPressed || ctx.IsAltPressed) {
                return;
            }
            var markers = MapRenderer.Current?.Markers;
            if (markers == null || Pick(markers, pos) == 0) { return; }
            S.Sig.SelectEntity.Send(picked[0]);
            ctx.ClickUpConsumed = true;
        }

        // Fog of war hides beings in the dark, and their markers with them (BeingMarker.shader).
        // Nothing on the CPU knows what is lit, so with fog of war on only our own and guests'
        // markers can be hovered or clicked, or the tooltip would give hidden raiders away.
        private int Pick(BeingMarkers markers, Vector2 pos) {
            if (markers.Pick(pos, picked) == 0) { return 0; }
            if (ModifierFogOfWar.IsEnabled) {
                for (int i = picked.Count - 1; i >= 0; i--) {
                    if (!picked[i].Data.IsFriendly) {
                        picked.RemoveAt(i);
                    }
                }
            }
            return picked.Count;
        }

        // Species, then which side the being is on when it is not ours. Internal factions
        // (crawlers, a restrained guest's temporary one) have no name worth showing.
        private static void AppendWho(StringBuilder sb, Being b) {
            var data = b.Data;
            var side = BeingMarkers.SideOf(data);
            var fac = data.Mind.Faction;
            var hasFacName = !fac.IsInternal;
            if (side == MarkerSide.Hostile) {
                sb.Append(Texts.Red(hasFacName ? $"{b.Species.Name}, {fac.NameT}"
                    : b.Species.Name));
                return;
            }
            sb.Append(b.Species.Name);
            if (side == MarkerSide.Guest) {
                sb.Append(", ");
                sb.Append(T.NounGuest);
                if (hasFacName) {
                    sb.Append(", ");
                    sb.Append(fac.NameT);
                }
            }
        }
    }
}
