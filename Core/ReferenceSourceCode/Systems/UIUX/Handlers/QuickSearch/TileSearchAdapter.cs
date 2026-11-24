using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Research;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public class TileSearchAdapter : BaseSearchAdapter {
        [SelfInit]
        private static void Register() {
            Register(AdapterId, new TileSearchAdapter());
        }

        private const string AdapterId = "Tile";
        public override string Id => AdapterId;
        public override string ResultLabel => T.NounDevicesOnShip;
        public override int ResultOrder => 30;
        public override bool AggregateResults => true;

        protected override void Query(string input, string q, Regex wordStart) {
            var displayed = Caches.IntSet;
            displayed.Clear();

            var orderedEntities = A.S.Map.AllEntities.Values.Where(
                e => e.Definition.LayerId == WorldLayer.Objects
                    || e.Definition.HasComponent(ComponentIdH.ElectricNode))
                    .Cast<Tile>()
                    .OrderBy(e => e.Definition.NameT);

            QueryItems(orderedEntities, q, wordStart,
                d => d.Id,
                d => d.Definition.SearchNameT,
                d => d.Definition.NameT,
                d => d.Definition.DescT,
                (d, idx) => new TileSearchResult(this, d, idx));
        }

        public override BaseSearchResult LoadResult(string resultId, int order) {
            if (int.TryParse(resultId, out int tileId)) {
                var tile = A.S.FindEntity<Tile>(tileId);
                if (tile != null && tile.IsActive) {
                    return new TileSearchResult(this, tile, order);
                }
            }
            return null;
        }
    }
}
