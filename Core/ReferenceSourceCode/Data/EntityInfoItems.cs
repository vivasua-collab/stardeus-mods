#if UNITY_EDITOR
// #define DEBUG_INFO_ITEMS
#endif
using System.Collections.Generic;
using System.Text;
using Game.Constants;
using Game.Utils;
using KL.Utils;
using KL.Pool;
using System.Runtime.CompilerServices;

namespace Game.Data {
    public sealed class EntityInfoItems : IClassPoolable {
        private const float cycleDelay = 1.5f;
        public Entity Entity;
        public bool IsPoolableSpawned { get; set; }

        private Tile tile;
        private bool canMove;
        private Being being;
        private Obj obj;
        private readonly List<InfoItem> items = new();
        private const int typesCount = 4;
        private readonly InfoItem[] currentItems = new InfoItem[typesCount];
        private readonly float[] currentItemsSince = new float[typesCount];
        private readonly float[] currentItemsTimeout = new float[typesCount];


        // Beings and carried things move all the time, so InfoSys updates them every frame
        // instead of in its slices of the rest
        public bool IsMover { get; private set; }

        public void OnSpawn() {}
        public void OnDespawn() {
            Entity = null;
            tile = null;
            being = null;
            obj = null;
            canMove = false;
            IsMover = false;
            items.Clear();
            itemCount = 0;
            for (int i = 0; i < typesCount; i++) {
                currentItems[i] = null;
                currentItemsSince[i] = 0;
                currentItemsTimeout[i] = 0;
            }
        }

        // Several items of one type take turns roughly every 20 frames. A per entity next
        // frame rather than a frame modulo, because InfoSys does not visit every entity
        // every frame
        private const int cycleCheckFrames = 20;
        private int nextCycleCheckFrame;

        public void SetEntity(Entity entity) {
            this.Entity = entity;
            nextCycleCheckFrame = GameTimer.frameCount + entity.Id % cycleCheckFrames;
            if (entity is Being b) {
                being = b;
                canMove = true;
            } else if (entity is Tile t) {
                tile = t;
            } else if (entity is Obj o) {
                obj = o;
                canMove = true;
            }
            RefreshMover();
        }

        public void RefreshMover() {
            IsMover = being != null || (obj != null && obj.IsCarriedByAny);
        }

        private int itemCount;

        public void Add(InfoItem item) {
            if (items.Contains(item)) {
                if (item.WasUpdated) {
                    Show(item);
                }
            } else {
                items.Add(item);
                itemCount = items.Count;
                if (item.Timeout != 0) {
                    currentItemsTimeout[item.TypeInt] = GameTimer.unscaledTime;
                }
                Show(item);
            }
        }

        public void Remove(InfoItem item) {
            if (!items.Remove(item)) {
                D.Err("Did not contain item! {0}", item);
            } else {
                var it = item.TypeInt;
                if (item.Timeout != 0) {
                    currentItemsTimeout[it] = 0;
                }
                if (currentItems[it] == item) {
                    currentItems[it] = null;
                }
                ClearItem(item);
            }
            itemCount = items.Count;
        }

        ///<returns>false if we need to remove this container</returns>
        public bool Update() {
            if (itemCount == 0) { return false; }
            if (!Entity.IsActive) {
                D.Err("Updating for inactive entity! {0} {1}",
                    Entity, D.Dump(items));
                ClearItems();
                return false;
            }
            if (Entity.IsInfoDisabled) {
                // D.Warn("Removing info for entity with disabled info! {0}", Entity);
                ClearItems();
                return false;
            }
            var time = GameTimer.unscaledTime;
            InfoItem toHide = null;
            int currentCount = 0;
            for (int i = 0; i < typesCount; i++) {
                var item = currentItems[i];
                if (item == null) { continue; }
                currentCount++;
                if (item.Timeout != 0 && G.ClockSpeed != ClockSpeed.Stopped) {
                    var since = currentItemsTimeout[i];
                    if (since < 0.01f) {
                        since = time;
                    }
                    if (time > since + item.Timeout) {
                        item.Timeout = 0;
                        // Time to hide
                        toHide = item;
                    }
                }
                if (item.WasUpdated) {
                    Show(item);
                }
                if (canMove) {
                    item.Move(Entity.Position);
                }
            }

            if (toHide != null) { Remove(toHide); }

            if (itemCount == currentCount) { return true; }

            var frame = GameTimer.frameCount;
            if (frame >= nextCycleCheckFrame) {
                nextCycleCheckFrame = frame + cycleCheckFrames;
                for (int j = 0; j < typesCount; j++) {
                    int type = j;
                    var item = currentItems[type];
                    if (item != null) {
                        CycleItem(item, time);
                    } else {
                        // Fallback to another item
                        for (int i = itemCount - 1; i >= 0; i--) {
                            var item2 = items[i];// items.SafeGet(i);
                            if (item2?.TypeInt == type) {
                                currentItemsSince[type] = time;
                                item2.Touch();
                                Show(item2);
                            }
                        }
                    }
                }
            }

            return true;
        }

