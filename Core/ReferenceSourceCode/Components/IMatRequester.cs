using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;

namespace Game.Components {
    public interface IMatRequester : IComponent {
        // If it's 0, we will try to go near the tile (esp. multitile)
        int InPort { get; }
        // Returns true if all material desires are fullfilled
        bool ProvideRequestedMat(UnstoredMatComp unstoredMat, Being worker,
            AIGoal goal = null);
        List<MatRequest> MissingMats { get; }
        // Up-to-date list of requested materials by this requester
        MatRequest NextMissingMat { get; }
        void NotifyMatDeficit(MatType type);
        void ClearLackNotification(MatType type);
        // Returns true if requester needs more than 1 distinct material type
        bool IsMulti {
            get {
                var mats = MissingMats;
                if (mats == null || mats.Count < 2) { return false; }
                var firstType = mats[0].Type;
                for (int i = 1; i < mats.Count; i++) {
                    if (mats[i].Type != firstType) { return true; }
                }
                return false;
            }
        }
        // Provides multiple materials at once. Returns true if all fulfilled.
        bool ProvideMulti(List<UnstoredMatComp> mats, Being worker,
                AIGoal goal = null) {
            bool fulfilled = false;
            for (int i = 0; i < mats.Count; i++) {
                fulfilled = ProvideRequestedMat(mats[i], worker, goal);
            }
            return fulfilled;
        }
    }
}
