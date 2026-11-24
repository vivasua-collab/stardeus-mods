#if UNITY_EDITOR
#endif
using System;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Stats;
using Game.Systems.UIUX;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Items {

    public sealed class InventoryItemComp : BaseComponent<InventoryItemComp>,
            IStatProvider {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new InventoryItemComp());
            displayStats = null;
        }

        public string StatsCategoryName => T.EquipableItem;

        public int StatsOrder => 100;

        private static StatsBeing displayStats;

        public void AddStats(Lifetime life, WGroup block, ref int idx) {
            var item = Obj.Data.Item;
            if (item == null) {
                return;
            }
            AddOriginRow(life, block, ref idx);
            if (!Arrays.IsEmpty(item.AttackMethods)) {
                foreach (var data in item.AttackMethods) {
                    var stats = new StatsAttack();
                    stats.FinalizeStats();
                    data.ApplyToStats(stats, Obj.Data.Quality);
                    var icon = stats.RangeMax.Value > 1f ? IconId.CAttackTarget : IconId.CFist;
                    var activeStats = new List<Stat>();
                    foreach (var stat in stats.FullStats) {
                        if (!stat.IsUnused) { activeStats.Add(stat); }
                    }
                    var listBtn = UISpawn.Widget<WContentListButton>(life,
                        UIPrefabId.WContentListButton, block.Container, idx++);
                    listBtn.Load(icon, data.NameT, Texts.Grey(T.AttackMethod));
                    var capturedStats = activeStats;
                    listBtn.LoadOnToggle(life, (l, container, willShow, wasLoaded) => {
                        if (!willShow) { return wasLoaded; }
                        if (wasLoaded) { return true; }
                        int i = 0;
                        foreach (var stat in capturedStats) {
                            var row = UISpawn.Widget<WIconText2>(l,
                                UIPrefabId.WContentText2, container, i++);
                            row.Load(Stat.IconFor(stat), Stat.NameFor(stat), stat.Display());
                        }
                        return true;
                    }, false);
                }
            }
            if (!Arrays.IsEmpty(item.AddsStatModifiers)) {
                displayStats ??= new StatsBeing();
                var mods = item.QualityAdjustedStatModifiers(Obj.Data.Quality);
                var modifiesRows = new List<(string icon, string name, string value)>();
                foreach (var mod in mods) {
                    if (!displayStats.Find(mod.Item1, out var stat)) { continue; }
                    stat.UpdateBase(0);
                    modifiesRows.Add((Stat.IconFor(stat), Stat.NameFor(stat), mod.Item2.DisplayValue(stat)));
                }
                StatListUI.AddExpandableList(life, block, ref idx, T.ModifiesStats, modifiesRows);
            }
        }

        // A loose item has no wearer to judge against, so this states what is known
        // about the item itself rather than how any particular being would feel.
        private void AddOriginRow(Lifetime life, WGroup block, ref int idx) {
            var origin = ItemOwnership.OriginLabel(Obj.Data, null);
            if (origin == null) { return; }
            var row = UISpawn.Widget<WIconText2>(life,
                UIPrefabId.WContentText2, block.Container, idx++);
            row.Load(ItemOwnership.OriginIcon(Obj.Data, null), origin);
        }
    }
}