        public void UnlinkItems() {
            for (int i = itemCount - 1; i >= 0; i--) {
                InfoItem item = items[i];
                item.ParentCache = null;
            }
        }

        private void CycleItem(InfoItem item, float time) {
            var type = item.TypeInt;
            var currentSince = currentItemsSince[type];
            if (currentSince + cycleDelay < time) {
                // Time to cycle
                var itemToShow = NextOfSameType(item);
                if (itemToShow == item) {
                    currentItemsSince[type] = time;
                } else {
                    itemToShow.Touch();
                    Show(itemToShow);
                }
            }
        }

        private InfoItem NextOfSameType(InfoItem current) {
            var idx = items.IndexOf(current);
            var curType = current.Type;
            // First check from now till end
            for (int i = idx + 1; i < itemCount; i++) {
                var item = items[i];
                if (item.Type == curType) {
                    return item;
                }
            }
            // Check from beginning till now
            for (int i = 0; i < idx; i++) {
                var item = items[i];
                if (item.Type == curType) {
                    return item;
                }
            }
            // Apparently this is the only item of this type
            return current;
        }
        private readonly HashSet<int> shownKeysCache = new();

        public bool AppendInfoText(StringBuilder sb, string prefix) {
            var prefixAdded = false;
            shownKeysCache.Clear();
            bool appended = false;
            for (int i1 = 0; i1 < itemCount; i1++) {
                InfoItem item = items[i1];
                if (item == null) { continue; }
                if (!item.HasText) { continue; }
                var k = item.Info;
                if (k == null) { continue; }
                if (!shownKeysCache.Add(Hashes.S(k))) {
                    // We already show this key
                    continue;
                }
                if (!prefixAdded) {
                    prefixAdded = true;
                    if (prefix != null) {
                        sb.Append(prefix);
                    }
                    if (being != null) {
                        var dname = being.Data.Mind.Name.DisplayNameOr(being.Data.Species.Name);
                        sb.Append(Texts.SubTitle(dname));
                    } else {
                        sb.Append(Texts.SubTitle(Entity.Definition.NameT));
                    }
                } else {
                    sb.AppendLine();
                }
                if (item.CanHaveCustomTooltip &&
                        item.Component is IUICustomTooltipProvider p) {
                    sb.Append(p.ShowCustomTooltip(k, item));
                } else {
                    sb.Append(k);
                }
                appended = true;
            }
            return appended;
        }

        private void Hide(InfoItem item) {
            if (item == null) { return; }
            var type = item.TypeInt;
            var current = currentItems[type];
            if (current == item) {
                currentItems[type] = null;
            }
            // if (item.Being != null) {
            //     D.Warn("Hiding info item: {0} -> {1}", item, item.Being);
            // }
            item.Hide();
        }

        private void Show(InfoItem item) {
            if (item == null) { return; }
            // if (item.Being != null) {
            //     D.Warn("Showing item: {0} -> {1}", item, item.Being);
            // }
            var type = item.TypeInt;
            var current = currentItems[type];
            if (current != item) {
                Hide(current);
            }
            if (item.IsBlank) {
                #if !PRODUCTION_BUILD
                //if (!The.TestsAreRunning && Ready.Game) {
                // D.Warn("Not showing empty info item!: {0} {1}", item, item.Entity);
                //}
                #endif
                Remove(item);
                return;
            }
            currentItems[type] = item;
            currentItemsSince[type] = GameTimer.unscaledTime;
            if (item.Being != being) {
                // being was removed?
                Remove(item);
                return;
            }
            if (being != null) {
                item.Move(being.Position);
            }
            item.Show();
        }

        public void UpdateNow(InfoItem item) {
            if (item.IsBlank) {
                // We've called update on item that is blank now and
                // not displayed yet (not added)
                item.WasUpdated = false;
                Hide(item);
                items.Remove(item);
                itemCount = items.Count;
            } else {
                if (!items.Contains(item)) {
                    Add(item);
                } else {
                    Show(item);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ClearItem(InfoItem item) {
            // if (item.Being != null) {
            //     D.Warn("Clearing info item: {0} -> {1}", item, item.Being);
            // }
            item.Clear();
        }
        public void ClearItems() {
            // TODO remove all info items
            for (int i = items.Count - 1; i >= 0; i--) {
                InfoItem item = items[i];
                ClearItem(item);
            }
            items.Clear();
            itemCount = 0;
        }
        public void MoveTileInfo(float dx, float dy) {
            for (int i = itemCount - 1; i >= 0; i--) {
                InfoItem item = items[i];
                if (item.IsShowing) {
                    item.MoveBy(dx, dy);
                }
            }
        }

        public override string ToString() {
            return "EntityInfoItems";
        }
        public string DebugInfo {
            get {
                var sb = new StringBuilder();
                sb.AppendFormat("EntityInfoItems[{1}/{0}]", Entity.Id,
                    Entity.DefinitionId);
                foreach (var item in items) {
                    sb.AppendFormat("\n  -- {0}", item.DebugInfo);
                }
                return sb.ToString();
            }
        }
    }
}
