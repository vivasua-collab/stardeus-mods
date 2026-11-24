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
    public sealed class StorePileAction : BaseEntityAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new StorePileAction());
        }

        public const string ActionId = "StorePile";
        public override string Id => ActionId;
        public override string HotkeyLabel => T.ActionStore;
        public override string HotkeyId => "Store";
        public override string HotkeyDefault => "<Keyboard>/h";

        protected override bool ApplyFor(GameState s, Obj obj, out EntityActionData data) {
            if (!CanOrderStore(obj, out var pile)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => Create(l, p, i, pile), Priority.Normal, -11);
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

        private WBase Create(Lifetime l, RectTransform p, int i, UnstoredMatComp pile) {
            var widget = SpawnButton(l, pile.HasCompatibleStorage(), p, i);
            widget.Load(IconId.CStore, T.ActionStore, () => {
                if (pile.Entity.IsActive) {
                    pile.OrderStore(true);
                    pile.IsForbidden = false;
                }
                A.S.UIQuery.RebuildEntityActions.Ask();
            });
            // if (!pile.HasCompatibleStorage()) {
            //     widget.SetInteractable(false);
            //     widget.SetText(Texts.Strike(T.ActionStore));
            // }
            UIUtils.AddTipFunc(l, widget, pile.DetailsTip);
            return widget;
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i, List<Obj> objs, int cnt) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, IconId.CStore, T.ActionStore,
                () => OrderStore(objs, false, out _));
            return widget;
        }

        private static bool OrderStore(List<Obj> objs, bool dryRun, out int cnt) {
            int c = 0;
            // Only the first pile without storage shows the error, not one per pile
            bool reportedNoStorage = false;
            foreach (var obj in objs) {
                if (!CanOrderStore(obj, out var pile)) {
                    continue;
                }
                c++;
                if (!dryRun) {
                    var report = true;
                    if (!pile.HasCompatibleStorage()) {
                        report = !reportedNoStorage;
                        reportedNoStorage = true;
                    }
                    pile.OrderStore(report);
                    pile.IsForbidden = false;
                }
            }

            cnt = c;
            if (cnt > 0 && !dryRun) {
                A.S.UIQuery.RebuildEntityActions.Ask();
            }
            return cnt > 0;
        }

        private static bool CanOrderStore(Obj obj, out UnstoredMatComp pile) {
            pile = null;
            if (!obj.IsActive || !obj.TryGetComponent(out pile)) {
                return false;
            }

            return !pile.TreatAsObj && !pile.HasAnyGoal;
        }
    }
}
