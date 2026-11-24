using System;
using Game.Components;
using KL.Clock;

namespace Game.Data {
    public interface ITickListener {
        void Tick(long currentTick);
    }
    public interface IRareTickListener {
        void RareTick(long currentTick);
    }
}
