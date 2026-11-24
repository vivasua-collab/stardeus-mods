using Game.CodeGen;
using Game.Constants;
using Game.Input;
using KL.Utils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game.UI {
    public class UIScrollSettings : MonoBehaviour, IPointerEnterHandler {
        [SerializeField] private bool isHorizontal;
        private ScrollRect scroll;
        private void Start() {
            scroll = GetComponent<ScrollRect>();
            UpdateScrollSettings();
        }
        private void OnPrefChange(string name) {
            if (name == Pref.GUIScrollInvert || name == Pref.GUIScrollSpeed) {
                UpdateScrollSettings();
            }
        }

        private void OnMouseSwitched() {
            UpdateScrollSettings();
        }

        private void UpdateScrollSettings() {
            if (scroll == null) { return; }
            var baseSpeed = VirtualMouseInput.IsLastUsedMouseVirtual
                ? Tunables.UGUIScrollSpeed_Gamepad
                : Tunables.UGUIScrollSpeed;
            var speed = The.Prefs.GetFloat(Pref.GUIScrollSpeed, PrefH.GUIScrollSpeed, Consts.DefaultUIScrollSpeed);
            var speedAdj = baseSpeed * speed;
            if (Tunables.UGUIScrollSpeedMax > 0f && speedAdj > Tunables.UGUIScrollSpeedMax) {
                speedAdj = Tunables.UGUIScrollSpeedMax;
            }
            var invert = The.Prefs.GetBool(Pref.GUIScrollInvert, PrefH.GUIScrollInvert, Consts.DefaultUIScrollInverted);
            if (invert) {
                speedAdj *= -1f;
            }
            // horizontal scrolls are inverted by default
            if (isHorizontal) {
                speedAdj *= -1f;
            }
            scroll.scrollSensitivity = speedAdj;
        }

        private void OnEnable() {
            if (The.SysSig == null) {
                D.Err("SysSig is null when enabling UIScrollSettings! {0}",
                    D.GOPath(transform));
            } else {
                The.SysSig.SettingChanged.AddListener(OnPrefChange, gameObject);
                The.SysSig.MouseSwitched.AddListener(OnMouseSwitched, gameObject);
            }
        }

        private void OnDisable() {
            The.SysSig?.SettingChanged.RemoveListener(OnPrefChange);
            The.SysSig?.MouseSwitched.RemoveListener(OnMouseSwitched);
        }

        public void OnPointerEnter(PointerEventData eventData) {
            MouseUtils.SetCurrentMouse(Mouse.current);
        }
    }
}
