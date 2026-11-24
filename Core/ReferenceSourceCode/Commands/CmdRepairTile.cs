using Game.Components;
using Game.Data;
using Game.Commands;

namespace Game.Commands {
    public class CmdRepairTile : Command {
        private Tile tile;
        private bool instant;

        public CmdRepairTile(Tile tile, bool instant) : base("repair_tile") {
            this.tile = tile;
            this.instant = instant;
        }

        protected override void Execute() {
            if (EntityUtils.IsPresent(tile)) {
                if (tile.Health != null &&
                        tile.Health is DamageableComp dmg) {
                    if (instant) {
                        dmg.Repair(0, true);
                    } else {
                        dmg.CreateRepairGoal();
                    }
                }
            }
        }
    }
}
