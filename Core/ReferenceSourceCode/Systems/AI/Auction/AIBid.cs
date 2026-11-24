using System;
using KL.Utils;

namespace Game.Systems.AI {
    public struct AIBid {
        public float Bid;
        // the worker's priority for the goal's job; 0 for personal goals
        public byte JobPriority;
        public bool IsValid => Bid > 0f;
        public AIGoal Goal;
        public AIAgentComp Agent;
        public AIPlan Plan;
        public long Ticks;
        public override string ToString() {
            if (Bid > 0f) {
                return $"Bid[{Bid}:{Agent?.BeingData.Name}->{Goal.Name}]";
            }
            return "Bid[Invalid]";
        }

        public static AIBid For(long ticks, AIGoal goal, AIAgentComp agent, AIPlan plan,
                int regDist, byte jobPriority = 0) {
            if (plan == null || plan.Cost <= 0f) {
                return default;
            }
            return new AIBid() {
                Goal = goal,
                Agent = agent,
                Ticks = ticks,
                Bid = plan.Cost + regDist,
                JobPriority = jobPriority,
                Plan = plan
            };
        }


        public static AIBid Better(AIBid a, AIBid b) {
            if (!b.IsValid) {
                D.Err("Trying to assign a invalid bid! {0} instead of {1}",
                    b, a);
                return a;
            }
            if (!a.IsValid) { return b; }
            // a worker with a higher priority for the job wins whatever the cost
            if (a.JobPriority != b.JobPriority) {
                return a.JobPriority > b.JobPriority ? a : b;
            }
            if (a.Bid < b.Bid) {
                return a;
            }
            return b;
        }

        public bool Assign(long ticks) {
            if (!IsValid) {
                D.Err("Trying to assign invalid bid! {0}", this);
                return false;
            }
            // invalidate the bid, but still have a way to see what it was
            Bid *= -1f;

            return Agent.SetGoal(Goal, Plan, ticks);
        }
    }
}
