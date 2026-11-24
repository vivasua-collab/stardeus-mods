using System.Linq;
using System.Text.RegularExpressions;
using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX {
    public class MatSearchAdapter : BaseSearchAdapter {
        [SelfInit]
        private static void Register() {
            Register(AdapterId, new MatSearchAdapter());
        }

        private const string AdapterId = "Mat";
        public override string Id => AdapterId;
        public override string ResultLabel => T.NounMaterial;
        public override int ResultOrder => 25;

        protected override void Query(string input, string q, Regex wordStart) {

            var displayed = Caches.IntSet;
            displayed.Clear();

            var counts = Caches.MatCounts;
            A.S.Sys.Inventory.LoadRemainingMaterials(counts);

            var orderedMats = counts.OrderBy(kv => kv.Key.NameT);

            QueryItems(orderedMats, q, wordStart,
                d => d.Key.IdH,
                d => d.Key.SearchNameT,
                d => d.Key.NameT,
                null,
                (d, idx) => new MatSearchResult(this, d.Key, d.Value, idx));
        }

        public override BaseSearchResult LoadResult(string resultId, int order) {
            return null;
        }
    }
}
