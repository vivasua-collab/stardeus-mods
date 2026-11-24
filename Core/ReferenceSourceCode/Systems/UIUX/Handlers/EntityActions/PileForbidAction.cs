using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class PileForbidAction : BaseEntityAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new PileForbidAction());
        }

        public const string ActionId = "PileForbid";
        public override string Id => ActionId;
        public override string HotkeyLabel => $"{T.VerbAllow} / {T.VerbForbid}";
        public override string HotkeyId => "ToggleAutoHaul";
        public override string HotkeyDefault => "<Keyboard>/f";

        protected override bool ApplyFor(GameState s, Obj obj, out EntityActionData data) {
            if (!CanForbid(obj, out var pile)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this, (l, p, i) => Create(l, p, i, pile), Priority.Normal, -10);
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

        private WBase Create(Lifetime l, RectTransform p, int i, UnstoredMatComp pile) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.CForbid, T.VerbForbid, () => {
                Forbid(pile);
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
                if (CanForbid(obj, out var pile)) {
                    c++;
                    if (!dryRun) {
                        Forbid(pile);
                    }
                }
            }

            cnt = c;
            if (cnt > 0 && !dryRun) {
                A.S.UIQuery.RebuildEntityActions.Ask();
            }
            return cnt > 0;
        }

        private static void Forbid(UnstoredMatComp pile) {
            pile.IsForbidden = true;
            var carrier = pile.Entity.Carrier;
            AIGoal carrierGoal = null;
            if (carrier?.Data.EffectiveFaction.IsPlayer == true) {
                carrierGoal = carrier.Goal;
                if (carrierGoal != null) {
                    AIGoal.Cancel(carrierGoal, G.Ticks, T.OperationForbidden);
                    carrier.Graphics.DropIfCarrying();
                }
                // One of several piles carried to storage, the goal is on another pile.
                // Cancelling would forbid that pile too, so only interrupt: the rest of
                // the load is dropped and ordered stored again
                var taskGoal = carrier.AIAgent?.Goal;
                if (taskGoal?.Target is UnstoredMatComp goalPile && goalPile != pile
                        && AIGoal.Interrupt(taskGoal, G.Ticks, T.OperationForbidden)) {
                    carrier.Graphics.DropIfCarrying();
                }
            }
            if (pile.Entity.Goal?.JobIdH == JobTypeIdH.Hauling && pile.Entity.Goal != carrierGoal) {
                AIGoal.Cancel(pile.Entity.Goal, G.Ticks);
            }
        }

        private static bool CanForbid(Obj obj, out UnstoredMatComp pile) {
            pile = null;
            if (!obj.IsActive) {
                return false;
            }
            if (!obj.TryGetComponent(out pile)) {
                return false;
            }
            return !pile.TreatAsObj && !pile.IsForbidden;
        }
    }
}
