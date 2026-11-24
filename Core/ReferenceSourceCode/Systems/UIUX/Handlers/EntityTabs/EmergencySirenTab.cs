using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class EmergencySirenTab : BaseEntityTab {
        public const string TabId = "EmergencySiren";

        [SelfInit]
        private static void Register() {
            RegisterTab(new EmergencySirenTab());
        }

        private Tile tile;
        private EmergencySirenComp siren;
        private GameState S;

        public override string Id => TabId;
        public override int Order => 10;
        public override string TabName => T.EmergencySiren;

        public override bool ShowFor(GameState s, Tile tile) {
            if (!tile.IsConstructed) { return false; }
            if (tile.ENode?.IsReachable == false) { return false; }
            if (!tile.TryGetComponent<EmergencySirenComp>(out var sc)) { return false; }
            this.tile = tile;
            this.siren = sc;
            S = s;
            return true;
        }

        public override bool LoadFooter(WGroup group) {
            if (tile.IsPendingMove) { return false; }

            int idx = 0;

            var silentRow = UISpawn.Widget<WToggleText>(lifetime,
                UIPrefabId.WContent4Toggle, group.Container, idx++);
            silentRow.Load(T.MakeSirenLoud, !siren.IsAlertSilent, (v) => {
                siren.SetSilent(!v);
                UpdateSoundText(silentRow);
            });
            UpdateSoundText(silentRow);

            var autoRow = UISpawn.Widget<WToggleText>(lifetime,
                UIPrefabId.WContent4Toggle, group.Container, idx++);
            autoRow.Load(T.EmergencySirenAutomatic, siren.IsAlertAutomatic, isOn => {
                siren.IsAlertAutomatic.Set(isOn);
            });
            UIUtils.AddTip(lifetime, autoRow, T.EmergencySirenAutomaticInfo);
            return true;
        }

        private void UpdateSoundText(WToggleText w) {
            if (siren.IsAlertSilent) {
                w.SetText(T.MakeSirenLoud);
            } else {
                w.SetText(T.MakeSirenSilent);
            }
        }

        public override bool LoadContent(WGroup group) {
            siren.StateChangedB.Bind(lifetime, () => ReloadTab(), false);

            var block = UISpawn.Block(lifetime, group, 0);
            int idx = 0;

            var statusRow = UISpawn.Widget<WIconText>(lifetime,
                UIPrefabId.WContentText, block.Container, idx++);
            new C2<bool, bool>(lifetime, siren.IsAlertActive, siren.IsAlertAutomatic,
                    (v1, v2) => {
                if (siren.IsAlertActive) {
                    statusRow.Load(IconId.CSiren, T.SirenActive);
                } else if (siren.IsAlertAutomatic) {
                    statusRow.Load(IconId.CSiren, T.SirenArmed);
                } else {
                    statusRow.Load(IconId.CCross, T.SirenOff);
                }
            });

            return true;
        }

        protected override void Cleanup() {
            tile = null;
            siren = null;
            S = null;
        }
    }
}
