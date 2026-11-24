using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class ArmBombAction : BaseEntityAction {
        public enum Kind { Arm, Disarm }

        [SelfInit]
        private static void Register() {
            RegisterAction(new ArmBombAction(Kind.Arm));
            RegisterAction(new ArmBombAction(Kind.Disarm));
        }

        private readonly Kind kind;

        public ArmBombAction(Kind kind) {
            this.kind = kind;
        }

        public override string Id => $"ArmBomb_{kind}";
        public override string HotkeyLabel => $"{T.BombArm} / {T.BombDisarm}";
        public override string HotkeyId => "ToggleBombArm";

        private string Label => kind == Kind.Arm ? T.BombArm : T.BombDisarm;
        private string Icon => kind == Kind.Arm ? IconId.CExplosion : IconId.CCheck;

        protected override bool ApplyFor(GameState s, Obj obj, out EntityActionData data) {
            if (!CanToggle(obj, out _)) {
                data = EntityActionData.Unavailable;
                return false;
            }
            data = new EntityActionData(this,
                (l, p, i) => Create(l, p, i, obj), Priority.Normal, -1);
            return true;
        }

        protected override bool ApplyForMany(GameState s, List<Obj> objs,
                out EntityActionData data) {
            if (!ToggleAll(objs, true, out var cnt)) {
                data = EntityActionData.Unavailable;
                return false;
            }
            data = new EntityActionData(this,
                (l, p, i) => CreateMulti(l, p, i, new List<Obj>(objs), cnt),
                Priority.Normal, -1);
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i, Obj obj) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(Icon, Label, () => {
                if (CanToggle(obj, out var bomb)) {
                    bomb.ToggleArm(kind == Kind.Arm);
                }
                UIUtils.RefreshSelectedEntityUI();
            });
            return widget;
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i,
                List<Obj> objs, int cnt) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, Icon, Label, () => ToggleAll(objs, false, out _));
            return widget;
        }

        private bool ToggleAll(List<Obj> objs, bool dryRun, out int cnt) {
            int c = 0;
            foreach (var obj in objs) {
                if (!CanToggle(obj, out var bomb)) { continue; }
                c++;
                if (!dryRun) {
                    bomb.ToggleArm(kind == Kind.Arm);
                }
            }
            cnt = c;
            if (cnt > 0 && !dryRun) {
                UIUtils.RefreshSelectedEntityUI();
            }
            return cnt > 0;
        }

        private bool CanToggle(Obj obj, out BombComp bomb) {
            bomb = null;
            if (!obj.IsActive || !obj.TryGetComponent(out bomb)) {
                return false;
            }
            if (!bomb.IsPlayerFaction || bomb.IsDetonating || bomb.IsDetonated) {
                return false;
            }
            return kind == Kind.Arm ? !bomb.IsArmed : bomb.IsArmed;
        }
    }
}
