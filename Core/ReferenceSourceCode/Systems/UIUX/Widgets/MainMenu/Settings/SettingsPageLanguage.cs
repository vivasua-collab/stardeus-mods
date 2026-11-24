using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Game.CodeGen;
using Game.Constants;
using Game.Input;
using Game.Platform;
using Game.Systems.Story;
using Game.UI;
using Game.Utils;
using KL.Collections;
using KL.Pool;
using KL.Utils;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Systems.UIUX {
    public sealed class SettingsPageLanguage : BaseSettingsPage {
        [SelfInit]
        private static void Register() {
            RegisterPage(new SettingsPageLanguage());
        }

        public const string PageId = "Language";
        public override string Id => PageId;
        public override int Order => 50;
        public override string TabName => "Language";

        private string selectedLocale;
        private bool isRestartRequired;
        private WPopupData lastPopup;

        private void GenerateValidityReportFor(string selectedLocale) {
            var curLocale = Translations.CurrentLocale;
            if (Translations.CurrentLocaleIsCommunity) {
                curLocale = $"{curLocale}:community";
            }

            Translations.SetLocale(selectedLocale, false);
            TranslationUtils.GenerateValidityReport();
            T.SetLocale(curLocale);
        }

        protected override void LoadTab() {
            // Footer buttons
            AddFooterButton(0, Texts.ExternalLink("Contribute a translation"), () => {
                Files.OpenOSUrl("https://github.com/kodolinija/stardeus-translations/");
            });
            if (isRestartRequired) {
                var restartBtn = AddFooterButton(1, Texts.Bold(T.RestartRequired),
                    () => PlatformUtils.SuggestRestart(false), T.RestartRequiredInfo);
                restartBtn.OverrideButtonTheme("Red");
            } else {
                AddFooterButton(1, Texts.ExternalLink("Video Tutorial"), () => {
                    Files.OpenOSUrl("https://www.youtube.com/watch?v=Q9yugSMBzrE");
                });
            }

            ShowFooter(true);

            int idx = 0;

            var curLang = GStr(Pref.GLanguage, Translations.DefaultLocale);
            // Build locale lists
            var localeListOff = new List<string>();
            var localeListComm = new List<string>();
            var localeMapTxt = new Dictionary<string, string>();
            var localeMapProgress = new Dictionary<string, float>();
            foreach (var loc in Translations.SupportedLocales.OrderBy(l => l)) {
                if (loc != curLang) {
                    Translations.SetLocale(loc, false);
                }
                float completeness;
                if (loc == Translations.DefaultLocale) {
                    completeness = 1f;
                } else {
                    completeness = Translations.GetLocaleCompleteness();
                }
                if (loc != curLang) {
                    Translations.SetLocale(curLang, false);
                }
                localeMapTxt[loc] = loc;
                localeMapProgress[loc] = completeness;
                if (curLang != loc) {
                    localeListOff.Add(loc);
                }
            }
            foreach (var loc in Translations.CommunityLocales.OrderBy(l => l)) {
                var locc = $"{loc}:community";
                if (locc != curLang) {
                    Translations.SetLocale(locc, false);
                }

                var completeness = Translations.GetLocaleCompleteness();
                if (locc != curLang) {
                    Translations.SetLocale(curLang, false);
                }

                localeMapTxt[locc] = loc;
                localeMapProgress[locc] = completeness;
                if (locc != curLang) {
                    localeListComm.Add(loc);
                }
            }
            if (curLang != Translations.DefaultLocale) {
                localeListOff.AddUniqueInefficient(Translations.DefaultLocale);
                localeMapTxt[Translations.DefaultLocale] = Translations.DefaultLocale;
                localeMapProgress[Translations.DefaultLocale] = 1f;
            }

            var selLang = "Selected Language";
            AddHeader(idx++, selLang);

            string curLangName = Texts.Bold(curLang);
            string curLangKey = curLang;
            if (Translations.CurrentLocaleIsCommunity) {
                var lang = curLang.Replace(":community", string.Empty);
                curLangName = $"{lang} (Community)";
                // curLangKey = $"{curLang}:community";
            }
            var comp = localeMapProgress[curLangKey];
            var wgt = AddProgressWithButton(idx++, $"{curLangName} ({Units.Percentage(comp)})", comp,
                null, Texts.Grey("Current"), TranslationUtils.GenerateValidityReport,
                    "Generate language validity report");
            wgt.Button.SetInteractable(false);
            wgt.ButtonIcon.Load(IconId.CCog, () => {
                ShowQuickChoiceFor(curLangKey, curLangName, wgt.ButtonIcon.Rect);
            });

            AddHeader(idx++, "Official Translations");

            // Add buttons for each language
            for (int i = 0; i < localeListOff.Count; i++) {
                var lang = localeListOff[i];
                var txt = localeMapTxt[lang];
                var progress = localeMapProgress[lang];
                AddLanguageEntry(idx++, lang, txt, progress);
            }

            if (localeListComm.Count > 0) {
                AddHeader(idx++, "Community Translations");
            }

            for (int i = 0; i < localeListComm.Count; i++) {
                var lang = localeListComm[i];
                var locc = $"{lang}:community";
                var txt = localeMapTxt[locc];
                var progress = localeMapProgress[locc];
                AddLanguageEntry(idx++, locc, txt, progress);
            }

            selectedLocale = Translations.CurrentLocale;
            if (Translations.CurrentLocaleIsCommunity) {
                selectedLocale = $"{selectedLocale}:community";
            }
        }

        private void ShowQuickChoiceFor(string curLangKey, string curLangName, RectTransform widget) {
            var qc = QuickChoice.Create();
            qc.Widget = widget;

            var rawLocale = curLangKey.Replace(":community", string.Empty);
            var reportDir = Persist.PersistentPath("Translations", rawLocale);
            var reportExists = Directory.Exists(reportDir);

            if (reportExists) {
                qc.AddButton("Open Validity Report", () => {
                    Files.OpenOSFolder(reportDir);
                });
                qc.AddButton("Update Validity Report", () => {
                    GenerateValidityReportFor(curLangKey);
                });
                foreach (var fileName in Files.ListDirectoryFiles(reportDir, ".csv", false)) {
                    if (fileName == $"{rawLocale}.csv") {
                        continue;
                    }
                    qc.AddButton($"Integrate {fileName}", () => {
                        IntegrateLocaleFor(curLangKey, fileName);
                    });
                }
            } else {
                qc.AddButton("Generate Validity Report", () => {
                    GenerateValidityReportFor(curLangKey);
                });
            }
            qc.AddButton("Open Translation Folder", () => {
                var sourceDir = Translations.GetLocaleSourceDirectory(rawLocale,
                    curLangKey.EndsWith(":community"));
                Files.OpenOSFolder(sourceDir);
            });
            qc.Show();
        }

        private void IntegrateLocaleFor(string curLangKey, string fileName) {
            var integrated = TranslationUtils.IntegrateLocale(
                curLangKey, fileName, out var summary, out var reportDir);
            if (lastPopup != null) {
                lastPopup.Despawn();
            }
            if (!integrated) {
                lastPopup = WPopup.Create(true, IconId.CWarning, curLangKey, null, summary)
                    .Spawn(UIPoolRoot.SystemPopupContainer);
                return;
            }
            Files.OpenOSFolder(reportDir);
            lastPopup = WPopup.Create(true, IconId.CInfo, curLangKey, null, summary)
                .Spawn(UIPoolRoot.SystemPopupContainer);
            ReloadTab();
        }

        private void AddLanguageEntry(int idx, string lang, string txt, float progress) {
            var wgt = AddProgressWithButton(idx, $"{Texts.Bold(txt)} ({Units.Percentage(progress)})", progress,
                $"Translation progress: {Units.PercentageColored(progress)}",
                    "Use", () => OnLanguageSelect(lang), null);
            wgt.ButtonIcon.Load(IconId.CCog, () => {
                ShowQuickChoiceFor(lang, txt, wgt.ButtonIcon.Rect);
            });
        }

        private void OnLanguageSelect(string lang) {
            selectedLocale = lang;
            var curLoc = GStr(Pref.GLanguage, Translations.DefaultLocale);
            if (lang != Translations.DefaultLocale) {
                Translations.SetLocale(lang, false);
                Translations.ValidateCurrentLocale(out var missing, out var invalid, out var badTags,
                    out var outdated, out var dupe, out var unknown, out var malformed,
                    out var wrongCounts, out float completeness);
                Translations.SetLocale(curLoc, false);
                string msg = null;
                if (wrongCounts.Count > 0) {
                    msg = $"Wrong column count in files: {string.Join(",", wrongCounts)}\nMalformed keys: {malformed.Count}\nMissing keys: {missing.Count}\nInvalid keys: {invalid.Count}\nKeys with unmatched tags: {badTags.Count}\nOutdated keys: {outdated.Count}\nDuplicate keys (can be ok): {dupe.Count}\n\nWould you like to switch to this translation?";
                } else {
                    var badCount = missing.Count + invalid.Count + outdated.Count + dupe.Count + malformed.Count;
                    if (badCount > 0) {
                        msg = $"Malformed keys: {malformed.Count}\nMissing keys: {missing.Count}\nInvalid keys: {invalid.Count}\nKeys with unmatched tags: {badTags.Count}\nOutdated keys: {outdated.Count}\nDuplicate keys (can be ok): {dupe.Count}\n\nWould you like to switch to this translation?";
                    } else {
                        msg = "Would you like to switch to this translation?";

                    }
                }
                var headerMsg = lang;
                var headerSub = $"{Texts.Accented(Units.PercentageWithFraction2(completeness))} complete";
                if (lang.Contains(":community")) {
                    headerMsg = $"{headerMsg.Replace(":community", "")}";
                    headerSub = $"Community Translation / {headerSub}";
                } else {
                    headerSub = $"Official Translation / {headerSub}";
                }
                var activate = $"{GlyphMap.SpriteCheck} {Texts.Bold("Activate")}";
                if (lastPopup != null) {
                    lastPopup.Despawn();
                }
                lastPopup = WPopup.Create(true, IconId.CInfo, headerMsg, headerSub, msg).
                    WithAction(activate, () => {
                        SetGStr(Pref.GLanguage, lang);
                        MaybeUnbindConsole(lang);
                        PlatformUtils.SuggestRestart(false);
                        isRestartRequired = true;
                        ReloadTab();
                    }).WithAction2($"{GlyphMap.SpriteCross} Cancel", null)
                    .Spawn(UIPoolRoot.SystemPopupContainer);
            } else {
                SetGStr(Pref.GLanguage, lang);
                PlatformUtils.SuggestRestart(false);
                isRestartRequired = true;
                ReloadTab();
            }
        }

        private void MaybeUnbindConsole(string lang) {
            lang = lang.ToLowerInvariant();
            if (lang.Contains("japanese")
                    || lang.Contains("chinese")
                    || lang.Contains("korean")) {
                var binding = The.Bindings.GetBinding(ActionType.ToggleConsole);
                binding.InputAction.ApplyBindingOverride("<Keyboard>/scrollLock");
                binding.Save();
                The.SaveGlobalSettings();
            }
        }

        protected override void BeforeUnloadTab() {
            if (lastPopup != null) {
                lastPopup.Despawn();
                lastPopup = null;
            }
        }
    }
}
