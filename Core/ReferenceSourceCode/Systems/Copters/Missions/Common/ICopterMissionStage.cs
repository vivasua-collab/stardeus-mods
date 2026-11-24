using Game.Data;

namespace Game.Systems.Copters {
    public interface ICopterMissionStage {
        int StageType { get; }
        // Progress will never update the miner deployment data, so we know that
        // the input is going to not change
        void TickProgress(GameState S, in CopterMissionData data);
        string TitleFor(GameState S, CopterMissionData data);
        bool TryTick(GameState S, long ticks, in CopterMissionData data,
            out CopterMissionData outData);
    }
}
