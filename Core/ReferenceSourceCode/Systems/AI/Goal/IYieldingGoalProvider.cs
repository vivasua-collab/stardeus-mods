namespace Game.Systems.AI {
    // An entity has a single goal slot shared by all of its components. A goal that can
    // wait for materials indefinitely (an ordered upgrade) must not starve a goal that
    // keeps the device running (refuelling a reactor). Components whose goal can safely
    // wait implement this so a more urgent component can borrow the slot and hand it
    // back afterwards, without the waiting component losing its pending work.
    public interface IYieldingGoalProvider : IAIGoalProvider {
        // Gives up the entity goal slot if our goal is starved. Must keep the pending
        // work so ResumeAfterYield can recreate the goal. Returns false when our goal
        // is making progress or could make progress, and the caller has to wait.
        bool TryYieldGoalSlot();
        // Called after the borrowing goal ended. Recreates our goal if there is still
        // pending work and the slot is free.
        void ResumeAfterYield();
    }
}
