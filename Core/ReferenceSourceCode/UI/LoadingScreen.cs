using System;
using Game.Input;
using Game.Systems.UIUX;
using Game.Utils;
using KL.Utils;
using KL.UweenExt;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Uween;

namespace Game.UI {
    public sealed class LoadingScreen : MonoBehaviour {
        private const int loadStagesCore = 7;
        private const int loadStagesBase = 14;
        // UIShowing.LoadingScreen is unset when this fade starts, not when it ends
        public const float FadeOutSeconds = 0.5f;
        [SerializeField] private CanvasGroup cg;
        [SerializeField] private RectTransform popupContainer;
        [SerializeField] private WProgressText progress;
        [SerializeField] private GameObject btnFeedback;
        private TweenCGA tween;
        private double loadStart;
        private float loadStageAdd;
        private int loadStage;
        private int loadStagesExtra = 0;
        private float lastProgress;
        private string lastLoadMsg;
        [ExecuteOnReload]
        private static void ClearStatics() {
            instance = null;
        }
        private static LoadingScreen instance;

        private void Awake() {
            if (instance != null) {
                DestroyImmediate(gameObject);
                return;
            }
            if (transform.parent == null) {
                DontDestroyOnLoad(this);
            }
            instance = this;
            D.Warn("Setting up LoadingScreen: {0}", gameObject.GetEntityId());
            loadStart = Time.realtimeSinceStartupAsDouble;
            progress.Load("Initializing", 0f);
            btnFeedback.SetActiveFast(false);
            cg.alpha = 1;
            UIShowing.Set(UIShowingFlags.LoadingScreen);
            The.SysSig.ShowLoadingScreen.AddListener(OnShowLoadingScreen, gameObject);
            The.SysSig.LoadingMsg.AddListener(OnShowLoadingMsg, gameObject);
            The.SysSig.AdvanceLoadingStage.AddListener(AdvanceStage, gameObject);
            The.SysSig.AddLoadingStages.AddListener(AddStages, gameObject);
            SceneManager.activeSceneChanged += OnSceneChange;
        }

        private void OnDestroy() {
            SceneManager.activeSceneChanged -= OnSceneChange;
        }

        private void OnSceneChange(Scene from, Scene to) {
            GetComponent<AudioListener>().enabled = to.name != "Game";
        }

        private void AddStages(int cnt) {
            loadStagesExtra += cnt;
            D.Warn("Adding {0} loading stages, now {1}+{2}", cnt,
                loadStagesBase, loadStagesExtra);
        }

        private void OnShowLoadingMsg(string msg) {
            lastLoadMsg = msg;
            UpdateLoadMsg();
        }

        private void UpdateLoadMsg() {
            progress.SetText(string.Format("{0:0}s / {1}",
                Time.realtimeSinceStartupAsDouble - loadStart, lastLoadMsg));
        }

        private void AdvanceStage() {
            // D.Warn("Advancing stage: {0}", loadStage);
            loadStage++;
            loadStageAdd = 0f;
            if (loadStage <= loadStagesBase + loadStagesExtra) {
                return;
            }
            D.ErrSoft("Load stage exceeded total! Register extra stages! Stage: {0}. Total: {1}. Extra: {2}",
                loadStage, loadStagesBase, loadStagesExtra);
        }

        private void Update() {
            CheckFeedbackButton();
            var loadProgress = (loadStage + loadStageAdd) / (loadStagesBase + loadStagesExtra);
            if (lastProgress < loadProgress) {
                if (loadStageAdd < 0.3f) {
                    loadStageAdd += Mathf.Max(0.01f, Mathf.Clamp(Time.unscaledDeltaTime * 0.01f, 0f, 0.03f));
                }
                lastProgress = loadProgress;
                progress.SetValue(loadProgress);
            } else {
                loadStageAdd = 0f;
                // D.Warn("Last load progress greater! keeping: {0} > {1}",
                // lastProgress, loadProgress);
            }
            UpdateLoadMsg();
            // D.Warn("Load prg: {0} + {1} / {2} = {3}",
            //     loadStage, loadStageAdd, loadStagesTotal, loadProgress);
        }

        private void CheckFeedbackButton() {
            if (GameAnalytics.IsWaitingForConsentChoice) { return; }
            if (btnFeedback.gameObject.activeSelf) { return; }
            bool wantsActive = false;
            if (Keyboard.current != null) {
                if (Keyboard.current.anyKey.wasPressedThisFrame) {
                    wantsActive = true;
                }
            }
            if (Mouse.current != null) {
                if (Mouse.current.leftButton.wasPressedThisFrame) {
                    wantsActive = true;
                } else if (Mouse.current.rightButton.wasPressedThisFrame) {
                    wantsActive = true;
                }
            }
            if (wantsActive) {
                btnFeedback.SetActiveFast(true);
                VirtualMouseInput.Instance.CenterCursorIfUnmoved();
            }
        }

        private void OnShowLoadingScreen(string text, bool show) {
            if (tween != null) {
                if (!tween.IsComplete) {
                    tween.Skip();
                }
                tween = null;
            }
            if (show) {
                btnFeedback.SetActiveFast(false);
                loadStart = Time.realtimeSinceStartupAsDouble;
                UIUtils.ToggleCG(cg, true);
                gameObject.SetActiveFast(true);
                UIShowing.Set(UIShowingFlags.LoadingScreen);
            } else {
                var sysCont = GameObject.FindWithTag("SystemPopupContainer");
                if (sysCont != null && sysCont.TryGetComponent<RectTransform>(out var sysContRT)) {
                    MiscUtils.TransferChildren(popupContainer, sysContRT);
                }
                UIShowing.Unset(UIShowingFlags.LoadingScreen);
                // It sorts above the main menu: fading, it would still take clicks meant for it
                cg.interactable = false;
                cg.blocksRaycasts = false;
                tween = TweenCGA.Add(gameObject, FadeOutSeconds, 0f).Then(Hide);
            }
        }

        private void OnApplicationFocus(bool hasFocus) {
            if (!hasFocus && gameObject.activeSelf && cg.alpha > 0) {
                Application.runInBackground = true;
            }
        }

        private void Hide() {
            var totalStages = loadStagesBase + loadStagesExtra;
            if (loadStage > totalStages) {
                D.ErrSoft("Loading stage {0} > total {1}+{2}",
                    loadStage, loadStagesBase, loadStagesExtra);
            }
            D.Log("Hiding the loading screen");
            gameObject.SetActiveFast(false);
            UIUtils.ToggleCG(cg, false);
            lastProgress = 0f;
            loadStage = 0;
            loadStageAdd = 0f;
            // After core is loaded, we'll never want to load all steps again
            loadStagesExtra = -loadStagesCore;
            progress.SetValue(0f);
            VirtualMouseInput.Instance.CenterCursorIfUnmoved();
            Bootstrap.RestoreBackgroundChoice();
        }
    }
}
