using KL.Utils;

namespace Game.Systems.Space {
    public sealed class GenSpacePOIs : GenSpaceAssignables<POIConfig> {
        public GenSpacePOIs() : base(POIConfig.All) { }
        protected override bool CanAssignLeftoversTo(SpaceSector sec) {
            if (sec.Biome.Template is SectorTemplate tpl) {
                // if (sec.Biome.FactionsByDominance?.Count > 0) {
                //     D.Log("COMPATIBLE: {0}", sec);
                // }
                return tpl.NumPOIsCurve.Evaluate(1f) > 0f;
            }
            return false;
        }
    }
}
