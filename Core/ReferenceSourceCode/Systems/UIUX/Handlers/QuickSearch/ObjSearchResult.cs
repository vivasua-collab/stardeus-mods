using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Grid;
using KL.Utils;

namespace Game.Systems.UIUX {
    public class ObjSearchResult : BaseSearchResult {
        private readonly Obj obj;
        public override string Id => Units.Num(IdH);
        public override int IdH => obj.Id;
        public override Def Def => obj.Definition;

        public ObjSearchResult(BaseSearchAdapter adapter, Obj obj, int order) : base(adapter) {
            this.obj = obj;
            Order = order;
        }

        public override void SpawnWidget(Lifetime life, int idx, WGroup wContainer) {
            var w = UISpawn.Widget<WQuickSearchResult>(life, UIPrefabId.WQuickSearchResult_Small, wContainer.Container, idx);
            w.Load(obj.Definition.Preview, obj.Definition.NameT, Pos.String(obj.PosIdx), OnSelect);
            w.SetDef(life, obj.Definition);
            w.SetNotPinnable();
        }

        public override void SpawnPinned(Lifetime life, int idx, WGroup wContainer) {
        }

        public override void OnSelect() {
            if (obj.IsActive) {
                if (!A.S.Query.IsExclusivelySelected.Ask(obj)) {
                    A.S.Sig.SelectEntity.Send(obj);
                }
                EntityUtils.CameraFocusOn(obj);
            } else {
                WPopup.Warning(obj.Definition.NameT, T.ObjectNoLongerAvailable);
            }
        }
    }
}
