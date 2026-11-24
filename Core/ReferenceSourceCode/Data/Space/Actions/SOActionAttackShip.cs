using Game.Constants;
using Game.Systems.Ships;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;

namespace Game.Data.Space {
    public sealed class SOActionAttackShip : SOAction {
        public const string ActionId = "AttackShip";
        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionAttackShip());
        }

        public override bool IsAvailable(SOActionContext ctx) {
            var ship = ctx.SO.Ship;
            if (ship == null) { return false; }
            if (ship.IsPlayer) { return false; }
            if (ship.IsDerelict) { return false; }
            if (ship.IsInCombat) { return false; }
            if (ctx.SO.Parent != ctx.Player.Parent) { return false; }
            if (ctx.S.Clock.IsForceStopped) { return false; }
            return NPCShipBehavior_Sector_Combat.CanPlayerAttack(ctx.SO.Ship);
        }

        public override bool AddOptions(QuickChoice qc) {
            qc.AddButton(T.CombatObjectiveAttack, Attack, autoClose: false);
            return true;
        }

        // public override UDB GetUIBlock() {
        //     return dataBlock ??= UDB.Create(
        //         this, UDBT.DBtn, IconId.CAttackTarget,
        //             T.CombatObjectiveAttack).WithClickFunction(Attack);
        // }

        // A misclick next to Hail can end a playthrough, so attacking asks to confirm (STA-1827)
        private void Attack() {
            if (!CanAttack(ctx)) { return; }
            // the action is a shared instance and hovering another object reloads its context
            var c = ctx;
            var qc = QuickChoice.Create(T.CombatObjectiveAttack);
            qc.AddButton(T.Confirm, () => DoAttack(c));
            c.S.UIQuery.ShowQuickChoice.Ask(qc);
        }

        private bool CanAttack(SOActionContext c) {
            if (!IsAvailable(c)) {
                UISounds.PlayActionDenied();
                return false;
            }
            if (A.Player.Ship.CombatState.Weapons.Count == 0) {
                c.S.Sig.ShowErrorMessage.Send($"{T.MenuWeapons}: {T.NotAvailable}");
                return false;
            }
            return true;
        }

        private void DoAttack(SOActionContext c) {
            if (!CanAttack(c)) { return; }
            c.SO.Ship.NearbyPosFrom(A.Player.Location, out var isNear);
            if (isNear) {
                c.S.Query.StartShipCombatWith.Ask(c.SO.Ship, true);
            } else {
                SOUtils.CheckAbandonOnTravel(c.S, () => {
                    c.S.Sys.Ship.SetDestination(c.SO, () => {
                        // The target can be destroyed, hacked into a derelict or start a
                        // battle of its own while we fly over (STA-1865, STA-1898);
                        // StartShipCombatWith decides what still makes a fight
                        var target = c.SO.Ship;
                        if (target == null) { return; }
                        c.S.Query.StartShipCombatWith.Ask(target, true);
                    });
                });
            }
        }

        public override string Id => ActionId;

        public override int UIPriority => 50;
    }
}
