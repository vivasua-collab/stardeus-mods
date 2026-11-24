using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    // Players look for a vent's controls in the entity panel, not in the bottom bar.
    // The bar keeps its Open / Close Vent buttons as well, because entity hotkeys
    // press bar buttons.
    public sealed class VentTab : BaseEntityTab {
        public const string TabId = "Vent";

        [SelfInit]
        private static void Register() {
            RegisterTab(new VentTab());
        }

        private VentComp vent;
        private FloorVentComp floorVent;

        public override string Id => TabId;
        public override int Order => -5;
        public override string TabName => floorVent != null
            ? T.StructureFloorsFloorvent01
            : T.StructureWallsVent01;

        public override bool ShowFor(GameState s, Tile tile) {
            if (!tile.IsConstructed) { return false; }
            if (tile.ENode?.IsReachable == false) { return false; }
            if (tile.TryGetComponent<VentComp>(out var v)) {
                vent = v;
                floorVent = null;
                return true;
            }
            if (tile.TryGetComponent<FloorVentComp>(out var fv)) {
                vent = null;
                floorVent = fv;
                return true;
            }
            return false;
        }

        public override bool LoadContent(WGroup group) {
            bool isClosed;
            bool isPowered;
            bool isExposed;
            Action toggle;
            if (vent != null) {
                var v = vent;
                isClosed = v.IsClosed;
                isPowered = v.IsPowered;
                isExposed = !isClosed && v.IsExposedToOpenSpace();
                toggle = v.ToggleVent;
            } else {
                var fv = floorVent;
                isClosed = fv.IsClosed;
                isPowered = fv.IsPowered;
                // An open floor vent is a hole into space by design
                isExposed = !isClosed;
                toggle = fv.ToggleVent;
            }

            string warning = null;
            if (!isPowered) {
                warning = T.Unpowered;
            } else if (isExposed) {
                warning = T.AreaExposedToSpace;
            }

            var canToggle = isPowered && !BlockActions;
            Action onClick = () => {
                if (!canToggle) {
                    UISounds.PlayActionDenied();
                    return;
                }
                toggle();
                ReloadTab();
                A.S.UIQuery.RebuildEntityActions.Ask();
            };

            var block = UISpawn.Block(lifetime, group, 0);
            int idx = 0;

            var row = UISpawn.Widget<WContentButton2Text2>(lifetime,
                UIPrefabId.WContent2Button2Text2, block.Container, idx++);
            row.Item.Load(isClosed ? IconId.CNoFlow : IconId.CFlow,
                isClosed ? T.VentClosed : T.VentOpen, null, onClick);
            row.Button.Load(isClosed ? T.Open : T.Close, onClick);
            // The state row stays readable, only the button looks disabled
            row.Button.SetInteractable(canToggle);

            // Its own row: the state row has no room for a second text next to the button
            if (warning != null) {
                var warningRow = UISpawn.Widget<WIconText>(lifetime,
                    UIPrefabId.WContent2Text, block.Container, idx++);
                warningRow.Load(IconId.CWarning, Texts.Red(warning));
            }

            return true;
        }

        // Bar buttons and their hotkeys toggle vents too, the open tab must follow them
        public static void ReloadIfShown() {
            var tab = Registries.EntityTabs.Get(TabId, false);
            if (tab != null) {
                A.S.UIQuery.ReloadEntityTab.Ask(tab);
            }
        }

        protected override void Cleanup() {
            vent = null;
            floorVent = null;
        }
    }
}
