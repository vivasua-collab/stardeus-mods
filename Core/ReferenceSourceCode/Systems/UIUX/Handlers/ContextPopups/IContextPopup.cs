using Game.Utils;

namespace Game.Systems.UIUX {
    public interface IContextPopup {
        /// <summary>
        /// If this is not zero, when showing a popup, any other popup with same hash will get closed.
        /// Example use: space objects are all individual popup loaders, but we only want to show one
        /// popup at a time for them.
        /// </summary>
        int UniquePopupHash { get; }
        /// <summary>
        /// If this is false, the popup will be located near the mouse cursor.
        /// </summary>
        bool ShowInScreenCenter { get; }
        /// <summary>
        /// Implement this method to load the tooltip contents
        /// </summary>
        /// <param name="lifetime">lifetime for the popup content</param>
        /// <param name="wContent">content container to load to</param>
        /// <param name="wFooter">footer container to load to</param>
        /// <returns>Popup title</returns>
        string LoadPopupContent(Lifetime lifetime, WGroup wContent, WGroup wFooter);
        void OnPopupClosed();
        void OnPopupOpened();
    }
}
