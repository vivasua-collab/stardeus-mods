using System.Text;

namespace Game.UI {
    public interface IDebugOutputProvider {
        public string DebugPanelTitle { get; }
        public bool ResetScrollOnRefresh => false;
        public bool OnTick(StringBuilder sb);
        /// <returns>true if we need to OnTick</returns>
        public bool EveryFrame();
    }
}
