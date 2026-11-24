using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class TrapFloorTab : BaseEntityTab {
        public const string TabId = "TrapFloor";

        [SelfInit]
        private static void Register() {
            RegisterTab(new TrapFloorTab());
        }

        private Tile tile;
        private TrapFloorComp comp;
        private GameState S;

        private static readonly List<string> triggerOpts = new();
        private static List<string> TriggerOpts {
            get {
                if (triggerOpts.Count == 0) {
                    triggerOpts.Add(T.NoTarget);
                    triggerOpts.Add($"{T.NounTarget}: {T.NounEnemy}");
                    triggerOpts.Add($"{T.NounTarget}: {T.All}");
                }
                return triggerOpts;
            }
        }

        public override string Id => TabId;
        public override int Order => 10;
        public override string TabName => T.StructureFloorsTrapfloor;

        public override bool ShowFor(GameState s, Tile tile) {
            if (!tile.IsConstructed) { return false; }
            if (tile.ENode?.IsReachable == false) { return false; }
            if (!tile.TryGetComponent<TrapFloorComp>(out var c)) { return false; }
            this.tile = tile;
            this.comp = c;
            S = s;
            return true;
        }

        public override bool LoadContent(WGroup group) {
            var block = UISpawn.Block(lifetime, group, 0);
            int idx = 0;

            if (comp.IsClosed) {
                var progressRow = UISpawn.Widget<WIconText2>(lifetime,
                    UIPrefabId.WContent2Text2, block.Container, idx++);
                progressRow.Load(IconId.CCross, T.TrapCharging,
                    Units.Percentage(comp.ChargeProgress));
                comp.ChargeProgressB.Bind(lifetime, (p) => {
                    progressRow.SetText2(Units.Percentage(p));
                }, false);
            } else if (comp.IsTrapReady) {
                var statusRow = UISpawn.Widget<WIconText>(lifetime,
                    UIPrefabId.WContent2Text, block.Container, idx++);
                statusRow.Load(IconId.CCheck, T.TrapReady);
            } else {
                var statusRow = UISpawn.Widget<WIconText>(lifetime,
                    UIPrefabId.WContent2Text, block.Container, idx++);
                statusRow.Load(IconId.CCross, T.TrapUnpowered);
            }

            var dropdown = UISpawn.Widget<WDropdownWithIcon>(lifetime,
                UIPrefabId.WContent2Dropdown, block.Container, idx++);
            dropdown.Icon.Load(IconId.CTarget);
            int currentType = comp.TriggerAnyone ? 2 : (comp.TriggerEnemy ? 1 : 0);
            dropdown.Dropdown.Load(TriggerOpts, currentType, type => {
                if (type == 2) {
                    if (!A.HasAsimovOverride) {
                        S.Sig.ShowErrorMessage.Send(T.AsimovViolation1);
                        ReloadTab();
                        return;
                    }
                    comp.TriggerEnemy = false;
                    comp.TriggerAnyone = true;
                } else if (type == 1) {
                    comp.TriggerEnemy = true;
                    comp.TriggerAnyone = false;
                } else {
                    comp.TriggerEnemy = false;
                    comp.TriggerAnyone = false;
                }
            });

            comp.StateChangedB.Bind(lifetime, () => ReloadTab(), false);

            return true;
        }

        protected override void Cleanup() {
            tile = null;
            comp = null;
            S = null;
        }
    }
}
