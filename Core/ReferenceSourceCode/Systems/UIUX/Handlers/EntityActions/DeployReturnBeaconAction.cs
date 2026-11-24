using Game.CodeGen;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class DeployReturnBeaconAction : BaseEntityAction {
        public const string ActionId = "DeployReturnBeacon";
        [SelfInit]
        private static void Register() {
            RegisterAction(new DeployReturnBeaconAction());
        }
        public override string Id => ActionId;

        protected override bool ApplyFor(GameState s, Tile tile, out EntityActionData data) {
            if (tile.IsConstructed && tile.TryGetComponent<ReturnBeaconComp>(out var beacon)) {
                var applicable = !A.IsInHyperspace && !tile.Health.IsHeavilyDamaged;
                data = new EntityActionData(this, (l, p, i) => {
                    var btn = SpawnButton(l, applicable, p, i);
                    btn.Load(GraphicsId.SpaceObjectsMiscReturnBeacon01, T.Deploy,
                        () => DeployBeacon(s, tile));
                    return btn;
                });
                return true;
            }
            data = EntityActionData.Unavailable;
            return false;
        }
        private void DeployBeacon(GameState s, Tile tile) {
            if (tile.Health.IsHeavilyDamaged) {
                s.Sig.ShowErrorMessage.Send(T.OperationFailedDeviceHeavilyDamaged);
                return;
            }
            if (A.IsInHyperspace) {
                s.Sig.ShowErrorMessage.Send(T.ExitHyperspaceFirst);
                return;
            }
            foreach (var so in A.Player.Parent.Children) {
                if (so.Type.IdH == SpaceObjectTypeIdH.ReturnBeacon) {
                    s.Sig.ShowErrorMessage.Send(T.ObjectAlreadyExists_V1.F(
                        Texts.LinkToStarmap(so, T.NounReturnBeacon)));
                    return;
                }
            }

            SOUtils.DeployReturnBeacon(s, tile.Definition.IdH);
            UISounds.PlayDigitalConfirm();
            new CmdRemoveTile(tile, false, false).Execute(s);
        }
    }

}
