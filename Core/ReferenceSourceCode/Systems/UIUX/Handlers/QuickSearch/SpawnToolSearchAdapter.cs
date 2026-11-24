using System.Linq;
using System.Text.RegularExpressions;
using Game.Constants;
using Game.Data;
using Game.Tools;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class SpawnToolSearchAdapter : BaseSearchAdapter {
        [SelfInit]
        private static void Register() {
            Register(SpawnObjAdapterId, new SpawnToolSearchAdapter(
                SpawnObjAdapterId, ToolSpawnObj.ToolId, false, 100));
            Register(SpawnBeingAdapterId, new SpawnToolSearchAdapter(
                SpawnBeingAdapterId, ToolSpawnBeing.ToolId, true, 101));
        }

        private const string SpawnObjAdapterId = "SpawnObj";
        private const string SpawnBeingAdapterId = "SpawnBeing";

        private readonly string adapterId;
        private readonly string toolId;
        private readonly bool isBeing;
        private readonly int resultOrder;

        public override string Id => adapterId;
        public override string ResultLabel => isBeing ? T.ToolSpawn_Being : T.ToolSpawn_Obj;
        public override int ResultOrder => resultOrder;

        private SpawnToolSearchAdapter(string adapterId, string toolId,
                bool isBeing, int resultOrder) {
            this.adapterId = adapterId;
            this.toolId = toolId;
            this.isBeing = isBeing;
            this.resultOrder = resultOrder;
        }

        protected override void Query(string input, string q, Regex wordStart) {
            Results.Clear();
            displayed.Clear();
            if (!A.InstantBuild) { return; }

            var tool = A.S.Toolbox.Get(toolId);
            var orderedDefs = tool.AvailableParams
                .Select(id => The.Defs.Get(id))
                .Where(def => def != null)
                .OrderBy(def => def.NameT);

            QueryItems(orderedDefs, q, wordStart,
                def => def.IdH,
                def => def.SearchNameT,
                def => def.NameT,
                def => def.DescT,
                (def, idx) => new SpawnToolSearchResult(this, def, idx));
        }

        public override BaseSearchResult LoadResult(string resultId, int order) {
            if (!IsResultAvailable(resultId)) { return null; }
            var def = The.Defs.Get(resultId);
            return def == null ? null : new SpawnToolSearchResult(this, def, order);
        }

        public bool IsResultAvailable(string resultId) {
            if (!A.InstantBuild) { return false; }
            var tool = A.S.Toolbox.Get(toolId);
            return tool.AvailableParams?.Contains(resultId) == true;
        }

        public bool TryActivate(string resultId) {
            if (!IsResultAvailable(resultId)) { return false; }
            A.S.Toolbox.SetActive(A.S.Toolbox.Get(toolId), resultId);
            return true;
        }
    }
}
