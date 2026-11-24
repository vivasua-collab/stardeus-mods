using Game.Audio;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class JukeboxTab : BaseEntityTab {
        public const string TabId = "Jukebox";

        [SelfInit]
        private static void Register() {
            RegisterTab(new JukeboxTab());
        }

        private GameState S;

        public override string Id => TabId;
        public override int Order => 10;
        public override string TabName => T.ObjectsDevicesJukebox;

        public override bool ShowFor(GameState s, Tile tile) {
            if (!tile.IsConstructed || tile.ENode?.IsReachable == false) {
                return false;
            }
            if (!tile.TryGetComponent<JukeboxComp>(out _)) {
                return false;
            }
            S = s;
            return true;
        }

        public override bool LoadContent(WGroup group) {
            var block = UISpawn.Block(lifetime, group, 0);
            int idx = 0;

            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.ObjectsDevicesJukebox);

            var nextBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, block.Container, idx++);
            nextBtn.Load(IconId.CSoundOn, null, null, () => {
                The.SysSig.PlayNextTrack.Send();
            });
            SoundMixer.CurrentTrackNameB.Bind(lifetime, (n) => {
                nextBtn.SetText(T.NowPlaying_V1.F(n));
            });
            UIUtils.AddTip(lifetime, nextBtn, T.Next);

            return true;
        }

        public override bool LoadFooter(WGroup group) {
            var btn = UISpawn.Widget<WButtonIconText>(lifetime,
                UIPrefabId.WPanelButtonIcon, group.Container, 0);
            btn.Load(IconId.WSettingsAudio, T.MenuSettings, () => {
                S.UIQuery.ShowMainMenu.Ask(true);
                var mainMenu = (MainMenuWidget) Registries.UIWidgets.Get(UIWidgetId.MainMenu, false);
                if (mainMenu != null) {
                    mainMenu.ShowAudioSettings();
                }
            });
            return true;
        }

        protected override void Cleanup() {
            S = null;
        }
    }
}
