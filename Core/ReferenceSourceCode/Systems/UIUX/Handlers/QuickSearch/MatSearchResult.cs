using System;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Grid;
using KL.Utils;

namespace Game.Systems.UIUX {
    public class MatSearchResult : BaseSearchResult {
        private readonly MatType matType;
        private readonly int count;
        public override string Id => Units.Num(IdH);
        private int initialTileId;
        public override int IdH => initialTileId;
        public MatSearchResult(BaseSearchAdapter adapter, MatType type, int count, int order) : base(adapter) {
            matType = type;
            this.count = count;
            Order = order;
        }

        public override void SpawnWidget(Lifetime life, int idx, WGroup wContainer) {
            var w = UISpawn.Widget<WQuickSearchResult>(life, UIPrefabId.WQuickSearchResult, wContainer.Container, idx);
            w.Load(matType.IconId, matType.NameT, $"{T.OwnedCount}: {Units.AbbrevNum(count)}", OnSelect);
            w.SetDef(life, matType.Def);
            w.SetTint(matType.IconTint);
            w.SetNotPinnable();
        }

        public override void SpawnPinned(Lifetime life, int idx, WGroup wContainer) {
        }

        public override void OnSelect() {
            FocusNext(matType);
        }

        public static void FocusNext(MatType matType) {
            D.Warn("WIll focus on next result: {0}", matType);
            var storage = A.S.Sys.Inventory.FindNextMat(matType, true);
            if (storage != null) {
                A.S.Query.DeselectAny.Ask();
                A.S.Sig.SelectEntity.Send(storage.Entity);
                EntityUtils.CameraFocusOn(storage.Entity);
            }
        }
    }
}
