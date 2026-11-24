using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Conditions;
using Game.Systems.Creatures;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;
using System;
using Game.Systems.Stats;

namespace Game.Systems.AI {
    public abstract class AINeed {
        public string Id;
        public int IdH;
        public NeedType Type;
        public virtual bool IsInternal => false;
        /// <summary>
        /// True for needs that are meaningless without Mind.Health (the depression loop in
        /// AINeedFun is the only one today). Species.ValidateMentalNeeds uses it to reject a
        /// species that lists such a need without also listing MentalHealth.
        /// </summary>
        public virtual bool RequiresMentalHealth => false;
        /// <summary>
        /// If true, the need will be highlighted in the UI as something that requires attention. For example, if the being is starving, we want to draw player's attention to that fact.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="iconId">icon to display when </param>
        /// <param name="priority">we will only show the most important need icon, priority is used to determine which need to show</param>
        /// <returns></returns>
        public virtual bool RequiresAttention(BeingData data, out string iconId, out float priority) {
            iconId = null;
            priority = 0f;
            return false;
        }

        // How well the need is met right now, 0 (not at all) to 1 (fully), shown as a bar in the
        // Needs tab. Each need owns the meaning, since raw stats differ in units (battery units,
        // ambient O2, Kelvin). Needs without a level return false and stay plain text rows.
        public virtual bool TryGetSatisfaction(BeingData data, out float satisfaction) {
            satisfaction = 0f;
            return false;
        }

        public virtual string SatisfactionText(BeingData data, float satisfaction) {
            return Units.Percentage(satisfaction);
        }

        // Share of required body parts that are unharmed, and nothing while downed. Coarse, but
        // the same proxy raid weights and combat repair urgency already use.
        protected static float BodyIntegrity(BeingData data) {
            return data.Body.IsIncapacitated ? 0f : 1f - data.Body.InjuryPercent;
        }

        protected static float MainStatSatisfaction(Stat stat) {
            return Maths.Clamp(stat.Value, 0f, 1f);
        }

        protected static void RegisterNeed(AINeed need) {
            Registries.AINeeds.Add(need.Id, need);
        }
        /// <summary>
        /// Happens any time needs list changes. After load, after change of
        /// body part, etc.
        /// </summary>
        public abstract void InitializeFor(BeingData data);
        /// <summary>
        /// Happens only once when beingdata gets initialized for the first time
        /// </summary>
        public abstract void InitializeForNew(BeingData data);
        /// <summary>
        /// When for example brain is removed, we have to remove stats that the need
        /// added
        /// </summary>
        public virtual void OnRemoveNeed(BeingData data) {}
        /// <summary>
        /// When brain gets transplanted into a different being that didn't have a need
        /// </summary>
        public virtual void OnAddNeed(BeingData data) {}
        public abstract void Tick(GameState s, BeingData data, long ticks);
        protected AINeed(string id) {
            Id = id;
            IdH = Hashes.S(id);
        }

        /// <summary>
        /// Mostly for displaying the stats in the UI. The first stat in this list
        /// will be treated as "main" and its value is shown next to needs that have
        /// no satisfaction bar (see TryGetSatisfaction).
        /// Additional stats will be displayed in hover tooltips.
        /// </summary>
        public abstract int[] RelatedStats { get; }

        /// <returns>true if goal was made available</returns>
        protected bool SetAvailableWithCooldown(AIGoal g, AIAgentComp agent, long ticks,
                int cooldownFrom = Consts.TicksPer30Minutes,
                int cooldownTo = Consts.TicksPerHour) {
            if (g.State == AIGoalState.Available) {
                return false;
            }
            if (g.State == AIGoalState.Failed) {
                agent.BB.SetCooldown(IdH, ticks, cooldownFrom, cooldownTo);
                // let's keep it as completed, because pending would get assigned
                g.ChangeStateTo(AIGoalState.CoolingDown, ticks);
                return false;
            } else {
                if (agent.BB.IsCoolingDown(IdH, ticks)) {
                    return false;
                }
                g.ChangeStateTo(AIGoalState.Available, ticks);
                g.CreatedAt = ticks;
                g.ResetWorldState();
                return true;
            }
        }

        public bool TryGetMainStatFor(StatsBeing stats, out Stat stat) {
            if (Arrays.IsEmpty(RelatedStats)) {
                stat = null;
                return false;
            }
            if (stats.Find(RelatedStats[0], out stat)) {
                return !stat.IsUnused;
            }
            return false;
        }

        public override string ToString() {
            return $"AINeed[{Id}]";
        }

    }
}
