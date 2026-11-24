using KL.Grid;

namespace Game.Systems.Motion {
    public sealed class MotionProgress {
        public int GoalPosIdx;
        public MotionProgressFlags Flags;
        public string FailReason;
        public bool HasPath => (Flags & MotionProgressFlags.HasPath) == MotionProgressFlags.HasPath;
        public bool HasFailed => (Flags & MotionProgressFlags.Failed) == MotionProgressFlags.Failed;
        public bool IsUnsafe => (Flags & MotionProgressFlags.Unsafe) == MotionProgressFlags.Unsafe;
        public bool IsTeleporting => (Flags & MotionProgressFlags.Teleporting) == MotionProgressFlags.Teleporting;
        public bool IsMoving => (Flags & (MotionProgressFlags.Moving | MotionProgressFlags.Teleporting)) != MotionProgressFlags.None;

        public override string ToString() {
            return $"MoveProg[{Pos.String(GoalPosIdx)}|{Flags}|{FailReason}]";
        }
    }
}
