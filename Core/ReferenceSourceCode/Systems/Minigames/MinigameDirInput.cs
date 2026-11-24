using UnityEngine;

namespace Game.Systems.Minigames {
    // Turns the combined movement vector into discrete presses. Each axis
    // latches the direction it fired in and is released only once it falls
    // back near zero. Comparing two consecutive frames instead (the old way)
    // misses analog sticks: they ramp up over several frames, so no single
    // frame changes by the press threshold. The gap between the two
    // thresholds keeps a stick resting near 0.5 from firing repeatedly.
    public sealed class MinigameDirInput {
        private const float PressAt = 0.5f;
        private const float ReleaseBelow = 0.25f;

        private int heldX;
        private int heldY;

        // Takes the input in without firing: a direction held while the
        // window was unfocused must not turn on the first focused frame
        public void Sync(Vector2 raw) {
            Latch(raw.x, ref heldX, out _);
            Latch(raw.y, ref heldY, out _);
        }

        // Fires on press only, a release never produces a turn (it would
        // undo the action, e.g. a piece moving back on key release)
        public bool Update(Vector2 raw, out Vector2Int dir) {
            dir = default;
            var xPress = Latch(raw.x, ref heldX, out var xSign);
            var yPress = Latch(raw.y, ref heldY, out var ySign);
            if (!xPress && !yPress) { return false; }
            if (xPress && (!yPress || Mathf.Abs(raw.x) > Mathf.Abs(raw.y))) {
                dir = new Vector2Int(xSign, 0);
            } else {
                dir = new Vector2Int(0, ySign);
            }
            return true;
        }

        private static bool Latch(float v, ref int held, out int sign) {
            var abs = Mathf.Abs(v);
            sign = v > 0f ? 1 : -1;
            if (abs < ReleaseBelow) {
                held = 0;
                return false;
            }
            if (abs >= PressAt && sign != held) {
                held = sign;
                return true;
            }
            return false;
        }
    }
}
