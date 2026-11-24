#if !PRODUCTION_BUILD
// #define DEBUG_CLOCK_SET_SPEED
#endif
using Game.UI;
using KL.Clock;
using Game.Commands;
using KL.Utils;
using Game.Constants;

namespace Game.Commands {
    public class CmdSetGameSpeed : Command {
        private ClockSpeed speed;
        private bool toggle;

        public CmdSetGameSpeed(ClockSpeed speed) : base("game_speed") {
            #if DEBUG_CLOCK_SET_SPEED
            D.Warn("Will set clock speed: {0}", speed);
            #endif
            toggle = false;
            this.speed = speed;
        }

        public CmdSetGameSpeed(int diff, ClockSpeed curSpeed) : base("game_speed") {
            #if DEBUG_CLOCK_SET_SPEED
            D.Warn("Will move clock speed: {0} -> {1}", curSpeed, diff);
            #endif
            toggle = diff == 0;
            if (toggle) { return; }
            if (diff > 0) {
                switch (curSpeed) {
                    case ClockSpeed.Stopped: speed = ClockSpeed.Normal; break;
                    case ClockSpeed.Normal: speed = ClockSpeed.Fast1; break;
                    case ClockSpeed.Fast1: speed = ClockSpeed.Fast2; break;
                    default: speed = ClockSpeed.Normal; break;
                }
            } else if (diff < 0) {
                switch (curSpeed) {
                    case ClockSpeed.Stopped: speed = ClockSpeed.Stopped; break;
                    case ClockSpeed.Normal: speed = ClockSpeed.Stopped; break;
                    case ClockSpeed.Fast1: speed = ClockSpeed.Normal; break;
                    case ClockSpeed.Fast2: speed = ClockSpeed.Fast1; break;
                    default: speed = ClockSpeed.Normal; break;
                }
            }
        }

        protected override void Execute() {
            if (toggle) {
                S.Clock.Toggle();
                UISounds.PlayClockChange(S.Clock.Speed);
            } else if (G.ClockSpeed != speed) {
                S.Clock.SetSpeed(speed);
                UISounds.PlayClockChange(S.Clock.Speed);
            }
        }
    }
}
