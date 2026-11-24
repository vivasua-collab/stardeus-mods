using System.Linq;
using System.Text.RegularExpressions;
using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX {
    public class ObjSearchAdapter : BaseSearchAdapter {
        [SelfInit]
        private static void Register() {
            Register(AdapterId, new ObjSearchAdapter());
        }

        private const string AdapterId = "Obj";
        public override string Id => AdapterId;
        public override string ResultLabel => T.NounObjectsOnShip;
        public override int ResultOrder => 27;
        public override bool AggregateResults => true;

        protected override void Query(string input, string q, Regex wordStart) {
            var orderedObjs = A.S.Objs.All
                .Where(obj => obj.IsActive)
                .Where(obj => obj.Pile?.TreatAsObj != false)
                .OrderBy(obj => obj.Definition.NameT);

            QueryItems(orderedObjs, q, wordStart,
                obj => obj.Id,
                obj => obj.Definition.SearchNameT,
                obj => obj.Definition.NameT,
                obj => obj.Definition.DescT,
                (obj, idx) => new ObjSearchResult(this, obj, idx));
        }

        public override BaseSearchResult LoadResult(string resultId, int order) {
            if (int.TryParse(resultId, out int objId)) {
                var obj = A.S.FindEntity<Obj>(objId);
                if (obj != null && obj.IsActive && obj.Pile?.TreatAsObj != false) {
                    return new ObjSearchResult(this, obj, order);
                }
            }
            return null;
        }
    }
}
