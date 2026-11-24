using Game.Data;
using Game.Systems.Firex;
using KL.Grid;
using KL.Utils;

namespace Game.Procgen {
    public class FireGenStage : BaseShipGenStage {
        public FireGenStage(float chance) {
            this.chance = chance;
        }
        private float chance;
        public override void Apply() {
            var rng = spec.Rng.Fork();
            spec.ForEachTile((i) => {
                if (!IsWithinGenArea(i)) { return; }
                if (rng.Chance(chance)) {
                    var obj = EntityUtils.GetTopmostParentTile(i);
                    if (obj != null && obj.IsConstructed) {
                        if (!obj.Flammable.IsOnFire) {
                            S.Sys.Firex.TrySetOnFire(obj.Flammable,
                                Fire.Create(rng.Range(0.5f, 0.9f),
                                    rng.Range(0.4f, 0.8f)));
                        }
                    }
                }
            });
        }
    }
}
