using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Game.Constants;
using Game.Data;
using Game.Systems.Items;
using Game.Utils;

namespace Game.Systems.UIUX {
    public class EquipmentSearchAdapter : BaseSearchAdapter {
        [SelfInit]
        private static void Register() {
            Register(AdapterId, new EquipmentSearchAdapter());
        }

        private const string AdapterId = "Equipment";
        public override string Id => AdapterId;
        public override string ResultLabel => T.Equipment;
        public override int ResultOrder => 26;

        protected override void Query(string input, string q, Regex wordStart) {
            var allObjs = A.S.Query.GetAllObjs.Ask();
            var equipmentCounts = new Dictionary<InventoryItemDef, (int owned, int equipped)>();

            foreach (var kv in allObjs) {
                var obj = kv.Value;
                if (obj.Item == null) { continue; }
                if (obj.State == ObjState.Destroyed || obj.State == ObjState.InDevice) { continue; }

                var itemDef = obj.Item;
                if (!equipmentCounts.TryGetValue(itemDef, out var counts)) {
                    counts = (0, 0);
                }

                if (obj.State == ObjState.Equipped) {
                    counts.equipped++;
                } else {
                    counts.owned++;
                }
                equipmentCounts[itemDef] = counts;
            }

            var orderedItems = equipmentCounts.OrderBy(kv => kv.Key.Def.NameT);

            QueryItems(orderedItems, q, wordStart,
                d => d.Key.IdH,
                d => d.Key.Def.SearchNameT,
                d => d.Key.Def.NameT,
                null,
                (d, idx) => new EquipmentSearchResult(this, d.Key, d.Value.owned, d.Value.equipped, idx));
        }

        public override BaseSearchResult LoadResult(string resultId, int order) {
            return null;
        }
    }
}
