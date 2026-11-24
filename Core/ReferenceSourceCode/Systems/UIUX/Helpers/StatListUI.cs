using System;
using System.Collections.Generic;
using Game.Constants;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    // Shared expandable stat-row list for IStatProvider.AddStats implementations.
    // See UISpawnExamples.cs for the IStatProvider widget constraints this follows.
    public static class StatListUI {
        public static void AddExpandableList(Lifetime life, WGroup block, ref int idx,
                string title, List<(string icon, string name, string value)> rows) {
            if (rows.Count == 0) { return; }
            var listBtn = UISpawn.Widget<WContentListButton>(life,
                UIPrefabId.WContentListButton, block.Container, idx++);
            listBtn.Load(IconId.CInfo, title, Units.Num(rows.Count));
            var capturedRows = rows;
            listBtn.LoadOnToggle(life, (l, container, willShow, wasLoaded) => {
                if (!willShow) { return wasLoaded; }
                if (wasLoaded) { return true; }
                int i = 0;
                foreach (var (icon, name, value) in capturedRows) {
                    var row = UISpawn.Widget<WIconText2>(l,
                        UIPrefabId.WContentText2, container, i++);
                    row.Load(icon, name, value);
                }
                return true;
            }, false);
        }

        // Same shape, with an optional tooltip per row, built only when hovered
        public static void AddExpandableList(Lifetime life, WGroup block, ref int idx,
                string title, List<(string icon, string name, string value, Func<string> tip)> rows) {
            if (rows.Count == 0) { return; }
            var listBtn = UISpawn.Widget<WContentListButton>(life,
                UIPrefabId.WContentListButton, block.Container, idx++);
            listBtn.Load(IconId.CInfo, title, Units.Num(rows.Count));
            var capturedRows = rows;
            listBtn.LoadOnToggle(life, (l, container, willShow, wasLoaded) => {
                if (!willShow) { return wasLoaded; }
                if (wasLoaded) { return true; }
                int i = 0;
                foreach (var (icon, name, value, tip) in capturedRows) {
                    var row = UISpawn.Widget<WIconText2>(l,
                        UIPrefabId.WContentText2, container, i++);
                    row.Load(icon, name, value);
                    if (tip != null) {
                        UIUtils.AddTipFunc(l, row, tip);
                    }
                }
                return true;
            }, false);
        }

        // Same shape, for lists whose entries have no value column (e.g. abilities).
        public static void AddExpandableList(Lifetime life, WGroup block, ref int idx,
                string title, List<(string icon, string name)> rows,
                string titleIcon = IconId.CInfo) {
            if (rows.Count == 0) { return; }
            var listBtn = UISpawn.Widget<WContentListButton>(life,
                UIPrefabId.WContentListButton, block.Container, idx++);
            listBtn.Load(titleIcon, title, Units.Num(rows.Count));
            var capturedRows = rows;
            listBtn.LoadOnToggle(life, (l, container, willShow, wasLoaded) => {
                if (!willShow) { return wasLoaded; }
                if (wasLoaded) { return true; }
                int i = 0;
                foreach (var (icon, name) in capturedRows) {
                    var row = UISpawn.Widget<WIconText>(l,
                        UIPrefabId.WContentText, container, i++);
                    row.Load(icon, name);
                }
                return true;
            }, false);
        }
    }
}
