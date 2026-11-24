using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class ObjForbidAction : BaseEntityAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new ObjForbidAction());
        }

        public const string ActionId = "ObjForbid";
        public override string Id => ActionId;
        public override string HotkeyLabel => $"{T.VerbAllow} / {T.VerbForbid}";
        public override string HotkeyId => "ToggleAutoHaul";
        public override string HotkeyDefault => "<Keyboard>/f";

        protected override bool ApplyFor(GameState s, Obj obj, out EntityActionData data) {
            if (!CanForbid(obj, out var storable)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this, (l, p, i) => Create(l, p, i, storable), Priority.Normal, -10);
            return true;
        }

        protected override bool ApplyForMany(GameState s, List<Obj> objs, out EntityActionData data) {
            if (!ForbidAll(objs, true, out var cnt)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => CreateMulti(l, p, i, new List<Obj>(objs), cnt), Priority.Normal, -10);
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i, StorableObjComp storable) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.CForbid, T.VerbForbid, () => {
                Forbid(storable);
                A.S.UIQuery.RebuildEntityActions.Ask();
            });
            return widget;
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i, List<Obj> objs, int cnt) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, IconId.CForbid, T.VerbForbid,
                () => ForbidAll(objs, false, out _));
            return widget;
        }

        public static bool ForbidAll(List<Obj> objs, bool dryRun, out int cnt) {
            int c = 0;
            foreach (var obj in objs) {
                if (CanForbid(obj, out var storable)) {
                    c++;
                    if (!dryRun) {
                        Forbid(storable);
                    }
                }
            }

            cnt = c;
            if (cnt > 0 && !dryRun) {
                A.S.UIQuery.RebuildEntityActions.Ask();
            }
            return cnt > 0;
        }

        private static void Forbid(StorableObjComp storable) {
            storable.IsForbidden = true;
            var carrier = storable.Entity.Carrier;
            AIGoal carrierGoal = null;
            if (carrier?.Data.EffectiveFaction.IsPlayer == true) {
                carrierGoal = carrier.Goal;
                if (carrierGoal != null) {
                    AIGoal.Cancel(carrierGoal, G.Ticks, T.OperationForbidden);
                    carrier.Graphics.DropIfCarrying();
                }
            }
            if (storable.Entity.Goal?.JobIdH == JobTypeIdH.Hauling
                    && storable.Entity.Goal != carrierGoal) {
                AIGoal.Cancel(storable.Entity.Goal, G.Ticks);
            }
        }

        private static bool CanForbid(Obj obj, out StorableObjComp storable) {
            storable = null;
            if (!obj.IsActive) {
                return false;
            }
            if (!obj.TryGetComponent(out storable)) {
                return false;
            }
            if (obj.Pile?.TreatAsObj == false) {
                return false;
            }
            return !storable.IsForbidden;
        }
    }
}
