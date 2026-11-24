using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Utils;
using Game.Systems.Creatures;

namespace Game.Systems.Mental {
    // Skill decay and the daily learning cap, the two brakes on skills. Without decay a
    // skill once raised stays there forever, and with skill now moving hit chance and work
    // speed a lot, a colony would only ever grow stronger. The cap keeps one long session
    // (a siege, a day at the workbench) from jumping several levels. Both counters live on the
    // JobSlot, so this system saves nothing of its own.
    public sealed class SkillUpkeepSys : GameSystem {
        public const string SysId = "SkillUpkeepSys";
        public override string Id => SysId;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new SkillUpkeepSys());
        }

        // off the hour, so this does not share a tick with the other hourly sweeps
        private const int HourTickOffset = 23;

        private Curve decayPerDay;

        protected override void OnInitialize() {
            S.Sig.AfterLoadState.AddListener(OnLoadState);
        }

        private void OnLoadState(GameState state) {
            state.Clock.OnTick.AddListener(OnTick);
        }

        public override void Unload() {
            decayPerDay = null;
        }

        private void OnTick(long ticks) {
            if (ticks % Consts.TicksPerHour != HourTickOffset) { return; }
            bool newDay = ticks % Consts.TicksPerDay == HourTickOffset;
            decayPerDay ??= CurveDef.FastGet(CurveIdH.Skills_DecayPerDay);
            foreach (var data in S.Query.AllBeingData.Ask().Values) {
                var jobs = data?.Mind?.Jobs;
                if (jobs == null) { continue; }
                // the dead, sleepers in stasis and those gone to a colony do not forget: a
                // sleeper would otherwise wake up days later without its best skills
                bool forgets = data.HasStateFlag(BeingState.Alive)
                    && !data.HasStateFlag(BeingState.Stored)
                    && !data.HasStateFlag(BeingState.InColony);
                foreach (var slot in jobs.JobSlots.Values) {
                    if (newDay) {
                        slot.XpToday = 0f;
                    }
                    if (!forgets || slot.Level < 10) { continue; }
                    slot.Forget(decayPerDay.Evaluate(slot.Level) / 24f);
                }
            }
        }
    }
}
