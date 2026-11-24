using System.Collections.Generic;

namespace Game.Components {
    public interface IRangedCached : IRanged {
        HashSet<int> GetCachedRange(int origin);
    }
}
