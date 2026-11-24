using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class TerraformEntityAction : BaseEntityAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new TerraformEntityAction());
        }

        public const string ActionId = "Terraform";
        public override string Id => ActionId;

        protected override bool ApplyFor(GameState s, Tile tile, out EntityActionData data) {
            tile = EntityUtils.ParentObj(tile);

            if (!tile.IsConstructed
                    || (!tile.HasComponent<TerraformControlsComp>()
                        && !tile.HasComponent<TelescopeComp>()
                        && !tile.HasComponent<TerraformerComp>())) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this, Create, Priority.First);
            return true;
        }

        protected override bool ApplyForMany(GameState s, List<Tile> tiles, out EntityActionData data) {
            if (!HasTerraformComp(tiles)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this, (l, p, i) => Create(l, p, i), Priority.First);
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.CTerraform, T.Terraforming, () => {
                A.S.UIQuery.ToggleTerraformConfig.Ask(!UIShowing.Terraforming);
            });
            return widget;
        }

        private static bool HasTerraformComp(List<Tile> tiles) {
            foreach (var t in tiles) {
                var p = EntityUtils.ParentObj(t);
                if (p.IsConstructed
                        && (p.HasComponent<TerraformControlsComp>()
                            || p.HasComponent<TelescopeComp>()
                            || p.HasComponent<TerraformerComp>())) {
                    return true;
                }
            }
            return false;
        }
    }
}
