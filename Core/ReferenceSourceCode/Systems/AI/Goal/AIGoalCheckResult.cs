namespace Game.Systems.AI {
    public struct AIGoalCheckResult {
        public bool IsDoable;
        public AIGoalState WantedState;
        public string Reason;
        public bool IsRetryable;
        public string Icon;
        /// <summary>
        /// If this is 0, it will be automatic
        /// </summary>
        public int RetryAfterTicks;
        // public readonly static AIGoalCheckResult FailWithoutRetry = new AIGoalCheckResult() {
        //     IsDoable = false,
        //     IsRetryable = false,
        //     WantedState = AIGoalState.Failed,
        // };
        // privte readonly static AIGoalCheckResult FailWithDefaultRetry = new AIGoalCheckResult() {
        //     IsDoable = false,
        //     IsRetryable = true,
        //     WantedState = AIGoalState.Failed,
        // };
        public readonly static AIGoalCheckResult Doable = new AIGoalCheckResult() {
            IsDoable = true
        };
        public static AIGoalCheckResult Fail(string reason, bool retry, string icon = null,
                int retryAfterTicks = 0) {
            return new AIGoalCheckResult() {
                IsDoable = false,
                IsRetryable = retry,
                WantedState = AIGoalState.Failed,
                Reason = reason,
                Icon = icon,
                RetryAfterTicks = retryAfterTicks,
            };
        }
        public static AIGoalCheckResult Error(string reason) {
            return new AIGoalCheckResult() {
                IsDoable = false,
                IsRetryable = false,
                WantedState = AIGoalState.Error,
                Reason = reason,
            };
        }
        public readonly static AIGoalCheckResult Completed = new AIGoalCheckResult() {
            IsDoable = false,
            IsRetryable = false,
            WantedState = AIGoalState.Completed,
        };
    }
}
