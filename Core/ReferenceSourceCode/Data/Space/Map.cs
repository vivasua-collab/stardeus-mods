using System;
using System.Collections;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Migrations;
using Game.Procgen;
using Game.Rendering;
using Game.Scenarios;
using Game.Systems.Atmo;
using Game.Systems.Creatures;
using Game.UI;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Data.Space {
    public class Map {
        // Contains tiles and objects that exist in here
        public int LastId = 0;
        public int NextId => ++LastId;
        public Beings Beings;
        public Objs Objs;
        public Grid<int> Grid;
        public AtmoLayer O2;
        public AtmoLayer Temp;
        public ShipSpec ShipSpec;
        public Grid<int> Plan;
        public Grid<Tile>[] Grids;
        public Grid<Tile>[] DataGrids;
        public ComponentState Components;
        public int GameId;
        public int Width;
        public int Height;
        public int Size;
        public readonly Situation Situation = new();
        public Dictionary<int, Situation> SituationHistory;
        private MapData data;
        public bool IsDeserialized => Grid != null;
        public readonly List<Grid<Tile>> RenderableGrids = new();
        private readonly Dictionary<int, Entity> Entities = new();
        public Dictionary<int, Entity> AllEntities => Entities;
        private readonly Dictionary<int, Dictionary<int, Entity>> EntitiesByDef = new();
        // Don't modify the output
        public bool TryAllByDef(Def def, out Dictionary<int, Entity> found) {
            return EntitiesByDef.TryGetValue(def.IdH, out found);
        }

        public bool TryEntityById(int id, out Entity entity) {
            return Entities.TryGetValue(id, out entity);
        }

        public GameState S;

        public bool RegisterEntity(Entity e) {
            #if !PRODUCTION_BUILD
            if (!A.IsMainThread) {
                D.Err("Map.RegisterEntity called not in main thread! {0}", e);
            }
            #endif
            var id = e.Id;
            if (id == 0) {
                e.IsActive = false;
                D.Err("Trying to register entity with Id 0! {0}", e.DefinitionId);
                return false;
            }
            if (e.Definition == null) {
                D.Err("Skipping entity without definition: {0} -> {1}", e.Id,
                    e.DefinitionId);
                e.IsActive = false;
                return false;
            }
            if (Entities.TryGetValue(id, out var existing)) {
                if (e == existing) {
                    D.Err("Trying to register same entity that already is registered: {0}", e);
                    return false;
                } else {
                    D.Err("Trying to register a different entity with same id: {0}. Existing: {1}. New: {2}",
                        id, existing, e);
                    Entities[id] = e;
                }
            } else {
                //D.Log("Adding entity: {0} -> {1} -> {2}", id, e.DefinitionId, e);
                Entities.Add(id, e);
            }
            if (!EntitiesByDef.TryGetValue(e.Definition.IdH, out var byDef)) {
                byDef = new();
                EntitiesByDef.Add(e.Definition.IdH, byDef);
            }
            if (!byDef.TryAdd(id, e)) {
                D.Err("Failed to add to byDef: {0} -> {1} -> {2}", id, e.DefinitionId, e);
            //} else {
                //D.Log("Added entity to byDef: {0} -> {1} -> {2}", id, e.DefinitionId, e);
            }
            return true;
        }

        public bool UnregisterEntity(Entity e) {
            #if !PRODUCTION_BUILD
            if (!A.IsMainThread) {
                D.Err("Not in main thread!");
            }
            #endif
            var id = e.Id;
            if (id == 0) {
                id = e.OriginalId;
                if (id == 0) {
                    D.Err("Trying to unregister entity that has 0 Id and 0 OriginalId! {0}", e);
                    return false;
                }
            }
            if (!Entities.Remove(id)) {
                D.Err("Tried to remove entity by id {0} which was not registered: {1}", id, e);
                return false;
            //} else {
                //D.Log("removing entity: {0} -> {1}", id, e);
            }
            if (EntitiesByDef.TryGetValue(e.Definition.IdH, out var byDef)) {
                if (!byDef.Remove(id)) {
                    D.Err("Tried to remove entity with def {0} from byDef, but it was not there! {1}.",
                        e.DefinitionId, e);
                } else {
                    //D.Log("Removed entity from byDef: {0} -> {1} -> {2}", id, e.DefinitionId, e);
                    return true;
                }
            } else {
                D.Err("Tried to remove entity with def {0} but here was nothing registered by that def: {1}",
                    e.DefinitionId, e);
            }
            return false;
        }

        public void Initialize(int width, int height, GameState s) {
            D.Ass(s != null, "State is not null");
            S = s;
            Pos.Prime(width, height);
            Consts.SetCornerMargin(width);
            // Adverts = new();
            // Adverts.S = s;
            Components = new();
            Components.S = s;
            Components.Map = this;
            Beings = new();
            Objs = new();
            this.Width = width;
            this.Height = height;
            Size = width * height;
            Beings.Initialize(this);
            Objs.Initialize(this);
        }

        public void Initialize(GameState state) {
            The.SysSig.LoadingMsg.Send("Initializing map");
            Initialize(data.Width, data.Height, state);
        }

        public void SetData(MapData data) {
            this.data = data;
        }

        // Warning - do not try to act smart and not recreate it if
        // size is the same! It will then leak state.
        public void CreateGrids() {
            if (Grid != null) {
                // TODO refactor
                // We already created grids, fuck off
                return;
            }
            Grid = new("GameGrid",
                Width, Height, Consts.GridTileSize, Vector3.zero);
            Plan = new("Plan",
                Width, Height, Consts.GridTileSize, Vector3.zero);
            Grids = new Grid<Tile>[RenderLayers.MaxIdExclusive];
            DataGrids = new Grid<Tile>[RenderLayers.MaxIdExclusive];
            foreach (var layer in RenderLayers.GridLayerIds) {
                Grids[layer] = new Grid<Tile>(WorldLayer.Name(layer),
                    Width, Height, Consts.GridTileSize, Vector3.zero);
                Grids[layer].Layer = layer;
                Grids[layer].EnableOnChangeEvents();
                if (RenderLayers.HasTileRendererLayer(layer)) {
                    RenderableGrids.Add(Grids[layer]);
                }
                if (RenderLayers.IsData(layer)) {
                    DataGrids[layer] = Grids[layer];
                }
            }
            Shader.SetGlobalVector("_WorldSize", new Vector2(Width, Height));
            Shader.SetGlobalInteger("_GridWidth", Width);
            Shader.SetGlobalInteger("_GridHeight", Height);
            Shader.SetGlobalInteger("_GridSize", Size);
            O2 = new AtmoLayer("O2", Tunables.O2LossInSpace, Width, Height);
            Temp = new AtmoLayer("Temp", Tunables.TempLossInSpace, Width, Height);
        }

        public void CleanupGrids() {
            Entities.Clear();
            EntitiesByDef.Clear();
            RenderableGrids.Clear();
            Grid?.Release();
            Plan?.Release();
            if (!Arrays.IsEmpty(Grids)) {
                foreach (var grid in Grids) {
                    grid?.Release();
                }
            }
            Grid = null;
            Plan = null;
            Grids = null;
            DataGrids = null;
            O2?.Cleanup();
            O2 = null;
            Temp?.Cleanup();
            Temp = null;
        }

        public void Subscribe() {
            // Adverts.Subscribe();
            Components.Subscribe();
        }

        ///<summary>When a new map is being loaded or game is exiting, clean up</summary>
        public void Unload() {
            Beings?.Clear();
            Objs?.Clear();
            // Adverts?.Clear();
            Components?.Clear();
            //Adverts?.Unsubscribe();
            //Components?.Unsubscribe();
            CleanupGrids();
        }

        public IEnumerator Deserialize(int gameId) {
            LastId = data.LastId;
            GameId = gameId;
            Width = data.Width;
            Height = data.Height;

            if (data.ComponentsData == null) {
                D.Err("Trying to deserialize with ComponentsData null! Aborting operation.");
                S.IsInconsistent = true;
                yield break;
            }
            Components.Data = data.ComponentsData;
            The.SysSig.LoadingMsg.Send("Restoring components");
            Components.RestoreAfterLoad(gameId);

            CreateGrids();

            foreach (var layer in RenderLayers.PersistentTileGridLayerIds) {
                var tiles = SavedTilesForLayer(layer);
                if (tiles == null) { continue; }
                var componentCount = RenderLayers.ById(layer).HasPersistentComponents
                    ? Components.CountInLayer(layer)
                    : 0;
                The.SysSig.LoadingMsg.Send(
                    $"Loading {componentCount} {ComponentLoadingName(layer)} components");
                DeserializeTiles(tiles, layer);
            }

            The.SysSig.LoadingMsg.Send($"Loading {data.ObjsData.Count} objects");
            DeserializeObjs(data.ObjsData);
            The.SysSig.LoadingMsg.Send($"Loading {data.BeingsData.Count} beings");
            DeserializeBeings(data.BeingsData);

            The.SysSig.LoadingMsg.Send("Populating data grids");
            yield return null;
            if (!GameState.IsCurrent(S)) { yield break; }
            foreach (var layer in RenderLayers.PersistentTileGridLayerIds) {
                var tiles = SavedTilesForLayer(layer);
                if (tiles == null) { continue; }
                var grid = Grids[layer];
                if (grid == null) {
                    D.Err("Skipping saved tiles for layer without grid: {0}",
                        WorldLayer.Name(layer));
                    continue;
                }
                grid.Load(tiles);
            }
            //Sections.Load(data.SectionsData);
            Plan.Load(data.PlanData);

            O2.Load(data.OxygenData, data.AirtightData);
            Temp.Load(data.HeatData, data.InsulationData);

            if (data.Situation != null) {
                Situation.RestoreFrom(data.Situation);
                data.Situation = null;
            }
            SituationHistory = data.SituationHistory;

            UnstuckFreeEntities();
            S.Clock.OnNextUpdate(CheckStructuralIntegrity);
        }

        private void VerifyObjectValidity() {
            var g = Grids[WorldLayer.Objects].Data;
            for (int i = 0; i < Size; i++) {
                var obj = g[i];
                if (obj?.Transform.IsChild == false && obj.Transform.PosIdx < 1) {
                    D.Err("Removing uninitialized broken object with invalid position: {0}. Data I: {1}", obj,
                        Pos.String(i));
                    obj.Transform.PosIdx = i;
                    S.CmdQ.Enqueue(new CmdRemoveTile(obj));
                }
            }
        }

        public void CheckStructuralIntegrity() {
            VerifyObjectValidity();
            var gf = Grids[WorldLayer.Floor].Data;
            var gw = Grids[WorldLayer.Walls].Data;
            for (int i = 0; i < Size; i++) {
                var wall = gw[i];
                if (wall?.IsActive == true) {
                    var floor = gf[i];
                    if (floor?.IsActive != true) {
                        // D.Warn("Removing dangling wall: {0}", wall);
                        S.CmdQ.Enqueue(new CmdRemoveTile(wall));
                    }

                }
            }
        }

        private void UnstuckFreeEntities() {
            var allObjs = Objs.All;
            var oc = allObjs.Count;
            The.SysSig.LoadingMsg.Send($"Unstucking {oc} free objects");
            for (int i = 0; i < oc; i++) {
                allObjs[i].Transform.UnstuckIfNeeded();
            }
        }

        public MapData GetData() {
            if (!IsDeserialized) { return data; }
            Components.SerializeBeforeSave();
            return new MapData() {
                LastId = LastId,
                Width = Width,
                Height = Height,
                BeingsData = Beings.GetData(),
                ObjsData = Objs.GetData(),
                // AvailableAds = Adverts.AvailableAdverts,
                // EntityAds = Adverts.EntityAdverts,
                TilesFloor = Grids[WorldLayer.Floor].Data,
                TilesWalls = Grids[WorldLayer.Walls].Data,
                TilesObjects = Grids[WorldLayer.Objects].Data,
                ExtraGridTiles = ExtraGridTilesData(),
                Situation = Situation,
                SituationHistory = SituationHistory,
                PlanData = Plan.Data,
                //SectionsData = Sections.Data,
                HeatData = Temp.Data.ToArray(),// Heat.Data.ToArray(),
                OxygenData = O2.Data.ToArray(),// Oxygen.Data.ToArray(),
                InsulationData = Temp.Res.ToArray(),// Insulation.Data.ToArray(),
                AirtightData = O2.Res.ToArray(),// Airtight.Data.ToArray(),
                ComponentsData = Components.Data,
            };
        }
        public void LogSituation() {
            var sit = Situation;
            if (sit.Islands == 0 && S.Scenario is not SandboxScenario) {
                D.ErrSoft("Trying to log situation when there are 0 islands");
            }
            (SituationHistory ??= new())[sit.Days] = sit.Copy();
        }

        private void DeserializeNonTileEntities<T>(List<T> entities,
                int layer) where T : Entity {
            if (entities == null || entities.Count == 0) { return; }
            var defs = The.Defs;
            for (int i = 0; i < entities.Count; i++) {
                T v = entities[i];
                v.S = S;
                var def = defs.Get(v.DefinitionId);
                if (def == null) {
                    D.Err("Got null definition from variations of: {0}. Disabling entity: {1}", def, v);
                    v.Id = 0;
                } else {
                    v.Definition = def;
                    v.OriginalId = v.Id;
                }
                v.IsActive = v.Id > 0;
                if (v.IsActive) {
                    if (Entities.TryGetValue(v.Id, out var existing)) {
                        D.Err("Duplicate entity with same id! {0} and {1}. keeping the first one", existing, v);
                        entities[i] = null;
                    } else {
                        RegisterEntity(v);
                    }
                #if !PRODUCTION_BUILD
                } else {
                    D.Err("Deserializing an inactive entity! {0}", v);
                #endif
                }
            }
            Components.Reinitialize(entities, layer);
        }

        private void DeserializeBeings(List<Being> beings) {
            DeserializeNonTileEntities(beings, WorldLayer.FreeEntities);
            Beings.SetData(beings);
        }

        private void DeserializeObjs(List<Obj> objs) {
            DeserializeNonTileEntities(objs, WorldLayer.FreeEntities);
            Objs.SetData(objs);
        }

        public void Dispose() {
            //Oxygen.Release();
            //Heat.Release();
            //Insulation.Release();
            //Airtight.Release();
        }

        private Tile[] SavedTilesForLayer(int layer) {
            switch (layer) {
                case WorldLayer.Floor: return data.TilesFloor;
                case WorldLayer.Walls: return data.TilesWalls;
                case WorldLayer.Objects: return data.TilesObjects;
                default: {
                    if (data.ExtraGridTiles == null) { return null; }
                    data.ExtraGridTiles.TryGetValue(layer, out var tiles);
                    return tiles;
                }
            }
        }

        private Dictionary<int, Tile[]> ExtraGridTilesData() {
            var extra = new Dictionary<int, Tile[]>();
            foreach (var layer in RenderLayers.PersistentTileGridLayerIds) {
                switch (layer) {
                    case WorldLayer.Floor:
                    case WorldLayer.Walls:
                    case WorldLayer.Objects: {
                        continue;
                    }
                }
                var grid = Grids[layer];
                if (grid?.Data != null) {
                    extra.Add(layer, grid.Data);
                }
            }
            return extra;
        }

        private static string ComponentLoadingName(int layer) {
            switch (layer) {
                case WorldLayer.Floor: return "floor";
                case WorldLayer.Walls: return "wall";
                case WorldLayer.Objects: return "device";
                default: return WorldLayer.NameKey(layer);
            }
        }

        private void DeserializeTiles(Tile[] tiles, int layer) {
            if (tiles == null) { return; }
            var defs = The.Defs;
            int nonBlanks = 0;
            for (int i = 0; i < tiles.Length; i++) {
                var tile = tiles[i];
                if (EntityUtils.IsBlankBeforeDeserialize(tile)) {
                    tiles[i] = null;
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(tile.DefinitionId)) {
                    var defIdH = Hashes.S(tile.DefinitionId);
                    var defn = defs.FastGet(defIdH);
                    if (defn == null) {
                        if (!Legacy.RemovedDefs.Contains(defIdH)) {
                            D.Err("Definition not found by id: {0}", tile.DefinitionId);
                        }
                        tiles[i] = null;
                        continue;
                    }
                    tile.Definition = defn;
                    tile.IsActive = true;
                    tile.OriginalId = tile.Id;
                    nonBlanks++;
                    tile.S = S;
                } else {
                    D.Err("Tile definition id is blank: {0}", tile);
                }
            }
            /*
            if (nonBlanks == 0) {
                // D.Warn("Zero non-blank tiles in layer {0} after load! Corrupt save?",
                    WorldLayer.Name(layer));
            }
            */
            Components.Reinitialize(tiles, layer);
            for (int i = 0; i < tiles.Length; i++) {
                var tile = tiles[i];
                if (tile != null && tile.Transform == null) {
                    tiles[i] = null;
                    continue;
                }
                if (tile == null) { continue; }
                tile.S = S;
                if (!tile.Transform.IsChild) {
                    RegisterEntity(tile);
                }
                // Expand multi tiles
                if (tile.Transform.IsMulti && !tile.Transform.IsChild) {
                    int cnt = EntityUtils.LoadMultitile(tile, Caches.Tiles);
                    for (int ci = 0; ci < cnt; ci++) {
                        var child = Caches.Tiles[ci];
                        if (child.Transform.IsChild) {
                            // D.LogF("Expanding multi tile child: {0} => {1}", tile, child.Transform.Pos);
                            if (EntityUtils.IsWithinBounds(child.PosIdx)) {
                                tiles[child.Transform.PosIdx] = child;
                            } else {
                                D.Err("Multi tile child failed to load, out of grid bounds: {0}, {1}",
                                   child, child.PosIdx);
                            }
                        }
                    }
                }
            }
        }

        public void PrimePos() {
            int w, h;
            if (IsDeserialized) {
                w = Width;
                h = Height;
            } else {
                w = data.Width;
                h = data.Height;
            }
            Pos.Prime(w, h);
            Consts.SetCornerMargin(w);
        }
    }
}
