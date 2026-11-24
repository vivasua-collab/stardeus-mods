namespace Game.UI {
    public interface IReactsToScroll {
        public bool Scroll(float delta);
        // In starmap we want to pass through when we scroll the widget,
        // so we can zoom the starmap. same with research
        public bool PassThroughZoomOnScroll { get; }
    }
}
