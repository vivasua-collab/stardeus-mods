using System;
using System.Collections.Generic;
using Game.CodeGen;
using KL.Collections;
using KL.Utils;
using MessagePack;

namespace Game.Data.Space {
    [MessagePackObject]
    public class UniverseData {
        [Key("MaxMapWidth")] public int MaxMapWidth;
        [Key("MaxMapHeight")] public int MaxMapHeight;
        [Key("RootId")] public int RootId;
        [Key("Version")] public int Version;
        [Key("PlayerId")] public int PlayerId;
        [Key("LastId")] public int LastId;
        [Key("HyperspaceId")] public int HyperspaceId;
        [Key("StepsInHyperspace")] public int StepsInHyperspace;
        [Key("HyperspaceLocationId")] public int HyperspaceLocationId;
        [Key("IsUsingHyperspaceRelay")] public bool IsUsingHyperspaceRelay;
        [Key("Objects")] public Dictionary<int, SpaceObjectData> SpaceObjects;
        public static UniverseData Serialize(Universe u) {
            var data = new UniverseData();
            data.SpaceObjects = new Dictionary<int, SpaceObjectData>();
            foreach (var so in u.ObjectsById.Values) {
                if (so.Parent == null && so.Ship == null &&
                        so.Type.IdH != SpaceObjectTypeIdH.Player &&
                        so.Type.IdH != SpaceObjectTypeIdH.Universe &&
                        so.Type.IdH != SpaceObjectTypeIdH.Hyperspace) {
                    D.Err("Skipping serialization of an object without parent: {0}", so);
                    continue;
                }
                var d = so.GetData();
                if (!data.SpaceObjects.TryAdd(d.Id, d)) {
                    D.Err("Can't save space object with duplicate id: {0}: {1}. Existing: {2}",
                        d.Id, so, data.SpaceObjects[d.Id]);
                }
            }
            data.RootId = u.Root.Id;
            data.PlayerId = u.Player.Id;
            data.Version = u.Version;
            data.MaxMapWidth = u.MaxMapWidth;
            data.MaxMapHeight = u.MaxMapHeight;
            data.LastId = u.LastId;
            data.StepsInHyperspace = u.StepsInHyperspace;
            data.HyperspaceId = u.Hyperspace.Id;
            data.IsUsingHyperspaceRelay = u.IsUsingHyperspaceRelay;
            data.HyperspaceLocationId = u.HyperspaceLocation?.Id ?? 0;
            return data;
        }

        public void Deserialize(Universe universe) {
            var obs = new Dictionary<int, SpaceObject>();
            Map lastMap = null;
            // first iteration, unpack all objects
            foreach (var e in SpaceObjects) {
                var o = new SpaceObject();
                o.S = universe.S;
                if (!o.SetData(e.Value)) {
                    continue;
                }
                if (lastMap == null && o.Map != null) {
                    lastMap = o.Map;
                }
                obs[o.Id] = o;
                // D.Warn("Deserialized SOid: {0}", o.Id);
            }
            // second iteration, restore the links
            foreach (var e in obs) {
                var o = e.Value;
                o.SetParent(obs.Get(o.SaveData.ParentId, null));
                if (!Arrays.IsEmpty(o.SaveData.ChildrenIds)) {
                    o.Children = new(o.SaveData.ChildrenIds.Count);
                    for (int i = 0; i < o.SaveData.ChildrenIds.Count; i++) {
                        int c = o.SaveData.ChildrenIds[i];
                        if (!o.Children.AddUniqueInefficient(obs.Get(c, null))) {
                            D.Err("Trying to add child {0} to {1} more than once", c, o);
                        }
                    }
                }
            }
            var root = obs.Get(RootId, null)
                ?? throw new Exception("Panic! Couldn't find root space object in save! Save is corrupted, please try loading a backup.");
            var player = obs.Get(PlayerId, null)
                ?? throw new Exception("Panic! Couldn't find player space object in save!. Save is corrupted, please try loading a backup.");
            universe.ObjectsById = obs;
            universe.Root = root;
            universe.Player = player;
            if (player.Map == null) {
                if (lastMap != null) {
                    D.Err("Lost player map but recovered from one of other space objects: {0}", lastMap);
                    player.Map = lastMap;
                } else {
                    throw new Exception("Panic! Couldn't find player map data in save! Save is corrupted, please try loading a backup.");
                }
            }
            universe.Version = Version;
            universe.MaxMapWidth = MaxMapWidth;
            universe.MaxMapHeight = MaxMapHeight;
            universe.LastId = LastId;
            universe.StepsInHyperspace = StepsInHyperspace;
            // universe.IsUsingHyperspaceRelay = IsUsingHyperspaceRelay;
            universe.Hyperspace = obs.Get(HyperspaceId, null);
            if (universe.Hyperspace == null) {
                D.ErrSoft("Hyperspace not found!");
            }
            if (HyperspaceLocationId != 0) {
                universe.HyperspaceLocation = obs.Get(HyperspaceLocationId, null);
            }
        }
    }
}
