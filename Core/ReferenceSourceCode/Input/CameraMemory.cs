using System;
using Game.Constants;
using Game.Data;
using Game.UI;
using KL.Console;
using KL.Grid;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Input {
    public sealed class CameraMemory : MonoBehaviour {
        public CameraControls CamControls;
        public Action AfterChange;
        private void Start() {
            CamControls = GetComponent<CameraControls>();
        }
        private void Update() {
            var kc = Keyboard.current;
            if (kc == null || !Ready.TextInput || UIShowing.FullScreenWidget) { return; }
            else if (kc.numpad0Key.wasPressedThisFrame || kc.digit0Key.wasPressedThisFrame) { HandleNumpadPress(0); }
            else if (kc.numpad1Key.wasPressedThisFrame) { HandleNumpadPress(1); }
            else if (kc.numpad2Key.wasPressedThisFrame) { HandleNumpadPress(2); }
            else if (kc.numpad3Key.wasPressedThisFrame) { HandleNumpadPress(3); }
            else if (kc.numpad4Key.wasPressedThisFrame) { HandleNumpadPress(4); }
            else if (kc.numpad5Key.wasPressedThisFrame || kc.digit5Key.wasPressedThisFrame) { HandleNumpadPress(5); }
            else if (kc.numpad6Key.wasPressedThisFrame || kc.digit6Key.wasPressedThisFrame) { HandleNumpadPress(6); }
            else if (kc.numpad7Key.wasPressedThisFrame || kc.digit7Key.wasPressedThisFrame) { HandleNumpadPress(7); }
            else if (kc.numpad8Key.wasPressedThisFrame || kc.digit8Key.wasPressedThisFrame) { HandleNumpadPress(8); }
            else if (kc.numpad9Key.wasPressedThisFrame || kc.digit9Key.wasPressedThisFrame) { HandleNumpadPress(9); }
        }

        public void HandleNumpadPress(int num) {
            if (UIShowing.FullScreenWidget) { return; }
            if (!Ready.TextInput) { return; }
            if (!Ready.TextInputFocus) { return; }
            if (InGameConsole.IsVisible) { return; }
            #if UNITY_EDITOR
            // spajus: I use another software that uses ctrl + numpad
            if (The.Bindings.IsPressed(ActionType.CtrlModifier)
                && The.Bindings.IsPressed(ActionType.ShiftModifier)) {
            #else
            if (The.Bindings.IsPressed(ActionType.CtrlModifier)) {
            #endif
                SaveSlot(num);
            } else {
                TryJumpTo(num);
            }
        }

        private string KeyFor(int num, string key) {
            return string.Intern($"cam.mem.{num}.{key}");
        }

        public void DeleteSlot(int num) {
            var prefs = The.R.State.Prefs;
            prefs.UnsetFloat(KeyFor(num, "x"));
            prefs.UnsetFloat(KeyFor(num, "y"));
            prefs.UnsetFloat(KeyFor(num, "z"));
            AfterChange?.Invoke();
            UISounds.PlayDigitalConfirm();
        }

        public void SaveSlot(int num) {
            var prefs = The.R.State.Prefs;
            var camPos = CamControls.PrecisePosition;
            prefs.SetFloat(KeyFor(num, "x"), camPos.x);
            prefs.SetFloat(KeyFor(num, "y"), camPos.y);
            prefs.SetFloat(KeyFor(num, "z"), CamControls.PreciseOrtho);
            AfterChange?.Invoke();
            var msg = $"{T.CodexMUiCameramemory}: {GlyphMap.KB($"numpad{num}")} {GlyphMap.ArrowRight} {Pos.String(EntityUtils.ToPos(camPos))}";
            The.R.Sig.ShowFanfare.Send(msg);
            The.R.Sys.Log.AddLine(msg);
            UISounds.PlayDigitalConfirm();
        }

        public bool TryGetSlot(int num, out float x, out float y, out float z) {
            var prefs = The.R.State.Prefs;
            x = prefs.GetFloatAutoHash(KeyFor(num, "x"), -9999f);
            if (x < -9998f) {
                y = z = 0f;
                return false;
            }
            y = prefs.GetFloatAutoHash(KeyFor(num, "y"), 0f);
            z = prefs.GetFloatAutoHash(KeyFor(num, "z"), 10f);
            return true;
        }

        public void TryJumpTo(int num) {
            var prefs = The.R.State.Prefs;
            var x = prefs.GetFloatAutoHash(KeyFor(num, "x"), -9999f);
            if (x < -9998f) {
                UISounds.PlayDigitalCancel();
                return;
            }
            var y = prefs.GetFloatAutoHash(KeyFor(num, "y"), 0f);
            var z = prefs.GetFloatAutoHash(KeyFor(num, "z"), 10f);

            The.R.State.Sig.CameraFocus.Send(new(x, y), Consts.V2Zero,
                z * 2);
            UISounds.PlayHover(UISounds.HoverType.Lowest);
        }

        public string GetSlot(int num) {
            var prefs = The.R.State.Prefs;
            var x = prefs.GetFloatAutoHash(KeyFor(num, "x"), -9999f);
            if (x < -9998f) {
                return $"[{num}] Empty";
            }
            var y = prefs.GetFloatAutoHash(KeyFor(num, "y"), 0f);
            var z = prefs.GetFloatAutoHash(KeyFor(num, "z"), 10f);

            return string.Format("[{0}] {1:0}:{2:0} z:{3:0}",
                num, x, y, z);
        }
    }
}
