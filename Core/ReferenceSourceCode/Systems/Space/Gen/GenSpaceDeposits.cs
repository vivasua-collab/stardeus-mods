namespace Game.Systems.Space {
    public sealed class GenSpaceDeposits : GenSpaceAssignables<DepositConfig> {
        public GenSpaceDeposits() : base(DepositConfig.All) { }
        protected override bool CanAssignLeftoversTo(SpaceSector sec) {
            if (sec.Biome.Template is SectorTemplate tpl) {
                return tpl.NumDepositsCurve.Evaluate(1f) > 0f;
            }
            return false;
        }

    }
}
