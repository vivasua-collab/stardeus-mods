using System;
using Game.Constants;
using Game.Data.Space;
using Game.Systems.Ships;
using Game.Systems.Space;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class StarmapContext {
        public SpaceObject Player => A.S.Universe.Player;
        public bool IsInHyperspace => A.IsInHyperspace;
        public SpaceObject ParentSO;
        // redo into const?
        public string Layer;
        public SpaceRegion Region;
        public SpaceSector Sector;
        public RectTransform Container;
        public Lifetime MainLifetime;
        public Lifetime MenuLifetime;
        public float StarmapScale => Container.localScale.x;
        private SpaceSector lastOpenedSector;
        private HyperjumpProgress hyperjumpProgress;
        public HyperjumpProgress HyperjumpProgress =>
            hyperjumpProgress ??= A.S.Query.GetHyperjumpProgress.Ask();
        public bool IsCrossRegionHyperjump {
            get {
                var p = HyperjumpProgress;
                return p.IsMoving && p.From is SpaceRegion && p.To is SpaceRegion;
            }
        }
        /// <summary>
        /// If we are looking at a starmap layer above the player's parent,
        /// this object will show where the player is
        /// </summary>
        public SpaceObject PlayerLocationInChildren {
            get {
                var parent = A.S.Universe.HyperspaceLocation ?? A.S.Universe.Player.Parent;
                // limit attempts to prevent infinite loop if there's a detached hierarchy!
                int attempts = 10;
                while (parent != null && parent.Parent != ParentSO) {
                    parent = parent.Parent;
                    if (attempts-- < 0) {
                        D.Err("Out of attempts when trying to get player location in children! Will return {0}", parent);
                        break;
                    }
                }
                return parent;
            }
        }

        public bool ShouldRefocusOnOpen(SpaceSector currentSector) {
            if (lastOpenedSector == null || currentSector == null) {
                return false;
            }
            if (IsInHyperspace) {
                return lastOpenedSector.Region != currentSector.Region;
            }
            return lastOpenedSector != currentSector;
        }

        public void RememberOpenedSector(SpaceSector sector) {
            lastOpenedSector = sector;
        }

        public bool SwitchLayer(string layer) {
            if (Layer == layer) { return true; }
            if (A.IsInHyperspace && layer == UniverseLayer.Sector) {
                return false;
            }
            if (IsCrossRegionHyperjump && layer == UniverseLayer.Region) {
                return false;
            }
            D.Warn("Switching starmap layer: {0}->{1}", Layer, layer);
            // if starmap is showing, it will notice the switch and rebuild
            Layer = layer;
            if (UIShowing.Starmap) {
                A.S.UIQuery.RebuildStarmap.Ask(true, true);
            }
            return true;
        }
    }
}
