using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Systems.Stats;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine.Pool;

namespace Game.Systems.UIUX {
    public sealed class EntityStatsTab : BaseEntityTab {
        public const string TabId = "EntityStats";

        [SelfInit]
        private static void Register() {
            RegisterTab(new EntityStatsTab());
        }

        private Entity entity;
        private GameState S;
        public override bool SuppressWarningOverlay => true;

        public override string TabName => T.NounStats;
        public override int Order => 100;

        public override bool ShowFor(GameState s, Tile tile) {
            if (!tile.IsConstructed) { return false; }
            S = s;
            entity = tile;
            return true;
        }
        public override bool ShowFor(GameState s, ObjData obj) {
            if (obj.Obj == null) { return false; }
            S = s;
            entity = obj.Obj;
            return true;
        }

        public override bool LoadContent(WGroup group) {
            int idx = 0;
            int blockIdx = 0;
            WGroup block = null;
            string lastCategory = null;

            var provs = ListPool<IStatProvider>.Get();
            foreach (var comp in entity.Components) {
                if (comp is IStatProvider prov) {
                    if (prov.StatsCategoryName == null) {
                        D.Warn("Skipping stat category provider: {0}", entity);
                        continue;
                    }
                    provs.Add(prov);
                }
            }
            provs.Sort((a, b) => {
                var catCmp = string.CompareOrdinal(a.StatsCategoryName, b.StatsCategoryName);
                if (catCmp != 0) {
                    return catCmp;
                }
                return a.StatsOrder.CompareTo(b.StatsOrder);
            });
            foreach (var prov in provs) {
                if (lastCategory != prov.StatsCategoryName) {
                    block = UISpawn.Block(lifetime, group, blockIdx++);
                    idx = 0;
                    var header = UISpawn.Widget<WText>(lifetime,
                        UIPrefabId.WContentHeader, block.Container, idx++);
                    header.Load(prov.StatsCategoryName);
                    lastCategory = prov.StatsCategoryName;
                }
                prov.AddStats(lifetime, block, ref idx);
            }
            provs.Clear();
            ListPool<IStatProvider>.Release(provs);

            return true;
        }

        protected override void Cleanup() {
            S = null;
            entity = null;
        }

        public override string Id => TabId;
    }
}
