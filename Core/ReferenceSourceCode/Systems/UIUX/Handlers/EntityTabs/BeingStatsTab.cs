using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.AI.Combat;
using Game.Systems.Creatures;
using Game.Systems.Stats;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class BeingStatsTab : BaseEntityTab {
        public const string TabId = "BeingStats";

        [SelfInit]
        private static void Register() {
            RegisterTab(new BeingStatsTab());
        }

        private bool abilitiesExpanded;
        private bool attackMethodsExpanded;

        private BeingData being;
        private GameState S;

        public override string TabName => T.NounStats;
        public override int Order => 100;

        public override bool ShowFor(GameState s, BeingData being) {
            if (being == null) { return false; }
            S = s;
            this.being = being;
            return true;
        }

        public override bool LoadContent(WGroup group) {
            int idx = 0;
            int blockIdx = 0;

            var bd = being;
            int lastCatId = 0;
            WGroup block = null;
            // abilities are a bit special, everything else comes from stats
            (block, lastCatId) = LoadAbilities(lifetime, group, ref blockIdx, ref idx);
            foreach (var s in bd.StatsBeing.AllStats
                    .OrderBy(s => Stat.CategoryFor(s))
                    .ThenBy(s => Stat.NameFor(s))) {
                if (s.IsUnused) { continue; }
                if (s.IsHiddenFor(bd.Species.StatFlags)) { continue; }
                if (s.RequiredNeedId != 0 && !bd.HasNeed(s.RequiredNeedId)) {
                    // stat should be skipped
                    continue;
                }
                if (s.RequiredAbilityId != 0 && !bd.HasAbility(s.RequiredAbilityId)) {
                    continue;
                }
                // lastCatId = Stat.AddUDBForStat(this, s, res, lastCatId);
                (block, lastCatId) = StatUI.AddStat(s, lifetime, group, block, lastCatId, ref blockIdx, ref idx);
            }

            return true;
        }

        private (WGroup, int) LoadAbilities(Lifetime lifetime, WGroup group, ref int gidx, ref int idx) {

            var unique = new HashSet<Ability>();
            foreach (var src in being.AbilitySources) {
                if (!src.Ability.IsInternal) { unique.Add(src.Ability); }
            }
            var block = UISpawn.Block(lifetime, group, gidx++);

            if (unique.Count > 0) {
                var header = UISpawn.Widget<WText>(lifetime,
                    A.InstantBuild ? UIPrefabId.WContent2Header : UIPrefabId.WContentHeader,
                        block.Container, idx++);
                header.Load(T.StatCategoryAbility);

                var listBtn = UISpawn.Widget<WContentListButton>(lifetime,
                    UIPrefabId.WContentListButton, block.Container, idx++);
                listBtn.Load(IconId.CWrench2, T.NounAbilities, Units.Num(unique.Count));
                listBtn.LoadOnToggle(lifetime, (life, container, willShow, wasLoaded) => {
                    abilitiesExpanded = willShow;
                    if (!willShow) { return wasLoaded; }
                    if (wasLoaded) { return true; }
                    int tIdx = 0;
                    foreach (var ab in unique.OrderBy(a => a.NameT)) {
                        var capturedAb = ab;
                        var w = UISpawn.Widget<WIconText>(life,
                            UIPrefabId.WContentText, container, tIdx++);
                        w.Load(ab.IconId, ab.NameT);
                        UIUtils.AddTipFunc(life, w, () => BuildAbilityTip(being, capturedAb));
                    }
                    return true;
                }, abilitiesExpanded);
            }


            // Attack methods expandable list
            var am = being.Body.AttackMethods;
            if (am.Count > 0) {
                // No entity while off the map (expedition, stasis, storage); the methods
                // come from the body, only the best/current highlights need the unit
                var unit = being.Being?.AIAgent?.CombatUnit;

                var methodsBtn = UISpawn.Widget<WContentListButton>(lifetime,
                    UIPrefabId.WContentListButton, block.Container, idx++);
                methodsBtn.Load(IconId.CAttackTarget, T.NounAttackMethods, Units.Num(am.Count));
                methodsBtn.LoadOnToggle(lifetime, (life, innerContainer, willShow, wasLoaded) => {
                    attackMethodsExpanded = willShow;
                    if (!willShow) {
                        var subPanel = A.S.UIQuery.GetEntitySubPanel.Ask();
                        subPanel.Close();
                        return wasLoaded;
                    }
                    if (wasLoaded) { return true; }
                    for (int i = 0; i < am.Count; i++) {
                        var meth = am[i];
                        string icon = unit?.BestAttack == meth ? IconId.CBookmark
                            : meth.IsMelee ? IconId.CFist : IconId.CAttackTarget;
                        string title = unit?.CurrentAttack == meth
                            ? Texts.Accented(meth.NameT) : meth.NameT;
                        var row = UISpawn.Widget<WButtonIconText2>(life,
                            UIPrefabId.WContent2Button, innerContainer, i);
                        row.Load(icon, title, meth.Provider.ItemName, () => {
                            ShowAttackMethodSubPanel(lifetime, row, meth);
                        });
                    }
                    LayoutRebuilder.ForceRebuildLayoutImmediate(innerContainer);
                    LayoutRebuilder.ForceRebuildLayoutImmediate(methodsBtn.Rect);
                    return true;
                }, attackMethodsExpanded);
            }


            return (block, Hashes.S(T.StatCategoryAbility));
        }

        private string BuildAbilityTip(BeingData being, Ability ab) {
            var sb = Caches.StringBuilder3;
            sb.Clear();
            sb.Append(T.NounSource);
            sb.AppendLine(":");
            foreach (var abs in being.AbilitySources) {
                if (abs.Ability == ab) {
                    abs.AppendDescription(sb);
                    sb.AppendLine();
                }
            }
            return sb.ToString();
        }

        private static void ShowAttackMethodSubPanel(Lifetime tabLifetime,
                WBase anchor, AttackMethod meth) {
            var subPanel = A.S.UIQuery.GetEntitySubPanel.Ask();
            var subId = $"AttackMethod_{meth.Id}";
            if (subPanel.IsShowing && SubPanelWidget.IsOpenFor == subId) {
                subPanel.Close();
                return;
            }

            var l = subPanel.CreateLifetime(tabLifetime);
            subPanel.SetTitle(meth.NameT);
            int bIdx = 0;

            // Block 0: attack overview
            var overviewBlock = UISpawn.Block(l, subPanel.Container, bIdx++);
            int idx = 0;
            var typeText = meth.IsMelee ? T.CombatStateMeleeattack : T.CombatStateRangedattack;
            UISpawn.Widget<WText>(l, UIPrefabId.WContentHeader, overviewBlock.Container, idx++)
                .Load(typeText);
            UISpawn.Widget<WIconText2>(l, UIPrefabId.WContentText2, overviewBlock.Container, idx++)
                .Load(meth.IsMelee ? IconId.CFist : IconId.CAttackTarget, meth.NameT, null);
            UISpawn.Widget<WIconText2>(l, UIPrefabId.WContentText2, overviewBlock.Container, idx++)
                .Load(IconId.CExplosion, T.DamageType, meth.TypeNameT);

            // Block 1: source
            var sourceBlock = UISpawn.Block(l, subPanel.Container, bIdx++);
            idx = 0;
            UISpawn.Widget<WText>(l, UIPrefabId.WContentHeader, sourceBlock.Container, idx++)
                .Load(T.NounSource);
            UISpawn.Widget<WIconText2>(l, UIPrefabId.WContentText2, sourceBlock.Container, idx++)
                .Load(IconId.CCog, meth.Provider.ItemName, meth.Provider.SlotName);

            // Block 2: stats
            var statsBlock = UISpawn.Block(l, subPanel.Container, bIdx++);
            idx = 0;
            UISpawn.Widget<WText>(l, UIPrefabId.WContentHeader, statsBlock.Container, idx++)
                .Load(T.UiGroupStats);
            foreach (var stat in meth.Stats.AllStats) {
                if (stat.IsUnused) { continue; }
                UISpawn.Widget<WIconText2>(l, UIPrefabId.WContentText2, statsBlock.Container, idx++)
                    .Load(Stat.IconFor(stat), Stat.NameFor(stat), stat.Display());
            }

            subPanel.ShowFooter(false);
            subPanel.Show(anchor, subId);
        }

        protected override void Cleanup() {
            S = null;
        }

        public override string Id => TabId;
    }
}
