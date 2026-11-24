using Game.Constants;
using Game.Data;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Utils;
using KL.Grid;

namespace Game.Systems.UIUX {
    public class BodyPartSearchResult : BaseSearchResult {
        private readonly BodyPartDef bodyPartDef;
        private readonly int ownedCount;
        private readonly int equippedCount;
        private int findIdx;

        private static BodyPartDef lastBodyPartDef;
        private static int staticFindIdx;

        public override string Id => Units.Num(IdH);
        public override int IdH => bodyPartDef.IdH;

        public BodyPartSearchResult(BaseSearchAdapter adapter, BodyPartDef def, int owned, int equipped, int order) : base(adapter) {
            bodyPartDef = def;
            ownedCount = owned;
            equippedCount = equipped;
            Order = order;
        }

        public override void SpawnWidget(Lifetime life, int idx, WGroup wContainer) {
            var w = UISpawn.Widget<WQuickSearchResult>(life, UIPrefabId.WQuickSearchResult, wContainer.Container, idx);
            var countText = $"{T.OwnedCount}: {Units.AbbrevNum(ownedCount)}";
            if (equippedCount > 0) {
                countText += $" ({T.ItemStateEquipped}: {Units.AbbrevNum(equippedCount)})";
            }
            w.Load(bodyPartDef.Def.Preview, bodyPartDef.Def.NameT, countText, OnSelect);
            w.SetDef(life, bodyPartDef.Def);
            w.SetNotPinnable();
        }

        public override void SpawnPinned(Lifetime life, int idx, WGroup wContainer) {
        }

        public override void OnSelect() {
            findIdx = FocusNext(bodyPartDef, findIdx);
        }

        public static int FocusNext(BodyPartDef bodyPartDef, int findIdx) {
            if (lastBodyPartDef != bodyPartDef) {
                lastBodyPartDef = bodyPartDef;
                staticFindIdx = 0;
                findIdx = 0;
            }

            var foundObjs = Caches.ObjDataList;
            foundObjs.Clear();

            A.S.Query.FindObjs.Ask(foundObjs, o =>
                o.BodyPart == bodyPartDef &&
                o.State != ObjState.Destroyed &&
                o.State != ObjState.InDevice);

            if (foundObjs.Count == 0) {
                return 0;
            }

            var idx = findIdx % foundObjs.Count;
            var obj = foundObjs[idx];
            var nextIdx = idx + 1;
            if (nextIdx >= foundObjs.Count) {
                nextIdx = 0;
            }
            staticFindIdx = nextIdx;

            FocusOnObj(obj);
            return nextIdx;
        }

        public static void FocusOnObj(ObjData obj) {
            if (obj.State == ObjState.Equipped) {
                var beingData = A.S.Query.FindBeingData.Ask(obj.BeingDataId);
                if (beingData != null) {
                    BeingUtils.SelectFocused(beingData);
                }
            } else {
                var entity = EntityFor(obj);
                if (entity != null) {
                    A.S.Query.DeselectAny.Ask();
                    A.S.Sig.SelectEntity.Send(entity);
                    EntityUtils.CameraFocusOn(entity);
                }
            }
        }

        private static Entity EntityFor(ObjData obj) {
            Entity entity = obj.Obj;
            if (entity == null) {
                if (obj.State == ObjState.Stored) {
                    entity = A.S.FindEntity<Tile>(obj.StorageId);
                }
            }
            return entity;
        }
    }
}
