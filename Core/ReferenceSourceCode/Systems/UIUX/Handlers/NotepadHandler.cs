using Game.Constants;
using Game.Data;

namespace Game.Systems.UIUX {
    public sealed class NotepadHandler : BaseWidgetHandler<NotepadWidget> {
        private string notes = string.Empty;
        private string noteToSelf;

        public NotepadHandler(GameState s, NotepadWidget widget) : base(s, widget) {
            widget.Initialize(s, notes, OnNoteEdit);
            s.UIQuery.ToggleNotes.SetResponder(ToggleNotes);
            s.UIQuery.SetNoteToSelf.SetResponder(t => {
                SetNoteToSelf(t);
                return true;
            });
        }

        private void OnNoteEdit(string text) {
            notes = text;
        }

        private bool ToggleNotes(bool show) {
            widget.Toggle(show);
            return true;
        }

        public void SetNoteToSelf(string text) {
            noteToSelf = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        public void OnSave(UIUXSysData data) {
            data.Notes = notes;
            data.NoteToSelf = noteToSelf;
        }

        public void OnLoad(UIUXSysData data) {
            notes = data?.Notes ?? string.Empty;
            widget.SetText(notes);

            var note = data?.NoteToSelf;
            noteToSelf = null; // one-shot: wiped so it never shows again or re-saves
            if (!string.IsNullOrEmpty(note)) {
                Ready.WhenGame(S, "NoteToSelf", () =>
                    WPopup.Create(false, IconId.CInfo, T.NoteToSelfTitle, null, note)
                        .Spawn(center: true));
            }
        }
    }
}
