using Game.Data;
using Game.Systems;
using Game.Commands;
using Game.Tools;
using Game.Systems.UIUX;

namespace Game {
    /// <summary>
    /// When game session is destroyed, the whole runtime goes down.
    /// Except for anything stored in static variables, that's why those
    /// have to be avoided, or if unavoidable, meticulously cleaned up
    /// in between sessions. Otherwise there will be bugs / memory leaks.
    /// </summary>
    public sealed class GameRuntime {
        public bool IsCurrent;
        public GameSystems Sys;
        public GameSig Sig;
        public GameQuery Query;
        public UISig UISig;
        public UIQuery UIQuery;
        public GameState State;
        public CommandQueue CmdQ;
        public ActionQueue ActQ;
        public Toolbox Toolbox;

        public GameRuntime() {
            A.HasRuntime = true;
            Sig = new GameSig();
            UISig = new UISig();
            UIQuery = new UIQuery();
            Query = new GameQuery();
            Sys = new GameSystems();
            State = new GameState();
            A.S = State;
            CmdQ = new CommandQueue();
            ActQ = new ActionQueue();
            Toolbox = new Toolbox();
            State.Sys = Sys;
            State.Sig = Sig;
            State.UISig = UISig;
            State.UIQuery = UIQuery;
            State.Query = Query;
            EntityUtils.S = State;
            State.CmdQ = CmdQ;
            State.ActQ = ActQ;
            CmdQ.S = State;
            State.SetRuntime(this);
            Toolbox.Initialize(State);
        }

        public void Unload() {
            Sys = null;
            ActQ = null;
            Sig = null;
            UISig = null;
            UIQuery = null;
            Query = null;
            State = null;
            CmdQ = null;
            Toolbox = null;
            IsCurrent = false;
        }
    }
}
