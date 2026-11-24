using System;
using Game.Constants;
using Game.Utils;
using MessagePack;

namespace Game.Systems.Story {
    [MessagePackObject]
    public class StorySchedule {
        public static int Count;
        [Key("Outcome")] public StoryEventOutcome Outcome;
        [Key("IsAllowed")] public bool IsAllowed;
        [Key("ChangeAfter")] public long ChangeAfter;
        /// <summary>
        /// Periodic events started under the current schedule cycle by the EventPicker.
        /// Hook / on-demand events of the same outcome do not count.
        /// </summary>
        [Key("Executed")] public int Executed;
        /// <summary>
        /// How many periodic events the current cycle may start, rolled by the Schedule aspect
        /// on every flip to ON. 0 = uncapped (schedules without an EventsPerCycleCurve).
        /// </summary>
        [Key("MaxEvents")] public int MaxEvents;
        /// <summary>
        /// Setting to -1 will refresh the points if schedule is allowed
        /// </summary>
        [Key("PointAllowance")] public int PointAllowance;
        /// <summary>
        /// Used for keeping track of how much points we had at the start.
        /// Also used for OnDemand events, which can happen outside schedule.
        /// </summary>
        [Key("MaxPointAllowance")] public int MaxPointAllowance;
        /// <summary>
        /// When we fire an event if schedule is allowing events,
        /// we will cool down for a certain amount of time before adding
        /// another event in the same cycle.
        /// </summary>
        [Key("CooldownUntil")] public long CooldownUntil;
        [Key("Cycle")] public uint Cycle;
        [IgnoreMember] public StoryScheduleModifier DifficultyMod;
        [IgnoreMember] public bool HasEventsLeft => MaxEvents <= 0 || Executed < MaxEvents;
        public void ChangeNextTick() {
            ChangeAfter = 0;
            if (!IsAllowed) {
                CooldownUntil = 0;
                // it will become allowed, so we want to recalculate points
                PointAllowance = -1;
            }
        }

        public bool CanScheduleNow(long ticks) {
            return IsAllowed && PointAllowance > 0 && CooldownUntil <= ticks && HasEventsLeft;
        }
        public bool CanStartPeriodicEvent(long ticks, out bool isAllowed, out bool isCoolingDown) {
            isAllowed = IsAllowed;
            isCoolingDown = CooldownUntil > ticks;
            return isAllowed && !isCoolingDown && PointAllowance > 0 && HasEventsLeft;
        }

        public void ForceAllowNextTick() {
            if (!IsAllowed) {
                ChangeNextTick();
            }
        }

        public void Reset() {
            IsAllowed = false;
            ChangeAfter = 0;
            CooldownUntil = 0;
            PointAllowance = -1;
            Executed = 0;
            MaxEvents = 0;
            DifficultyMod = null;
        }
        public override string ToString() {
            return $"StorySchedule[{Outcome},Active:{IsAllowed},PA: {PointAllowance}/{MaxPointAllowance},ChangeIn:{Units.TicksToTime(ChangeAfter - G.Ticks)},Executed:{Executed}/{MaxEvents},Cycle#:{Cycle})";
        }
    }
}
