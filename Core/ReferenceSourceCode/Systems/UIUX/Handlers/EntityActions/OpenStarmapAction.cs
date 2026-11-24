using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class OpenStarmapAction : BaseEntityAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new OpenStarmapAction());
        }

        public const string ActionId = "OpenStarmap";
        public override string Id => ActionId;

        protected override bool ApplyFor(GameState s, Tile tile, out EntityActionData data) {
            tile = EntityUtils.ParentObj(tile);

            if (!IsWorkingStarmapDevice(tile)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this, Create, Priority.High);
            return true;
        }

        protected override bool ApplyForMany(GameState s, List<Tile> tiles, out EntityActionData data) {
            foreach (var t in tiles) {
                var tile = EntityUtils.ParentObj(t);
                if (IsWorkingStarmapDevice(tile)) {
                    data = new EntityActionData(this, Create, Priority.High);
                    return true;
                }
            }

            data = EntityActionData.Unavailable;
            return false;
        }

        private WBase Create(Lifetime l, RectTransform p, int i) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.CStarmap, T.OpenStarmap, () => {
                A.S.UIQuery.ShowStarmap.Ask(true);
            });
            return widget;
        }

        // These devices only do their starmap work while their electric node consumes
        private static bool IsWorkingStarmapDevice(Tile tile) {
            return tile.IsConstructed
                && tile.ENode?.IsConsuming == true
                && HasStarmapComp(tile);
        }

        private static bool HasStarmapComp(Tile tile) {
            return tile.HasComponent<BridgeControlsComp>()
                || tile.HasComponent<TelescopeComp>()
                || tile.HasComponent<TerraformControlsComp>()
                || tile.HasComponent<CommunicatorComp>();
        }
    }
}
