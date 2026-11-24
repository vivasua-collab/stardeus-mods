using KL.Grid;
using Game.Data;
using UnityEngine;
using System;
using KL.Utils;
using Game.Systems.Firex;

namespace Game.Procgen {
    //[Obsolete("Make this phase async")]
    // TODO make this phase async?
    public class ShipFireGenStage : BaseShipGenStage {
        public override void Apply() {
            for (int i = 0; i < rng.Range(3, 7); i++) {
                SetOnFire(RandomX(), RandomY(), new(5, 20));
            }
        }

        private void SetOnFire(int x, int y, Vector2 radiusRange) {
            var pos = Pos.FromXY(x, y);
            Pos.ForEachInRadius(pos, rng.Range(radiusRange), true, (p) => {
                var tile = EntityUtils.GetTopmostTile(p);
                S.Sys.Firex.TrySetOnFire(tile, Fire.Create(fuel: 1f));
                return true;
            });
        }
    }
}
