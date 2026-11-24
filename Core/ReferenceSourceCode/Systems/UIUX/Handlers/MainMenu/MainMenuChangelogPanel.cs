using System;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class MainMenuChangelogPanel : BaseMainMenuPanel {
        override protected string Id => "Changelog";
        private static string changelog;

        public MainMenuChangelogPanel(WallOfTextWidget wgt,
                MainMenuContext ctx, Lifetime parentLife) : base(ctx, parentLife) {
            ParseChangelog(false);
            wgt.Load(changelog, CloseTab);
            wgt.SetActive(true);
        }
        private void CloseTab() {
            ctx.CloseTab();
        }

        protected override void OnLoadContents() {
            // nothing to load
        }

        public static void ParseChangelog(bool showPopup) {
            if (changelog != null) { return; }

            var fullChangelog = Res.LoadText("sync/changelog");

            if (VersionInfo.HasUpdated) {
                var sb = Caches.StringBuilder;
                sb.Clear();
                sb.AppendFormat("<color={0}>", Texts.TMPColorGreen);
                var lines = fullChangelog.Split("\n");
                int changeCount = 0;
                bool hasToSearchForLastPlayed = true;
                var ourVersionHeader = $"<b>{VersionInfo.HasUpdatedFromVersionText}";

                foreach (var line in lines) {
                    if (line.StartsWith(ourVersionHeader)) {
                        hasToSearchForLastPlayed = false;
                        sb.AppendFormat("</color>");
                    }
                    if (hasToSearchForLastPlayed && line.StartsWith("- [")) {
                        changeCount++;
                    }

                    if (sb.Length < 32000) {
                        sb.AppendLine(line);
                    }
                    if (!hasToSearchForLastPlayed && sb.Length > 16000) {
                        break;
                    }
                }

                if (showPopup) {
                    WPopup.Create(true, IconId.CUpgrade, T.NewUpdate, null,
                        T.NewUpdateNote_V6.F(
                            VersionInfo.CleanVersion(
                                VersionInfo.HasUpdatedFromVersionText),
                            VersionInfo.HasUpdateFromDate,
                            VersionInfo.CleanVersion(Consts.Version),
                            Consts.BuildDate,
                            Texts.TMPColorGreen,
                            hasToSearchForLastPlayed ? T.Unknown : changeCount))
                            .WithAction(T.Ok, null)
                            .WithAction2(T.Changelog, () => A.S.UIQuery.ShowChangelog.Ask())
                            .WithDistToEdge(0.25f)
                            .Spawn(UIPoolRoot.SystemPopupContainer);
                }

                changelog = sb.ToString();
            } else {
                changelog = fullChangelog.Substring(0, System.Math.Min(16000, fullChangelog.Length));
            }
        }
    }
}
