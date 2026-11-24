using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class OrderOpenCapsuleAction : BaseEntityAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new OrderOpenCapsuleAction());
        }

        public const string ActionId = "OrderOpenCapsule";
        public override string Id => ActionId;
        public override string HotkeyLabel => T.OpenCapsule;
        // Shared with QuestPackageAction: both open a container
        public override string HotkeyId => OpenContainerHotkeyId;
        public override string HotkeyDefault => "<Keyboard>/o";
        public const string OpenContainerHotkeyId = "OpenContainer";

        protected override bool ApplyFor(GameState s, Obj obj, out EntityActionData data) {
            if (!CanOrderOpen(obj, out var capsule)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => Create(l, p, i, capsule),
                Priority.Normal);
            return true;
        }

        protected override bool ApplyForMany(GameState s, List<Obj> objs, out EntityActionData data) {
            if (!OrderOpen(objs, true, out var cnt)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => CreateMulti(l, p, i, new List<Obj>(objs), cnt),
                Priority.Normal);
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i, StorageCapsuleComp capsule) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.COpenPackage, T.OpenCapsule, () => {
                if (!capsule.TryOrderOpen()) {
                    UISounds.PlayActionDenied();
                }
                A.S.UIQuery.RebuildEntityActions.Ask();
            });
            return widget;
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i, List<Obj> objs, int cnt) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, IconId.COpenPackage, T.OpenCapsule,
                () => OrderOpen(objs, false, out _));
            return widget;
        }

        private static bool OrderOpen(List<Obj> objs, bool dryRun, out int cnt) {
            int c = 0;
            foreach (var obj in objs) {
                if (!CanOrderOpen(obj, out var capsule)) {
                    continue;
                }
                c++;
                if (!dryRun) {
                    capsule.TryOrderOpen();
                }
            }

            cnt = c;
            if (cnt > 0 && !dryRun) {
                A.S.UIQuery.RebuildEntityActions.Ask();
            }
            return cnt > 0;
        }

        private static bool CanOrderOpen(Obj obj, out StorageCapsuleComp capsule) {
            capsule = null;
            if (!obj.IsActive || !obj.TryGetComponent(out capsule)) {
                return false;
            }

            return capsule.HasLanded && !capsule.IsOpen && !capsule.HasGoal;
        }
    }
}
