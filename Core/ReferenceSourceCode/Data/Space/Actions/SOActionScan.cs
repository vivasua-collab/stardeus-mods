using System.Collections;
using System.Collections.Generic;
using Game.Constants;
using Game.Data.Planets;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using KL.Utils;
using UnityEngine;
using Game.Systems.UIUX;

namespace Game.Data.Space {
    public sealed class SOActionScan : SOAction {
        public const string ActionId = "Scan";
        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionScan());
        }

        public override bool IsAvailable(SOActionContext ctx) {
            return ctx.S.Universe.HyperspaceLocation == null
                && ctx.SO.Type.IsScannable
                && !ctx.SO.Knowledge.IsFullyScanned
                && ctx.S.Sys.Sec.IsRadarActive();
        }

        public override bool AddOptions(QuickChoice qc) {
            qc.AddButton(T.Scan, PerformScan);
            return true;
        }

        // public override UDB GetUIBlock() {
        //     return dataBlock ??= UDB.Create(
        //         this, UDBT.DBtn, IconId.CCommunications,
        //             T.Scan).WithClickFunction(PerformScan);
        // }

        private void PerformScan() {
            if (!IsAvailable(ctx)) {
                ctx.S.Sig.ShowErrorMessage.Send(T.RadarNotAvailable);
                return;
            }
            ScanObject(ctx.SO);
            // if (ctx.UISO != null) {

            // }
            // This messes up the pagination
            //dataBlock.NeedsListRebuild = true;
        }

        public static void ScanResource(SpaceObject poiSO, out MatType matType) {
            poiSO.SetType(SpaceObjectType.Find(SpaceObjectTypeId.Resource));
            var matIdH = poiSO.Data.GetInt(PlanetResource.MatType);
            matType = MatType.FastGet(matIdH);
            poiSO.Parent.Knowledge.DiscoverMat(matType);
            poiSO.UpdateName(T.OreDeposit_V1.F(matType.NameT));
            poiSO.Icon = matType.IconId;
            poiSO.Data.SetString(SpaceObjectLooks.VarSprite, IconId.CPickaxe);
            // if (UIShowing.Starmap && UIShowing.ContextMenu) {
            //     poiSO.S.Sig.RebuildContextMenu.Send();
            // }
            return;
        }

        public static void ScanObject(SpaceObject so) {
            // D.Warn("Scanning object: {0}->{1}", so.Parent.Name, so.Name);
            if (so.Type.IdH == SpaceObjectTypeIdH.ResourceUnknown) {
                ScanResource(so, out _);
                return;
            }
            var knowledge = so.Knowledge;
            if (!Arrays.IsEmpty(so.Mats)) {
                if (Arrays.IsEmpty(knowledge.Mats)) {
                    knowledge.Mats = new List<int>(so.Mats.Count);
                } else {
                    // we will rewrite
                    knowledge.Mats.Clear();
                }
                var mats = so.Mats;
                for (int i = 0; i < mats.Count; i++) {
                    MatType m = mats[i];
                    knowledge.Mats.AddUniqueInefficient(m.IdH);
                }
            }
            // if (UIShowing.Starmap && UIShowing.ContextMenu) {
            //     so.S.Sig.RebuildContextMenu.Send();
            // }
            knowledge.POIs = so.POIs;
            knowledge.FactionId = so.FactionId;
            if (!knowledge.IsFullyScanned) {
                bool isScanned = knowledge.IsFullyScanned;
                D.Err("Scanned a SO but knowledge thinks it's not fully scanned! {0} | {1}",
                    D.DumpInstance(so), D.DumpInstance(so.Knowledge));
            }
            so.UI?.Refresh();
        }

        public override string Id => ActionId;

        public override int UIPriority => 50;
    }
}
