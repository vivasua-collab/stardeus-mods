using System;
using Game.Constants;
using Game.Data;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class QuestHandler : BaseWidgetHandler<QuestWidget> {
        public QuestHandler(GameState s, QuestWidget widget) : base(s, widget) {
            widget.Initialize();
            widget.SetActive(false);
            The.SysSig.ResolutionChanged.AddListener(OnResChanged);
            The.SysSig.SettingChanged.AddListener(OnPrefChanged);
            S.UISig.ToggleShipCombatMode.AddListener(OnToggleShipCombat);
        }

        private void OnToggleShipCombat(bool on) {
            widget.SetForceHidden(on);
        }

        private void OnPrefChanged(string pref) {
            if (pref == Pref.GUIScale || pref == Pref.GUIAutoScale) {
                widget.OnResChanged();
            }
        }

        private void OnResChanged() {
            widget.OnResChanged();
        }

        public void Cleanup() {
            The.SysSig.ResolutionChanged.RemoveListener(OnResChanged);
            The.SysSig.SettingChanged.RemoveListener(OnPrefChanged);
        }
    }
}
