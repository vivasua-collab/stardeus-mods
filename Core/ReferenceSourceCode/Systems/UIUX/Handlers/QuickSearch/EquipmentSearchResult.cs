using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Utils;
using KL.Grid;

namespace Game.Systems.UIUX {
    public class EquipmentSearchResult : BaseSearchResult {
        private readonly InventoryItemDef itemDef;
        private readonly int ownedCount;
        private readonly int equippedCount;
        private int findIdx;

        private static InventoryItemDef lastItemDef;
        private static int staticFindIdx;

        public override string Id => Units.Num(IdH);
        public override int IdH => itemDef.IdH;

        public EquipmentSearchResult(BaseSearchAdapter adapter, InventoryItemDef def, int owned, int equipped, int order) : base(adapter) {
            itemDef = def;
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
            w.Load(itemDef.Def.Preview, itemDef.Def.NameT, countText, OnSelect);
            w.SetDef(life, itemDef.Def);
            w.SetNotPinnable();
        }

        public override void SpawnPinned(Lifetime life, int idx, WGroup wContainer) {
        }

        public override void OnSelect() {
            findIdx = FocusNext(itemDef, findIdx);
        }

        public static int FocusNext(InventoryItemDef itemDef, int findIdx) {
            if (lastItemDef != itemDef) {
                lastItemDef = itemDef;
                staticFindIdx = 0;
                findIdx = 0;
            }

            var foundObjs = Caches.ObjDataList;
            foundObjs.Clear();

            A.S.Query.FindObjs.Ask(foundObjs, o =>
                o.Item == itemDef &&
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
