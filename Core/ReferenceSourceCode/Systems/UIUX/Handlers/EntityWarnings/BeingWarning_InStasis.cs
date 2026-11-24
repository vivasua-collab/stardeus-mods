using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    // A sleeper's panel is reached from the Manifest tab of their array, and opening it
    // deselects the array. This button is the way back, so going through a manifest is
    // a click per person rather than a trip to the map per person.
    public sealed class BeingWarning_InStasis : BaseEntityWarning {
        public const string WarningId = "Being_InStasis";

        [SelfInit]
        private static void Register() {
            RegisterWarning(new BeingWarning_InStasis());
        }

        private BeingData being;

        public override string Id => WarningId;
        public override int Order => 20;
        public override bool ShowFor(GameState s, BeingData being, out bool blockActions) {
            blockActions = false;
            if (being?.Mind == null || being.Mind.Role != ColonyRole.InStasis) {
                return false;
            }
            this.being = being;
            return true;
        }

        public override void LoadContent(Lifetime life, WGroup group, ref int idx) {
            var data = being;
            var warning = UISpawn.Widget<WButtonIconText2>(life,
                UIPrefabId.WContent2Button, group.Container, idx++);
            warning.Load(IconId.CCryogenics, T.BeingInStasis, null, () => {
                var array = StasisUtils.ArrayOf(A.S, data);
                if (array == null) { return; }
                A.S.UIQuery.ShowSelectedEntityTab.Ask(StasisManifestTab.TabId);
                EntityUtils.SelectOrFocus(array.Tile);
            });
            if (StasisUtils.IsUnrevealed(data)) {
                UIUtils.AddTip(life, warning, T.BeingInStasisUnrevealed);
            }
        }

        public override void Unload() {
            being = null;
        }
    }
}
