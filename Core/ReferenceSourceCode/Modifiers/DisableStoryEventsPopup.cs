using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Modifiers;
using Game.Systems.Story;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Modifiers {
    public sealed class DisableStoryEventsPopup : IContextPopup {
        private static readonly int popupHash = Hashes.S("StoryEventsPopup");
        public int UniquePopupHash => popupHash;
        public bool ShowInScreenCenter => true;

        private readonly bool inGame;
        private readonly Action onChanged;

        public DisableStoryEventsPopup(bool inGame, Action onChanged) {
            this.inGame = inGame;
            this.onChanged = onChanged;
        }

        public void OnPopupOpened() { }
        public void OnPopupClosed() { }

        public string LoadPopupContent(Lifetime lifetime, WGroup wContent, WGroup wFooter) {
            var life = lifetime.CreateChild("StoryEvents");
            var mod = ModifierDisableStoryEvents.Get;

            var block = UISpawn.Block(life, wContent, 0);
            int idx = 0;

            var toggles = new List<WToggleText>();

            foreach (var e in StoryEventDef.Ordered) {
                var capturedE = e;
                var toggle = UISpawn.Widget<WToggleText>(life,
                    UIPrefabId.WContent2Toggle, block.Container, idx++);
                toggle.Load(e.Id, !mod.IsDisabled(e, inGame), (v) => {
                    mod.SetDisabled(capturedE, !v, inGame);
                    onChanged?.Invoke();
                });
                toggles.Add(toggle);
                UIUtils.AddTipFunc(life, toggle, () => capturedE.Tooltip);
            }

            var resetBtn = UISpawn.Widget<WButtonText>(life,
                UIPrefabId.WPanelButton, wFooter.Container, 0);
            resetBtn.Load(T.Reset, () => {
                mod.Reset(null);
                foreach (var toggle in toggles) {
                    toggle.SetValue(true, false);
                }
                onChanged?.Invoke();
            });
            wFooter.SetActiveFast(true);

            return T.SituationStoryevents;
        }
    }
}
