using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.AI;
using Game.Systems.Creatures;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class BeingNeedsTab : BaseEntityTab {
        public const string TabId = "BeingNeeds";

        [SelfInit]
        private static void Register() {
            RegisterTab(new BeingNeedsTab());
        }

        // Needs drift about 1% per 14 ticks, so a few ticks of lag is invisible, while a
        // per-tick refresh would run dozens of times per second at high clock speeds
        private const int RefreshEveryTicks = 5;

        private sealed class NeedRow {
            public AINeed Need;
            public WIconText2 Item;
            public WIconText2Progress Bar;
            public bool IsRed;
        }

        private BeingData being;
        private GameState S;
        private readonly List<NeedRow> rows = new();
        private readonly HashSet<AINeed> seenNeeds = new();

        public override string TabName => T.Needs;
        private readonly B<string> tabNameB = new(T.Needs);
        public override B<string> TabNameB => tabNameB;
        private string tabNameWithWarning;
        public override int Order => 10;

        public override bool ShowFor(GameState s, BeingData being) {
            if (being == null) { return false; }
            if (!being.IsFriendly || being.HasStateFlag(BeingState.Dead)) {
                return false;
            }
            foreach (var src in being.NeedSources) {
                if (!src.Need.IsInternal) {
                    S = s;
                    this.being = being;
                    return true;
                }
            }
            return false;
        }

        protected override void OnSpawnTab(WTabButton widget, Lifetime parentLifetime) {
            tabNameB.Set(NeedsTabName());
            // A clock refresh instead of binding each need's main stat: those change every
            // tick, and needs without a main stat (Equipment, Health) never triggered one
            S.Clock.TickB.Bind(parentLifetime, ticks => {
                if (ticks % RefreshEveryTicks != 0) { return; }
                tabNameB.Set(NeedsTabName());
            }, notifyImmediately: false);
        }

        private string NeedsTabName() {
            if (TryGetTopAttentionNeed(out var icondId)) {
                tabNameWithWarning ??= $"{GlyphMap.SpriteWarning} {TabName}";
                return tabNameWithWarning;
            }
            return TabName;
        }

        private bool TryGetTopAttentionNeed(out string iconId) {
            var seen = seenNeeds;
            seen.Clear();
            string bestIconId = null;
            float bestPriority = 0f;
            foreach (var src in being.NeedSources) {
                if (src.Need.IsInternal) { continue; }
                if (!seen.Add(src.Need)) { continue; }
                if (!src.Need.RequiresAttention(being, out var needIconId, out var priority)) {
                    continue;
                }
                // D.Warn("Need {0} requires attention with priority {1}", src.Need.Type.NameT, priority);
                if (bestIconId == null || priority > bestPriority) {
                    bestIconId = needIconId;
                    bestPriority = priority;
                }
            }
            iconId = bestIconId;
            return iconId != null;
        }

        public override bool LoadContent(WGroup group) {
            var block = UISpawn.Block(lifetime, group, 0);
            int idx = 0;

            // var header = UISpawn.Widget<WText>(lifetime, UIPrefabId.WContentHeader, block.Container, idx++);
            // header.Load(T.Needs);

            var unique = new HashSet<AINeed>();
            foreach (var src in being.NeedSources) {
                if (!src.Need.IsInternal) { unique.Add(src.Need); }
            }

            rows.Clear();
            foreach (var n in unique.OrderBy(n => n.Type.NameT)) {
                var capturedN = n;
                var row = new NeedRow {
                    Need = n,
                    IsRed = n.RequiresAttention(being, out _, out _),
                };
                var name = row.IsRed ? Texts.Red(n.Type.NameT) : n.Type.NameT;
                WBase tipTarget;
                if (n.TryGetSatisfaction(being, out var satisfaction)) {
                    var bar = UISpawn.Widget<WIconText2Progress>(lifetime,
                        UIPrefabId.WContentProgress, block.Container, idx++);
                    bar.Load(n.Type.IconId, name, satisfaction, n.SatisfactionText(being, satisfaction));
                    row.Bar = bar;
                    row.Item = bar.Item;
                    tipTarget = bar;
                } else {
                    var w = UISpawn.Widget<WIconText2>(lifetime, UIPrefabId.WContentText2, block.Container, idx++);
                    string sideText = null;
                    if (n.TryGetMainStatFor(being.StatsBeing, out var mainStat)) {
                        sideText = mainStat.Display();
                        mainStat.BFloat.Bind(lifetime, v => {
                            w.SetText2(mainStat.Display(v));
                        });
                    }
                    w.Load(n.Type.IconId, name, sideText);
                    row.Item = w;
                    tipTarget = w;
                }
                rows.Add(row);
                UIUtils.AddTipFunc(lifetime, tipTarget, () => BuildNeedTip(capturedN));
            }
            S.Clock.TickB.Bind(lifetime, ticks => {
                if (ticks % RefreshEveryTicks != 0) { return; }
                RefreshRows();
            }, notifyImmediately: false);

            return true;
        }

        private void RefreshRows() {
            for (int i = 0; i < rows.Count; i++) {
                var row = rows[i];
                var n = row.Need;
                bool isRed = n.RequiresAttention(being, out _, out _);
                if (isRed != row.IsRed) {
                    row.IsRed = isRed;
                    row.Item.SetText(isRed ? Texts.Red(n.Type.NameT) : n.Type.NameT);
                }
                if (row.Bar != null && n.TryGetSatisfaction(being, out var satisfaction)) {
                    row.Bar.SetProgress(satisfaction, n.SatisfactionText(being, satisfaction));
                }
            }
        }

        private string BuildNeedTip(AINeed n) {
            var sb = Caches.StringBuilder3;
            sb.Clear();
            bool needsBr = false;
            if (!Arrays.IsEmpty(n.RelatedStats)) {
                bool appended = false;
                foreach (var sId in n.RelatedStats) {
                    if (being.StatsBeing.Find(sId, out var stat)) {
                        if (stat.IsUnused) { continue; }
                        if (needsBr) {
                            sb.Append('\n');
                        } else {
                            needsBr = true;
                        }
                        sb.Append(stat.StatString());
                        if (!Arrays.IsEmpty(stat.Modifiers)) {
                            sb.AppendFormat("\n {0} {1}", Texts.Grey(GlyphMap.ArrowRight),
                                Texts.Grey(T.StatValueBase_V1.F(stat.DisplayBase())));
                            foreach (var m in stat.Modifiers) {
                                m.AppendInfo(sb, stat, true, subItem: true);
                            }
                            sb.AppendFormat("\n {0} {1}", Texts.Grey(GlyphMap.ArrowRight),
                                Texts.Grey(T.StatValueEffective_V1.F(stat.Display())));
                        }
                        appended = true;
                    }
                }
                if (appended) { sb.Append(GlyphMap.BRHR); }
            }
            sb.Append(T.NounSource);
            sb.AppendLine(":");
            foreach (var ns in being.NeedSources) {
                if (ns.Need == n) {
                    ns.AppendDescription(sb);
                    sb.AppendLine();
                }
            }
            bool isActive = S.Query.CheckNeedActive.Ask(being, n.IdH);
            if (!isActive) {
                sb.Append(Texts.Red(T.Inactive));
            }
            return sb.ToString();
        }

        protected override void Cleanup() {
            being = null;
            S = null;
            rows.Clear();
        }

        public override string Id => TabId;
    }
}
