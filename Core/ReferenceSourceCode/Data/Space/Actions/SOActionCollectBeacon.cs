using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Commands;
using Game.Constants;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Collections;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Data.Space {
    public sealed class SOActionCollectBeacon : SOAction {
        public const string ActionId = "CollectBeacon";
        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionCollectBeacon());
        }

        public override bool IsAvailable(SOActionContext ctx) {
            if (ctx.SO.Type.IdH != SpaceObjectTypeIdH.ReturnBeacon) {
                return false;
            }
            return ctx.S.Sys.Ship.IsNearby(ctx.SO);
        }

        public override bool AddOptions(QuickChoice qc) {
            qc.AddButton(T.VerbCollect, CollectBeacon);
            return true;
        }
        // public override UDB GetUIBlock() {
        //     return dataBlock ??= UDB.Create(
        //         this, UDBT.DBtn, GraphicsId.SpaceObjectsMiscReturnBeacon01,
        //             T.VerbCollect).WithClickFunction(CollectBeacon);
        // }

        private void CollectBeacon() {
            if (!IsAvailable(ctx)) {
                UISounds.PlayActionDenied();
                return;
            }
            var tile = EntityUtils.CreatePrototype<Tile>(
                The.Defs.FastGet(DefIdsH.ObjectsDevicesReturnBeacon));
            foreach (var area in ctx.S.Sys.Areas.OuterAreas.Values.OrderBy(v => Rng.UValue)) {
                foreach (var p in area.OrderBy(v => Rng.UValue)) {
                    if (CmdPlaceTile.CanPlace(p, tile, WorldLayer.Objects, warn: false)) {
                        var cmd = new CmdPlaceTile(p, tile, warn: false, true, false, (tile) => {
                            if (UIShowing.Starmap) {
                                ctx.CloseStarmap();
                            }
                            EntityUtils.CameraFocusOn(tile);
                        });
                        cmd.Execute(ctx.S);
                        if (cmd.IsSuccess) {
                            SOUtils.RemoveSO(ctx.SO);
                            return;
                        } else {
                        }
                    }
                }
            }
            ctx.S.Sig.ShowErrorMessage.Send($"{T.OperationFailed}: {T.StatusFull}");
        }

        public override string Id => ActionId;

        public override int UIPriority => 50;
    }
}
