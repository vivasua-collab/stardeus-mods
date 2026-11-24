using KL.Signals;
using Game.Data;
using Game.Systems.Crafting;
using Game.Systems.Creatures;
using System.Reflection;
using Game.UI;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class UISig {
        /// <summary>
        /// bool is true = show modal block
        /// </summary>
        public Signal2<BeingData, bool> ShowBeingProfileUI = new(nameof(ShowBeingProfileUI), isOptional: false);
        /// <summary>
        /// Bool = is hovering now
        /// </summary>
        public Signal2<UISpaceObject, bool> OnHoverUISpaceObject = new(nameof(OnHoverUISpaceObject));
        public Signal1<BeingData> ShowingBeingPanel = new(nameof(ShowingBeingPanel), isOptional: true);
        /// <summary>
        /// Entity panel started showing a being that has no Being entity (expedition,
        /// stored). Null = it stopped showing it. Selection signals can't cover this,
        /// because nothing gets selected or deselected in that case.
        /// </summary>
        public Signal1<BeingData> DetachedBeingPanel = new(nameof(DetachedBeingPanel), isOptional: true);
        /// <summary>
        /// A new selection's actions took over the bottom bar: the one in-world entity they
        /// belong to (a tile as its parent), or null when the bar stopped showing the actions
        /// of a single entity (deselected, many selected, none applicable). Refreshes of the
        /// same selection send nothing.
        /// </summary>
        public Signal1<Entity> SelectionActionsShown = new(nameof(SelectionActionsShown), isOptional: true);
        public Signal1<bool> ToggleShipCombatMode = new(nameof(ToggleShipCombatMode));
        public Signal0 ReloadQuests = new(nameof(ReloadQuests));
        // The quest window shows something new to read (opened, reloaded, details shown)
        public Signal0 QuestsContentChanged = new(nameof(QuestsContentChanged), isOptional: true);
        public Signal1<CraftingOrder> CraftingOrderSelected = new(nameof(CraftingOrderSelected), isOptional: true);
        /// <summary>
        /// Screen space drag rect, same space as UIQuery.TrySelectBeingAvatars
        /// </summary>
        public Signal1<Rect> PreviewBeingAvatarSelection = new(nameof(PreviewBeingAvatarSelection), isOptional: true);

        public void ReleaseAll() {
            foreach (var field in GetType().GetFields(
                    BindingFlags.Public | BindingFlags.Instance)) {
                var sig = field.GetValue(this);
                if (sig is ISignal s) {
                    // D.Warn("Removing all listeners from game sig: {0}", s.Handle);
                    s.RemoveAllListeners();
                }
            }
        }
    }
}
