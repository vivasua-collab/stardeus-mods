using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class DeviceWarning_BreachCapsule : BaseEntityWarning {
        public const string WarningId = "Device_BreachCapsule";

        [SelfInit]
        private static void Register() {
            RegisterWarning(new DeviceWarning_BreachCapsule());
        }

        public override string Id => WarningId;
        public override int Order => 40;

        private BreachCapsuleComp comp;
        private Tile tile;

        public override bool ShowFor(GameState s, Tile tile, out bool blockActions) {
            blockActions = false;
            if (!tile.TryGetComponent<BreachCapsuleComp>(out var c)) { return false; }
            if (!c.IsOpened) { return false; }
            if (s.Sys.Combat.IsHostileCombatInProgress) { return false; }
            if (c.HasAnyGoal) { return false; }
            comp = c;
            this.tile = tile;
            return true;
        }

        public override void LoadContent(Lifetime life, WGroup group, ref int idx) {
            var btn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button,
                group.Container, idx++);
            btn.Load(IconId.CDemolition, T.Deconstruct, Disassemble);
        }

        private void Disassemble() {
            tile.Constructable.CreateDeconstructGoal();
            A.S.UIQuery.ReloadCurrentEntity.Ask();
            A.S.UIQuery.RebuildEntityActions.Ask();
        }

        public override void Unload() {
            comp = null;
            tile = null;
        }
    }
}
