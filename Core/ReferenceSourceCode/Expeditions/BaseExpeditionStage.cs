using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Systems.UIUX;
using KL.Utils;
using UnityEngine;

namespace Game.Expeditions {
    public abstract class BaseExpeditionStage : IExpeditionStage {
        public abstract int StageType { get; }
        public virtual bool CanCallBack(Expedition exp) {
            return true;
        }
        public virtual bool IsStageCancellable => false;
        public virtual bool IsReturning => false;

        public virtual bool TryCancelStage(out int nextStage) {
            nextStage = ExpeditionStageTypeH.Done;
            return false;
        }

        public abstract string Title { get; }
        public virtual string TitleNotification => Title;
        public virtual string TitleTooltip => null;
        public virtual B<string> TitleB => null;
        protected readonly B<string> subtitle = new(null);
        public B<string> Subtitle => subtitle;
        // Text should be very short!
        // Mining resource stage should show mined amount only (shuttle storage used capacity)
        // Then various stages like takeoff, landing, flying, returning, exploring should just show one short word
        public readonly B<string> MiniWidgetText = new(null);
        /// <summary>
        /// Icon key for the status indicator. Override to return an IconId constant per stage type.
        /// E.g. IconId.CPickaxe for mining, IconId.CPropulsion for flight, IconId.CMagnifier for exploring.
        /// </summary>
        public virtual string MiniWidgetStatusIcon => null;
        /// <summary>
        /// When true the mini expedition widget shows a storage fill progress bar
        /// bound to the shuttle's MaterialStorage. Only MineResource overrides this to true.
        /// </summary>
        public virtual bool MiniWidgetShowsStorage => false;

        public virtual void Abort(Expedition exp) {
            // D.Log("Aborting: {0}", exp);
        }
        public abstract IExpeditionStage Tick(Expedition exp, long ticks);

        protected SpaceObject PlayerSO(GameState s) {
            return s.Universe.Player;
        }
    }
}
