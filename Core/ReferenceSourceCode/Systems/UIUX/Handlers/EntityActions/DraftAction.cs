using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI.Combat;
using Game.Tools;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class DraftAction : BaseEntityAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new DraftAction());
        }
        public const string ActionId = "Draft";
        public override string Id => ActionId;
        public override string HotkeyLabel => $"{T.VerbDraft} / {T.VerbUndraft}";
        public const string ToggleDraftHotkeyId = "ToggleDraft";
        public override string HotkeyId => ToggleDraftHotkeyId;
        public override string HotkeyDefault => "<Keyboard>/f";

        protected override bool ApplyFor(GameState s, Being entity, out EntityActionData data) {
            if (!CanSetDirectControl(entity, true, out var control)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this, (l, p, i) => Create(l, p, i, control), Priority.High);
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i, DirectControlComp control) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.CDraft, T.VerbDraft, () => {
                control.SetDirectControl(true);
                UIUtils.RefreshSelectedEntityUI();
                UISounds.Play("FX/DraftSFX", 0.3f);
            });
            return widget;
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i, List<Being> beings, int cnt) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, IconId.CDraft, T.VerbDraft,
                () => DraftAll(beings, false, out _));
            return widget;
        }

        protected override bool ApplyForMany(GameState s, List<Being> beings, out EntityActionData data) {
            if (!DraftAll(beings, true, out var cnt)) {
                data = EntityActionData.Unavailable;
                return false;
            }

            data = new EntityActionData(this,
                (l, p, i) => CreateMulti(l, p, i, new List<Being>(beings), cnt), Priority.High);

            return true;
        }

        public static bool DraftAll(List<Being> beings, bool dryRun, out int cnt) {
            int c = 0;
            foreach (var being in beings) {
                if (CanSetDirectControl(being, true, out var control)) {
                    c++;
                    if (!dryRun) {
                        control.SetDirectControl(true);
                    }
                }
            }

            cnt = c;
            if (cnt > 0 && !dryRun) {
                UIUtils.RefreshSelectedEntityUI();
                UISounds.Play("FX/DraftSFX", 0.3f);
            }
            return cnt > 0;
        }

        private static bool CanSetDirectControl(Being being, bool value, out DirectControlComp control) {
            control = null;
            if (!being.IsActive || !being.TryGetComponent(out DirectControlComp dc)) {
                return false;
            }

            if (dc.IsUnderDirectControl == value) {
                return false;
            }

            if (!dc.CheckCanToggleDirectControl(out _)) {
                return false;
            }

            var cu = being.AIAgent?.CombatUnit;
            if (cu?.Army != null &&
                    (cu.Army?.Flags & CombatFlags.Friendly) == CombatFlags.Friendly) {
                return false;
            }

            control = dc;
            return true;
        }
    }
}
