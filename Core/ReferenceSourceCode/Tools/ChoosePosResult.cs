namespace Game.Tools {
    public struct ChoosePosResult {
        public static readonly ChoosePosResult Success = new() { ClearTool = true, IsSuccess = true, };
        public static readonly ChoosePosResult Fail = new() { ClearTool = false, IsSuccess = false, };
        public static readonly ChoosePosResult SuccessDontClear = new() { ClearTool = false, IsSuccess = true, };
        public bool IsSuccess;
        public bool ClearTool;
    }
}
