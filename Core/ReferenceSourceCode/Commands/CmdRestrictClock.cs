using Game.UI;
using KL.Clock;
using Game.Commands;
using KL.Utils;
using Game.Constants;
using Game.CodeGen;

namespace Game.Commands {
    public class CmdRestrictClock : Command {
        private readonly ClockSpeed speed;

        public CmdRestrictClock(ClockSpeed speed) : base("restrict_clock") {
            this.speed = speed;
        }

        protected override void Execute() {
            #if !PRODUCTION_BUILD
            if (The.TestsAreRunning) { return; }
            #endif
            if (A.InstantBuild) { return; }
            if (UIShowing.ScreenBlock) {
                // D.Warn("Will not restrict the clock, CPU is cooling down or screen block is showing");
                return;
            }
            if (!S.Prefs.GetBool(Pref.RestrictClockOnDisasters,
                    PrefH.RestrictClockOnDisasters, true)) {
                return;
            }
            // S.Clock.PreventMultiTick();
            // Restrict already lowers the current speed to the cap, and auto pausing is
            // handled by Clock.DangerPause, which the same callers invoke right after us.
            S.Clock.Restrict(speed, Consts.TicksPer5Minutes);
        }
    }
}
