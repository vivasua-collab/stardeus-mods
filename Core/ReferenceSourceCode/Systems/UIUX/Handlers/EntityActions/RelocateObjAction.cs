using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Tools;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class RelocateObjAction : BaseEntityAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new RelocateObjAction());
        }
        public const string ActionId = "RelocateObj";
        public override string Id => ActionId;
        public override string HotkeyLabel => T.Relocate;
        public override string HotkeyId => "Relocate";
        public override string HotkeyDefault => "<Keyboard>/g";
        protected override bool ApplyFor(GameState s, Being entity, out EntityActionData data) {
            return ApplyFor(s, entity.Movable, out data);
        }
        protected override bool ApplyFor(GameState s, Obj entity, out EntityActionData data) {
            return ApplyFor(s, entity.Movable, out data);
        }
        private bool ApplyFor(GameState s, MovableComp mov, out EntityActionData data) {
            if (mov == null || !mov.IsReachableForCommonAction) {
                data = EntityActionData.Unavailable;
                return false;
            }
            data = new EntityActionData(this, (l, p, i) => Create(l, p, i, mov));
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i, MovableComp mov) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.CMove, T.Relocate, mov.BeginRelocation);
            return widget;
        }
        private WBase CreateMulti(Lifetime l, RectTransform p, int i, int cnt,
                List<Obj> objs, List<Being> beings) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, IconId.CMove, T.Relocate,
                () => Relocate(objs, beings, false, out _));
            return widget;
        }

        protected override bool ApplyForMany(GameState s, List<Being> beings, out EntityActionData data) {
            if (!Relocate(null, beings, true, out var cnt)) {
                data = EntityActionData.Unavailable;
                return false;
            }
            data = new EntityActionData(this, (l, p, i) => CreateMulti(l, p, i, cnt, null, beings));
            return true;
        }
        protected override bool ApplyForMany(GameState s, List<Obj> objs, out EntityActionData data) {
            if (!Relocate(objs, null, true, out var cnt)) {
                data = EntityActionData.Unavailable;
                return false;
            }
            data = new EntityActionData(this, (l, p, i) => CreateMulti(l, p, i, cnt, objs, null));
            return true;
        }
        public static bool Relocate(List<Obj> objs, List<Being> beings, bool dryRun, out int cnt) {
            bool hasObjs = objs?.Count > 0;
            bool hasBeings = beings?.Count > 0;
            if (!hasObjs && !hasBeings) {
                cnt = 0;
                return false;
            }
            List<MovableComp> movables = new();
            if (hasObjs) {
                foreach (var obj in objs) {
                    if (obj.Movable?.IsReachableForCommonAction == true) {
                        movables.Add(obj.Movable);
                    }
                }
            }
            if (hasBeings) {
                foreach (var being in beings) {
                    if (being.Movable?.IsReachableForCommonAction == true) {
                        movables.Add(being.Movable);
                    }
                }
            }
            cnt = movables.Count;
            if (cnt == 0) {
                return false;
            }

            if (!dryRun) {
                BeginRelocate(movables);
            }
            return true;
        }

        private static void BeginRelocate(List<MovableComp> common) {
            var sources = new List<Vector2>();
            for (int i = common.Count - 1; i >= 0; i--) {
                var m = common[i];
                sources.Add(m.Entity.Position);
            }
            Func<Vector2, (bool, Vector2)> validator = null;
            if (common.Count == 1) {
                validator = common[0].CanMoveTo;
            } else {
                validator = (potentialV) => (EntityUtils.IsEmptyFloorAt(
                    EntityUtils.ToPos(potentialV), true), potentialV);
            }

            ToolChoosePos.BeginObj(sources, (v) => {
                if (Arrays.IsEmpty(common)) {
                    return new ChoosePosResult {
                        IsSuccess = false,
                        ClearTool = false,
                    };
                }
                var ordered = 0;
                foreach (var r in common) {
                    if (r.Entity.IsActive && r.OrderMove(
                            v + Rng.UInsideUnitCircle() * 0.7f)) {
                        ordered++;
                    }
                }
                The.R.Query.CameraHideLink.Ask();
                return ordered > 0 ? ChoosePosResult.Success : ChoosePosResult.Fail;
            }, validator);
        }
    }
}
