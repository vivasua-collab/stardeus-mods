using System;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Platform;
using Game.Systems;
using Game.Systems.Twitch;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class SettingsPageTwitch : BaseSettingsPage {
        private WText oauthLabel;

        [SelfInit]
        private static void Register() {
            RegisterPage(new SettingsPageTwitch());
        }

        public const string PageId = "Twitch";
        public override string Id => PageId;
        public override int Order => 100; // Adjust order as needed
        public override string TabName => T.MenuSettingsTwitch;
        public static string InactiveWarning = "Twitch integration is configured but inactive. Relaunch to activate.";

        protected override void LoadTab() {
            var cfg = TwitchSys.GlobalData.Config;
            AddFooterButton(0, T.HowItWorks, ShowHelp);
            AddFooterButton(1, T.TestConfiguration, TestConfiguration);

            ShowFooter(true);
            int idx = 0;

            AddHeader(idx++, T.MenuSettingsTwitch);

            if (TwitchSys.IsActive && A.S.Sys.Twitch.IsDeactivated) {
                AddText2WithButton(idx++, Texts.Red(InactiveWarning), null, null, T.RestartNow, () => {
                    PlatformUtils.SuggestRestart();
                }, null);
            }

            AddToggle(idx++, T.TwitchActive, cfg.IsActive, (v) => {
                TwitchSys.GlobalData.Config.IsActive = v;
                AfterConfigChanged();
                ReloadTab();
            });

            AddInput(idx++, T.TwitchChannel, T.EnterName, cfg.ChannelName, OnChannelChanged);

            AddInput(idx++, T.TwitchBotName, T.EnterName, cfg.BotName, OnBotNameChanged);

            oauthLabel = AddHeader(idx++, "");
            SetOauthLabel();

            AddButton(idx++, T.TwitchBotOauthGenerate, () => {
                WPopup.Create(true, GraphicsId.IconsColorTwitchGlitch,
                    T.TwitchBotOauthGenerateTitle,
                    T.TwitchBotOauthGenerateMessage_V2.F(Texts.Accented(T.OpenUrl),
                        Texts.Accented(Consts.TwitchTokenGeneratorURLBase)))
                    .WithAction2(T.Cancel, null)
                    .WithAction(Texts.ExternalLink(T.OpenUrl),
                        () => Files.OpenOSUrl(Consts.TwitchTokenGeneratorURL))
                    .Spawn(UIPoolRoot.SystemPopupContainer);
            });

            AddButton(idx++, T.PasteFromClipboard, () => {
                var oauth = GUIUtility.systemCopyBuffer;
                if (string.IsNullOrWhiteSpace(oauth)) {
                    TwitchSys.GlobalData.Config.OAuth = null;
                    WPopup.Create(true, IconId.CWarning, T.FailedPastingValue, T.ClipboardIsEmpty)
                        .Spawn(UIPoolRoot.SystemPopupContainer);
                } else {
                    oauth = oauth.Trim();
                    if (!oauth.StartsWith("oauth:")) {
                        oauth = $"oauth:{oauth}";
                    }
                    if (oauth.Contains(' ') || oauth.Contains('\n')) {
                        WPopup.Create(true, IconId.CWarning, T.FailedPastingValue,
                            "Pasted text does not have the right format. It should be similar to <b>oauth:abcdef12345678</b>")
                                .Spawn(UIPoolRoot.SystemPopupContainer);
                        TwitchSys.GlobalData.Config.OAuth = null;
                    } else {
                        TwitchSys.GlobalData.Config.OAuth = oauth.Trim();
                        // Assuming sound play
                        ReloadTab();
                    }
                }
                AfterConfigChanged();
                SetOauthLabel();
            });

            if (cfg.OAuth != null) {
                AddButton(idx++, T.CopyToClipboard, () => {
                    var val = TwitchSys.GlobalData.Config.OAuth;
                    if (string.IsNullOrWhiteSpace(val)) {
                        WPopup.Create(true, IconId.CWarning, T.FailedCopyingValue, T.ValueIsEmpty)
                            .Spawn(UIPoolRoot.SystemPopupContainer);
                    }
                    GUIUtility.systemCopyBuffer = val;
                });
            }

            if (!Arrays.IsEmpty(TwitchSys.GlobalData.Viewers)) {
                AddButton(idx++, T.TwitchDeleteData, () => {
                    var viewerCount = TwitchSys.GlobalData.Viewers.Count;
                    var gameCount = TwitchSys.GlobalData.GameData.Count;
                    WPopup.Create(true, IconId.CWarning, T.Warning,
                        T.TwitchDeleteDataWarn_V2.F(Texts.Accented(viewerCount), Texts.Accented(gameCount)))
                        .WithAction2(T.Cancel, null)
                        .WithAction(T.Yes, () => {
                            TwitchSys.GlobalData.Viewers.Clear();
                            TwitchSys.GlobalData.GameData.Clear();
                            AfterConfigChanged();
                        })
                        .Spawn(UIPoolRoot.SystemPopupContainer);
                });
            }
        }

        private void ShowHelp() {
            WPopup.Create(true, GraphicsId.IconsColorTwitchGlitch, T.HowItWorks, null,
                $"Click the {T.OpenUrl} button to open a browser window with instructions on how to configure and use the Twitch integration.")
                .WithAction2(T.Cancel, null)
                .WithAction(Texts.ExternalLink(T.OpenUrl), () => Files.OpenOSUrl(Consts.TwitchHelpURL))
                .Spawn(UIPoolRoot.SystemPopupContainer);
        }

        private void TestConfiguration() {
            var cfg = TwitchSys.GlobalData.Config;
            if (!cfg.IsActive) {
                WPopup.Create(true, GraphicsId.IconsColorTwitchGlitch, T.TwitchSettingsInvalid,
                    T.TwitchIntegrationDisabled_V1.F(Texts.Accented(T.TwitchActive)))
                        .Spawn(UIPoolRoot.SystemPopupContainer);
                return;
            }
            var chanName = cfg.ChannelName;
            if (string.IsNullOrWhiteSpace(chanName)) {
                WPopup.Create(true, GraphicsId.IconsColorTwitchGlitch, T.TwitchSettingsInvalid, T.TwitchChannelNameMissing)
                    .Spawn(UIPoolRoot.SystemPopupContainer);
                return;
            }
            var botName = cfg.BotName;
            if (string.IsNullOrWhiteSpace(botName)) {
                WPopup.Create(true, GraphicsId.IconsColorTwitchGlitch, T.TwitchSettingsInvalid, T.TwitchBotNameMissing)
                    .Spawn(UIPoolRoot.SystemPopupContainer);
                return;
            }
            var botOauth = cfg.OAuth;
            if (string.IsNullOrWhiteSpace(botOauth)) {
                WPopup.Create(true, GraphicsId.IconsColorTwitchGlitch, T.TwitchSettingsInvalid,
                    T.TwitchBotOauthMissing_V1.F(Consts.TwitchTokenGeneratorURLBase))
                        .Spawn(UIPoolRoot.SystemPopupContainer);
                return;
            }
            // Assuming testCfgBtn is not needed, or handle interactivity differently
            TwitchSys.TestConfiguration(() => { });
        }

        private void SetOauthLabel() {
            if (oauthLabel != null) {
                var oauth = TwitchSys.GlobalData.Config.OAuth;
                if (string.IsNullOrWhiteSpace(oauth)) {
                    oauthLabel.Load(string.Format("{0} [{1}]", T.TwitchBotOauth, Texts.Red(T.NotConfigured)));
                } else {
                    oauthLabel.Load(string.Format("{0} [{1}]", T.TwitchBotOauth, Texts.Green(T.Present)));
                }
            }
        }

        private void OnChannelChanged(string text) {
            TwitchSys.GlobalData.Config.ChannelName = text.Trim();
            AfterConfigChanged();
        }

        private void OnBotNameChanged(string text) {
            TwitchSys.GlobalData.Config.BotName = text.Trim();
            AfterConfigChanged();
        }

        private void AfterConfigChanged() {
            TwitchSys.SaveGlobalData();
        }
    }
}
