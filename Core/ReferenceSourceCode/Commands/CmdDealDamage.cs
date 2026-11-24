using Game.Constants;
using Game.Data;
using Game.Commands;
using KL.Utils;
using Game.Utils;
using Game.CodeGen;
using System;

namespace Game.Commands {
    public class CmdDealDamage : Command {
        private readonly int pos;
        private readonly int layer;
        private readonly float amount;
        private DamageType type;

        public CmdDealDamage(int pos, int layer, float amount, DamageType type) : base("deal_dmg") {
            this.pos = pos;
            this.layer = layer;
            this.amount = amount;
        }

        public static void For(GameState s, int layer, int pos, float amt,
                DamageType type) {
            var tile = s.Map.Grids[layer].Get(pos);
            if (layer == WorldLayer.Objects) {
                tile = EntityUtils.ParentObj(tile);
            }
            if (EntityUtils.IsPresent(tile) && tile.Health != null) {
                tile.Health.TakeDamage(amt, type);
            }
        }

        protected override void Execute() {
            For(S, layer, pos, amount, type);
        }
    }
}
