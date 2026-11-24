using Game.Data;
using Game.Commands;
using KL.Grid;
using KL.Utils;
using Game.Utils;
using Game.CodeGen;
using Game.UI;
using Game.Constants;
using KL.Randomness;
using Game.Systems.Firex;
using System;

namespace Game.Commands {
    public class CmdStartFire : Command {
        private readonly int pos;
        private Fire fire;
        private readonly float str;
        private readonly float fuel;

        public CmdStartFire(int pos, float str = 0.75f, float fuel = 0.0f) : base("start_fire") {
            this.str = str;
            this.fuel = fuel;
            fire.PosIdx = pos;
            fire.Position = Pos.ToVector(pos);
            this.pos = pos;
        }

        public CmdStartFire(int pos, in Fire fire, float radius = 0) : base("start_fire") {
            this.fire = fire;
            this.pos = pos;
        }

        protected override void Execute() {
            var tile = EntityUtils.GetTopmostParentTile(pos);
            TrySetOnFire(tile);
        }

        private void TrySetOnFire(Tile tile) {
            if (tile == null || !tile.IsActive) { return;}
            var flam = tile.Flammable;
            if (flam == null || flam.IsOnFire || !flam.IsActive) {
                return;
            }
            fire = Fire.CreateBalanced(S, str, fuel);
            S.Sys.Firex.TrySetOnFire(tile.Flammable, fire);
        }
    }
}
