using System;
using System.Collections.Generic;
using Game.Components;
using Game.Data;
using Game.Systems;
using KL.Utils;
using UnityEngine;

namespace Game.Visuals {
    // Merges the area outlines of selected devices into one outline per kind
    // (component type and color), so heaters and heated floors read as the area
    // they cover together, while turrets and nanobot hives in the same color
    // stay separate
    public sealed class RangeOutlineGroups {
        private sealed class Group {
            public Type Type;
            public Color Color;
            public readonly RangeAreaOutline Outline = new();
        }

        private static readonly Func<IRanged, bool> IsRangedNonZero = r => r.Range > 0f;

        private readonly List<Group> groups = new();
        private int groupCount;

        public void Clear() {
            for (int i = 0; i < groupCount; i++) {
                groups[i].Outline.Clear();
            }
            groupCount = 0;
        }

        // False when the tile has no area outline range, so the caller draws
        // it the usual way
        public bool TryAdd(Tile tile, AreaSys areas) {
            if (!tile.TryGetComponent<IRanged>(out var ranged, IsRangedNonZero)) {
                return false;
            }
            if (ranged is not IRangedAreaOutline || !ranged.RadiusSameArea) {
                return false;
            }
            var pos = tile.PosIdx;
            // On a wall there is no area to outline, same as SelectionVFX.AddRange
            if (areas.At(pos) == -1) { return true; }
            GetGroup(ranged.GetType(), ranged.RadiusColor).Outline.Add(pos,
                tile.Transform.RotatedCenter(pos), ranged.Range);
            return true;
        }

        public int AddTo(VFXTiles marked, AreaSys areas, List<int> buffer, int count) {
            for (int i = 0; i < groupCount; i++) {
                var group = groups[i];
                group.Outline.Collect(areas, buffer);
                var color = Colors.ToF4(group.Color);
                for (int j = 0; j < buffer.Count; j++) {
                    count = marked.AddTile(buffer[j], color, 0.9f, count);
                }
            }
            return count;
        }

        private Group GetGroup(Type type, Color color) {
            for (int i = 0; i < groupCount; i++) {
                var g = groups[i];
                if (g.Type == type && g.Color == color) { return g; }
            }
            if (groupCount == groups.Count) {
                groups.Add(new Group());
            }
            var group = groups[groupCount++];
            group.Type = type;
            group.Color = color;
            return group;
        }
    }
}
