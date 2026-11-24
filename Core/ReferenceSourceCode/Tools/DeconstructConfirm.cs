using System;
using System.Text;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;

namespace Game.Tools {
    // A single device whose research is still locked cannot be built again once it is gone,
    // so deconstructing one asks first (STA-1830: a scenario's only shuttle was deconstructed
    // by mistake). Drag selections have their own confirmation in ToolRemoveTiles. Lootable
    // devices with no research at all are left out, or salvaging a derelict piece by piece
    // would ask every time.
    public static class DeconstructConfirm {
        public static void OrderOrConfirm(Tile tile, Action order) {
            var body = ConfirmBodyFor(tile);
            if (body == null) {
                order();
                return;
            }
            WPopup.Create(false, IconId.CDemolition, T.ConfirmObjectRemoval, null, body)
                .WithAction(T.Deconstruct, order)
                .WithAction2(T.Cancel, null)
                .Spawn();
        }

        private static string ConfirmBodyFor(Tile tile) {
            if (tile == null || !tile.IsActive || !tile.IsConstructed) { return null; }
            var def = tile.Definition;
            if (def.LayerId != WorldLayer.Objects) { return null; }
            if (tile.Goal?.JobIdH == JobTypeIdH.Demolition) { return null; }
            var sb = new StringBuilder();
            sb.Append(tile.NameLink);
            if (!def.AppendUnlockInstructions(sb, includeName: false, hasTextAbove: true)) {
                return null;
            }
            return sb.ToString();
        }
    }
}
