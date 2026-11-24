using System;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class NotepadWidget : BaseWidget, ICloseableWidget {
        [SerializeField] private WButtonText header;
        [SerializeField] private WInput input;

        public Action<string> OnEdit;

        private void Awake() {
            header.Load(T.OverlayTipNotes, Close);
            input.Load(null, OnChangeNotes);
            Close();
        }

        public void Initialize(GameState s, string text, Action<string> onEdit) {
            Register(UIWidgetId.Notes, this);
            input.SetValue(text, false);
            input.InputField.onSelect.AddListener(OnSelectInput);
            OnEdit = onEdit;
        }

        private void OnSelectInput(string _) {
            Rect.SetAsLastSibling();
        }

        private void Close() {
            Toggle(false);
        }

        public override void Toggle(bool show) {
            UIShowing.Set(UIShowingFlags.Notes, show);
            base.Toggle(show);
            if (show) {
                Rect.SetAsLastSibling();
            }
        }

        public void SetText(string text) {
            input.SetValue(text, false);
        }

        private void OnChangeNotes(string text) {
            OnEdit(text);
        }

        public bool TryCloseWidget() {
            Close();
            return true;
        }
    }
}
