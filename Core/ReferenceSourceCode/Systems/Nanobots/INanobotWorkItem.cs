using Game.Data;
using Game.Systems.AI;
using UnityEngine;

namespace Game.Systems.Nanobots {
    internal interface INanobotWorkItem {
        int Id { get; }
        AIGoal Goal { get; }
        Entity Entity { get; }
        Vector2 Position { get; }
        Vector2 Size { get; }
        bool CanHiveReach(NanobotHiveComp hive);
        bool CanHiveWork(NanobotHiveComp hive);
        void AddWork(NanobotHiveComp hive, long ticks, out AIGoal workedGoal);
    }
}
