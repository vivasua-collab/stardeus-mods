using System;
using System.Text;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Research;
using Game.Tools;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.UI;
using KL.Utils;
using KL.UweenExt;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Uween;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    /// textMain = header
    /// text2 = header subtext
    /// text3 = lock text
    public class WTooltipCodex : WText3, ICustomPoolable, ICloseableWidget {
        [SerializeField] private Image preview;
        [SerializeField] private WButton wPanelHeader;
        [SerializeField] private TMP_Text tooltipText;
        [SerializeField] private WText ownedText;
        [SerializeField] private WButtonText actionBtn;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private RawImage chart;
        [SerializeField] private GameObject body;
        [SerializeField] private GameObject chartArea;
        public bool IsLocked;
        private bool isRigged;
        private string lockText;
        private bool canShowActionBtn;
        public Def Def;
        private Lifetime lifetime;
        private Action<WTooltipCodex> onClose;
        private bool isListeningToTick;
        private CodexTooltipChart chartRenderer;
        private bool isChartTooltipActive;
        private int lastChartMouseX = -1, lastChartMouseY = -1;
        private readonly StringBuilder bodySB = new();
        public bool IsChartMode => chartArea.activeSelf;

        public static void SpawnFor(GameState s, Def def) {
            var cont = UIShowing.MainMenu ? UIPoolRoot.SystemPopupContainer : UIPoolRoot.PopupContainer;
            var prefab = ResourceLibrary.FindPrefab<WTooltipCodex>(UIPrefabId.WTooltipCodex);
            var tip = PrefabPool.Spawn(prefab, cont, preInstantiate: true);
            tip.Load(s, def, true);
            UIUtils.AfterShowPopup(tip.Rect);
            s.Clock.ExecuteAfterFramesForUI(3, () => {
                UIUtils.AfterShowPopup(tip.Rect);
            });
        }

        public void Load(GameState s, Def def, bool locked) {
            if (lifetime != null) {
                lifetime.Dispose();
            }
            lifetime = Lifetime.Create($"CodexTooltip_{def.Id}");
            UIUtils.ToggleCG(canvasGroup, false);
            Def = def;
            A.S.UIQuery.RegisterCodexTooltipFor.Ask(def, this, true);
            onClose = null;
            IsLocked = locked;
            wPanelHeader.SetActiveFast(locked);
            if (!isRigged) {
                isRigged = true;
                wPanelHeader.SetOnClick(Close);
            }
            var isUnlocked = s.Research.IsDefUnlocked(def);
            preview.sprite = def.PreviewSprite;
            SetText(def.NameWithPrice(s, false));
            SetText2(def.DescT);
            tooltipText.SetText(BodyText(def, isUnlocked));
            actionBtn.SetActiveFast(false);
            if (locked) {
                UISounds.PlayPopup();
                SetText3(null);
            } else {
                SetText3(lockText ??= UIUtils.GetTooltipLockText());
            }
            if (isUnlocked) {
                TryShowBuildButton(s, def);
            } else {
                TryShowResearchButton(s, def);
            }
            ForceRebuildAllLayouts();
            s.Clock.ExecuteAfterFramesForUI(3, RebuildLayouts);
            if (def.MatType != null) {
                var bCount = s.UIQuery.GetBindableMatCountFor.Ask(def.MatType);
                bCount.Bind(lifetime, (c) => {
                    ownedText.SetText($"{T.OwnedCount}: {c}");
                });
            } else {
                if (def.IsBeing) {
                    ownedText.Load(T.CountOnboard_V1.F(s.Sys.Inventory.CountOf(def)));
                } else if (def.LayerId < WorldLayer.FreeEntities) {
                    ownedText.Load(T.CountBuilt_V1.F(s.Sys.Inventory.CountOf(def)));
                } else {
                    ownedText.Load($"{T.OwnedCount}: {s.Sys.Inventory.CountOf(def)}");
                }
            }
        }

        private string BodyText(Def def, bool isUnlocked) {
            var desc = def.ExtendedDesc(true);
            return desc;
            // Those unlock instructions suck ass, let's not have them
            // if (isUnlocked) {
            //     return desc;
            // }
            // bodySB.Clear();
            // if (!def.AppendUnlockInstructions(bodySB, false, false)) {
            //     return desc;
            // }
            // bodySB.Append(GlyphMap.BRHR);
            // bodySB.Append(desc);
            // var res = bodySB.ToString();
            // bodySB.Clear();
            // return res;
        }

        private void TryShowBuildButton(GameState s, Def def) {
            canShowActionBtn = false;
            if (def.SkipToolListing || !def.IsConstructable) {
            } else {
                canShowActionBtn = true;
                // Opens the build menu on the def's tab rather than placing it right away:
                // the player finds and picks it there, and learns where it lives
                actionBtn.Load(T.AbilityBuild, () => {
                    Close();
                    UIUtils.CloseFullScreenUI();
                    s.UIQuery.ShowBuildMenuFor.Ask(def);
                });
            }
            if (IsLocked) {
                actionBtn.gameObject.SetActiveFast(canShowActionBtn);
            }
        }
        private void TryShowResearchButton(GameState s, Def def) {
            canShowActionBtn = false;
            if (!Researchable.IsEmptyPrereq(def)) {
                var prereqIdH = The.Defs.Get(def.Researchable.Prerequisites[0]).IdH;
                if (s.Research.IsDiscovered(prereqIdH) && !s.Research.IsUnlocked(prereqIdH)) {
                    actionBtn.Load(UIShowing.Research ? T.Find : T.OpenResearch, () => {
                        Close();
                        s.Sys.Research.ShowResearch(prereqIdH);
                    });
                    canShowActionBtn = true;
                }
            }
            if (IsLocked) {
                actionBtn.gameObject.SetActiveFast(canShowActionBtn);
            }
        }

        private void ForceRebuildAllLayouts() {
            LayoutRebuilder.ForceRebuildLayoutImmediate(textMain.rectTransform);
            LayoutRebuilder.ForceRebuildLayoutImmediate(tooltipText.rectTransform);
            LayoutRebuilder.ForceRebuildLayoutImmediate(Rect);
            UIUtils.RebuildLayoutRecursive(Rect);
        }

        private void RebuildLayouts() {
            UIUtils.RebuildLayoutRecursive(Rect);
            if (IsLocked) {
                UIUtils.ToggleCG(canvasGroup, true);
            } else {
                TweenCGA.Add(gameObject, Tunables.UITooltipFadeInSeconds, 1f).Then(() => {
                    UIUtils.ToggleCG(canvasGroup, true);
                });
            }
            UIUtils.MoveIntoScreenBounds(Rect);
        }

        public void Close() {
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
            onClose?.Invoke(this);
            if (gameObject.activeSelf) {
                PrefabPool.Despawn(gameObject);
            }
        }

        public override string ToString() {
            return "WCodexTooltip[]";
        }

        public void SetLocked(Action<WTooltipCodex> onClose) {
            if (!IsLocked) {
                UISounds.PlayButtonClick();
                IsLocked = true;
                wPanelHeader.SetActiveFast(true);
                SetText3(string.Empty);
                if (canShowActionBtn) {
                    actionBtn.gameObject.SetActiveFast(true);
                    RebuildLayout();
                }
            }
            this.onClose = onClose;
        }

        public void PoolDeactivate() {
            if (isListeningToTick) {
                isListeningToTick = false;
                A.S.Clock.OnTick.RemoveListener(OnTick);
            }
            chartRenderer?.End();
            if (isChartTooltipActive) {
                HideChartTooltip();
            }
            chartArea.SetActiveFast(false);
            body.SetActiveFast(true);
            if (Def != null) {
                A.S.UIQuery.RegisterCodexTooltipFor.Ask(Def, this, false);
            }
            Def = null;
            onClose = null;
            UIUtils.ToggleCG(canvasGroup, false);
            gameObject.SetActiveFast(false);
            var rt = Rect;
            // reset pooled anchors
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1f);
        }

        public void PoolActivate() {
            gameObject.SetActiveFast(true);
        }

        private void Awake() {
            The.SysSig.UIThemeReloaded.AddListener(OnThemeReloaded, gameObject);
        }

        private void OnThemeReloaded() {
            if (chartRenderer != null) {
                chartRenderer.SetColors();
                if (IsChartMode) {
                    chartRenderer.Tick();
                }
            }
        }

        public bool TryCloseWidget() {
            if (UIShowing.Research) { return false; }
            if (canvasGroup.alpha < 1f) {
                return false;
            }
            Close();
            return true;
        }

        public void SetChartMode(bool isChartMode) {
            if (IsLocked) {
                D.Err("Trying to change chart mode on a locked tooltip!");
                return;
            }
            if (isChartMode) {
                body.SetActiveFast(false);
                chartArea.SetActiveFast(true);
                chartRenderer ??= new CodexTooltipChart(chart);
                chartRenderer.Begin(Def);
                if (!isListeningToTick) {
                    isListeningToTick = true;
                    A.S.Clock.OnTick.AddListener(OnTick, gameObject);
                }
            } else {
                body.SetActiveFast(true);
                chartArea.SetActiveFast(false);
                if (isListeningToTick) {
                    isListeningToTick = false;
                    A.S.Clock.OnTick.RemoveListener(OnTick);
                }
                chartRenderer?.End();
                if (isChartTooltipActive) {
                    HideChartTooltip();
                }
            }
            Coroutines.OnNextFrame(RebuildLayout);
        }

        private void OnTick(long ticks) {
            chartRenderer?.Tick();
        }

        private void Update() {
            if (!IsChartMode || chartRenderer == null || !chartRenderer.HasValidData) {
                if (isChartTooltipActive) {
                    HideChartTooltip();
                }
                return;
            }
            var sp = The.Bindings.Mouse.ScreenPosition;
            var mx = Mathf.RoundToInt(sp.x);
            var my = Mathf.RoundToInt(sp.y);
            if (mx == lastChartMouseX && my == lastChartMouseY) { return; }
            lastChartMouseX = mx;
            lastChartMouseY = my;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    chart.rectTransform, sp, null, out var localPoint)) {
                if (isChartTooltipActive) { HideChartTooltip(); }
                return;
            }

            var rect = chart.rectTransform.rect;
            if (!rect.Contains(localPoint)) {
                if (isChartTooltipActive) { HideChartTooltip(); }
                return;
            }

            var pivot = chart.rectTransform.pivot;
            float texX = localPoint.x + rect.width * pivot.x;
            float texY = localPoint.y + rect.height * pivot.y;

            var tooltip = chartRenderer.TooltipAt(new Vector2(texX, texY));
            if (tooltip != null) {
                isChartTooltipActive = true;
                The.SysSig.ShowTooltip.Send(tooltip, true);
            } else if (isChartTooltipActive) {
                HideChartTooltip();
            }
        }

        private void OnDestroy() {
            The.SysSig.UIThemeReloaded.RemoveListener(OnThemeReloaded);
        }

        private void HideChartTooltip() {
            lastChartMouseX = -1;
            lastChartMouseY = -1;
            isChartTooltipActive = false;
            The.SysSig.HideTooltip.Send();
        }
    }
}
