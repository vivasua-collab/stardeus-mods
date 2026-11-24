using Game.Constants;
using Game.Commands;
using KL.Grid;
using KL.Utils;

namespace Game.Commands {
    public class CmdTilemapClear : Command {
        public CmdTilemapClear() : base("tilemap_clear") { }

        protected override void Execute()  {
            for (int i = 0; i < S.Map.Grids.Length; i++) {
                var grid = S.Map.Grids[i];
                if (grid != null) {
                    new CmdClearLayer(grid.Layer).Execute(S);
                }
            }
        }
    }
}
