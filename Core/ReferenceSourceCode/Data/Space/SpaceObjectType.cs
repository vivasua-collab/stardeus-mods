using System;
using Game.Procgen.Names;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using System.Collections.Generic;
using UnityEngine;
using Game.UI;
using Game.Systems.UIUX;

namespace Game.Data.Space {
    [Serializable]
    public class SpaceObjectType {
        [SelfInit]
        private static void Init() {
            _types = null;
        }
        private static Dictionary<string, SpaceObjectType> _types;
        private static Dictionary<string, SpaceObjectType> types {
            get {
                if (_types == null) {
                    _types = new();
                    var tt = The.ModLoader.LoadObjects<SpaceObjectType>(
                        "Config/Generation/Universe");
                    if (tt.Count == 0) {
                        D.Err("Panic - zero known space object types! Check Config/Generation/Universe");
                    }
                    for (int i = 0; i < tt.Count; i++) {
                        SpaceObjectType t = tt[i];
                        t.DistanceUnitsT = t.DistanceUnitsKey?.TOrNull();
                        if (t.Id == null) {
                            D.Err("Found a space object type without id: {0}", t);
                        } else {
                            t.IdH = Hashes.S(t.Id);
                            _types[t.Id] = t;
                        }
                    }
                }
                return _types;
            }
        }
        public static SpaceObjectType Find(int id) {
            // FIXME
            var name = SpaceObjectTypeIdH.ResolveName(id);
            var obj = Find(name);
            if (obj == null) {
                D.Err("Space object type not found by id hash: {0}", name);
            }
            return obj;
        }

        public static SpaceObjectType Find(string id) {
            if (id == null) {
                D.Err("Trying to find SpaceObjectType by null id!");
                return null;
            }
            if (types.TryGetValue(id, out var type)) {
                return type;
            }
            #if !PRODUCTION_BUILD
            D.Err("PANIC - cannot load space object type by id: {0}", id);
            WPopup.SystemWarning("GAME BREAKING BUG", $"Could not find SpaceObjectType by id {id}. Check Config/Generation/Universe folder");
            #else
            D.Err("Cannot load space object type by id: {0}", id);
            #endif
            return null;
        }

        public Dictionary<SpaceObjectType, float> PossibleSubObjects(bool dynamic) {
            var res = new Dictionary<SpaceObjectType, float>();
            foreach (var tt in types) {
                var t = tt.Value;
                if (t.IsDynamic == dynamic && Arrays.ContainsSafe(t.ParentIds, Id)) {
                    res[t] = t.SpawnChance;
                }
            }
            return res;
        }
        public string Id;
        [NonSerialized] public int IdH;
        public string Icon;
        // Where can this thing be spawned
        public string[] ParentIds;
        ///<summary>The space object has detailed interaction</summary>
        public bool CanBeExplored;
        /// <summary>
        /// For POIs like anomalies that disappear after consumed
        /// </summary>
        public bool IsConsumable;
        /// <summary>
        /// When this object is seen in sector view, it automatically counts as visited
        /// </summary>
        public bool IsAutoVisited;
        // For closeup objects
        public bool InheritParentLooks;
        public bool IsScannable;
        public bool IsDeposit => IdH == SpaceObjectTypeIdH.Resource
            || IdH == SpaceObjectTypeIdH.ResourceUnknown;
        /// <summary>
        /// If this is set to true, a name will not be generated, type name will be used instead
        /// </summary>
        public bool UseTypeName;
        public bool HasTrader;
        // Basically to avoid doing if type == planet
        public bool IsTerraformable;
        public bool CanHaveCustomIcon;
        public bool CanBeOwned;
        public int NameMaxWords;
        public string CustomNames;
        public float[] NameChanceWeights;
        public NamePartDef[] NameParts;

        public float SpawnChance;
        // Size range, if appropriate
        public float SizeMin;
        public float SizeMax;
        public string DistanceUnitsKey;
        public string DistanceUnitsT;
        public float MinDistanceBetweenSubObjects;
        /// <summary>
        /// When generating points of interest, we will pick points
        /// either on planet / moon / asteroid surface, or in open space.
        /// This is irrelevant for objects except for what can be generated as POIs
        /// </summary>
        public bool IsOnSurface;

        // The scaled distance in starmap is roughly in pixels. Though in some layers
        // shuttle should be able to reach the ship / target from either closer or further away
        // this distance controls it. Larger number will allow to reach the target / ship from further away
        // Mostly for the planet layer, where shuttle should be able to travel quite far in the starmap
        public int ShuttleReachDist;


        /// <summary>
        /// Used to increase the nearby distance
        /// </summary>
        public float LocationApproxMagnitude;
        // Additional starmap scale multiplier, used to make object look bigger in starmap
        // This is not part of looks because things like PlanetCloseup inherits looks from Planet
        // so this multiplier is on the type itself
        public float StarmapSizeMul;
        // Additional starfield scale multiplier, used to make object look bigger in starfield (behind the ship)
        public float StarfieldSizeMul;
        public bool StarmapDecoration;
        ///<summary>This will be used for runtime objects that will be dynamically added and removed to the starmap </summary>

        public bool IsDynamic;
        ///<summary>Will be able to move in space (in it's own layer).</summary>
        public bool CanMoveInStarmap;

        public float MinExitDist;
        public bool IsInteractive;
        private string infoT;
        public string NameKey;
        public string InfoKey;
        public string InfoT => infoT ??= (InfoKey ?? $"space.object.info.{Id}").T();
        private string nameT;
        public string NameT => nameT ??= (NameKey ?? $"space.object.{Id}").T();
        private string _nameWithTypeKey;
        private string nameWithTypeKey => _nameWithTypeKey ??= $"space.object.name.{Id}";
        private bool hasTCache;
        private bool hasHasTCache; //doh
        public int StarfieldSortOrder;
        // Drawn in the starfield as a UI icon rather than a body in space, so it is
        // hidden where it would read as UI, like behind the main menu
        public bool IsIconMarker;

        public override string ToString() {
            return $"SpaceObjectType[{Id}]";
        }
        public string NameWithType(string name) {
            if (IdH == SpaceObjectTypeIdH.Shuttle) {
                return name;
            }
            if (!hasHasTCache) {
                hasTCache = nameWithTypeKey.HasT();
                hasHasTCache = true;
            }
            if (hasTCache) {
                return nameWithTypeKey.T(name);
            } else {
                return name;
            }
        }
    }
}
