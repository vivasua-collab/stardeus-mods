using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Data.Economy;
using Game.Systems.Creatures;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class WakeUpChoiceWidget : BaseWidget {
        [SerializeField] private WGroup container;
        [SerializeField] private ScrollRect scroll;
        [SerializeField] private WText header;
        public bool IsShowing => gameObject.activeSelf;
        private Action<BeingData> onChoose;
        private Lifetime lifetime;
        public void Initialize() {
            Register(UIWidgetId.WakeUpChoice, this);
            container.DestroyChildren();
            header.Load(T.ChooseWhoAwakens);
            Toggle(false);
        }

        public bool Show(GameState s, List<BeingData> options, Action<BeingData> onChoose) {
            if (IsShowing) { return true; }
            if (!s.UIQuery.ShowPanelModalFor.Ask(this, true)) {
                // another modal is showing!
                return false;
            }
            if (lifetime != null) {
                lifetime.Dispose();
            }
            lifetime = Lifetime.Create("WakeUpChoiceWidget");
            // Nobody woken yet in this game: this is the first one, and it says so
            header.Load(ManifestSys.For(s).WokenCount == 0
                ? T.StasisFirstWakeupChoose : T.ChooseWhoAwakens);
            Toggle(true);
            s.UIQuery.ToggleGameUI.Ask(false, false);
            var prefab = ResourceLibrary.FindPrefab<WakeUpBeingOptionWidget>(
                UIPrefabId.WakeUpBeingOptionWidget);
            int idx = 0;
            this.onChoose = onChoose;
            foreach (var opt in options) {
                var w = UISpawn.Widget(lifetime, prefab, container.Container, idx++);
                w.Load(lifetime, opt, OnChoose);
            }
            return true;
        }

        private void OnChoose(BeingData data) {
            onChoose.Invoke(data);
            Toggle(false);
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
            A.S.UIQuery.ToggleGameUI.Ask(true, false);
            A.S.UIQuery.ShowPanelModalFor.Ask(this, false);
        }
    }
}
