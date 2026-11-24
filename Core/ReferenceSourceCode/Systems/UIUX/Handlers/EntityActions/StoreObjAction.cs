using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class StoreObjAction : BaseEntityAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new StoreObjAction());
        }

        public const string ActionId = "StoreObj";
        public override string Id => ActionId;
        public override string HotkeyLabel => T.ActionStore;
        public override string HotkeyId => "Store";
        public override string HotkeyDefault => "<Keyboard>/h";

        protected override bool ApplyFor(GameState s, Obj obj, out EntityActionData data) {
            if (!CanOrderStore(obj, out var storable)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => Create(l, p, i, storable), Priority.Normal, -11);
            return true;
        }

        protected override bool ApplyForMany(GameState s, List<Obj> objs, out EntityActionData data) {
            if (!OrderStore(objs, true, out var cnt)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => CreateMulti(l, p, i, new List<Obj>(objs), cnt), Priority.Normal, -11);
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i, StorableObjComp storable) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.CStore, T.ActionStoreItem, () => {
                if (storable.Entity.IsActive) {
                    // An explicit order allows the item, as StorePileAction does
                    storable.IsForbidden = false;
                    storable.TryOrderStoring(true, true, out _);
                }
                A.S.UIQuery.RebuildEntityActions.Ask();
            });
            return widget;
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i, List<Obj> objs, int cnt) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, IconId.CStore, T.ActionStoreItem,
                () => OrderStore(objs, false, out _));
            return widget;
        }

        private static bool OrderStore(List<Obj> objs, bool dryRun, out int cnt) {
            int c = 0;
            foreach (var obj in objs) {
                if (!CanOrderStore(obj, out var storable)) {
                    continue;
                }
                c++;
                if (!dryRun) {
                    storable.IsForbidden = false;
                    storable.TryOrderStoring(true, true, out _);
                }
            }

            cnt = c;
            if (cnt > 0 && !dryRun) {
                A.S.UIQuery.RebuildEntityActions.Ask();
            }
            return cnt > 0;
        }

        private static bool CanOrderStore(Obj obj, out StorableObjComp storable) {
            storable = null;
            if (!obj.IsActive || !obj.TryGetComponent(out storable)) {
                return false;
            }

            return !storable.HasAnyGoal && storable.HasCompatibleStorage();
        }
    }
}
