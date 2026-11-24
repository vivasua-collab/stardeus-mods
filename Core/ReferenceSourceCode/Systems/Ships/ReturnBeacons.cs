using Game.CodeGen;
using Game.Data;
using Game.Data.Space;

namespace Game.Systems.Ships {
    public class ReturnBeacons {
        private readonly GameState S;
        private SpaceObject[] _beacons;
        public SpaceObject[] Beacons => beacons;
        public bool HasBeacons => beacons.Length > 0;
        public int NumBeacons => beacons.Length;
        public ReturnBeacons(GameState s) {
            S = s;
            S.Sig.SpaceObjectAdded.AddListener(OnSOChanged);
            S.Sig.SpaceObjectRemoved.AddListener(OnSOChanged);
            S.Sig.UniverseRegenerated.AddListener(OnUniverseRegen);
        }

        private void OnUniverseRegen() {
            _beacons = null;
        }

        private void OnSOChanged(SpaceObject so) {
            if (so.Type.IdH == SpaceObjectTypeIdH.ReturnBeacon) {
                // rebuild the cache
                _beacons = null;
            }
        }


        private SpaceObject[] beacons {
            get {
                if (_beacons != null) { return _beacons; }
                Caches.SpaceObjectList.Clear();
                foreach (var obj in S.Universe.ObjectsById.Values) {
                    if (obj.Type.IdH == SpaceObjectTypeIdH.ReturnBeacon) {
                        Caches.SpaceObjectList.Add(obj);
                    }
                }
                _beacons = Caches.SpaceObjectList.ToArray();
                return _beacons;
            }
        }
    }
}
