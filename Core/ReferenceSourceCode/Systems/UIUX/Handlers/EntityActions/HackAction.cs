using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Conditions;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    /// <summary>
    /// Signal Intrusion: forces the Hacked condition onto a hostile robotic being.
    /// The condition is the same one the Virus uses against us - it takes hours to
    /// take hold, a Firewall Chip stops it outright, and even a completed hack only
    /// flips the victim's faction half the time. Rogue AI tech.
    /// </summary>
    public sealed class HackAction : BaseEntityAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new HackAction());
        }
        public const string ActionId = "Hack";
        public override string Id => ActionId;
        public override string HotkeyLabel => T.VerbHack;

        protected override bool ApplyFor(GameState s, Being entity, out EntityActionData data) {
            if (!CanHack(s, entity)) {
                data = EntityActionData.Unavailable;
                return false;
            }
            data = new EntityActionData(this, (l, p, i) => Create(l, p, i, entity),
                Priority.Normal);
            return true;
        }

        protected override bool ApplyForMany(GameState s, List<Being> beings,
                out EntityActionData data) {
            if (!HackAll(s, beings, true, out var cnt)) {
                data = EntityActionData.Unavailable;
                return false;
            }
            data = new EntityActionData(this,
                (l, p, i) => CreateMulti(l, p, i, new List<Being>(beings), cnt),
                Priority.Normal);
            return true;
        }

        private WBase Create(Lifetime l, RectTransform p, int i, Being being) {
            var widget = SpawnButton(l, true, p, i);
            widget.Load(IconId.CRobotEye, T.VerbHack, () => {
                Hack(A.S, being);
                A.S.UIQuery.RebuildEntityActions.Ask();
            });
            UIUtils.AddTipFunc(l, widget, () => T.VerbHackTip_V1.F(
                Texts.LinkToCodex(DefIdsH.ObjPartsImplantFirewallChip)));
            return widget;
        }

        private WBase CreateMulti(Lifetime l, RectTransform p, int i,
                List<Being> beings, int cnt) {
            var widget = SpawnButtonMulti(l, true, p, i);
            widget.LoadMulti(cnt, IconId.CRobotEye, T.VerbHack,
                () => HackAll(A.S, beings, false, out _));
            return widget;
        }

        public static bool HackAll(GameState s, List<Being> beings, bool dryRun,
                out int cnt) {
            int c = 0;
            foreach (var being in beings) {
                if (!CanHack(s, being)) { continue; }
                c++;
                if (!dryRun) {
                    Hack(s, being);
                }
            }
            cnt = c;
            if (cnt > 0 && !dryRun) {
                s.UIQuery.RebuildEntityActions.Ask();
            }
            return cnt > 0;
        }

        private static void Hack(GameState s, Being being) {
            if (!ConditionState.CreateFor(s, ConditionIdH.Hacked, being.Data,
                    Severity.Mild, true)) {
                D.Err("Failed to create Hacked condition for {0}", being.Data);
                return;
            }
            s.Sys.Log.AddLine(T.HackStarted_V1.F(Texts.LinkToBeing(being.Data)));
        }

        private static bool CanHack(GameState s, Being being) {
            if (!s.BB.Get(BBKeyH.SignalIntrusion, false)) { return false; }
            if (being == null || !being.IsActive || being.IsDead) { return false; }
            var data = being.Data;
            // Biologicals only run the Hacked condition through an Obey Chip, which
            // is not something an enemy is going to be wearing for us.
            if (data.Species == null || data.Species.IsBiological) { return false; }
            if (data.IsFriendly) { return false; }
            if (data.HasCondition(ConditionIdH.Hacked)) { return false; }
            // Nothing left to talk to.
            if (data.Body.MindSlot == null || data.Body.MindSlot.IsPartRemoved) {
                return false;
            }
            return true;
        }
    }
}
