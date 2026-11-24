using Game.CodeGen;
using Game.Constants;
using Game.Systems.AI;
using Game.Systems.Stats;
using UnityEngine;

namespace Game.Systems.Mental {
    // One fun session, stored on the agent blackboard (Long1 = planned end tick,
    // Bool1 = would the session have satisfied the need at full novelty).
    public static class FunSession {
        // Strong fun sessions end at this Fun level
        public const float SatisfiedAt = 0.99f;
        // The fun goal opens at or below this Fun level
        public const float GoalThreshold = 0.9f;
        // Below this, the depression curve starts, so no rest cooldown is applied
        public const float RestMinFun = 0.33f;
        private const int MinRestTicks = Consts.TicksPer2Hours;
        private const int MaxRestTicks = Consts.TicksPer12Hours;

        public static float GainPerTick(StatsBeing stats, float novelty,
                float activityMultiplier) {
            return stats.FunIncreaseRate.Value * activityMultiplier * novelty;
        }

        // Fun gained per tick at full novelty, after the drain that keeps running during
        // the session. Used to plan how long a full novelty session would last.
        public static float FullNetGainPerTick(StatsBeing stats, float activityMultiplier) {
            return stats.FunIncreaseRate.Value * activityMultiplier - stats.FunDropRate.Value;
        }

        // Plans the session to last exactly as long as it would at full novelty, so an
        // over-used source leaves the need unsatisfied instead of dragging on for hours.
        public static void Begin(AIAgentComp agent, long ticks, float fullGainPerTick,
                long minTicks, long maxTicks) {
            var fun = agent.BeingData.StatsBeing.Fun.Value;
            var need = SatisfiedAt - fun;
            long toSatisfy;
            if (need <= 0f) {
                toSatisfy = 0;
            } else if (fullGainPerTick <= 0f) {
                toSatisfy = long.MaxValue;
            } else {
                toSatisfy = (long) Mathf.Ceil(need / fullGainPerTick);
            }
            var planned = toSatisfy;
            if (planned < minTicks) {
                planned = minTicks;
            } else if (planned > maxTicks) {
                planned = maxTicks;
            }
            agent.BB.Long1 = planned >= long.MaxValue - ticks ? long.MaxValue : ticks + planned;
            agent.BB.Bool1 = toSatisfy != long.MaxValue && toSatisfy <= maxTicks;
        }

        public static bool IsStarted(AIAgentComp agent) {
            return agent.BB.Long1 > 0;
        }

        public static bool IsOver(AIAgentComp agent, long ticks) {
            return agent.BB.Long1 > 0 && ticks >= agent.BB.Long1;
        }

        public static void Clear(AIAgentComp agent) {
            agent.BB.Long1 = 0;
            agent.BB.Bool1 = false;
        }

        // Call on success. When the session was cut short only by low novelty, rest for as
        // long as a satisfied session would have kept the need closed (99% -> 90% drain),
        // so beings keep today's rhythm but get less out of each session.
        public static void Finish(AIAgentComp agent, long ticks) {
            var bb = agent.BB;
            if (bb.Bool1 && IsOver(agent, ticks)) {
                var stats = agent.BeingData.StatsBeing;
                var fun = stats.Fun.Value;
                if (fun >= RestMinFun && fun < GoalThreshold) {
                    var rest = RestTicksFor(stats);
                    bb.SetCooldown(NeedIdH.Fun, ticks, rest, rest + rest / 10);
                }
            }
            Clear(agent);
        }

        public static int RestTicksFor(StatsBeing stats) {
            var drop = stats.FunDropRate.Value;
            if (drop <= 0f) {
                return MaxRestTicks;
            }
            var rest = (SatisfiedAt - GoalThreshold) / drop;
            return (int) Mathf.Clamp(rest, MinRestTicks, MaxRestTicks);
        }
    }
}
