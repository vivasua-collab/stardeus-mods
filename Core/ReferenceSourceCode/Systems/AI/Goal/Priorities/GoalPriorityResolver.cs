using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI.Combat;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.AI {
    public abstract class GoalPriorityResolver {
        public string Id;
        public int IdH;
        protected static void RegisterResolver(GoalPriorityResolver res) {
            Registries.GoalPriorityResolvers.Add(res.Id, res);
        }

        /// <returns>false if goal should not be enqueued</returns>
        public abstract bool UpdatePriorityFor(AIGoal goal,
                AIAgentComp agent, long ticks);

        protected GoalPriorityResolver(string id) {
            Id = id;
            IdH = Hashes.S(id);
        }
    }
}
