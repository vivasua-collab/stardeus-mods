using System.Text;
using Game.Data;

namespace Game.Components {
    public interface IDefDescriptionProvider {
        /// <summary>
        /// One-line short info
        /// <returns>true if anything was added
        /// </summary>
        bool AddDefInfoAdditional(StringBuilder sb, Def def, bool addedAnything);
        /// <summary>
        /// Multi-line long info
        /// </summary>
        void AddDefInfo(StringBuilder sb, Def def);
    }
}
