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
    public sealed class DisposeAction : BaseEntityAction {
        public const string ActionIdBeing = "OrderDisposeBeing";
        public const string ActionIdBio = "OrderDisposeBio";
        public const string ActionIdRobo = "OrderDisposeRobo";

        [SelfInit]
        private static void Register() {
            RegisterAction(new DisposeAction(ActionIdBeing));
            RegisterAction(new DisposeAction(ActionIdBio));
            RegisterAction(new DisposeAction(ActionIdRobo));
        }

        public override string Id => actId;
        public override string HotkeyLabel => T.VerbDispose;
        public override string HotkeyId => "Dispose";
        public override string HotkeyDefault => "<Keyboard>/x";
        private readonly string actId;

        private DisposeAction(string id) {
            actId = id;
        }

        protected override bool ApplyFor(GameState s, Being entity, out EntityActionData data) {
            if (!TryGetApplicableDisposable(entity, actId, DisposeCheck.Single, out var disposable)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            disposable.GetButtonData(out var icon, out var verb);
            data = new EntityActionData(this, (l, p, i) => Create(l, p, i, disposable, icon, verb), Priority.Low);
            return true;
        }

        protected override bool ApplyFor(GameState s, Obj obj, out EntityActionData data) {
            if (!TryGetApplicableDisposable(obj, actId, DisposeCheck.Single, out var disposable)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            disposable.GetButtonData(out var icon, out var verb);
            data = new EntityActionData(this, (l, p, i) => Create(l, p, i, disposable, icon, verb), Priority.Low);
            return true;
        }

        /// <summary>
        /// Button for a single explicitly picked entity. A living, player owned, damaged bot
        /// that is not yet beyond repair asks for confirmation first, since disassembling it
        /// destroys something that could still be repaired.
        /// </summary>
        private WBase Create(Lifetime l, RectTransform p, int i, DisposableComp disposable,
                string icon, string verb) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(icon, verb, () => {
                if (disposable.NeedsScrapConfirmation) {
                    var qc = QuickChoice.Create(T.AreYouSure);
                    qc.AddButton(T.Confirm, () => OrderSingle(disposable));
                    qc.Show();
                    return;
                }
                OrderSingle(disposable);
            });
            UIUtils.AddTipFunc(l, widget, disposable.OutputTip);
            return widget;
        }

        private void OrderSingle(DisposableComp disposable) {
            if (disposable.TryOrderDispose(true, out var reason, true)) {
                UIUtils.RefreshSelectedEntityUI();
            } else {
                if (reason != null) {
                    A.S.Sig.ShowErrorMessage.Send(reason);
                }
            }
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i,
                List<Being> beings, List<Obj> objs, int cnt,
                string icon, string verb) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, icon, verb, () => {
                OrderDispose(beings, objs, false, out _, out _, out _, actId);
            });
            return widget;
        }

        protected override bool ApplyForMany(GameState s, List<Being> beings, out EntityActionData data) {
            if (!OrderDispose(beings, null, true, out var cnt, out var icon, out var verb, actId)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => CreateMulti(l, p, i, new List<Being>(beings), null, cnt, icon, verb));
            return true;
        }

        protected override bool ApplyForMany(GameState s, List<Obj> objs, out EntityActionData data) {
            if (!OrderDispose(null, objs, true, out var cnt, out var icon, out var verb, actId)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => CreateMulti(l, p, i, null, new List<Obj>(objs), cnt, icon, verb));
            return true;
        }

        /// <summary>
        /// Orders disposal for the given beings/objs. When <paramref name="actId"/> is null,
        /// all disposable categories (Being/Bio/Robo) are considered; otherwise only entities
        /// matching that category. Shared by the per-category entity actions and by
        /// <see cref="Game.Tools.FreeSelectContext.TryDisposeSelected"/>. A selection that adds
        /// up to exactly one entity is treated the same as clicking that entity directly, so a
        /// drag-select of a single damaged bot behaves like the single button.
        /// </summary>
        public static bool OrderDispose(List<Being> beings, List<Obj> objs,
                bool dryRun, out int cnt, out string icon, out string verb, string actId = null) {
            bool hasBeings = beings?.Count > 0;
            bool hasObjs = objs?.Count > 0;
            if (!hasBeings && !hasObjs) {
                cnt = 0;
                icon = null;
                verb = null;
                return false;
            }

            int total = (hasBeings ? beings.Count : 0) + (hasObjs ? objs.Count : 0);
            bool single = total == 1;
            var check = single ? DisposeCheck.Single : DisposeCheck.Bulk;

            int c = 0;
            icon = null;
            verb = null;
            string errorReason = null;
            if (hasBeings) {
                foreach (var b in beings) {
                    if (TryGetApplicableDisposable(b, actId, check, out var disposable)) {
                        if (icon == null) {
                            disposable.GetButtonData(out icon, out verb);
                        }
                        c++;
                        if (!dryRun) {
                            if (!disposable.TryOrderDispose(true, out var reason, single) && reason != null) {
                                errorReason = reason;
                            }
                        }
                    }
                }
            }
            if (hasObjs) {
                foreach (var o in objs) {
                    if (TryGetApplicableDisposable(o, actId, check, out var disposable)) {
                        if (icon == null) {
                            disposable.GetButtonData(out icon, out verb);
                        }
                        c++;
                        if (!dryRun) {
                            if (!disposable.TryOrderDispose(true, out var reason, single) && reason != null) {
                                errorReason = reason;
                            }
                        }
                    }
                }
            }

            cnt = c;
            if (cnt > 0 && !dryRun) {
                if (errorReason != null) {
                    A.S.Sig.ShowErrorMessage.Send(errorReason);
                }
                UIUtils.RefreshSelectedEntityUI();
            }
            return cnt > 0;
        }

        private static bool TryGetApplicableDisposable(Being being, string actId,
                DisposeCheck check, out DisposableComp disposable) {
            disposable = null;
            if (!being.IsActive || !being.TryGetComponent(out disposable)) {
                return false;
            }
            return IsApplicable(disposable, actId, check);
        }

        private static bool TryGetApplicableDisposable(Obj obj, string actId,
                DisposeCheck check, out DisposableComp disposable) {
            disposable = null;
            if (!obj.IsActive || !obj.TryGetComponent(out disposable)) {
                return false;
            }
            return IsApplicable(disposable, actId, check);
        }

        private static bool IsApplicable(DisposableComp disposable, string actId, DisposeCheck check) {
            return (actId == null || disposable.CommonActionId == actId) && disposable.CanOrderDispose(check);
        }
    }
}
