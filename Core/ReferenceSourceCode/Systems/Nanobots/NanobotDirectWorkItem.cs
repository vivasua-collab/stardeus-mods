using Game.Data;
using Game.Systems.AI;
using UnityEngine;

namespace Game.Systems.Nanobots {
    internal sealed class NanobotDirectWorkItem : INanobotWorkItem {
        private readonly INanobotCompatible workable;

        public NanobotDirectWorkItem(AIGoal goal, INanobotCompatible workable) {
            Goal = goal;
            this.workable = workable;
        }

        public int Id => workable.EntityId;
        public AIGoal Goal { get; }
        public Entity Entity => workable.Entity;
        public Vector2 Position => workable.Entity.Position;
        public Vector2 Size => workable.Entity.Size;

        public bool CanHiveReach(NanobotHiveComp hive) {
            return NanobotReachability.CanHiveReach(hive,
                workable.Entity.PosIdx, workable.Entity.Position, workable);
        }

        public bool CanHiveWork(NanobotHiveComp hive) {
            return true;
        }

        public void AddWork(NanobotHiveComp hive, long ticks,
                out AIGoal workedGoal) {
            workable.AddNanobotsEffort(hive.ActualWorkSpeed, out workedGoal);
            if (workedGoal != null) {
                hive.MarkWorked(workable.Entity);
            }
        }
    }
}
