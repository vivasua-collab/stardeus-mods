namespace Game.Systems.Space {
    public sealed class GenSpaceEffects : GenSpaceAssignables<SpaceEffectConfig> {
        public GenSpaceEffects() : base(SpaceEffectConfig.All) { }
        protected override bool CanAssignLeftoversTo(SpaceSector sec) {
            if (sec.Biome.Template is SectorTemplate tpl) {
                return tpl.MustHavePOIs;
            }
            return false;
        }
    }
}
