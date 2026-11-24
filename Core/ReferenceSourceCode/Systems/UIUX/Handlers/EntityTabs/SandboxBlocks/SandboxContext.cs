using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public class SandboxContext {
        public Tile Tile;
        public ObjData Obj;
        public BeingData Being;
        public Lifetime Lifetime;
        public GameState S;

        public void Reset() {
            Tile = null;
            Obj = null;
            Being = null;
            if (Lifetime != null) {
                Lifetime.Dispose();
                Lifetime = null;
            }
            S = null;
        }
    }
}
