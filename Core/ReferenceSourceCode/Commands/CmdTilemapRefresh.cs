using Game.Constants;
using Game.Commands;
using KL.Grid;

namespace Game.Commands {
    public class CmdTilemapRefresh : Command {
        public CmdTilemapRefresh() : base("tilemap_refresh", unique: true) { }
        protected override void Execute() {
            S.Sig.ResetTileGraphics.Send();
        }
    }
}
