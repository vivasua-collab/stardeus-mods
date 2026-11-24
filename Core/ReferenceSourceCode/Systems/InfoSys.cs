using System;
using System.Collections.Generic;
using System.Diagnostics;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using KL.Collections;
using Game.Rendering;
using KL.Text;
using KL.Utils;
using UnityEngine;
using KL.Pool;
using System.Linq;
using Game.Utils;
using Game.Systems.Inventory;

namespace Game.Systems {
    // FIXME refactor
    public sealed class InfoSys : GameSystem {
        public const string SysId = "InfoSys";
        public override string Id => SysId;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new InfoSys());
        }
        private readonly Dictionary<Entity, EntityInfoItems> entityItems = new();
        private readonly ClassPool<EntityInfoItems> entityItemsPool = new();
        private readonly List<EntityInfoItems> allItems = new();
        private MatDeficitUI deficitUI;
        private DeviceIdleUI idleUI;
        private DeviceUnconfiguredUI unconfiguredUI;

        public void Show(InfoItem item) {
            var entity = item.Entity;
            if (!GameState.IsCurrent(entity.S)) {
                D.ErrSoft("Trying to show info item from another game! {0}", item);
                return;
            }
            if (entity.IsInfoDisabled) { return; }
            if (!entity.IsActive) {
                var deactivatedTickAgo = G.Ticks - entity.DeactivationTime;
                if (deactivatedTickAgo > 1) {
                    if (entity is Being b) {
                        D.Err("Trying to show entity info for being that was deactivated {0} ticks ago: {1}. {2} | {3}", deactivatedTickAgo, item, b, b.Data);
                    } else {
                        D.Err("Trying to show entity info for entity that was deactivated {0} ticks ago: {1}. {2}", deactivatedTickAgo, item, item.Entity);
                    }
                }
                return;
            }
            var itemsCached = item.ParentCache;
            if (itemsCached != null && itemsCached.Entity == entity) {
                // we have a good cache, let's keep it
            } else {
                if (!entityItems.TryGetValue(entity, out var infoItems)) {
                    // D.Warn("Adding info item for: {0} | {1}", entity, entity.GetHashCode());
                    infoItems = entityItemsPool.Spawn();
                    infoItems.SetEntity(entity);
                    entityItems.Add(entity, infoItems);
                    allItems.Add(infoItems);
                // } else {
                //     D.Warn("Already have info item for: {0} | {1}", entity, entity.GetHashCode());
                }
                item.ParentCache = infoItems;
            }
            item.ParentCache.Add(item);
        }

        public void UpdateNow(InfoItem item) {
            if (item == null) {
                D.Err("Trying to update an info item that is null!");
                return;
            }
            var entity = item.Entity;
            if (!GameState.IsCurrent(entity.S)) {
                D.Err("Trying to update info item from another game! {0}", item);
                return;
            }
            if (entity.IsInfoDisabled) { return; }
            if (!entity.IsActive) { return; }
            item.ParentCache?.UpdateNow(item);
        }

        private void ClearItems() {
            for (int i = allItems.Count - 1; i >= 0; i--) {
                EntityInfoItems item = allItems[i];
                item.ClearItems();
                item.UnlinkItems();
                entityItemsPool.Despawn(item);
            }
            entityItems.Clear();
            allItems.Clear();
        }

        private void OnEntityRemoved(Entity entity) {
            if (entityItems.TryGetValue(entity, out var items)) {
                allItems.RemoveRef(items);
                items.ClearItems();
                items.UnlinkItems();
                entityItems.Remove(entity);
                entityItemsPool.Despawn(items);
            }
        }

        protected override void OnInitialize() {
            // S.Sig.BeforeInitState.AddListener(ClearItems);
            S.Sig.AfterLoad.AddListener(OnGameLoaded);
            deficitUI = new MatDeficitUI(S);
            idleUI = new DeviceIdleUI(S);
            unconfiguredUI = new DeviceUnconfiguredUI(S);
        }

        private void OnGameLoaded() {
            S.Sig.ToggleOverlay.AddListener(OnToggleOverlay);
            // This gets autocleared when game gets loaded, so we have to do this here
            S.Sig.EntityRemoved.AddListener(OnEntityRemoved);
            S.Clock.OnUpdate60.AddListener(AfterTickProgress);
            S.Clock.OnTick.AddListener(OnTick);
        }

        private void OnTick(long ticks) {
            deficitUI.OnTick();
            idleUI.OnTick();
            unconfiguredUI.OnTick();
        }

        private bool isShowingOverlay;

        private void OnToggleOverlay(OverlayInfo info, bool on) {
            if (!on || info.Id != OverlayId.Search) {
                if (isShowingOverlay) {
                    isShowingOverlay = false;
                    // S.Sig.HideQuickSearch.Send(true);
                }
            } else {
                if (!isShowingOverlay) {
                    isShowingOverlay = true;
                    // S.Sig.QuickSearch.Send(true);
                }
            }
        }

        // Entities that sit still are updated in StaticSlices turns, one slice per frame.
        // Large colonies show icons on thousands of piles and devices, and touching all of
        // them every frame cost more than the rare icon change they pick up, which now
        // shows at most StaticSlices - 1 frames later. Movers stay in every frame
        private const int StaticSlices = 4;
        private int staticSlice;

        private void AfterTickProgress() {
            if (!Ready.Game || !GameState.IsCurrent(S)) { return; }
            idleUI.Update();
            unconfiguredUI.Update();
            deficitUI.Update();
            staticSlice = (staticSlice + 1) % StaticSlices;
            for (int i = allItems.Count - 1; i >= 0; i--) {
                EntityInfoItems items = allItems[i];
                if (!items.IsMover && i % StaticSlices != staticSlice) { continue; }
                if (!items.Update()) {
                    items.UnlinkItems();
                    allItems.RemoveAt(i);
                    entityItems.Remove(items.Entity);
                    entityItemsPool.Despawn(items);
                }
            }
        }

        public override void Unload() {
            ClearItems();
        }

        // Picked up or put down: its icons start or stop following it every frame
        public void OnCarryChanged(Entity entity) {
            if (entityItems.TryGetValue(entity, out var items)) {
                items.RefreshMover();
            }
        }

        // This is used for generation
        public void MoveTileInfo(Tile tile, float dx, float dy) {
            if (entityItems.TryGetValue(tile, out var items)) {
                items.MoveTileInfo(dx, dy);
            }
        }

        public int InfoFor(int pos, EntityInfoItems[] infos) {
            int idx = 0;
            var center = EntityUtils.CenterOf(pos);
            var b = S.Beings.FindClosestVisual(center, includeProxy: false);
            if (b != null) {
                var info = entityItems.Get(b, null);
                if (info != null) {
                    infos[idx++] = info;
                }
            }
            var o = S.Objs.FindClosestVisual(center, includeProxy: false);
            if (o != null) {
                var info = entityItems.Get(o, null);
                if (info != null) {
                    infos[idx++] = info;
                }
            }
            var obj = EntityUtils.ParentObjAt(pos);
            if (obj != null) {
                var info = entityItems.Get(obj, null);
                if (info != null) {
                    infos[idx++] = info;
                }
            }
            var wall = EntityUtils.WallAt(pos);
            if (wall != null) {
                var info = entityItems.Get(wall, null);
                if (info != null) {
                    infos[idx++] = info;
                }
            }
            var floor = EntityUtils.FloorAt(pos);
            if (floor != null) {
                var info = entityItems.Get(floor, null);
                if (info != null) {
                    infos[idx++] = info;
                }
            }
            for (int i = 0; i < idx; i++) {
                if (infos[i].Entity == null) {
                    var entity = entityItems.FirstOrDefault(v => v.Value == infos[i]).Key;
                    D.Err("Infos contains entry without entity! Poolable spawned: {0}: {1}. Former entity: {2}",
                        infos[i].IsPoolableSpawned, infos[i], entity);
                    entityItems.Remove(entity);
                    infos[i] = null;
                }
            }
            return idx;
        }
    }

}
