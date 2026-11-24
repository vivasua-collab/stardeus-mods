using Game.CodeGen;
using Game.Components;
using Game.Data;

namespace Game.Systems.Space {
    public class ShieldableSpaceThreat : IThreat {
        public Entity Entity => null;
        public int ThreatType => ThreatIdH.ShieldableSpaceThreat;
        public bool IsThreatActive => true;
        public bool AffectsSoundtrack => true;
    }
}
