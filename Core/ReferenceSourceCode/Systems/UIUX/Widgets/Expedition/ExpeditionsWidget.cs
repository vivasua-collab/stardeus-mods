using System;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Systems.Creatures;
using Game.Systems.Space;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.UI;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class ExpeditionsWidget : BaseWidget, ICloseableWidget {
        /// <summary>
        /// WExpeditionMini instances should be spawned in this container
        /// for the whole duration of the expedition after it gets launched.
        /// </summary>
        [SerializeField] private WGroup miniWidgetsContainer;
        [SerializeField] private WButtonText wPanelHeader;
        [SerializeField] private ScrollRect scroll;
        [SerializeField] private WGroup wItems;
        [SerializeField] private WText aboutText;
        public SubPanelWidget SubPanel;
        public WGroup WItems => wItems;
        public WGroup MiniWidgetsContainer => miniWidgetsContainer;
        private Lifetime lifetime;
        private Lifetime footLife;
        private GameState S;
        private ExpeditionContext ctx;
        private ExpeditionContext setupCtx;
        public ExpeditionContext Ctx => ctx;
        public bool IsShowing => gameObject.activeSelf;
        private readonly List<ExpeditionMenuBlock> menuBlocks = new();
        private bool menuBlocksSorted;
        // True when this widget paused the clock during setup, so Launch can resume it.
        // Cleared as soon as the clock leaves the pause, so a player's own choice is kept.
        private bool pausedForSetup;
        private ClockSpeed speedBeforeSetup;
        public void Initialize(GameState s) {
            S = s;
            s.Clock.OnSpeedChange.AddListener(OnClockSpeedChange, gameObject);
            wPanelHeader.Load(T.Expeditions, Close);
            aboutText.Load(T.ExpeditionsAbout);
            wItems.DestroyChildren();
            SubPanel.Initialize();
            gameObject.SetActiveFast(false);
            miniWidgetsContainer.DestroyChildren();
            CreateMenuBlocks(s);
        }

        private void CreateMenuBlocks(GameState s) {
            menuBlocks.Add(new ExpeditionMenuBlock_Destination(this, s));
            menuBlocks.Add(new ExpeditionMenuBlock_Crew(this, s));
            // mods can hook in here
        }

        public void Reload() {
            if (ctx == null) { return; }
            D.Warn("Reloading expedition menu");
            if (ctx.Expedition?.IsDone == true || ctx.Expedition?.IsValid == false) {
                ctx = NewSetupContext();
                SubPanel.Close();
            }
            Load(ctx);
        }

        private ExpeditionContext NewSetupContext() {
            return new ExpeditionContext {
                MiningOrders = ExpeditionMiningOrders.Default(S),
            };
        }

        public void Load(ExpeditionContext newCtx) {
            Cleanup();
            if (newCtx.IsSetup) {
                setupCtx = newCtx;
            }
            ctx = newCtx;
            lifetime = Lifetime.Create("Expeditions");
            ctx.MainLifetime = lifetime;

            if (A.IsInHyperspace && ctx.IsSetup) {
                ctx.TargetSO.Set(null);
            }

            var block = UISpawn.Block(lifetime, wItems, 0);

            var idx = 0;
            WBase shuttleW = null;
            // null on a ship that never had a shuttle
            var allShuttles = S.Components.AllByType<ShuttleComp>();
            var shuttles = allShuttles == null
                ? Enumerable.Empty<IComponent>()
                : allShuttles.Where(s => s.IsConstructed).OrderBy(s => ((ShuttleComp) s).Name);
            foreach (ShuttleComp shuttle in shuttles) {
                var w = UISpawn.Widget<WExpeditionItem>(lifetime, UIPrefabId.WExpeditionItem, block.Container, idx++);
                var exp = S.Sys.Expedition.FindForShuttle(shuttle.EntityId);
                if (exp == null) {
                    if (IsShuttleUsableNow(shuttle, out string reason)) {
                        w.Item.Load(shuttle.Tile.Definition.Preview,
                            Texts.Green(T.ReadyForExpedition), null,
                            () => OnSelectShuttle(w, shuttle, null));
                    } else {
                        w.Item.Load(shuttle.Tile.Definition.Preview, null,
                            Texts.Red(reason), () => {
                                UISounds.PlayActionDenied();
                            });
                    }
                    shuttle.NameB.Bind(lifetime, n => w.Shuttle.Label.Load(n));
                    UIUtils.AddTipFunc(lifetime, w, () => shuttle.MiningPowerText);
                } else {
                    w.Item.Load(shuttle.Tile.Definition.Preview,
                        exp.CurrentStage.Title, null,/*exp.CurrentStage.Subtitle.Value,*/
                            () => OnSelectShuttle(w, shuttle, exp));
                    shuttle.NameB.Bind(lifetime, n => w.Shuttle.Label.Load(n));
                    Lifetime subtitleLife = null;
                    var shownStageType = exp.CurrentStage?.StageType ?? 0;
                    exp.OnChange.Bind(lifetime, () => {
                        if (exp.IsDone) {
                            // Expedition returned completely: close the open subtab.
                            if (SubPanel.IsShowingFor(w)) {
                                SubPanel.Close();
                            }
                            Reload();
                            return;
                        }
                        subtitleLife?.Dispose();
                        if (w.IsDisposed) { return; }
                        subtitleLife = lifetime.CreateChild("Expedition.Subtitle");
                        var stage = exp.CurrentStage;
                        // w.Item.SetText(stage.Title);
                        if (stage.TitleB == null) {
                            w.Item.SetText(stage.Title ?? T.GridUpdating);
                        } else {
                            stage.TitleB.Bind(subtitleLife, t => w.Item.SetText(t ?? T.GridUpdating));
                        }
                        // stage.Subtitle.Bind(subtitleLife, s => w.Item.SetText2(s));
                        // Rebuild the open subtab when the stage changes (e.g. into a returning
                        // stage) so the footer (Recall button) and blocks reflect the new stage.
                        var newStageType = stage?.StageType ?? 0;
                        if (newStageType != shownStageType) {
                            shownStageType = newStageType;
                            if (SubPanel.IsShowingFor(w)) {
                                ctx = exp.Context;
                                LoadMenu(shuttle, exp);
                                SubPanel.SetTitle(T.Expedition);
                                SubPanel.Show(w, toggle: false);
                            }
                        }
                    });
                    UIUtils.AddTipFunc(lifetime, w, () => exp.CurrentStage?.TitleTooltip ?? shuttle.MiningPowerText);
                }
                if (shuttle == ctx.Shuttle) {
                    shuttleW = w;
                }
                var st = shuttle.Tile.MaterialStorage;
                st.OnChange.Bind(lifetime, () => {
                    var used = st.UsedCapacity;
                    var total = st.MaxCapacity;
                    w.Shuttle.Progress.Load($"{T.Storage} {used}/{total}",
                        total > 0 ? (float) used / total : 0f);
                });
            }
            if (!ctx.IsSetup && !SubPanel.IsShowing && shuttleW != null) {
                Coroutines.OnNextFrame(() => {
                    PreSelectShuttle(shuttleW, ctx.Shuttle, ctx.Expedition);
                });
            }
        }

        private bool IsShuttleUsableNow(ShuttleComp sh, out string reason) {
            if (sh == null) {
                reason = T.Missing;
                return false;
            }
            var tile = sh.Tile;
            if (sh.IsInUse) {
                var exp = S.Sys.Expedition.FindForShuttle(sh.EntityId);
                if (exp == null) {
                    reason = T.AdjBusy;
                    return false;
                }
                reason = T.ReservedFor_V1.F(exp.Target.StarmapLink);
                return false;
            }
            if (!tile.IsConstructed) {
                reason = T.ElDeviceNotConstructed;
                return false;
            }
            if (sh.IsLost) {
                reason = T.ShuttleLostInfo;
                return false;
            }
            if (tile.Health.IsDamaged) {
                reason = T.Damaged;
                return false;
            }
            reason = null;
            return true;
        }

        public override void Toggle(bool show) {
            UIShowing.Set(UIShowingFlags.Expeditions, show);
            if (!show) {
                Cleanup();
                pausedForSetup = false;
            }
            base.Toggle(show);
            if (show) {
                Rect.SetAsLastSibling();
            }
        }

        private void PreSelectShuttle(WBase w, ShuttleComp shuttle, Expedition exp) {
            LoadMenu(shuttle, exp);
            SubPanel.SetTitle(exp == null ? T.ExpeditionSetup : T.Expedition);
            SubPanel.Show(w);
        }

        private void OnSelectShuttle(WBase w, ShuttleComp shuttle, Expedition exp) {
            if (footLife != null) {
                footLife.Dispose();
            }
            if (SubPanel.IsShowing && SubPanel.IsShowingFor(w)) {
                SubPanel.Close();
                return;
            }
            var prevCtx = ctx;
            if (exp == null && setupCtx == null) {
                setupCtx = new ExpeditionContext();
                setupCtx.MiningOrders = ExpeditionMiningOrders.Default(S);
            }
            ctx = exp == null ? setupCtx : exp.Context;
            if (prevCtx != ctx && prevCtx?.MenuLifetime != null) {
                prevCtx.MenuLifetime.Dispose();
                prevCtx.MenuLifetime = null;
            }

            LoadMenu(shuttle, exp);
            SubPanel.SetTitle(exp == null ? T.ExpeditionSetup : T.Expedition);
            SubPanel.Show(w);
            if (ctx.IsSetup && !Arrays.IsEmpty(ctx.crewCandidates)) {
                // Crew was autofilled into a fresh setup panel: slow the clock so the player
                // has time to review/launch before any of them go stale (e.g. become tired).
                SlowClockToMinSpeed();
            }
        }

        private void LoadFooter() {
            if (footLife != null) {
                footLife.Dispose();
            }
            footLife = lifetime.CreateChild("Expedition.Foot");
            if (ctx.TargetSO.Value == null) {
                SubPanel.ShowFooter(false);
                return;
            }
            int idx = 0;
            if (ctx.IsSetup || ctx.Expedition.StageType == ExpeditionStageTypeH.Prep) {
                var wRequireCrew = UISpawn.Widget<WToggleText>(footLife, UIPrefabId.WFooterItem_Toggle,
                    SubPanel.Footer.Container, idx++);
                // When a guest passenger is in the crew the escort requirement is mandatory —
                // show the toggle on and non-interactable so the player sees the constraint.
                // Apply the same locked behavior to every quest-required crew member.
                var forceRequireCrew = ctx.HasGuestCrew
                    || ctx.IsQuestCrewTarget(ctx.TargetSO.Value);
                var requireCrewLabel = forceRequireCrew
                    ? Texts.Grey(T.ExpRequireAllCrew) : T.ExpRequireAllCrew;
                wRequireCrew.Load(requireCrewLabel,
                    forceRequireCrew || ctx.MiningOrders.AbortOnMissingCrew, (v) => {
                    ctx.MiningOrders.AbortOnMissingCrew = v;
                    ctx.MiningOrders.SavePrefs(S);
                    ctx.OnOrdersChanged.Invoke();
                });
                if (forceRequireCrew) {
                    wRequireCrew.SetInteractable(false);
                }
                UIUtils.AddTip(footLife, wRequireCrew, T.ExpRequireAllCrewInfo);
            }
            if (!ctx.IsExploration) {
                var wAutoUnload = UISpawn.Widget<WToggleText>(footLife, UIPrefabId.WFooterItem_Toggle,
                    SubPanel.Footer.Container, idx++);
                wAutoUnload.Load(T.ExpeditionAutoUnload, ctx.MiningOrders.IsAutoUnload, (v) => {
                    ctx.MiningOrders.IsAutoUnload = v;
                    ctx.MiningOrders.SavePrefs(S);
                    ctx.OnOrdersChanged.Invoke();
                });
                UIUtils.AddTip(footLife, wAutoUnload, T.ExpeditionAutoUnloadInfo);
                var wMinRate = UISpawn.Widget<WSliderWithLabel>(footLife, UIPrefabId.WFooterItem_Slider,
                    SubPanel.Footer.Container, idx++);
                wMinRate.Label.Load(T.MinMineRate);
                wMinRate.Slider.Load(ctx.MiningOrders.MinRate, (v) => {
                    ctx.MiningOrders.MinRate = Mathf.RoundToInt(v);
                    ctx.MiningOrders.SavePrefs(S);
                    ctx.OnOrdersChanged.Invoke();
                    UpdateMinRate(wMinRate);
                }, 0f, 100f);
                wMinRate.Slider.SetValueTextFunc((v) => null);
                UpdateMinRate(wMinRate);
                UIUtils.AddTip(footLife, wMinRate, T.MinMineRateTip);
            }
            if (ctx.IsSetup) {
                var launchBtn = UISpawn.Widget<WButtonText>(footLife, UIPrefabId.WFooterItem_ConfirmButton,
                    SubPanel.Footer.Container, idx++);
                launchBtn.Load(T.Launch, () => {
                    if (ctx.Launch()) {
                        UISounds.PlayDigitalConfirm();
                        pausedForSetup = false;
                        SubPanel.Close();
                        ctx.RebuildUI();
                    }
                });
                new C3<SpaceObject, int, int>(footLife, ctx.TargetSO,
                        ctx.OnOrdersChanged, ctx.OnCrewChanged, (so, _, _) => {
                    launchBtn.SetInteractable(ctx.CanLaunch());
                });
                UIUtils.AddTipFunc(footLife, launchBtn, ctx.LaunchTip);
            } else {
                if (ctx.IsExploration && ctx.TargetSO.Value == A.Player) {
                    // returning to base
                    SubPanel.ShowFooter(false);
                    return;
                }
                var isReturning = ctx.Expedition.CurrentStage?.IsReturning == true;
                if (isReturning) {
                    if (!ctx.MiningOrders.IsAutoUnload) {
                        // no need to show recall button again
                        SubPanel.ShowFooter(false);
                        return;
                    }
                }
                var callBackButton = UISpawn.Widget<WButtonText>(footLife, UIPrefabId.WFooterItem_ConfirmButton,
                    SubPanel.Footer.Container, idx++);
                var isActionRequired = ctx.IsActionRequired;
                var title = isActionRequired ? T.VerbHail : T.VerbRecall;
                callBackButton.Load(title, () => {
                    if (ctx.TryRequiredAction()) {
                        ctx.ClearRequiredAction();
                        return;
                    }
                    if (S.Sys.Expedition.CallExpeditionBack(ctx.Expedition)) {
                        UISounds.PlayDigitalConfirm();
                        SubPanel.Close();
                        ctx.MenuLifetime.Dispose();
                        ctx.MenuLifetime = null;
                        ctx = NewSetupContext();
                        Reload();
                    }
                });
            }

            SubPanel.ShowFooter(true);
        }

        private void UpdateMinRate(WSliderWithLabel wMinRate) {
            ctx.EstimateMiningRate(out bool areMiningOrdersOK);
            string rate = Units.Num(ctx.MiningOrders.MinRate);
            if (!areMiningOrdersOK) {
                rate = Texts.Red(rate);
            }
            wMinRate.Label.SetText2(rate);
        }

        public void LoadMenu(ShuttleComp shuttle, Expedition exp) {
            if (ctx.MenuLifetime != null) {
                ctx.MenuLifetime.Dispose();
            }

            var l = ctx.MenuLifetime = SubPanel.Lifetime = lifetime.CreateChild("Expedition.Sub");
            int idx = 0;

            if (ctx.IsSetup) {
                ctx.Shuttle = shuttle;
                ctx.Expedition = exp;
                ctx.AutofillCrew();
            }

            if (!menuBlocksSorted) {
                menuBlocks.Sort((a, b) => a.Order.CompareTo(b.Order));
                menuBlocksSorted = true;
            }

            foreach (var block in menuBlocks) {
                block.Load(idx, shuttle, exp);
                idx++;
            }

            if (!gameObject.activeSelf) {
                gameObject.SetActiveFast(true);
                UISounds.PlayHover(UISounds.HoverType.Lowest);
            }
            // When the target crosses a quest/non-quest crew boundary (quest POI → normal or
            // vice-versa), reset the crew and rebuild the whole panel so the locked guest is
            // dropped / added and the footer (including the "Require All Crew" toggle) reflects
            // the new state. For ordinary target changes just refresh the footer.
            var lastTarget = ctx.TargetSO.Value;
            var lastWasQuestCrew = ctx.IsQuestCrewTarget(lastTarget);
            ctx.TargetSO.Bind(l, (so) => {
                var nowQuestCrew = ctx.IsQuestCrewTarget(so);
                var crossesQuestBoundary = so != lastTarget && (lastWasQuestCrew || nowQuestCrew);
                lastTarget = so;
                lastWasQuestCrew = nowQuestCrew;
                if (crossesQuestBoundary && ctx.IsSetup) {
                    ctx.ResetCrewSelection();
                    Coroutines.OnNextFrame(() => {
                        if (ctx == null || ctx.Shuttle == null) { return; }
                        LoadMenu(ctx.Shuttle, ctx.Expedition);
                    });
                    return;
                }
                LoadFooter();
            });
            ctx.OnCrewChanged.Bind(l, () => {
                // Coroutines.OnNextFrame(() => {
                    // UIUtils.RebuildScroll(scroll, false);
                    UIUtils.RebuildLayoutRecursive(Rect);
                    // GetComponent<UIHeightAdjuster>().Reset();
                // });
            }, false);
        }

        /// <summary>
        /// Slows the clock down to <see cref="Game.Clock.Clock.MinSpeed"/> (usually a pause) so
        /// crew selected for an expedition don't go stale (e.g. become tired) while the player
        /// is still browsing the crew subpanel or the crew choice popup.
        /// </summary>
        public void SlowClockToMinSpeed() {
            if (G.ClockSpeed > S.Clock.MinSpeed) {
                speedBeforeSetup = G.ClockSpeed;
                // Pause the way the pause key does, so the key also resumes at this speed
                S.Clock.Toggle();
                UISounds.PlayClockChange(G.ClockSpeed);
                pausedForSetup = true;
            }
        }

        // Called for every launch, also the ones confirmed later through a popup
        public void ResumeClockAfterSetup() {
            if (!pausedForSetup) { return; }
            pausedForSetup = false;
            S.Clock.SetSpeed(speedBeforeSetup);
            UISounds.PlayClockChange(G.ClockSpeed);
        }

        private void OnClockSpeedChange(ClockSpeed speed) {
            if (speed > S.Clock.MinSpeed) {
                pausedForSetup = false;
            }
        }

        public void Close() {
            Toggle(false);
        }

        public void Cleanup() {
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
            ctx = null;
            setupCtx = null;
            SubPanel.Close();
        }

        public bool TryCloseWidget() {
            if (SubPanel.IsShowing) {
                SubPanel.Close();
                return true;
            }
            if (gameObject.activeSelf) {
                Close();
                return true;
            }
            return false;
        }
    }
}
