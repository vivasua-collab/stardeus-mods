using Game.Constants;
using Game.Systems.Ships;
using Game.Systems.UIUX;
using Game.Utils;

namespace Game.Data.Space {
    /// <summary>
    /// Lets the player pick a side in an ongoing NPC vs NPC battle, turning it into a
    /// real time combat with the helped ship fighting as an ally.
    /// </summary>
    public sealed class SOActionJoinShipBattle : SOAction {
        public const string ActionId = "JoinShipBattle";
        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionJoinShipBattle());
        }

        public override string Id => ActionId;

        public override int UIPriority => 49;

        public override bool IsAvailable(SOActionContext ctx) {
            var battle = FindBattle(ctx);
            if (battle == null) { return false; }
            return ctx.S.Query.CanJoinNPCShipBattle.Ask(battle, battle.Attacker)
                || ctx.S.Query.CanJoinNPCShipBattle.Ask(battle, battle.Defender);
        }

        public override bool AddOptions(QuickChoice qc) {
            var battle = FindBattle(ctx);
            if (battle == null) { return false; }
            var added = AddSideButton(qc, battle, battle.Attacker, battle.Defender);
            added |= AddSideButton(qc, battle, battle.Defender, battle.Attacker);
            return added;
        }

        private bool AddSideButton(QuickChoice qc, NPCShipBattle battle,
                NPCShip sideToHelp, NPCShip opponent) {
            if (!ctx.S.Query.CanJoinNPCShipBattle.Ask(battle, sideToHelp)) {
                return false;
            }
            qc.AddButton(T.ShipJoinBattleHelp_V1.F(sideToHelp.NameWithTag),
                () => FlyOverAndJoin(sideToHelp),
                T.ShipJoinBattleTip_V2.F(sideToHelp.NameWithTag, opponent.NameWithTag));
            return true;
        }

        private void FlyOverAndJoin(NPCShip sideToHelp) {
            var s = ctx.S;
            var so = ctx.SO;
            so.Ship.NearbyPosFrom(A.Player.Location, out var isNear);
            if (isNear) {
                Join(s, sideToHelp);
            } else {
                SOUtils.CheckAbandonOnTravel(s, () => s.Sys.Ship.SetDestination(so, () => Join(s, sideToHelp)));
            }
        }

        /// <summary>
        /// The battle is re-resolved from scratch: while the player was flying over it
        /// could have ended, and either participant could have died or jumped out.
        /// </summary>
        private static void Join(GameState s, NPCShip sideToHelp) {
            var battle = s.Query.GetNPCShipBattleFor.Ask(sideToHelp);
            if (battle == null || !s.Query.CanJoinNPCShipBattle.Ask(battle, sideToHelp)) {
                s.Sig.ShowErrorMessage.Send(T.ShipJoinBattleUnavailable);
                return;
            }
            s.Query.JoinNPCShipBattle.Ask(battle, sideToHelp);
        }

        private static NPCShipBattle FindBattle(SOActionContext ctx) {
            var ship = ctx.SO?.Ship;
            if (ship == null) { return null; }
            return ctx.S.Query.GetNPCShipBattleFor.Ask(ship);
        }
    }
}
