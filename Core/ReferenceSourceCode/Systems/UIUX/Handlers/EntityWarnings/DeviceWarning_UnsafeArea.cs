using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class DeviceWarning_UnsafeArea : BaseEntityWarning {
        public const string WarningId = "Device_UnsafeArea";

        [SelfInit]
        private static void Register() {
            RegisterWarning(new DeviceWarning_UnsafeArea());
        }

        public override string Id => WarningId;
        public override int Order => 25;

        private SafetyCheckComp comp;

        public override bool ShowFor(GameState s, Tile tile, out bool blockActions) {
            blockActions = false;
            if (!tile.TryGetComponent<SafetyCheckComp>(out var c)) { return false; }
            if (!s.Prefs.GetBool(Pref.WarnUnsafeArea, PrefH.WarnUnsafeArea, true)) { return false; }
            if (c.IsSafe && !c.IsIgnored) { return false; }
            comp = c;
            return true;
        }

        public override void LoadContent(Lifetime life, WGroup group, ref int idx) {
            var warning = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button,
                group.Container, idx++);
            if (comp.IsIgnored) {
                warning.Load(IconId.CDanger, T.SafetyCheckOff, ShowQuickChoice);
            } else {
                warning.Load(IconId.CDanger, T.ObjectInUnsafeArea, ShowQuickChoice);
                UIUtils.AddTip(life, warning, comp.LastWarning);
            }
        }

        private void ShowQuickChoice() {
            var qc = QuickChoice.Create();
            if (comp.IsIgnored) {
                qc.AddButton(T.SwitchOn, () => {
                    comp.SetIgnored(false);
                    A.S.UIQuery.ReloadCurrentEntity.Ask();
                });
            } else {
                qc.AddButton(T.Ignore, () => {
                    comp.SetIgnored(true);
                    A.S.UIQuery.ReloadCurrentEntity.Ask();
                });
            }
            A.S.UIQuery.ShowQuickChoice.Ask(qc);
        }

        public override void Unload() {
            comp = null;
        }
    }
}
