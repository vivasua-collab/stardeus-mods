using System;
using System.Collections.Generic;
using Game.Procgen.Factions;
using Game.Systems.Space;
using KL.Randomness;
using KL.Utils;

namespace Game.Data.Space {
    public class Universe {
        public GameState S;
        public Dictionary<int, SpaceObject> ObjectsById;
        public SpaceObject Root;
        /// <summary>
        /// While player is inbetween sectors / regions, hyperspace object will
        /// be the parent for the player
        /// </summary>
        public SpaceObject Hyperspace;
        public SpaceObject Player;
        /// <summary>
        /// When player jumps into hyperspace, this space object will be used to
        /// figure out where the player can move. We're clamped to one object
        /// at a time and we can only move along the links
        /// </summary>
        public SpaceObject HyperspaceLocation;
        public int StepsInHyperspace;
        public bool IsUsingHyperspaceRelay => CurrentRelay != null;
        public SpaceObject CurrentRelay;
        public int Version;
        public int LastId;
        public int NextId => ++LastId;
        public int MaxMapWidth;
        public int MaxMapHeight;
        public FactionReport FactionReport;
        public SpaceObject CreateObject() {
            var o = new SpaceObject();
            o.S = S;
            o.Id = NextId;
            ObjectsById ??= new Dictionary<int, SpaceObject>();
            ObjectsById.Add(o.Id, o);
            // D.Warn("Creating SOId: {0} with {1}", o.Id, GetHashCode());
            return o;
        }

        /// <summary>
        /// Sector where the player is currently at, regardless of hyperspace status
        /// </summary>
        public SpaceObject CurrentSector {
            get {
                SpaceObject loc;
                if (A.IsInHyperspace) {
                    loc = S.Universe.HyperspaceLocation;
                } else {
                    loc = S.Universe.Player.Parent;
                }
                while (loc != null && loc.SpaceEntity == null) {
                    loc = loc.Parent;
                }
                if (loc == null || loc.SpaceEntity == null) {
                    D.Err("Failed to find parent with non null SpaceEntity! HS: {0}. PP: {1}",
                        S.Universe.HyperspaceLocation, S.Universe.Player.Parent);
                    return null;
                }
                return loc;
            }
        }

        public SpaceBiome CurrentBiome => CurrentSector?.SpaceEntity?.Biome;

        public void Unload() {
            S = null;
            Root = null;
            Player = null;
            if (ObjectsById != null) {
                foreach (var o in ObjectsById) {
                    o.Value.Unload();
                }
                ObjectsById = null;
            }
        }

        public UniverseData GetData() {
            return UniverseData.Serialize(this);
        }

        public void SetData(UniverseData data) {
            data.Deserialize(this);
        }

        public bool TryFind(int id, out SpaceObject so) {
            if (id == 0) {
                so = null;
                return false;
            }
            return ObjectsById.TryGetValue(id, out so);
        }

        public SpaceObject Find(int id) {
            // special case, we didn't expect to find anything
            if (id == 0) { return null; }
            if (ObjectsById.TryGetValue(id, out var res)) {
                return res;
            }
            D.Err("Cannot find SO by id: {0}", id);
            return null;
        }
    }
}
