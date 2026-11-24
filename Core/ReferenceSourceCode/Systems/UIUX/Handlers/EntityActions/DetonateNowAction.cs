using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class DetonateNowAction : BaseEntityAction {
        public const string ActionId = "DetonateNow";

        [SelfInit]
        private static void Register() {
            RegisterAction(new DetonateNowAction());
        }

        public override string Id => ActionId;

        protected override bool ApplyFor(GameState s, Obj obj, out EntityActionData data) {
            if (!CanTrigger(obj, out _)) {
                data = EntityActionData.Unavailable;
                return false;
            }
            data = new EntityActionData(this,
                (l, p, i) => Create(l, p, i, s, obj));
            return true;
        }

        protected override bool ApplyForMany(GameState s, List<Obj> objs,
                out EntityActionData data) {
            if (!Trigger(objs, true, out var cnt)) {
                data = EntityActionData.Unavailable;
                return false;
            }
            data = new EntityActionData(this,
                (l, p, i) => CreateMulti(l, p, i, s, new List<Obj>(objs), cnt));
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i, GameState s, Obj obj) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.CExplosion, T.BombDetonateNow, () => {
                var one = new List<Obj>(1) { obj };
                ShowConfirm(s, one, widget);
            });
            return widget;
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i, GameState s,
                List<Obj> objs, int cnt) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, IconId.CExplosion, T.BombDetonateNow,
                () => ShowConfirm(s, objs, widget));
            return widget;
        }

        private static void ShowConfirm(GameState s, List<Obj> objs, WBase anchor) {
            var qc = QuickChoice.Create(T.BombDetonateNow);
            qc.Widget = anchor.Rect;
            qc.AddButton(T.Confirm, () => {
                if (s.Clock.Speed == ClockSpeed.Stopped) {
                    var sp = s.Clock.SetSpeed(ClockSpeed.Normal);
                    UISounds.PlayClockChange(sp);
                }
                Trigger(objs, false, out _);
            });
            qc.Show();
        }

        private static bool Trigger(List<Obj> objs, bool dryRun, out int cnt) {
            int c = 0;
            foreach (var obj in objs) {
                if (!CanTrigger(obj, out var detonatable)) { continue; }
                c++;
                if (!dryRun) {
                    detonatable.TriggerDetonation();
                }
            }
            cnt = c;
            if (cnt > 0 && !dryRun) {
                UIUtils.RefreshSelectedEntityUI();
            }
            return cnt > 0;
        }

        private static bool CanTrigger(Obj obj, out IDetonatable detonatable) {
            detonatable = null;
            if (!obj.IsActive || !obj.TryGetComponent(out detonatable)) {
                return false;
            }
            return detonatable.CanTriggerDetonation;
        }
    }
}
