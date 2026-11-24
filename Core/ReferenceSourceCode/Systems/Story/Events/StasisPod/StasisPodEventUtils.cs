using Game.Components;
using Game.Data;

namespace Game.Systems.Story {
    public static class StasisPodEventUtils {
        public static bool ValidateEventData(GameState s, StoryEventData data, out StasisPodComp pod) {
            var podTile = data.TargetTile;
            if (podTile == null) {
                data.FailEvent(s, "TargetTile is not set for StasisPod Event!");
                pod = null;
                return false;
            }
            if (!podTile.IsActive) {
                data.FailEvent(s, "TargetTile is inactive for StasisPod Event!");
                pod = null;
                return false;
            }
            if (!podTile.TryGetComponent(out pod)) {
                data.FailEvent(s, "TargetTile has no StasisPodComp for StasisPod Event!");
                return false;
            }
            return true;
        }
    }
}
