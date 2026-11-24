using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using Game.Commands;
using Game.Data.Factions;

namespace Game.Components {
    public sealed class SectorNukeComp : BaseComponent<SectorNukeComp> {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new SectorNukeComp());
        }

        protected override void OnConfig() {
        }
        public override string ToString() {
            return " * Sector Nuke";
        }

        public void ExecuteDeploy() {
            if (Tile.Health.IsHeavilyDamaged) {
                S.Sig.ShowErrorMessage.Send(T.OperationFailedDeviceHeavilyDamaged);
                return;
            }
            if (A.IsInHyperspace) {
                S.Sig.ShowErrorMessage.Send(T.ExitHyperspaceFirst);
                return;
            }
            S.Query.RigSpaceObjectForDestruction.Ask(A.PlayerSector, Consts.TicksPer8Hours);
            var fac = A.PlayerSector.Faction;
            if (fac != null) {
                S.Query.ChangeFactionAttitude.Ask(fac, FactionDef.Player, -0.75f);
            }
            UISounds.PlayDigitalConfirm();
            new CmdRemoveTile(Tile, false, false).Execute(S);
        }
    }
}
