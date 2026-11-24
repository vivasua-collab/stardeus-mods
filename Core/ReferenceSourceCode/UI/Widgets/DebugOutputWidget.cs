using System;
using System.Text;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Game.Systems.UIUX;
using Game.Systems.AI;

namespace Game.UI {
    public sealed class DebugOutputWidget : BaseWidget, IPoolableSingleton,
            ICustomPoolable, IPointerEnterHandler, IPointerExitHandler {
        [SerializeField] private WButton closeButton;
        [SerializeField] private WInput textArea;
        [SerializeField] private TMP_InputField textAreaInput;
        private UIResizeHandle resizeHandle;
        private UIDraggable draggable;
        private RectTransform rekt;
        private GameState S;
        private IDebugOutputProvider provider;
        private readonly StringBuilder sb = new();
        private bool isMouseOver;
        private void Awake() {
            resizeHandle = GetComponentInChildren<UIResizeHandle>();
            resizeHandle.OnResize = OnResize;
            draggable = GetComponentInChildren<UIDraggable>();
            draggable.OnDragged = OnDragged;
            rekt = GetComponent<RectTransform>();
            var uiScale = Video.GetCurrentUIScale(out _);
            rekt.anchoredPosition = new Vector2(Screen.width / 2f, Screen.height / -2f)
                + new Vector2(rekt.sizeDelta.x * -0.5f, rekt.sizeDelta.y * 0.5f) * uiScale;
            S = A.S;
        }

        private void OnDragged(Vector2 vector) {
            SavePosition();
        }

        private void Start() {
            LoadPosition();
        }

        private void OnResize(RectTransform transform) {
            SavePosition();
        }

        private void Update() {
            if (!isMouseOver) { return; }
            var k = Keyboard.current;
            if (!k.ctrlKey.IsPressed()) { return; }
            if (k.numpadPlusKey.wasPressedThisFrame) {
                textAreaInput.pointSize += 1;
                SavePosition();
            } else if (k.numpadMinusKey.wasPressedThisFrame) {
                textAreaInput.pointSize -= 1;
                SavePosition();
            }
        }

        public static void SpawnFor(IDebugOutputProvider prov) {
            if (prov == null) {
                D.Err("DebugOutput: Cannot spawn for null provider!");
                return;
            }
            var prefab = ResourceLibrary.FindPrefab<DebugOutputWidget>();
            var w = PrefabPool.Spawn(prefab, UIPoolRoot.Container, preInstantiate: true);
            w.Show(prov);
        }

        private void OnTick(long tick) {
            if (provider == null) {
                D.Err("DebugOutput: Provider is null on tick! Should be unsubscribed!");
                return;
            }
            provider.OnTick(sb);
            textAreaInput.SetTextWithoutNotify(sb.ToString());
        }

        private void EveryFrame() {
            if (provider == null) { return; }
            if (provider.EveryFrame()) {
                var needScroll = provider.OnTick(sb);
                D.Warn("Setting debug output: {0}", sb.ToString());
                sb.Append("\n<style=Hint>Press CTRL and numpad +/- above the widget to adjust the font size</style>");
                textAreaInput.SetTextWithoutNotify(sb.ToString());
                if (needScroll && provider.ResetScrollOnRefresh) {
                    textAreaInput.verticalScrollbar.value = 0;
                };
            }
        }

        private void Show(IDebugOutputProvider prov) {
            try {
                textArea.Load(prov.DebugPanelTitle ?? "Debug Panel", OnChange);
                if (provider == null) {
                    S.Sig.EveryFrame.AddListener(EveryFrame, gameObject);
                    S.Clock.OnTick.AddListener(OnTick, gameObject);
                }
                provider = prov;
                closeButton.Load(Hide);
                Toggle(true);
                EveryFrame();
            } catch (Exception ex) {
                D.LogEx(ex, "Failed showing debug output for {0}", prov);
                Toggle(false);
            }
        }

        private void OnChange(string _) {
        }

        private new void Toggle(bool on) {
            if (on) {
                gameObject.SetActiveFast(true);
                UIUtils.AfterShowPopup(Rect, true, randShift: false);
            } else {
                sb.Clear();
                S.Sig.EveryFrame.RemoveListener(EveryFrame);
                S.Clock.OnTick.RemoveListener(OnTick);
                provider = null;
                if (gameObject.activeSelf) {
                    PrefabPool.Despawn(gameObject);
                }
            }
        }

        private void OnDestroy() {
            if (provider != null) {
                S?.Clock?.OnTick.RemoveListener(OnTick);
                S?.Sig?.EveryFrame.RemoveListener(EveryFrame);
                provider = null;
            }
            sb.Clear();
            S = null;
        }

        private void Hide() {
            SavePosition();
            PrefabPool.Despawn(gameObject);
        }

        private void SavePosition() {
            var sd = rekt.sizeDelta;
            var pos = rekt.anchoredPosition;
            CachedPlayerPrefs.SetFloat("debug.wgt.size.x", sd.x);
            CachedPlayerPrefs.SetFloat("debug.wgt.size.y", sd.y);
            CachedPlayerPrefs.SetFloat("debug.wgt.pos.x", pos.x);
            CachedPlayerPrefs.SetFloat("debug.wgt.pos.y", pos.y);
            CachedPlayerPrefs.SetFloat("debug.wgt.fnt.size", textAreaInput.pointSize);
        }

        private void LoadPosition() {
            var sd = rekt.sizeDelta;
            var pos = rekt.anchoredPosition;
            var sdx =  CachedPlayerPrefs.GetFloat("debug.wgt.size.x", sd.x);
            var sdy = CachedPlayerPrefs.GetFloat("debug.wgt.size.y", sd.y);
            var posx = CachedPlayerPrefs.GetFloat("debug.wgt.pos.x", pos.x);
            var posy = CachedPlayerPrefs.GetFloat("debug.wgt.pos.y", pos.y);
            textAreaInput.pointSize = CachedPlayerPrefs.GetFloat("debug.wgt.fnt.size", 12f);
            rekt.sizeDelta = new Vector2(sdx, sdy);
            rekt.anchoredPosition = new Vector2(posx, posy);
        }

        public void PoolDeactivate() {
            gameObject.SetActiveFast(false);
            isMouseOver = false;
        }

        public void PoolActivate() {
            gameObject.SetActiveFast(true);
        }

        public void OnPointerExit(PointerEventData eventData) {
            isMouseOver = false;
        }

        public void OnPointerEnter(PointerEventData eventData) {
            isMouseOver = true;
        }
    }
}
