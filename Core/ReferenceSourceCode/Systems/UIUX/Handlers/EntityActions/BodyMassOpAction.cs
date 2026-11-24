using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.BodyParts;
using Game.Systems.Creatures.Surgeries;
using Game.UI;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class BodyMassOpAction : BaseEntityAction {
        public const string ActionId = "BodySurgery";

        private static readonly SurgicalOpType[] OpTypes = {
            SurgicalOpType.RemoveAll,
            SurgicalOpType.ReplaceIrreparableAndMissing,
            SurgicalOpType.FillMissing,
            SurgicalOpType.RemoveIrreparable,
        };

        [SelfInit]
        private static void Register() {
            RegisterAction(new BodyMassOpAction());
        }

        public override string Id => ActionId;

        protected override bool ApplyFor(GameState s, Being entity, out EntityActionData data) {
            if (!HasAnyOp(s, entity) && !CanEuthanize(entity)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => Create(l, p, i, s, new List<Being> { entity }), Priority.Low);
            return true;
        }

        protected override bool ApplyForMany(GameState s, List<Being> beings,
                out EntityActionData data) {
            int cnt = 0;
            foreach (var being in beings) {
                if (HasAnyOp(s, being) || CanEuthanize(being)) {
                    cnt++;
                }
            }

            if (cnt == 0) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => CreateMulti(l, p, i, s, new List<Being>(beings), cnt), Priority.Low);
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i,
                GameState s, List<Being> beings) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.CBodyGeneric, T.NounSurgery, () => ShowChoice(s, beings, widget.Rect));
            return widget;
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i,
                GameState s, List<Being> beings, int cnt) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, IconId.CBodyGeneric, T.NounSurgery,
                () => ShowChoice(s, beings, widget.Rect));
            return widget;
        }

        private void ShowChoice(GameState s, List<Being> beings, RectTransform anchor) {
            var qc = QuickChoice.Create(T.NounSurgery);
            // FIXME: when it's a bottom bar button, the widget should go on top,
            // now it tries to render below and gets pushed up (I think)
            // qc.Widget = anchor;
            foreach (var opType in OpTypes) {
                var op = opType;
                int cnt = CountApplicable(s, beings, op);
                if (cnt == 0) {
                    continue;
                }

                var label = beings.Count > 1
                    ? $"{SurgicalOperation.NameFor(op, false)} (x{cnt})"
                    : SurgicalOperation.NameFor(op, false);
                qc.AddIconButton(IconFor(op), label, () => {
                    OrderOpForAll(s, beings, op);
                    UIUtils.RefreshSelectedEntityUI();
                });
            }
            int euthanizeCnt = CountEuthanizable(beings);
            if (euthanizeCnt > 0) {
                var label = beings.Count > 1
                    ? $"{T.VerbEuthanize} (x{euthanizeCnt})"
                    : T.VerbEuthanize;
                qc.AddIconButton(IconId.CKnife, label, () => {
                    EuthanizeAll(beings);
                    UIUtils.RefreshSelectedEntityUI();
                });
            }
            qc.Show();
        }

        private int CountEuthanizable(List<Being> beings) {
            int cnt = 0;
            foreach (var being in beings) {
                if (CanEuthanize(being)) {
                    cnt++;
                }
            }
            return cnt;
        }

        private void EuthanizeAll(List<Being> beings) {
            foreach (var being in beings) {
                if (CanEuthanize(being) && being.TryGetComponent<BodyComp>(out var body)) {
                    body.CreateEuthanizeGoal();
                }
            }
        }

        private static bool CanEuthanize(Being being) {
            return being.TryGetComponent<BodyComp>(out var body) && body.CanEuthanize;
        }

        private void OrderOpForAll(GameState s, List<Being> beings, SurgicalOpType opType) {
            foreach (var being in beings) {
                if (CanApply(s, being, opType, out var body)) {
                    OrderOp(s, body, opType);
                }
            }
        }

        private void OrderOp(GameState s, BodyComp body, SurgicalOpType opType) {
            var op = new SurgicalOperation();
            op.TargetBeing = body.Being.Data;
            op.OpType = opType;
            op.Icon = IconFor(opType);
            if (s.Query.RegisterSurgicalOperation.Ask(op, true)) {
                body.TryOrderOp(op);
            }
        }

        private bool HasAnyOp(GameState s, Being being) {
            foreach (var opType in OpTypes) {
                if (CanApply(s, being, opType, out _)) {
                    return true;
                }
            }
            return false;
        }

        private int CountApplicable(GameState s, List<Being> beings, SurgicalOpType opType) {
            int cnt = 0;
            foreach (var being in beings) {
                if (CanApply(s, being, opType, out _)) {
                    cnt++;
                }
            }
            return cnt;
        }

        private bool CanApply(GameState s, Being being, SurgicalOpType opType, out BodyComp body) {
            body = null;
            if (being.Data.HasStateFlag(Creatures.BeingState.InDevice)) {
                return false;
            }
            if (!being.IsActive || !being.TryGetComponent(out body)) {
                return false;
            }
            if (!body.IsReachableForCommonAction) {
                return false;
            }
            var bodyData = being.Data.Body;
            return bodyData.CanPerformMassAction(opType) && bodyData.HasSlotsForOperation(opType);
        }

        private static string IconFor(SurgicalOpType opType) {
            switch (opType) {
                case SurgicalOpType.RemoveAll: return IconId.CBodyRemoveAll;
                case SurgicalOpType.ReplaceIrreparableAndMissing: return IconId.CBodyReplaceBad;
                case SurgicalOpType.FillMissing: return IconId.CBodyInstallMissing;
                case SurgicalOpType.RemoveIrreparable: return IconId.CBodyRemoveBad;
                default: return IconId.CBodyGeneric;
            }
        }
    }
}
