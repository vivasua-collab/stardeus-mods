using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Tools;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    // Sends the player from a connector or an unlinked device to the Connect tool,
    // so that they learn the tool instead of leaning on auto connect
    public sealed class ConnectToolAction : BaseEntityAction {
        public const string ActionId = "ConnectTool";

        [SelfInit]
        private static void Register() {
            RegisterAction(new ConnectToolAction());
        }

        public override string Id => ActionId;

        // Single selection only: no ApplyForMany
        protected override bool ApplyFor(GameState s, Tile tile, out EntityActionData data) {
            tile = EntityUtils.ParentObj(tile);
            if (!AppliesTo(tile)) {
                data = EntityActionData.Unavailable;
                return false;
            }
            // In front of the RelatedViewAction rows
            data = new EntityActionData(this, Create, Priority.First, 0);
            return true;
        }

        private static bool AppliesTo(Tile tile) {
            if (tile?.IsActive != true || !tile.IsConstructed) {
                return false;
            }
            var node = tile.ENode;
            if (node == null) {
                return false;
            }
            // A temporary connection by a bot is no link, the device still needs one
            return node.IsConnector || node.Node.NumConnections == 0;
        }

        private WBase Create(Lifetime l, RectTransform p, int i) {
            var widget = SpawnButton(l, true, p, i);
            // The tool's own key, not an entity action hotkey: it already works
            // while something is selected
            widget.LoadWithShortcut(ActionType.ShortcutEnergyConnect,
                A.S.Toolbox.ConnectTool.MenuIcon, T.ToolConnect, ToolConnect.Start);
            return widget;
        }
    }
}
