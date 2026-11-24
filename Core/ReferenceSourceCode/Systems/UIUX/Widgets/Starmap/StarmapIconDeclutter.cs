using System.Collections.Generic;
using Game.Constants;
using Game.Data.Space;
using Game.UI;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Systems.UIUX {
    // Zoomed out far enough, the icon rows under the starmap nodes pile into each other. All of
    // them hide at once below one zoom level, so the map never shows a patchy mix. That level
    // comes from the typical spacing between nodes, not the closest pair, so one tight pair of
    // nodes does not hide the icons of a whole view that has room for them.
    public sealed class StarmapIconDeclutter {
        private readonly List<float> nearest = new();
        private float minZoom;
        private bool isShowing = true;
        public bool IsShowing => isShowing;

        public void Measure(Dictionary<SpaceObject, UISpaceObject> nodes, bool enabled) {
            nearest.Clear();
            minZoom = 0f;
            if (!enabled) { return; }
            foreach (var a in nodes.Values) {
                if (a.IsStarmapDecoration) { continue; }
                Vector2 pa = a.Rekt.localPosition;
                var best = float.MaxValue;
                foreach (var b in nodes.Values) {
                    if (b == a || b.IsStarmapDecoration) { continue; }
                    var d = ((Vector2) b.Rekt.localPosition - pa).sqrMagnitude;
                    if (d < best) {
                        best = d;
                    }
                }
                if (best < float.MaxValue) {
                    nearest.Add(math.sqrt(best));
                }
            }
            if (nearest.Count == 0) { return; }
            nearest.Sort();
            var typical = nearest[nearest.Count / 2];
            // Node positions are in the zoomed container, the labels and icons are counter-scaled
            // to a constant size, so on-screen spacing is the local distance times the zoom
            minZoom = typical > 0f ? Tunables.StarmapIconsMinSpacing / typical : 0f;
        }

        public void Apply(float zoom, Dictionary<SpaceObject, UISpaceObject> nodes, bool force) {
            var show = zoom >= minZoom;
            if (show == isShowing && !force) { return; }
            isShowing = show;
            foreach (var node in nodes.Values) {
                node.ShowIcons(show);
            }
        }
    }
}
