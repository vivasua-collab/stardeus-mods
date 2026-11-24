namespace Game.Systems.UIUX {
    // An overlay's legend. It docks above the overlay buttons instead of opening in the
    // screen centre, where it covered the map the overlay is there to show (STA-1626).
    // ShowInScreenCenter still applies when the overlay buttons are not on screen.
    public interface IOverlayLegendPopup : IContextPopup {
    }
}
