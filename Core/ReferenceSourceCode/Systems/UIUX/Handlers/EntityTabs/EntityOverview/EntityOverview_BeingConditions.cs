using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.BodyParts;
using Game.Systems.Conditions;
using Game.Systems.Creatures;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public class EntityOverview_BeingConditions : EntityOverviewBlock {
        public const string BlockId = "BeingConditions";
        public override int Order => -50;
        public override string Id => BlockId;
        private bool conditionsExpanded = true;
        private bool devExpanded;
        private bool woundsExpanded;

        private sealed class InjuryRefs {
            public WContentListButton Btn;
            public Lifetime WoundsLife;
        }

        [SelfInit]
        private static void Register() {
            Register(new EntityOverview_BeingConditions());
        }

        // This is where the damage an unstable stasis array does becomes visible
        public override bool ShowForUnrevealed => true;

        public override bool ShowFor(GameState s, BeingData being) {
            return true;
        }

        public override void LoadContent(EntityOverviewContext ctx, WGroup group, ref int gIdx) {
            var being = ctx.Being;
            if (being == null) { return; }
            var lifetime = ctx.Lifetime;
            var block = UISpawn.Block(lifetime, group, gIdx++);
            int idx = 0;

            Lifetime activeLife = null;
            Lifetime devBtnLife = null;
            Lifetime devLife = null;
            WContentListButton devBtn = null;
            var injury = new InjuryRefs();
            int lastActiveCount = GetActiveCount(being);

            var header = UISpawn.Widget<WContentListButton>(
                lifetime, UIPrefabId.WContentHeaderExpandable, block.Container, idx++);
            header.Load(T.NounConditions, Units.Num(lastActiveCount));
            header.LoadOnToggle(lifetime, (life, container, willShow, wasLoaded) => {
                conditionsExpanded = willShow;
                if (!willShow) { return wasLoaded; }

                if (activeLife == null) {
                    activeLife = lifetime.CreateChild("ActiveConditions");
                    SpawnActiveConditions(activeLife, container, being, injury);
                    LayoutRebuilder.ForceRebuildLayoutImmediate(container);
                    LayoutRebuilder.ForceRebuildLayoutImmediate(header.Rect);
                }

                if (HasDevConditions(being) && devBtnLife == null) {
                    CreateDevBtn();
                }

                return true;
            }, conditionsExpanded);

            void CreateDevBtn() {
                devBtnLife = lifetime.CreateChild("DevBtn");
                devBtn = UISpawn.Widget<WContentListButton>(
                    devBtnLife, UIPrefabId.WContentListButton, header.Container, -1);
                devBtn.Load(IconId.CCondition, T.NounDevelopingConditions, GetDevCount(being));
                devBtn.LoadOnToggle(devBtnLife, (life, container, willShow, wasLoaded) => {
                    devExpanded = willShow;
                    if (!willShow) { return wasLoaded; }
                    if (wasLoaded) { return true; }
                    devLife = lifetime.CreateChild("DevConditions");
                    SpawnDevelopingConditions(devLife, container, being);
                    LayoutRebuilder.ForceRebuildLayoutImmediate(container);
                    LayoutRebuilder.ForceRebuildLayoutImmediate(devBtn.Rect);
                    return true;
                }, devExpanded);
            }

            being.ConditionsListChangedB.Bind(lifetime, () => {
                int newCount = GetActiveCount(being);
                if (newCount == lastActiveCount && injury.Btn != null) {
                    // Structure unchanged, wounds changed: rebuild only the wound sub-list.
                    injury.WoundsLife?.Dispose();
                    injury.WoundsLife = null;
                    injury.Btn.SetText2(Severities.NameFor(WorstNonDecayWoundSeverity(being), false));
                    injury.Btn.RebuildContainer();
                    return;
                }
                lastActiveCount = newCount;
                injury.Btn = null;
                injury.WoundsLife = null; // disposed via activeLife below
                activeLife?.Dispose();
                activeLife = null;
                header.Button.SetText2(Units.Num(newCount));
                header.RebuildContainer();
            }, false);

            // DevelopingConditionsChangedB is also fired when active conditions change
            // (so a developing condition that became active is removed from this list too)
            being.DevelopingConditionsChangedB.Bind(lifetime, () => {
                if (HasDevConditions(being)) {
                    if (devBtnLife == null) {
                        CreateDevBtn();
                    } else {
                        devLife?.Dispose();
                        devLife = null;
                        devBtn.SetText2(GetDevCount(being));
                        devBtn.RebuildContainer();
                    }
                } else if (devBtnLife != null) {
                    devLife?.Dispose();
                    devLife = null;
                    devBtnLife.Dispose();
                    devBtnLife = null;
                    devBtn = null;
                }
            }, false);
        }

        private static int GetActiveCount(BeingData being) {
            bool hasRealWounds = HasNonDecayWounds(being);
            bool hasInjuryCondition = false;
            int cnt = 0;
            foreach (var c in being.Conditions.Values) {
                if (c.IsUndetected) {
                    #if PRODUCTION_BUILD
                    continue;
                    #endif
                }
                if (IsInjuryCondition(c)) {
                    hasInjuryCondition = true;
                    if (!hasRealWounds) { continue; }
                }
                cnt++;
            }
            if (!hasInjuryCondition && hasRealWounds) {
                cnt++;
            }
            return cnt;
        }

        private static bool IsInjuryCondition(ConditionState c) {
            return c.Condition.IdH == ConditionIdH.Injured || c.Condition.IdH == ConditionIdH.Damaged;
        }

        private static bool HasNonDecayWounds(BeingData being) {
            foreach (var bp in being.Body.Slots) {
                if (bp.IsPartRemoved) { continue; }
                var wounds = bp.State.Wounds;
                if (Arrays.IsEmpty(wounds)) { continue; }
                foreach (var w in wounds) {
                    if (w.Type != DamageType.Decay) { return true; }
                }
            }
            return false;
        }

        private static Severity WorstNonDecayWoundSeverity(BeingData being) {
            var maxSev = Severity.Irrelevant;
            foreach (var bp in being.Body.Slots) {
                if (bp.IsPartRemoved) { continue; }
                var wounds = bp.State.Wounds;
                if (Arrays.IsEmpty(wounds)) { continue; }
                foreach (var w in wounds) {
                    if (w.Type == DamageType.Decay) { continue; }
                    maxSev = Severities.Bigger(maxSev, w.Severity);
                }
            }
            return maxSev;
        }

        private static bool HasDevConditions(BeingData being) {
            foreach (var cond in being.ConditionProgress) {
                if (being.Conditions.ContainsKey(cond.Key)) { continue; }
                if (cond.Value < 0.01f) { continue; }
                return true;
            }
            return false;
        }

        private static string GetDevCount(BeingData being) {
            int cnt = 0;
            foreach (var cond in being.ConditionProgress) {
                if (being.Conditions.ContainsKey(cond.Key)) { continue; }
                if (cond.Value < 0.01f) { continue; }
                cnt++;
            }
            return Units.Num(cnt);
        }

        private void SpawnActiveConditions(Lifetime life, RectTransform container, BeingData being,
                InjuryRefs injury) {
            int idx = 0;
            int cnt = 0;
            bool hasRealWounds = HasNonDecayWounds(being);
            bool hasInjuryCondition = false;

            foreach (var c in being.Conditions.Values) {
                if (c.IsUndetected) {
                    #if PRODUCTION_BUILD
                    continue;
                    #endif
                }
                bool isInjury = IsInjuryCondition(c);
                if (isInjury) {
                    hasInjuryCondition = true;
                    // Decay-only wounds are already surfaced via Rotting/Corroding, so don't
                    // duplicate that info here when there's nothing else to report.
                    if (!hasRealWounds) { continue; }
                }
                string sideText;
                if (c.IsUndetected) {
                    sideText = "DEV: Undetected!";
                } else {
                    cnt++;
                    // Injury/Damaged severity is tracked over all wounds including decay
                    // (see ConditionInjured.Tick); use the worst non-decay severity instead
                    // so this matches what's actually shown in the expanded wound list.
                    sideText = isInjury
                        ? Severities.NameFor(WorstNonDecayWoundSeverity(being), false)
                        : Severities.NameFor(c.Severity, c.Condition.IsPositive);
                }
                if (isInjury) {
                    var cond = c;
                    SpawnInjuryButton(life, container, being, injury,
                        cond.Condition, sideText, () => cond.Tooltip(true), ref idx);
                    continue;
                }
                var cw = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, container, idx++);
                cw.Load(c.Condition.Icon, c.Condition.NameT, sideText);
                UIUtils.AddTipFunc(life, cw, () => c.Tooltip(true));
            }

            if (!hasInjuryCondition && hasRealWounds) {
                cnt++;
                bool isBio = being.Species.IsBiological;
                int condId = isBio ? ConditionIdH.Injured : ConditionIdH.Damaged;
                var condition = Registries.Conditions.FastGet(condId);
                var maxSev = WorstNonDecayWoundSeverity(being);
                SpawnInjuryButton(life, container, being, injury, condition,
                    Severities.NameFor(maxSev, false), () => condition.DescT, ref idx);
            }

            if (cnt == 0) {
                var empty = UISpawn.Widget<WIconText>(life, UIPrefabId.WContentText, container, idx++);
                empty.Load(IconId.CCheck, T.NoConditions);
            }
        }

        private void SpawnInjuryButton(Lifetime life, RectTransform container, BeingData being,
                InjuryRefs injury, Condition condition, string sideText, Func<string> tooltip, ref int idx) {
            var btn = UISpawn.Widget<WContentListButton>(life, UIPrefabId.WContentListButton, container, idx++);
            injury.Btn = btn;
            btn.Load(condition.Icon, condition.NameT, sideText);
            UIUtils.AddTipFunc(life, btn.PopupArea, tooltip);
            btn.LoadOnToggle(life, (toggleLife, woundsContainer, willShow, wasLoaded) => {
                woundsExpanded = willShow;
                if (!willShow) { return wasLoaded; }
                if (wasLoaded) { return true; }
                injury.WoundsLife = toggleLife.CreateChild("InjuryWounds");
                SpawnWoundRows(injury.WoundsLife, woundsContainer, being);
                LayoutRebuilder.ForceRebuildLayoutImmediate(woundsContainer);
                LayoutRebuilder.ForceRebuildLayoutImmediate(btn.Rect);
                return true;
            }, woundsExpanded);
        }

        private static void SpawnWoundRows(Lifetime life, RectTransform container, BeingData being) {
            var wounds = new List<(BodyPartSlot Slot, Wound Wound)>();
            foreach (var bp in being.Body.Slots) {
                if (bp.IsPartRemoved) { continue; }
                var bpWounds = bp.State.Wounds;
                if (Arrays.IsEmpty(bpWounds)) { continue; }
                foreach (var w in bpWounds) {
                    if (w.Type == DamageType.Decay) { continue; }
                    wounds.Add((bp, w));
                }
            }
            wounds.Sort((a, b) => (int) b.Wound.Severity - (int) a.Wound.Severity);

            int idx = 0;
            foreach (var (bp, wnd) in wounds) {
                bool isBio = bp.InstalledDef?.IsBiological ?? being.Species.IsBiological;
                var row = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, container, idx++);
                row.Load(IconId.CBlood, wnd.TitleFor(bp), bp.NameT);
                var state = bp.State;
                UIUtils.AddTipFunc(life, row, () => Wound.LiveTooltipFor(state, wnd, isBio));
            }
        }

        private static void SpawnDevelopingConditions(Lifetime life, RectTransform container, BeingData being) {
            int idx = 0;
            int cnt = 0;

            foreach (var cond in being.ConditionProgress) {
                if (being.Conditions.ContainsKey(cond.Key)) { continue; }
                if (cond.Value < 0.01f) { continue; }
                cnt++;
                var condition = Registries.Conditions.FastGet(cond.Key);
                var cw = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, container, idx++);
                cw.Load(condition.Icon, condition.NameT, Units.Percentage(cond.Value));
                UIUtils.AddTip(life, cw, condition.DescT);
            }

            if (cnt == 0) {
                var empty = UISpawn.Widget<WIconText>(life, UIPrefabId.WContentText, container, idx++);
                empty.Load(IconId.CCheck, T.Empty);
            }
        }
    }
}
