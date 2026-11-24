using System;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Modifiers;
using Game.Platform;
using Game.Systems;
using Game.Systems.Path;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using TMPro;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class SettingsPageCurrentGame : BaseSettingsPage {
        [SelfInit]
        private static void Register() {
            RegisterPage(new SettingsPageCurrentGame());
        }

        public const string PageId = "CurrentGame";
        public override bool IsHidden => G.IsDemoMode;
        public override string Id => PageId;
        public override int Order => 0;
        public override string TabName => T.MenuSettingsCurrent_Game;

        protected override void LoadTab() {
            ShowFooter(false);

            if (!GameState.IsCurrent(The.R?.State)) {
                AddHeader(0, "Not in game");
                return;
            }

            var S = The.R.State;
            int idx = 0;

            // Modifiers section
            bool isSandbox = S.Scenario.IsSandbox;
            string lastGroup = null;
            var mods = Modifier.All
                .Where(m => m.CanChangeInGame && !(m.SkipSandbox && isSandbox))
                .OrderBy(m => m.SortOrder).ThenBy(m => m.NameT).ToList();
            var shown = new Dictionary<string, Modifier>();
            foreach (var m in mods) {
                shown[m.Id] = m;
            }
            // A paired modifier is drawn by its partner, in the partner's spot
            var drawnByPartner = new HashSet<string>();
            foreach (var m in mods) {
                if (m.PairedWithId == null) {
                    continue;
                }
                if (shown.TryGetValue(m.PairedWithId, out var other)
                        && other.GroupName == m.GroupName) {
                    drawnByPartner.Add(other.Id);
                }
            }
            foreach (var m in mods) {
                if (drawnByPartner.Contains(m.Id)) {
                    continue;
                }
                if (lastGroup != m.GroupName) {
                    lastGroup = m.GroupName;
                    AddHeader(idx++, m.GroupName);
                }
                if (m.PairedWithId != null && shown.TryGetValue(m.PairedWithId, out var partner)
                        && drawnByPartner.Contains(partner.Id)) {
                    m.CreateInGameControlsPaired(lifetime, idx++, contentRect, partner);
                    continue;
                }
                m.CreateInGameControls(lifetime, idx++, contentRect);
            }

            // Gameplay section
            AddHeader(idx++, T.MenuSettingsGameplay);

            AddTogglePair(idx++,
                T.PrefAutoPauseDanger, S.Prefs.GetBool(
                        Pref.AutoPauseWhenDanger, PrefH.AutoPauseWhenDanger, false), (v) => {
                    S.Prefs.SetBool(Pref.AutoPauseWhenDanger, v);
                }, null,
                T.RestrictClockOnDisasters, S.Prefs.GetBool(
                        Pref.RestrictClockOnDisasters, PrefH.RestrictClockOnDisasters, true), (v) => {
                    S.Prefs.SetBool(Pref.RestrictClockOnDisasters, v);
                }, null);

            AddTogglePair(idx++, T.WarnUnsafeArea, S.Prefs.GetBool(Pref.WarnUnsafeArea, PrefH.WarnUnsafeArea, true), (v) => {
                S.Prefs.SetBool(Pref.WarnUnsafeArea, v);
            }, null,
            T.CrawlerCombatAlert, S.Prefs.GetBool(Pref.CrawlerCombatAlert, PrefH.CrawlerCombatAlert, true), (v) => {
                S.Prefs.SetBool(Pref.CrawlerCombatAlert, v);
            }, T.CrawlerCombatAlertDesc);

            AddTogglePair(idx++,
                T.SkipColonistChoice, S.Prefs.GetBool(Pref.SkipWakeUpChoice, PrefH.SkipWakeUpChoice, false), (v) => {
                    S.Prefs.SetBool(Pref.SkipWakeUpChoice, v);
                }, T.SkipColonistChoiceDesc,
                T.DeconstructOrderHaul, S.Prefs.GetBool(Pref.DeconstructOrderHaul, PrefH.DeconstructOrderHaul, false), (v) => {
                    S.Prefs.SetBool(Pref.DeconstructOrderHaul, v);
                }, T.DeconstructOrderHaulTip);

            AddTogglePair(idx++,
                T.DisableCodexPopups, S.Prefs.GetBool(Pref.DisableCodexPopups, PrefH.DisableCodexPopups, false), (v) => {
                    S.Prefs.SetBool(Pref.DisableCodexPopups, v);
                }, T.DisableCodexPopupsTip,
                T.LabelEnemyAshes, S.Prefs.GetBool(Pref.LabelEnemyAshes, PrefH.LabelEnemyAshes, Consts.LabelEnemyAshesDefault), (v) => {
                    S.Prefs.SetBool(Pref.LabelEnemyAshes, v);
                }, T.LabelEnemyAshesTip);

            // AddToggle(idx++, T.DisableCodexPopups, S.Prefs.GetBool(Pref.DisableCodexPopups, PrefH.DisableCodexPopups, false), (v) => {
            //     S.Prefs.SetBool(Pref.DisableCodexPopups, v);
            // }, T.DisableCodexPopupsTip);


            // Priorities
            var priorityOptions = new List<string> { "1", "2", "3", "4", "5" };

            AddDropdown(idx++, T.AutoRepairPriority, priorityOptions, A.AutoRepairPriority - 1, (opt) => {
                S.Prefs.SetInt(Pref.AutoRepairPriority, opt + 1);
            }, T.AutoRepairPriorityTip_V1.F(Texts.LinkToResearch(The.Defs.FastGet(DefIdsH.ResearchRoboticsAutoRepair))));

            AddDropdown(idx++, T.AutoRebuildPriority, priorityOptions, A.AutoRebuildPriority - 1, (opt) => {
                S.Prefs.SetInt(Pref.AutoRebuildPriority, opt + 1);
            }, T.AutoRebuildPriorityTip);

            AddDropdown(idx++, T.BodyPartProcedurePriority, priorityOptions, A.BodyPartProcedurePriority - 1, (opt) => {
                S.Prefs.SetInt(Pref.BodyPartProcedurePriority, opt + 1);
            }, T.BodyPartProcedurePriorityTip);

            // AddToggle(idx++, T.ShowHints, S.Prefs.GetBool(Pref.ShowHints, PrefH.ShowHints, true), (v) => {
            //     S.Prefs.SetBool(Pref.ShowHints, v);
            // });

            // Feedback/Performance section
            AddHeader(idx++, T.FeedbackPerformance);

            AddToggle(idx++,
                T.HighPrecisionAi, S.Prefs.GetBool(Pref.AIPrecision, PrefH.AIPrecision, true), (v) => {
                    S.Prefs.SetBool(Pref.AIPrecision, v);
                }, T.HighPrecisionAiTip);

            // AddToggle(idx++, T.InvinciblePets, S.Prefs.GetBool(Pref.InvinciblePets, PrefH.InvinciblePets, false), (v) => {
            //     S.Prefs.SetBool(Pref.InvinciblePets, v);
            //     foreach (var b in S.Beings.Alive) {
            //         if (b.Data.Species.IsPet) {
            //             b.Data.Body.CheckInvincible();
            //         }
            //     }
            // });

#if !STARDEUS_DEMO
            // Other section
            AddHeader(idx++, T.GroupOther);
            AddButton(idx++, T.ResetTips, ResetTips);

            // AddButton(idx++, "Dev Tools", () => {
            //     The.SysSig.HideMenu.Send();
            //     DevMenu.ShowMenu();
            // });

            #if !PRODUCTION_BUILD
            // We want to be able to disable this while testing saves in the editor
            AddToggle(idx++, "DEV: Auto Save on Exit", S.Prefs.GetBool(Pref.AutoSaveOnExit, PrefH.AutoSaveOnExit, true), (v) => {
                S.Prefs.SetBool(Pref.AutoSaveOnExit, v);
            });
            #endif

#endif
        }

        private void ResetTips() {
            var S = The.R.State;
            S.Sys.Hint.ResetState();
            S.Prefs.SetBool(Pref.ShowNoStorageWarning, true);
            S.Prefs.SetBool(Pref.ShowShieldOverbuildWarning, true);
            S.Prefs.SetBool(Pref.ShowClassPointsWarning, true);
            S.Prefs.SetBool(Pref.ShowFluxCapacityWarning, true);
            S.Prefs.SetBool(Pref.ShowFluxVentsWarning, true);
            S.Prefs.SetBool(Pref.ShowNoWorkerCapable, true);
            S.Prefs.SetBool(Pref.ShowTaskMemoryWarning, true);
            S.Prefs.SetBool(Pref.ShowFavHelp, true);
            S.Prefs.ClearMatchingBools("show.energy.error");
            UISounds.PlayShowDialog();
            ReloadTab();
        }
    }
}
