using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;

namespace Game.Expeditions {
    public interface IExpeditionStage {
        IExpeditionStage Tick(Expedition exp, long ticks);
        bool CanCallBack(Expedition exp);
        bool IsStageCancellable { get; }
        int StageType { get; }
        bool TryCancelStage(out int nextStage);
        bool IsReturning { get; }
        void Abort(Expedition exp);
        string Title { get; }
        string TitleNotification { get; }
        B<string> TitleB { get; }
        B<string> Subtitle { get; }
        string TitleTooltip { get; }
    }
}
