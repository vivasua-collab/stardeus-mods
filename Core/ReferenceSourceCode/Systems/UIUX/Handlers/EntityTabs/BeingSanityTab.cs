using System;
using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Mental;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class BeingSanityTab : BaseEntityTab {
        public const string TabId = "BeingSanity";
        [SelfInit]
        private static void Register() {
            RegisterTab(new BeingSanityTab());
        }

        private BeingData being;
        private MentalHealth health;
        private readonly List<FunSource> recentFun = new();

        internal static string BandLabel(MentalBreakdownBand band) {
            switch (band) {
                case MentalBreakdownBand.None: return T.RiskNone;
                case MentalBreakdownBand.Light: return T.RiskLight;
                case MentalBreakdownBand.Medium: return T.RiskMedium;
                case MentalBreakdownBand.Heavy: return T.RiskHeavy;
                default:
                    D.Err("Unhandled MentalBreakdownBand {0}", band);
                    return T.Unknown;
            }
        }
        public override string TabName => T.StatSanity;
        private readonly B<string> tabNameB = new(T.StatSanity);
        public override B<string> TabNameB => tabNameB;

        public override int Order => 5;

        protected override void OnSpawnTab(WTabButton widget, Lifetime parentLifetime) {
            health.Trend.Bind(parentLifetime, t => {
                tabNameB.Set($"{t} {T.StatSanity}");
            });
            health.EffectsChanged.Bind(parentLifetime, _ => {
                ReloadTab();
            }, false);
            health.FunMemory.Changed.Bind(parentLifetime, _ => {
                ReloadTab();
            }, false);

            UIUtils.AddTip(parentLifetime, widget,
                $"{Texts.SubTitle(T.StatSanity)}{T.StatSanityDesc}");
        }

        public override bool ShowFor(GameState s, BeingData being) {
            if (being.Mind.Health != null && being.IsFriendly
                    && being.HasStateFlag(BeingState.Alive)) {
                this.being = being;
                this.health = being.Mind.Health;
                return true;
            }
            return false;
        }

        public override bool LoadContent(WGroup group) {
            int idx = 0;
            LoadOverview(group, idx++);
            LoadOngoing(group, idx++);
            LoadRecreation(group, idx++);
            LoadHistory(group, idx++);
            return true;
        }

        private void LoadOverview(WGroup group, int gidx) {
            var block = UISpawn.Block(lifetime, group, gidx);
            int idx = 0;
            var header = UISpawn.Widget<WProgressWithLabel>(lifetime,
                UIPrefabId.WContentHeaderProgress_Green, block.Container, idx++);
            header.Label.Load(T.StatSanity);

            var risk = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContentText2, block.Container, idx++);
            risk.Load(IconId.CStress, T.MentalBreakdownRisk);

            var b2 = new C2<float, string>(lifetime, health.Sanity.BFloat, health.Trend, (s, t) => {
                header.Progress.SetPercent(s, false);
                header.Progress.SetText($"{t} {Units.Percentage(s)}");
                var h = being.Mind.Health;
                var band = h.CurrentBreakdownBand;
                // Show the current breakdown band; when in a band, the tooltip shows the
                // chance of a breakdown within a day, compounded from the hourly roll.
                risk.SetText2(BandLabel(band));
            });
            UIUtils.AddTipFunc(lifetime, risk, () => {
                var h = being.Mind.Health;
                if (h.CurrentBreakdownBand != MentalBreakdownBand.None) {
                    return $"{T.NounRisk}: {Units.Percentage(h.DailyBreakdownChance)}";
                }
                return null;
            });
        }

        private void LoadOngoing(WGroup group, int gidx) {
            var block = UISpawn.Block(lifetime, group, gidx);
            int idx = 0;
            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.AdjOngoing);

            int added = 0;
            // Permanent effects first: they stay until their cause is dealt with
            foreach (var e in health.CurrentEffects.OrderByDescending(c => c.IsPermanent)
                    .ThenByDescending(c => c.StartTick)) {
                AddEffect(block.Container, e, idx++, isHistory: false);
                added++;
            }
            if (added == 0) {
                var empty = UISpawn.Widget<WIconText>(lifetime,
                    UIPrefabId.WContent2Text, block.Container, idx++);
                empty.Load(IconId.CInfo, T.Empty);
            }
        }

        // Recently used fun sources, so the player can see why a source stopped working well
        private void LoadRecreation(WGroup group, int gidx) {
            var memory = health.FunMemory;
            var ticks = G.Ticks;
            var window = FunNovelty.WindowTicks;
            memory.CollectRecent(recentFun, ticks, window);
            if (recentFun.Count == 0) { return; }
            var block = UISpawn.Block(lifetime, group, gidx);
            int idx = 0;
            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContentHeader, block.Container, idx++);
            header.Load(T.JobTypeRecreation);

            foreach (var src in recentFun) {
                var count = memory.UseCount(src, ticks, window);
                var novelty = FunNovelty.Current(being, src, ticks);
                var row = UISpawn.Widget<WIconText2>(lifetime,
                    UIPrefabId.WContentText2, block.Container, idx++);
                row.Load(FunSubjects.Icon(src), FunSubjects.DisplayName(src),
                    $"{Units.Percentage(novelty)} {Texts.Grey($"×{count}")}");
                var captured = src;
                UIUtils.AddTipFunc(lifetime, row, () => RecreationTip(captured));
            }
            recentFun.Clear();
        }

        private string RecreationTip(FunSource src) {
            if (health == null) { return null; }
            var ticks = G.Ticks;
            var window = FunNovelty.WindowTicks;
            var memory = health.FunMemory;
            return T.FunSourceMemoryTip_V4.F(
                memory.UseCount(src, ticks, window),
                Mathf.RoundToInt(window / (float) Consts.TicksPerDay),
                Units.TicksAgoFromNow(memory.LastUseTick(src)),
                Units.Percentage(FunNovelty.Current(being, src, ticks)));
        }

        private void LoadHistory(WGroup group, int gidx) {
            var history = health.PastEffects;
            if (history.Count == 0) { return; }
            var block = UISpawn.Block(lifetime, group, gidx);
            int idx = 0;
            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.NounHistory);

            for (var i = history.Count - 1; i >= 0; i--) {
                var e = history[i];
                AddEffect(block.Container, e, idx++, isHistory: true);
            }

            var clearBtn = UISpawn.Widget<WButtonText>(lifetime,
                UIPrefabId.WContent2ButtonSimple, block.Container, idx++);
            clearBtn.Load(Texts.Italic(T.ClearHistory), ClearHistory);
        }

        private void ClearHistory() {
            health.PastEffects.Clear();
            ReloadTab();
        }

        private void AddEffect(RectTransform rt, MentalEffect e, int idx, bool isHistory) {
            string icon;
            if (e.Icon != null) {
                icon = e.Icon;
            } else if (e.Effect >= 0) {
                icon = IconId.CMoodGood;
            } else {
                icon = IconId.CMoodBad;
            }
            if (isHistory) {
                var w = UISpawn.Widget<WButtonIconText2>(lifetime,
                    UIPrefabId.WContent2Button, rt, idx);
                w.Load(icon, e.Reason, Texts.ColoredSignedNum(e.Effect), e.OnClickUI);
                var tip = UIUtils.AddTipFunc(lifetime, w, e.HistoryTip);
                tip.HoverFun = e.OnHoverUI;
            } else if (e.IsPermanent) {
                // No countdown bar: a draining bar told players the effect would pass on
                // its own, when it lasts as long as its cause
                var w = UISpawn.Widget<WButtonIconText2>(lifetime,
                    UIPrefabId.WContent2Button, rt, idx);
                w.Load(icon, e.Reason, Texts.ColoredSignedNum(e.Effect), e.OnClickUI);
                var tip = UIUtils.AddTipFunc(lifetime, w, e.OngoingTip);
                tip.HoverFun = e.OnHoverUI;
            } else {
                var w = UISpawn.Widget<WButtonIconText2Progress>(lifetime,
                    UIPrefabId.WContent2ProgressButton, rt, idx);
                w.Load(icon, e.Reason, 0f, e.OnClickUI, Texts.ColoredSignedNum(e.Effect));
                e.BProgress.Bind(lifetime, v => {
                    w.SetPercent(1f - v, false);
                });
                var tip = UIUtils.AddTipFunc(lifetime, w, e.OngoingTip);
                tip.HoverFun = e.OnHoverUI;
            }
        }

        protected override void Cleanup() {
            being = null;
            health = null;
        }

        public override string Id => TabId;
    }
}
