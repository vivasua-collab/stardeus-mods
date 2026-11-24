using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class ForgetAshesAction : BaseEntityAction {
        public const string ActionId = "ForgetAshes";

        [SelfInit]
        private static void Register() {
            RegisterAction(new ForgetAshesAction());
        }

        public override string Id => ActionId;
        public override string HotkeyLabel => $"{T.ObjMaterialsAshes} - {T.VerbForget}";

        protected override bool ApplyFor(GameState s, Obj obj, out EntityActionData data) {
            if (!CanForget(obj, out var ash)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => Create(l, p, i, s, ash), Priority.Normal);
            return true;
        }

        protected override bool ApplyForMany(GameState s, List<Obj> objs, out EntityActionData data) {
            if (!ForgetAll(s, objs, true, out var cnt)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => CreateMulti(l, p, i, s, new List<Obj>(objs), cnt), Priority.Normal);
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i,
                GameState s, BeingAshesComp ash) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.CFuneralRibbon, T.VerbForget, () => {
                BeingAshesComp.DoForget(s, ash.Obj.Data, true);
                A.S.UIQuery.RebuildEntityActions.Ask();
            });
            return widget;
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i,
                GameState s, List<Obj> objs, int cnt) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, IconId.CFuneralRibbon, T.VerbForget,
                () => ForgetAll(s, objs, false, out _));
            return widget;
        }

        private static bool ForgetAll(GameState s, List<Obj> objs,
                bool dryRun, out int cnt) {
            int c = 0;
            foreach (var obj in objs) {
                if (CanForget(obj, out var ash)) {
                    c++;
                    if (!dryRun) {
                        BeingAshesComp.DoForget(s, ash.Obj.Data, true);
                    }
                }
            }

            cnt = c;
            if (cnt > 0 && !dryRun) {
                A.S.UIQuery.RebuildEntityActions.Ask();
            }
            return cnt > 0;
        }

        private static bool CanForget(Obj obj, out BeingAshesComp ash) {
            ash = null;
            if (!obj.IsActive || !obj.TryGetComponent(out ash)) {
                return false;
            }
            return ash.IsNamed;
        }
    }
}
