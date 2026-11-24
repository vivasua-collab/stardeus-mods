using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Game.Constants;
using Game.Data;
using Game.Systems.BodyParts;
using Game.Systems.Items;
using Game.Utils;

namespace Game.Systems.UIUX {
    public class BodyPartSearchAdapter : BaseSearchAdapter {
        [SelfInit]
        private static void Register() {
            Register(AdapterId, new BodyPartSearchAdapter());
        }

        private const string AdapterId = "BodyPart";
        public override string Id => AdapterId;
        public override string ResultLabel => T.NounBodyParts;
        public override int ResultOrder => 27;

        protected override void Query(string input, string q, Regex wordStart) {
            var allObjs = A.S.Query.GetAllObjs.Ask();
            var bodyPartCounts = new Dictionary<BodyPartDef, (int owned, int equipped)>();

            foreach (var kv in allObjs) {
                var obj = kv.Value;
                if (obj.BodyPart == null) { continue; }
                if (obj.State == ObjState.Destroyed || obj.State == ObjState.InDevice) { continue; }

                var partDef = obj.BodyPart;
                if (!bodyPartCounts.TryGetValue(partDef, out var counts)) {
                    counts = (0, 0);
                }

                if (obj.State == ObjState.Equipped) {
                    counts.equipped++;
                } else {
                    counts.owned++;
                }
                bodyPartCounts[partDef] = counts;
            }

            var orderedParts = bodyPartCounts.OrderBy(kv => kv.Key.Def.NameT);

            QueryItems(orderedParts, q, wordStart,
                d => d.Key.IdH,
                d => d.Key.Def.SearchNameT,
                d => d.Key.Def.NameT,
                null,
                (d, idx) => new BodyPartSearchResult(this, d.Key, d.Value.owned, d.Value.equipped, idx));
        }

        public override BaseSearchResult LoadResult(string resultId, int order) {
            return null;
        }
    }
}
