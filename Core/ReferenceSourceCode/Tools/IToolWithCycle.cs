namespace Game.Tools {
    public interface IToolWithCycle {
        /// <returns>next param or null if tool should be closed</returns>
        string CycleParam();
    }
}
