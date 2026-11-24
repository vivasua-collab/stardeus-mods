using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Ships;
using Game.Utils;
using KL.Utils;

namespace Game.Systems {
    // Arrows pointing at things that arrived on their own and still wait for the player:
    // landed storage capsules, landed stasis pods, and Safe Boxes. A playtester never saw a
    // capsule land, because nothing pointed at it. Same arrows as the off-screen ship
    // markers, except they stay next to the object once it is on screen. An arrow goes away
    // for good once the player clicks it or selects the object, or once the object is
    // opened or gone; the ids are saved so arrows survive a load. The arrows are hints, so
    // Show Hints turns them off: nothing is tracked, and turning it off dismisses them all.
    public sealed class UnseenObjectsSys : GameSystem, ISaveable {
        public const string SysId = "UnseenObjectsSys";
        public override string Id => SysId;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new UnseenObjectsSys());
        }

        public static UnseenObjectsSys For(GameState s) {
            return s?.Sys?.Get<UnseenObjectsSys>(SysId);
        }

        // Entity ids by kind: tiles and objs have separate id spaces
        private readonly HashSet<int> tileIds = new();
        private readonly HashSet<int> objIds = new();
        private readonly Dictionary<Entity, ShipOffScreenMarker> markers = new();
        private readonly List<Entity> toDismiss = new();
        private const int CheckEveryTicks = 10;

        public bool IsTracked(Entity e) {
            return e != null && markers.ContainsKey(e);
        }

        internal ShipOffScreenMarker MarkerOf(Entity e) {
            return e != null && markers.TryGetValue(e, out var m) ? m : null;
        }

        public void Track(Entity e) {
            if (e == null || !e.IsActive) { return; }
            // Not even remembered, so turning hints back on does not bring a backlog of
            // arrows for everything that arrived meanwhile
            if (!A.ShowHints) { return; }
            if (e is Tile) {
                tileIds.Add(e.Id);
            } else if (e is Obj) {
                objIds.Add(e.Id);
            } else {
                return;
            }
            SpawnMarker(e);
        }

        public void Dismiss(Entity e) {
            if (e == null) { return; }
            if (e is Tile) {
                tileIds.Remove(e.Id);
            } else {
                objIds.Remove(e.Id);
            }
            if (markers.TryGetValue(e, out var m)) {
                markers.Remove(e);
                DespawnMarker(m);
            }
        }

        private void DismissAll() {
            foreach (var kv in markers) {
                DespawnMarker(kv.Value);
            }
            markers.Clear();
            tileIds.Clear();
            objIds.Clear();
        }

        private void OnSettingChanged(string key) {
            if (key == Pref.GShowHints && !A.ShowHints) {
                DismissAll();
            }
        }

        private void SpawnMarker(Entity e) {
            if (markers.ContainsKey(e)) { return; }
            if (S.Vars.IsTemplateMode) { return; }
            var prefab = ResourceLibrary.FindPrefab<ShipOffScreenMarker>();
            if (prefab == null) { return; }
            // Not from PrefabPool, see DespawnMarker
            var marker = UnityEngine.Object.Instantiate(prefab, Consts.V3Zero, Consts.QId,
                ResourceLibrary.Root);
            marker.SetupAsEntityMarker(e, OnMarkerClicked);
            markers[e] = marker;
        }

        // Destroyed, not despawned: ShipView takes markers from the same pool and relies on
        // the prefab's NPC ship mode, which Start() only applies once per instance, so a
        // recycled entity marker would come back pointing at nothing
        private static void DespawnMarker(ShipOffScreenMarker m) {
            if (m == null) { return; }
            m.ClearEntityTarget();
            UnityEngine.Object.Destroy(m.gameObject);
        }

        private void OnMarkerClicked(Entity e) {
            // Not SelectOrFocus: that only moves the camera on a second click, and the
            // arrow is gone after the first
            EntityUtils.SelectAndFocus(e);
            Dismiss(e);
        }

        private void OnObjSelected(List<Obj> objs) {
            if (objs == null || markers.Count == 0) { return; }
            for (int i = 0; i < objs.Count; i++) {
                if (markers.ContainsKey(objs[i])) {
                    Dismiss(objs[i]);
                }
            }
        }

        private void OnTilesSelected(List<Tile> tiles) {
            if (tiles == null || markers.Count == 0) { return; }
            for (int i = 0; i < tiles.Count; i++) {
                if (markers.ContainsKey(tiles[i])) {
                    Dismiss(tiles[i]);
                }
            }
        }

        // A plain click selects through MultiSelect alone; ObjSelected and TilesSelected
        // only come from focusing an entity (alerts, links) and from double clicks
        private void OnMultiSelect(List<Being> beings, List<Obj> objs, List<Tile> tiles) {
            OnObjSelected(objs);
            OnTilesSelected(tiles);
        }

        private void OnTick(long ticks) {
            if (markers.Count == 0 || ticks % CheckEveryTicks != 0) { return; }
            toDismiss.Clear();
            foreach (var kv in markers) {
                if (IsDone(kv.Key)) {
                    toDismiss.Add(kv.Key);
                }
            }
            for (int i = 0; i < toDismiss.Count; i++) {
                Dismiss(toDismiss[i]);
            }
            toDismiss.Clear();
        }

        // Nothing left to point at: the object is gone, or the player already dealt with it
        private static bool IsDone(Entity e) {
            if (!e.IsActive) { return true; }
            if (e.TryGetComponent<StorageCapsuleComp>(out var capsule)) {
                return capsule.IsOpen;
            }
            if (e.TryGetComponent<QuestPackageComp>(out var package)) {
                return package.IsOpen;
            }
            if (e.TryGetComponent<StasisPodComp>(out var pod)) {
                return pod.IsOpened;
            }
            return false;
        }

        private void OnAfterLoadState(GameState s) {
            if (!A.ShowHints) {
                DismissAll();
                return;
            }
            foreach (var id in tileIds) {
                var t = S.FindEntity<Tile>(id);
                if (t != null) { SpawnMarker(t); }
            }
            foreach (var id in objIds) {
                var o = S.FindEntity<Obj>(id);
                if (o != null) { SpawnMarker(o); }
            }
        }

        protected override void OnInitialize() {
            S.Sig.AfterLoadState.AddListener(OnAfterLoadState);
            S.Sig.ObjSelected.AddListener(OnObjSelected);
            S.Sig.TilesSelected.AddListener(OnTilesSelected);
            S.Sig.MultiSelect.AddListener(OnMultiSelect);
            S.Clock.OnTick.AddListener(OnTick);
            The.SysSig.SettingChanged.AddListener(OnSettingChanged);
        }

        public override void Unload() {
            The.SysSig.SettingChanged.RemoveListener(OnSettingChanged);
            DismissAll();
            toDismiss.Clear();
            data = null;
        }

        public string GetName() {
            return Id;
        }

        private ComponentData data;
        private ComponentData Data => data ??= new(0, GetName());

        public ComponentData Save() {
            var d = Data;
            d.SetIntSet("Tiles", ToArray(tileIds));
            d.SetIntSet("Objs", ToArray(objIds));
            return d;
        }

        public void Load(ComponentData data) {
            this.data = data;
            tileIds.Clear();
            objIds.Clear();
            var tiles = data.GetIntSet("Tiles", null);
            if (tiles != null) { tileIds.UnionWith(tiles); }
            var objs = data.GetIntSet("Objs", null);
            if (objs != null) { objIds.UnionWith(objs); }
        }

        private static int[] ToArray(HashSet<int> set) {
            var arr = new int[set.Count];
            set.CopyTo(arr);
            return arr;
        }
    }
}
