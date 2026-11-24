using System.Collections.Generic;
using Game.Data;
using Game.Research;

namespace Game.UI.Research {
    public interface IResearchLayout {
        Dictionary<string, ResearchTreeNode> Calculate(GameState S, List<Def> input);
    }
}
