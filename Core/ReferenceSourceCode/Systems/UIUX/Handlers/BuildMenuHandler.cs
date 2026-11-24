using System;
using Game.Data;

namespace Game.Systems.UIUX {
    public sealed class BuildMenuHandler : BaseWidgetHandler<BuildMenuWidget> {
        private readonly BuildMenuPicks picks = new();

        public BuildMenuHandler(GameState s, BuildMenuWidget widget) : base(s, widget) {
            widget.Initialize(s.Toolbox.BuildTool, picks);
            s.UIQuery.ShowBuildMenu.SetResponder(ShowMenu);
            s.UIQuery.ShowBuildMenuFor.SetResponder(ShowMenuFor);
            s.Sig.ResearchChanged.AddListener(OnResearchChanged);
            s.Sig.RebuildBuildMenu.AddListener(widget.Rebuild, widget.gameObject);
            The.SysSig.ResolutionChanged.AddListener(OnResolutionChange);
        }

        private void OnResearchChanged(Def def) {
            widget.Rebuild();
        }

        private void OnResolutionChange() {
            widget.CheckSize();
        }

        private bool ShowMenu(bool show) {
            widget.Toggle(show);
            return true;
        }

        private bool ShowMenuFor(Def def) {
            widget.ShowFor(def);
            return true;
        }
        public void Cleanup() {
            The.SysSig.ResolutionChanged.RemoveListener(OnResolutionChange);
        }

        public void OnSave(UIUXSysData data) {
            picks.OnSave(data);
        }

        public void OnLoad(UIUXSysData data) {
            picks.OnLoad(data);
        }

    }
}
