using Game.Components;
using Game.Systems.AI;
using UnityEngine;

namespace Game.Systems.Nanobots {
    public interface INanobotCompatible : IComponent {
        public void AddNanobotsEffort(float amt, out AIGoal goal);
    }
}
