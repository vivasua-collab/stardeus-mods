using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Hints;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class CleanableTab : BaseEntityTab {
        public const string TabId = "Cleanable";

        [SelfInit]
        private static void Register() {
            RegisterTab(new CleanableTab());
        }

        private CleanableComp cleanable;
        private GameState S;

        public override string Id => TabId;
        public override int Order => 35;
        public override string TabName => T.Cleanable;

        public override bool ShowFor(GameState s, Tile tile) {
            if (!tile.IsConstructed) {
                return false;
            }
            if (tile.ENode?.IsConnected == false) {
                return false;
            }
            if (!tile.TryGetComponent(out cleanable)) {
                return false;
            }
            S = s;
            return true;
        }

        public override bool LoadContent(WGroup group) {
            var infoBlock = UISpawn.Block(lifetime, group, 0);
            int idx = 0;

            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContentHeader, infoBlock.Container, idx++);
            header.Load(cleanable.CleanableText);

            var dirt = UISpawn.Widget<WIconText2Progress>(lifetime,
                UIPrefabId.WContentProgress, infoBlock.Container, idx++);
            dirt.Load(IconId.CMop, cleanable.DirtAmountText, cleanable.DirtPercent);
            cleanable.DirtPercentB.Bind(lifetime, (p) => {
                dirt.SetPercent(p, false);
                dirt.Item.SetText2(Units.XOutOfY(cleanable.DirtAmount, cleanable.DirtCapacity));
            });

            var settingsBlock = UISpawn.Block(lifetime, group, 1);
            idx = 0;

            var settingsHeader = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, settingsBlock.Container, idx++);
            settingsHeader.Load(T.MenuSettings);

            var autoClean = UISpawn.Widget<WToggleText>(lifetime,
                UIPrefabId.WContent2Toggle, settingsBlock.Container, idx++);
            autoClean.Load(T.AutoClean, cleanable.IsAutoClean, (v) => {
                cleanable.ToggleAutoClean(v);
                ReloadTab();
            });
            UIUtils.AddTip(lifetime, autoClean, T.AutoCleanInfo);

            if (cleanable.IsAutoClean) {
                var amount = UISpawn.Widget<WSliderWithIconText2>(lifetime,
                    UIPrefabId.WContent2Text2Slider, settingsBlock.Container, idx++);
                amount.Item.Load(IconId.CMop, T.AutocleanAmount,
                    Units.Percentage(cleanable.AutocleanAmount));
                amount.Slider.Load(cleanable.AutocleanAmount, (v) => {
                    cleanable.SetAutocleanAmountByUI(v);
                    amount.Item.SetText2(Units.Percentage(cleanable.AutocleanAmount));
                }, 0.1f, 1f);
                if (cleanable.Entity.HasComponent<SolarPanelComp>()) {
                    UIUtils.AddTip(lifetime, amount, T.AutocleanDustTooltip);
                }
            }

            return true;
        }

        public override bool LoadFooter(WGroup group) {
            int idx = 0;

            if (cleanable.IsAutoClean) {
                var priority = UISpawn.Widget<WPriorityUpDown>(lifetime,
                    UIPrefabId.WContent4Priority, group.Container, idx++);
                priority.Up.Load(IconId.WArrowUp, () => {
                    cleanable.AdjustCleanPriority(+1);
                    priority.Texts.SetText2(Units.XOutOfY(cleanable.CleanPriority, 5));
                });
                priority.Down.Load(IconId.WArrowDown, () => {
                    cleanable.AdjustCleanPriority(-1);
                    priority.Texts.SetText2(Units.XOutOfY(cleanable.CleanPriority, 5));
                });
                priority.Texts.Load(T.CleanPriority,
                    Units.XOutOfY(cleanable.CleanPriority, 5));
                UIUtils.AddTip(lifetime, priority, T.JobPriorityHint);
            }

            if (cleanable.CanOrderCleaning) {
                var cleanNow = UISpawn.Widget<WButtonIconText>(lifetime,
                    UIPrefabId.WPanelButtonIcon, group.Container, idx++);
                cleanNow.Load(IconId.CMop, T.CleanNow, () => {
                    S.Sys.Hint.TrackAction(HintCleaningAuto.Id);
                    cleanable.CreateCleaningGoal(true);
                    ReloadTab();
                });
            }

            return idx > 0;
        }

        protected override void Cleanup() {
            cleanable = null;
            S = null;
        }
    }
}
