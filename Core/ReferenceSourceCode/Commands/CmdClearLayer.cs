namespace Game.Commands {
    public class CmdClearLayer : Command {
        private readonly int layer;
        public CmdClearLayer(int layer) : base("clear_layer") {
            this.layer = layer;
        }
        protected override void Execute() {
            var grid = S.Map.Grids[layer];
            for (int i = 0; i < grid.CellCount; i++) {
                S.Map.Grids[layer].Set(i, null);
            }
            S.Components.RemoveLayer(layer);
        }
    }
}
