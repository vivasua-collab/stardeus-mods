using System;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Grid;
using KL.Utils;

namespace Game.Systems.UIUX {
    public class PosSearchResult : BaseSearchResult {
        public override string Id => Units.Num(pos);
        public override int IdH => pos;
        private readonly int pos;
        public PosSearchResult(BaseSearchAdapter adapter, int pos, int order) : base(adapter) {
            this.pos = pos;
            Order = order;
        }

        public override void SpawnWidget(Lifetime life, int idx, WGroup wContainer) {
            var w = UISpawn.Widget<WQuickSearchResult>(life, UIPrefabId.WQuickSearchResult, wContainer.Container, idx);
            w.Load(IconId.CDestination, Pos.String(pos), T.JumpToPosition, OnSelect);
            w.SetNotPinnable();
        }

        public override void SpawnPinned(Lifetime life, int idx, WGroup wContainer) {
        }

        public override void OnSelect() {
            A.S.Sig.CameraFocus.Send(Pos.ToVector(pos), Consts.V3One, 0f);
        }
    }
}
